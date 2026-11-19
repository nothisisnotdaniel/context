using System.Globalization;
using System.Text.RegularExpressions;

namespace Context;

/// <summary>What <see cref="DoneLog.Append(NoteStore, DateOnly, string, DateOnly?)"/> did.</summary>
internal enum DoneLogResult
{
    /// <summary>The line was appended (or the done note was created with it).</summary>
    Appended,

    /// <summary>The done note changed on disk on every attempt (or is sealed); nothing was written.</summary>
    ChangedOnDisk,

    /// <summary>The done note would grow past <see cref="NoteStore.MaxEditBytes"/>; nothing was written.</summary>
    TooLarge,

    /// <summary>The done note couldn't be read or written; nothing was written.</summary>
    Failed,
}

/// <summary>One line of the done log: <c>- yyyy-MM-dd &lt;text&gt;</c> with an optional <c> (due yyyy-MM-dd)</c>.</summary>
internal sealed record DoneEntry
{
    public DoneEntry(DateOnly date, string text, DateOnly? due, int line)
    {
        Date = date;
        Text = text;
        Due = due;
        Line = line;
    }

    /// <summary>
    /// Returns only the type name: the text is raw outside text, so formatting this record must never print it.
    /// Front ends draw it through their <c>Render</c> step.
    /// </summary>
    public override string ToString() => nameof(DoneEntry);

    /// <summary>The day the todo was done.</summary>
    public DateOnly Date { get; }

    /// <summary>The todo's text as written in the line, without the due part (raw: clean it before showing it).</summary>
    public string Text { get; }

    /// <summary>The todo's due date, when the line has one.</summary>
    public DateOnly? Due { get; }

    /// <summary>The 1-based line number in the note.</summary>
    public int Line { get; }
}

/// <summary>
/// The <c>done</c> note (<c>done.md</c>): each completed todo is appended to it as
/// <c>- yyyy-MM-dd &lt;text&gt;</c> plus <c> (due yyyy-MM-dd)</c> when it had a due date. It is an ordinary note the
/// user may edit; <see cref="Read(NoteStore)"/> takes only the lines that still match.
/// </summary>
internal static partial class DoneLog
{
    /// <summary>The note the log goes to (<c>done.md</c>, found in any case).</summary>
    public const string NoteName = "done";

    /// <summary>
    /// The log line for a todo done on <paramref name="day"/>: <c>- yyyy-MM-dd &lt;text&gt;</c> (the text cleaned to one
    /// line with <see cref="Inbox.Clean"/>, tags kept as written) plus <c> (due yyyy-MM-dd)</c> when
    /// <paramref name="due"/> is set. No final newline.
    /// </summary>
    public static string Line(DateOnly day, string text, DateOnly? due)
    {
        string line = $"- {Date(day)} {Inbox.Clean(text)}";
        return due is { } d ? $"{line} (due {Date(d)})" : line;
    }

    /// <summary>
    /// Appends <see cref="Line"/> plus <c>\n</c> to the done note, with the mechanics of the inbox capture
    /// (<see cref="Inbox.AppendLine"/>): a missing note is created as <c># done\n</c> plus the line, a missing final
    /// newline is added first, every other byte is kept and a change on disk is retried at most
    /// <see cref="Inbox.MaxRetries"/> times. Never throws.
    /// </summary>
    public static DoneLogResult Append(NoteStore store, DateOnly day, string text, DateOnly? due) =>
        Append(store, day, text, due, null);

    /// <summary>
    /// Like <see cref="Append(NoteStore, DateOnly, string, DateOnly?)"/>; <paramref name="beforeSave"/> runs after each
    /// read and before each save, so tests can change the note in between.
    /// </summary>
    internal static DoneLogResult Append(NoteStore store, DateOnly day, string text, DateOnly? due, Action? beforeSave)
    {
        try
        {
            return Inbox.AppendLine(store, NoteName, Line(day, text, due) + "\n", beforeSave) switch
            {
                CaptureResult.Captured => DoneLogResult.Appended,
                CaptureResult.TooLarge => DoneLogResult.TooLarge,
                _ => DoneLogResult.ChangedOnDisk,
            };
        }
        catch (Exception)
        {
            return DoneLogResult.Failed;
        }
    }

    /// <summary>
    /// The entries of the done note, in line order: an empty list when there is no plain done note. Throws
    /// <see cref="IOException"/> (also <see cref="FileTooLargeException"/>) or <see cref="UnauthorizedAccessException"/>
    /// when it can't be read.
    /// </summary>
    public static IReadOnlyList<DoneEntry> Read(NoteStore store)
    {
        if (!store.TryFind(NoteName, out NoteInfo? match) || match is null)
        {
            return [];
        }

        return Parse(store.Read(match));
    }

    /// <summary>
    /// The lines of <paramref name="markdown"/> outside fences (<see cref="TagParser.NumberedLinesOutsideFences"/>)
    /// that are <c>- yyyy-MM-dd &lt;text&gt;</c> with a real date, in line order. A trailing <c> (due yyyy-MM-dd)</c>
    /// with a real date becomes <see cref="DoneEntry.Due"/>. Every other line is ignored.
    /// </summary>
    public static IReadOnlyList<DoneEntry> Parse(string markdown)
    {
        List<DoneEntry> entries = [];
        foreach ((int number, string line) in TagParser.NumberedLinesOutsideFences(markdown))
        {
            Match entry = EntryLine().Match(line);
            if (!entry.Success || !TryDate(entry.Groups[1].Value, out DateOnly date))
            {
                continue;
            }

            string text = entry.Groups[2].Value.Trim();
            DateOnly? due = null;
            Match duePart = DuePart().Match(text);
            if (duePart.Success && TryDate(duePart.Groups[1].Value, out DateOnly dueDate)
                && text[..duePart.Index].Trim().Length > 0)
            {
                due = dueDate;
                text = text[..duePart.Index].Trim();
            }

            if (text.Length == 0)
            {
                continue;
            }

            entries.Add(new DoneEntry(date, text, due, number));
        }

        return entries;
    }

    private static string Date(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    [GeneratedRegex(@"^- ([0-9]{4}-[0-9]{2}-[0-9]{2}) (.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryLine();

    [GeneratedRegex(@" \(due ([0-9]{4}-[0-9]{2}-[0-9]{2})\)$", RegexOptions.CultureInvariant)]
    private static partial Regex DuePart();
}

/// <summary>One Monday-to-Sunday week of the done log.</summary>
internal sealed record DoneWeek
{
    public DoneWeek(DateOnly monday, IReadOnlyList<DoneEntry> entries)
    {
        Monday = monday;
        Entries = entries;
    }

    /// <summary>Returns only the type name, so the raw text never leaks through formatting.</summary>
    public override string ToString() => nameof(DoneWeek);

    /// <summary>The week's Monday.</summary>
    public DateOnly Monday { get; }

    /// <summary>The week's entries by date descending, then by line descending.</summary>
    public IReadOnlyList<DoneEntry> Entries { get; }
}

/// <summary>
/// The done log grouped by week, with no rendering: newest week first. All text in it is raw outside text: every
/// front end must clean it (<see cref="SafeText.Clean"/>) and escape it for its own output.
/// </summary>
internal sealed record DoneLogView
{
    public DoneLogView(IReadOnlyList<DoneWeek> weeks, bool unreadable)
    {
        Weeks = weeks;
        Unreadable = unreadable;
    }

    /// <summary>Returns only the type name, so the raw text never leaks through formatting.</summary>
    public override string ToString() => nameof(DoneLogView);

    /// <summary>The weeks with at least one entry, newest first.</summary>
    public IReadOnlyList<DoneWeek> Weeks { get; }

    /// <summary>True when the done note can't be read; then <see cref="Weeks"/> is empty.</summary>
    public bool Unreadable { get; }

    /// <summary>Every entry, week after week, in view order.</summary>
    public IEnumerable<DoneEntry> Entries => Weeks.SelectMany(w => w.Entries);

    /// <summary>Reads the done note (<see cref="DoneLog.Read"/>) and groups it. Never throws on an unreadable note.</summary>
    public static DoneLogView Build(NoteStore store)
    {
        IReadOnlyList<DoneEntry> entries;
        try
        {
            entries = DoneLog.Read(store);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new DoneLogView([], true);
        }

        return Group(entries);
    }

    /// <summary>Groups <paramref name="entries"/> into Monday-to-Sunday weeks, newest week first.</summary>
    public static DoneLogView Group(IReadOnlyList<DoneEntry> entries)
    {
        List<DoneWeek> weeks = entries
            .GroupBy(e => Monday(e.Date))
            .OrderByDescending(g => g.Key)
            .Select(g => new DoneWeek(
                g.Key,
                g.OrderByDescending(e => e.Date).ThenByDescending(e => e.Line).ToList()))
            .ToList();
        return new DoneLogView(weeks, false);
    }

    /// <summary>The Monday of the week <paramref name="day"/> is in (weeks run Monday to Sunday).</summary>
    public static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}
