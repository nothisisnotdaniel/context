using System.Globalization;
using System.Text;

namespace Context;

/// <summary>How a <see cref="DayEnd"/> run ended.</summary>
internal enum DayEndKind
{
    /// <summary>Everything that was due was written (possibly nothing).</summary>
    Done,

    /// <summary>A daily note it needed is sensitive (<see cref="DayEndResult.Name"/>); it was neither read nor written.</summary>
    Sensitive,

    /// <summary><c>todos.json</c> can't be read; no todos moved.</summary>
    TodosUnreadable,

    /// <summary>The file in <see cref="DayEndResult.Name"/> (a note or <c>todos.json</c>) couldn't be written.</summary>
    WriteFailed,

    /// <summary><c>settings.json</c> can't be read or written; the automatic run did nothing more.</summary>
    SettingsUnreadable,
}

/// <summary>
/// The answer of a <see cref="DayEnd"/> run: how it ended, the name it concerns (the day's note, or the note or file
/// that failed), the todos it moved and where to, and whether anything was written.
/// </summary>
internal sealed record DayEndResult
{
    public DayEndResult(DayEndKind kind, string name, IReadOnlyList<TodoItem> moved, DateOnly to, bool changed)
    {
        Kind = kind;
        Name = name;
        Moved = moved;
        To = to;
        Changed = changed;
    }

    public DayEndKind Kind { get; }

    /// <summary>The closed day's note name when done, else the note (or file) the problem is about; raw.</summary>
    public string Name { get; }

    /// <summary>The moved todos (with their new due date), in <see cref="TodoStore.List"/> order.</summary>
    public IReadOnlyList<TodoItem> Moved { get; }

    /// <summary>The day the todos moved to (the closed day plus one).</summary>
    public DateOnly To { get; }

    /// <summary>True when a note, <c>todos.json</c> or <c>settings.json</c> was written.</summary>
    public bool Changed { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(DayEndResult);
}

/// <summary>
/// Closes a day in its daily note: <see cref="RefreshWorked"/> writes a <c>## Worked</c> section from Times' day file,
/// <see cref="MoveTodos"/> moves the open todos due on or before the day to the next day and lists them in both
/// daily notes, <see cref="CloseDay"/> does both, and <see cref="AutoClose"/> is the automatic trigger, recorded in
/// <c>settings.json</c>. Times' folder is only ever read. A sensitive daily note is never read or written.
/// </summary>
internal sealed class DayEnd
{
    /// <summary>A leaving at or after this time closes the day and moves its todos automatically.</summary>
    public static readonly TimeOnly EveningCutoff = new(17, 0);

    /// <summary>The heading of the worked section.</summary>
    public const string WorkedHeading = "## Worked";

    /// <summary>The heading of the moved list in the closed day's note.</summary>
    public const string MovedToHeading = "## Moved to tomorrow";

    /// <summary>How many times a note write re-reads and retries when the note changed on disk in between.</summary>
    public const int MaxRetries = 3;

    private readonly NoteStore notes;
    private readonly TodoStore todos;
    private readonly CalendarStore calendar;
    private readonly CalendarFeed? feed;
    private readonly string? timesFolder;
    private readonly SettingsStore? settings;

    public DayEnd(
        NoteStore notes,
        TodoStore todos,
        CalendarStore calendar,
        CalendarFeed? feed,
        string? timesFolder,
        SettingsStore? settings)
    {
        this.notes = notes;
        this.todos = todos;
        this.calendar = calendar;
        this.feed = feed;
        this.timesFolder = timesFolder;
        this.settings = settings;
    }

    /// <summary>Runs after each note read and before each save (tests change the note in between).</summary>
    internal Action? BeforeNoteSave { get; set; }

    /// <summary>The heading of the moved list in the next day's note: <c>## Moved from yyyy-MM-dd</c>.</summary>
    public static string MovedFromHeading(DateOnly day) => "## Moved from " + DailyNote.NameFor(day);

    /// <summary>A duration as <c>H:mm</c> (hours not capped at 24).</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        long minutes = (long)Math.Floor(duration.TotalMinutes);
        return string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}:{minutes % 60:00}");
    }

    /// <summary>
    /// The worked section for <paramref name="day"/>: <c>## Worked</c>, then <c>- Arrived HH:mm, left HH:mm, worked
    /// H:mm</c> and one line per checkpoint group (<c>- #n a, b: H:mm</c> or <c>- name: H:mm</c>, <c> (open)</c> for an
    /// open group), or exactly one line saying why there are none. A missing arrival shows as <c>--:--</c>. Every name
    /// is cleaned to one line. <c>\n</c> line ends, ending in <c>\n</c>.
    /// </summary>
    public static string FormatWorked(TimesDay day)
    {
        StringBuilder text = new StringBuilder();
        text.Append(WorkedHeading).Append('\n');
        switch (day.Kind)
        {
            case TimesDayKind.Missing:
                text.Append("- No Times bookings for this day.\n");
                return text.ToString();
            case TimesDayKind.Unreadable:
                text.Append("- Can't read Times' day file.\n");
                return text.ToString();
            case TimesDayKind.Absence:
                text.Append("- Absent (").Append(AbsenceText(day.Absence)).Append(").\n");
                return text.ToString();
            default:
                break;
        }

        if (!day.HasLeft || day.Leaving is not { } leaving)
        {
            text.Append("- No leaving booked in Times yet.\n");
            return text.ToString();
        }

        string arrival = day.Arrival is { } a ? Time(a) : "--:--";
        text.Append("- Arrived ").Append(arrival).Append(", left ").Append(Time(leaving))
            .Append(", worked ").Append(FormatDuration(day.WorkTime)).Append('\n');
        foreach (TimesGroup group in day.Groups)
        {
            text.Append("- ");
            string names = string.Join(", ", group.Names.Select(OneLine));
            if (group.WorkItem is { } item)
            {
                text.Append('#').Append(item.ToString(CultureInfo.InvariantCulture)).Append(' ');
            }

            text.Append(names).Append(": ").Append(FormatDuration(group.Total));
            if (group.Open)
            {
                text.Append(" (open)");
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Writes the <c>## Worked</c> section of <paramref name="day"/>'s daily note from Times' day file, creating the
    /// note first (<see cref="DailyNote.Ensure"/> with the usual text) when it is missing. An existing section (a line
    /// that is exactly <c>## Worked</c>, up to the next <c>## </c> line) is replaced, else one is appended after a
    /// blank line; the rest of the note is unchanged. A sensitive note gives <see cref="DayEndKind.Sensitive"/> with
    /// nothing read (not even Times' file). Never throws.
    /// </summary>
    public DayEndResult RefreshWorked(DateOnly day)
    {
        string name = DailyNote.NameFor(day);
        DateOnly to = day.AddDays(1);
        if (IsSensitive(day))
        {
            return new DayEndResult(DayEndKind.Sensitive, name, [], to, false);
        }

        string section = FormatWorked(TimesDay.Read(timesFolder, day));
        NoteEdit edit = Edit(day, text => ReplaceSection(text, WorkedHeading, section));
        return new DayEndResult(edit.Kind, name, [], to, edit.Changed);
    }

    /// <summary>
    /// Moves the open todos due on or before <paramref name="day"/> to the next day (<see cref="TodoStore.MoveDue"/>)
    /// and lists each as <c>- &lt;text&gt;</c> under <c>## Moved to tomorrow</c> in the day's note (at the section's
    /// end; a new section goes after <c>## Worked</c>, or at the end) and under <c>## Moved from yyyy-MM-dd</c> in the
    /// next day's note (created first when missing, after the move). When either note is sensitive nothing moves and
    /// nothing is written; when nothing is due, nothing is written. Never throws.
    /// </summary>
    public DayEndResult MoveTodos(DateOnly day)
    {
        string name = DailyNote.NameFor(day);
        DateOnly to = day.AddDays(1);
        if (IsSensitive(day))
        {
            return new DayEndResult(DayEndKind.Sensitive, name, [], to, false);
        }

        if (IsSensitive(to))
        {
            return new DayEndResult(DayEndKind.Sensitive, DailyNote.NameFor(to), [], to, false);
        }

        IReadOnlyList<TodoItem> moved;
        try
        {
            moved = todos.MoveDue(day, to);
        }
        catch (TodoStoreException)
        {
            return new DayEndResult(DayEndKind.TodosUnreadable, TodoStore.FileName, [], to, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DayEndResult(DayEndKind.WriteFailed, TodoStore.FileName, [], to, false);
        }

        if (moved.Count == 0)
        {
            return new DayEndResult(DayEndKind.Done, name, [], to, false);
        }

        string lines = string.Concat(moved.Select(t => "- " + OneLine(t.Text) + "\n"));
        NoteEdit here = Edit(day, text => AddToday(text, lines));
        NoteEdit there = Edit(to, text => AppendToSection(text, MovedFromHeading(day), lines));
        if (here.Kind != DayEndKind.Done)
        {
            return new DayEndResult(here.Kind, name, moved, to, true);
        }

        if (there.Kind != DayEndKind.Done)
        {
            return new DayEndResult(there.Kind, DailyNote.NameFor(to), moved, to, true);
        }

        return new DayEndResult(DayEndKind.Done, name, moved, to, true);
    }

    /// <summary>
    /// <see cref="RefreshWorked"/>, then <see cref="MoveTodos"/> (not after a sensitive or failed Worked write); the
    /// first problem is the answer. Never reads or writes <c>settings.json</c>. Never throws.
    /// </summary>
    public DayEndResult CloseDay(DateOnly day)
    {
        DayEndResult worked = RefreshWorked(day);
        if (worked.Kind != DayEndKind.Done)
        {
            return worked;
        }

        DayEndResult moved = MoveTodos(day);
        return new DayEndResult(
            moved.Kind, moved.Kind == DayEndKind.Done ? worked.Name : moved.Name, moved.Moved, moved.To,
            worked.Changed || moved.Changed);
    }

    /// <summary>
    /// The automatic day's end at <paramref name="now"/> (local) on <paramref name="today"/>, recorded in
    /// <c>settings.json</c> before each action:
    /// <list type="number">
    /// <item>Catch-up: when yesterday's Times day has left and <c>dayEnd.moved</c> is neither yesterday nor today (and
    /// neither note is sensitive), it records <c>moved</c> = yesterday, refreshes yesterday's Worked and moves its
    /// todos to today.</item>
    /// <item>When today has left (the last leaving not later than <paramref name="now"/>) and that leaving differs
    /// from <c>dayEnd.worked</c>, it records it and refreshes today's Worked.</item>
    /// <item>When that leaving is at or after <see cref="EveningCutoff"/>, <c>dayEnd.moved</c> is not today and
    /// tomorrow's note is not sensitive, it records <c>moved</c> = today and moves today's todos.</item>
    /// </list>
    /// A sensitive today reads nothing. A broken <c>settings.json</c> stops everything and is never overwritten.
    /// Days before yesterday are never closed. Never throws.
    /// </summary>
    public DayEndResult AutoClose(DateOnly today, DateTime now)
    {
        string name = DailyNote.NameFor(today);
        DateOnly tomorrow = today.AddDays(1);
        DayEndResult nothing = new DayEndResult(DayEndKind.Done, name, [], tomorrow, false);
        if (settings is null || IsSensitive(today))
        {
            return nothing;
        }

        DayEndSettings recorded;
        try
        {
            recorded = settings.ReadDayEnd();
        }
        catch (SettingsStoreException)
        {
            return new DayEndResult(DayEndKind.SettingsUnreadable, SettingsStore.FileName, [], tomorrow, false);
        }

        List<DayEndResult> results = [];
        DateOnly yesterday = today.AddDays(-1);
        if (recorded.Moved != yesterday && recorded.Moved != today && !IsSensitive(yesterday)
            && TimesDay.Read(timesFolder, yesterday).HasLeft)
        {
            recorded = new DayEndSettings(recorded.Worked, yesterday);
            if (!Record(recorded))
            {
                return Stopped(results, tomorrow);
            }

            results.Add(RefreshWorked(yesterday));
            results.Add(MoveTodos(yesterday));
        }

        TimesDay day = TimesDay.Read(timesFolder, today);
        DateOnly nowDay = DateOnly.FromDateTime(now);
        if (day.HasLeft && day.Leaving is { } leaving
            && (nowDay > today || (nowDay == today && leaving <= TimeOnly.FromDateTime(now))))
        {
            string stamp = SettingsStore.Stamp(today, leaving);
            if (stamp != recorded.Worked)
            {
                recorded = new DayEndSettings(stamp, recorded.Moved);
                if (!Record(recorded))
                {
                    return Stopped(results, tomorrow);
                }

                results.Add(RefreshWorked(today));
            }

            if (leaving >= EveningCutoff && recorded.Moved != today && !IsSensitive(tomorrow))
            {
                recorded = new DayEndSettings(recorded.Worked, today);
                if (!Record(recorded))
                {
                    return Stopped(results, tomorrow);
                }

                results.Add(MoveTodos(today));
            }
        }

        return Combine(results, name, tomorrow, changedSettings: results.Count > 0);
    }

    private static DayEndResult Stopped(List<DayEndResult> results, DateOnly tomorrow)
    {
        bool changed = results.Count > 0;
        return new DayEndResult(
            DayEndKind.SettingsUnreadable, SettingsStore.FileName, results.SelectMany(r => r.Moved).ToList(),
            tomorrow, changed);
    }

    private static DayEndResult Combine(List<DayEndResult> results, string name, DateOnly tomorrow, bool changedSettings)
    {
        DayEndResult? problem = results.FirstOrDefault(r => r.Kind != DayEndKind.Done);
        List<TodoItem> moved = results.SelectMany(r => r.Moved).ToList();
        bool changed = changedSettings || results.Any(r => r.Changed);
        return problem is null
            ? new DayEndResult(DayEndKind.Done, name, moved, tomorrow, changed)
            : new DayEndResult(problem.Kind, problem.Name, moved, tomorrow, changed);
    }

    // Writes the day's end record; false when settings.json is broken or can't be written.
    private bool Record(DayEndSettings values)
    {
        try
        {
            settings!.SaveDayEnd(values);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // True when the day's note is sensitive: no plain note of that name, but a sealed one.
    private bool IsSensitive(DateOnly day)
    {
        string name = DailyNote.NameFor(day);
        return !(notes.TryFind(name, out NoteInfo? plain) && plain is not null)
            && notes.TryFindSensitive(name, out NoteInfo? sealedNote) && sealedNote is not null;
    }

    // Ensures the day's note, then applies change through ReadForEdit and SaveIfUnchanged, retrying on a change on disk.
    private NoteEdit Edit(DateOnly day, Func<string, string> change)
    {
        bool changed = false;
        try
        {
            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                DailyNoteResult ensured = DailyNote.Ensure(
                    notes, day, () => DailyNote.Compose(todos, calendar, feed, day));
                if (ensured.Kind == DailyNoteKind.Sensitive)
                {
                    return new NoteEdit(DayEndKind.Sensitive, changed);
                }

                changed |= ensured.Kind == DailyNoteKind.Created;
                if (!notes.TryFind(ensured.Name, out NoteInfo? note) || note is null)
                {
                    continue;
                }

                EditableNote opened = notes.ReadForEdit(note);
                string text = change(opened.Text);
                if (text == opened.Text)
                {
                    return new NoteEdit(DayEndKind.Done, changed);
                }

                BeforeNoteSave?.Invoke();
                switch (notes.SaveIfUnchanged(opened, text, out _))
                {
                    case NoteSaveResult.Saved:
                        return new NoteEdit(DayEndKind.Done, true);
                    case NoteSaveResult.TooLarge:
                        return new NoteEdit(DayEndKind.WriteFailed, changed);
                    default:
                        // ChangedOnDisk or Missing: read again.
                        continue;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new NoteEdit(DayEndKind.WriteFailed, changed);
        }

        return new NoteEdit(DayEndKind.WriteFailed, changed);
    }

    /// <summary>
    /// <paramref name="text"/> with the section under <paramref name="heading"/> (the line that is exactly it, up to
    /// the next <c>## </c> line or the end) replaced by <paramref name="section"/>, keeping a blank line before a
    /// following heading; with no such line, the section appended after a blank line.
    /// </summary>
    internal static string ReplaceSection(string text, string heading, string section)
    {
        int start = FindHeading(text, heading);
        if (start < 0)
        {
            return Append(text, section);
        }

        int end = SectionEnd(text, start);
        string replacement = end < text.Length ? section + "\n" : section;
        return string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(end));
    }

    /// <summary>
    /// <paramref name="text"/> with <paramref name="lines"/> added after the last non-blank line of the section under
    /// <paramref name="heading"/>; with no such section, <c>heading</c> and the lines appended after a blank line.
    /// </summary>
    internal static string AppendToSection(string text, string heading, string lines)
    {
        int start = FindHeading(text, heading);
        if (start < 0)
        {
            return Append(text, heading + "\n" + lines);
        }

        int at = ContentEnd(text, start);
        string prefix = at > 0 && text[at - 1] != '\n' ? "\n" : string.Empty;
        return string.Concat(text.AsSpan(0, at), prefix + lines, text.AsSpan(at));
    }

    // The closed day's moved list: at the end of an existing one, else a new section after Worked, else at the end.
    private static string AddToday(string text, string lines)
    {
        if (FindHeading(text, MovedToHeading) >= 0)
        {
            return AppendToSection(text, MovedToHeading, lines);
        }

        int worked = FindHeading(text, WorkedHeading);
        if (worked < 0)
        {
            return Append(text, MovedToHeading + "\n" + lines);
        }

        int at = ContentEnd(text, worked);
        string prefix = at > 0 && text[at - 1] != '\n' ? "\n\n" : "\n";
        string suffix = at < text.Length && text[at] != '\n' ? "\n" : string.Empty;
        return string.Concat(text.AsSpan(0, at), prefix + MovedToHeading + "\n" + lines + suffix, text.AsSpan(at));
    }

    // text, a final newline when missing, a blank line unless it already ends with one, then block.
    private static string Append(string text, string block)
    {
        if (text.Length == 0)
        {
            return block;
        }

        string body = text.EndsWith('\n') ? text : text + "\n";
        return body.EndsWith("\n\n", StringComparison.Ordinal) || body == "\n" ? body + block : body + "\n" + block;
    }

    // The index of the line that is exactly heading, or -1.
    private static int FindHeading(string text, string heading)
    {
        int start = 0;
        while (start <= text.Length)
        {
            int end = text.IndexOf('\n', start);
            int lineEnd = end < 0 ? text.Length : end;
            if (text.AsSpan(start, lineEnd - start).SequenceEqual(heading))
            {
                return start;
            }

            if (end < 0)
            {
                return -1;
            }

            start = end + 1;
        }

        return -1;
    }

    // The start of the first line after the heading line at start that begins with "## ", or the text's length.
    private static int SectionEnd(string text, int start)
    {
        int next = NextLine(text, start);
        while (next < text.Length)
        {
            if (text.AsSpan(next).StartsWith("## ", StringComparison.Ordinal))
            {
                return next;
            }

            next = NextLine(text, next);
        }

        return text.Length;
    }

    // Just after the last non-blank line of the section whose heading starts at start (the heading line itself when
    // the section is empty).
    private static int ContentEnd(string text, int start)
    {
        int end = SectionEnd(text, start);
        int at = NextLine(text, start);
        int line = at;
        while (line < end)
        {
            int next = NextLine(text, line);
            if (!text.AsSpan(line, next - line).IsWhiteSpace())
            {
                at = next;
            }

            line = next;
        }

        return Math.Min(at, text.Length);
    }

    // The start of the line after the one starting at index (just past its '\n'), or the text's length.
    private static int NextLine(string text, int index)
    {
        int end = text.IndexOf('\n', index);
        return end < 0 ? text.Length : end + 1;
    }

    private static string AbsenceText(TimesAbsence? absence) => absence switch
    {
        TimesAbsence.Vacation => "vacation",
        TimesAbsence.Sick => "sick",
        _ => "public holiday",
    };

    private static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string OneLine(string text) => SafeText.Clean(text).Replace('\n', ' ');

    private readonly record struct NoteEdit
    {
        public NoteEdit(DayEndKind kind, bool changed)
        {
            Kind = kind;
            Changed = changed;
        }

        public DayEndKind Kind { get; }

        public bool Changed { get; }
    }
}
