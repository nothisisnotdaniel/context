using System.Globalization;

namespace Context;

/// <summary>What <see cref="InboxTriage.Remove(NoteStore, string)"/> or <see cref="InboxTriage.Snooze(NoteStore, string, DateOnly)"/> did.</summary>
internal enum TriageResult
{
    /// <summary>The line was removed or rewritten.</summary>
    Done,

    /// <summary>The inbox note is missing or no longer holds the line; nothing was written.</summary>
    Gone,

    /// <summary>The inbox note changed on disk on every attempt; nothing was written.</summary>
    ChangedOnDisk,

    /// <summary>The inbox note would grow past <see cref="NoteStore.MaxEditBytes"/>; nothing was written.</summary>
    TooLarge,
}

/// <summary>
/// One line of the inbox that triage shows: a line that starts with <c>- </c>. <see cref="Line"/> is the line as
/// stored (without its line end), <see cref="Text"/> the rest after the <c>- </c>, the capture stamp and the snooze
/// token.
/// </summary>
internal sealed record InboxItem
{
    public InboxItem(string line, string text, DateTime? captured, DateOnly? snooze)
    {
        Line = line;
        Text = text;
        Captured = captured;
        Snooze = snooze;
    }

    /// <summary>The whole line as stored, without <c>\r</c> or <c>\n</c>; how the line is found again.</summary>
    public string Line { get; }

    /// <summary>The line's text without <c>- </c>, the capture stamp and a final snooze token. Raw outside text.</summary>
    public string Text { get; }

    /// <summary>The leading capture stamp (<c>yyyy-MM-dd HH:mm</c>), or <c>null</c>.</summary>
    public DateTime? Captured { get; }

    /// <summary>The final <c>snooze:yyyy-MM-dd</c> day, or <c>null</c>.</summary>
    public DateOnly? Snooze { get; }

    /// <summary>Only the type name, so the raw text never leaks through formatting.</summary>
    public override string ToString() => nameof(InboxItem);
}

/// <summary>The inbox lines to triage, in the order they are shown, and how many are snoozed to a later day.</summary>
internal sealed class InboxQueue
{
    public InboxQueue(IReadOnlyList<InboxItem> items, int snoozedCount)
    {
        Items = items;
        SnoozedCount = snoozedCount;
    }

    /// <summary>An inbox with no lines to triage.</summary>
    public static InboxQueue Empty { get; } = new InboxQueue([], 0);

    /// <summary>Due snoozed lines first (by snooze day, then file order), then the others in file order.</summary>
    public IReadOnlyList<InboxItem> Items { get; }

    /// <summary>The lines snoozed to a day after today; they are not in <see cref="Items"/>.</summary>
    public int SnoozedCount { get; }
}

/// <summary>
/// Triage of the <c>inbox</c> note (<see cref="Inbox"/>): reads its <c>- </c> lines into a queue and removes or
/// snoozes one line at a time. Every other line, the line ends and a BOM stay as they are.
/// </summary>
internal static class InboxTriage
{
    /// <summary>The token at the end of a snoozed line, before its day.</summary>
    public const string SnoozePrefix = " snooze:";

    private const string ItemPrefix = "- ";
    private const string StampFormat = "yyyy-MM-dd HH:mm";
    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>
    /// The items of the inbox <paramref name="text"/> (any line ends, a leading BOM ignored): every line that starts
    /// with <c>- </c>. A line snoozed to a day after <paramref name="today"/> is left out and counted; lines whose
    /// snooze day has come come first, by that day and then file order; the rest follow in file order.
    /// </summary>
    public static InboxQueue Items(string text, DateOnly today)
    {
        List<(InboxItem Item, int Index)> due = [];
        List<InboxItem> rest = [];
        int snoozed = 0;
        List<string> lines = Lines(text);
        for (int i = 0; i < lines.Count; i++)
        {
            InboxItem? item = Parse(lines[i]);
            if (item is null)
            {
                continue;
            }

            if (item.Snooze is not { } day)
            {
                rest.Add(item);
            }
            else if (day > today)
            {
                snoozed++;
            }
            else
            {
                due.Add((item, i));
            }
        }

        List<InboxItem> items = due.OrderBy(d => d.Item.Snooze).ThenBy(d => d.Index).Select(d => d.Item).ToList();
        items.AddRange(rest);
        return new InboxQueue(items, snoozed);
    }

    /// <summary>The item for one stored line, or <c>null</c> when it doesn't start with <c>- </c>.</summary>
    public static InboxItem? Parse(string line)
    {
        if (!line.StartsWith(ItemPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string rest = line[ItemPrefix.Length..];
        DateTime? captured = null;
        if (rest.Length > StampFormat.Length
            && rest[StampFormat.Length] == ' '
            && DateTime.TryParseExact(
                rest[..StampFormat.Length], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime stamp))
        {
            captured = stamp;
            rest = rest[(StampFormat.Length + 1)..];
        }

        DateOnly? snooze = null;
        if (SnoozeAt(rest) is { } at)
        {
            snooze = DateOnly.ParseExact(rest[(at + SnoozePrefix.Length)..], DayFormat, CultureInfo.InvariantCulture);
            rest = rest[..at];
        }

        return new InboxItem(line, rest, captured, snooze);
    }

    /// <summary>True when one line of <paramref name="text"/> (any line ends) is exactly <paramref name="line"/>.</summary>
    public static bool Contains(string text, string line) => Lines(text).Contains(line, StringComparer.Ordinal);

    /// <summary>
    /// <paramref name="line"/> with a final snooze token (if any) replaced by <c> snooze:yyyy-MM-dd</c> for
    /// <paramref name="until"/>.
    /// </summary>
    public static string WithSnooze(string line, DateOnly until)
    {
        string kept = SnoozeAt(line) is { } at ? line[..at] : line;
        return kept + SnoozePrefix + until.ToString(DayFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Removes the first line of the inbox note that equals <paramref name="line"/> (with its line end), through
    /// <see cref="NoteStore.ReadForEdit"/> and <see cref="NoteStore.SaveIfUnchanged"/>, so every other byte stays.
    /// When the note changed on disk in between it is read again, at most <see cref="Inbox.MaxRetries"/> more times.
    /// Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the note can't be read or
    /// written.
    /// </summary>
    public static TriageResult Remove(NoteStore store, string line) => Remove(store, line, null);

    /// <summary>Like <see cref="Remove(NoteStore, string)"/>; <paramref name="beforeSave"/> runs after each read and before each save.</summary>
    internal static TriageResult Remove(NoteStore store, string line, Action? beforeSave) =>
        Rewrite(store, line, null, beforeSave);

    /// <summary>
    /// Rewrites the first line of the inbox note that equals <paramref name="line"/> in place as
    /// <see cref="WithSnooze"/> for <paramref name="until"/>, like <see cref="Remove(NoteStore, string)"/>.
    /// </summary>
    public static TriageResult Snooze(NoteStore store, string line, DateOnly until) => Snooze(store, line, until, null);

    /// <summary>Like <see cref="Snooze(NoteStore, string, DateOnly)"/>; <paramref name="beforeSave"/> runs after each read and before each save.</summary>
    internal static TriageResult Snooze(NoteStore store, string line, DateOnly until, Action? beforeSave) =>
        Rewrite(store, line, WithSnooze(line, until), beforeSave);

    // Replaces the first line equal to line with replacement (null: removes it with its line end).
    private static TriageResult Rewrite(NoteStore store, string line, string? replacement, Action? beforeSave)
    {
        for (int attempt = 0; attempt <= Inbox.MaxRetries; attempt++)
        {
            if (!store.TryFind(Inbox.NoteName, out NoteInfo? match) || match is null)
            {
                return TriageResult.Gone;
            }

            EditableNote opened = store.ReadForEdit(match);
            string? changed = Replace(opened.Text, line, replacement);
            if (changed is null)
            {
                return TriageResult.Gone;
            }

            beforeSave?.Invoke();
            NoteSaveResult result = store.SaveIfUnchanged(opened, changed, out _);
            switch (result)
            {
                case NoteSaveResult.Saved:
                case NoteSaveResult.Unchanged:
                    return TriageResult.Done;
                case NoteSaveResult.TooLarge:
                    return TriageResult.TooLarge;
                default:
                    // ChangedOnDisk or Missing: read again.
                    continue;
            }
        }

        return TriageResult.ChangedOnDisk;
    }

    // The text (with \n) with the first line equal to line replaced, or removed with its \n; null when no line is.
    private static string? Replace(string text, string line, string? replacement)
    {
        int start = 0;
        while (start <= text.Length)
        {
            int end = text.IndexOf('\n', start);
            int stop = end < 0 ? text.Length : end;
            if (stop - start == line.Length && string.CompareOrdinal(text, start, line, 0, line.Length) == 0)
            {
                if (replacement is not null)
                {
                    return string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(stop));
                }

                int after = end < 0 ? text.Length : end + 1;
                return string.Concat(text.AsSpan(0, start), text.AsSpan(after));
            }

            if (end < 0)
            {
                break;
            }

            start = end + 1;
        }

        return null;
    }

    // The lines of text: \r\n and \n end a line, a leading BOM is dropped, and a final line end starts no line.
    private static List<string> Lines(string text)
    {
        string plain = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (plain.StartsWith('﻿'))
        {
            plain = plain[1..];
        }

        List<string> lines = [.. plain.Split('\n')];
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    // Where the final " snooze:yyyy-MM-dd" token of text starts (a valid day only), or null.
    private static int? SnoozeAt(string text)
    {
        int at = text.Length - SnoozePrefix.Length - DayFormat.Length;
        if (at < 0 || string.CompareOrdinal(text, at, SnoozePrefix, 0, SnoozePrefix.Length) != 0)
        {
            return null;
        }

        return DateOnly.TryParseExact(
            text[(at + SnoozePrefix.Length)..], DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? at
            : null;
    }
}
