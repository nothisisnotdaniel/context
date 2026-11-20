using System.Globalization;
using System.Text;

namespace Context;

/// <summary>What <see cref="DailyNote.Ensure"/> found or did.</summary>
internal enum DailyNoteKind
{
    /// <summary>There was no daily note; it was created.</summary>
    Created,

    /// <summary>The plain daily note exists; nothing was written.</summary>
    Existing,

    /// <summary>The daily note is a sensitive (sealed) note; nothing was read or written.</summary>
    Sensitive,
}

/// <summary>The answer of <see cref="DailyNote.Ensure"/>: what happened and the note's name.</summary>
internal sealed record DailyNoteResult
{
    public DailyNoteResult(DailyNoteKind kind, string name)
    {
        Kind = kind;
        Name = name;
    }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(DailyNoteResult);

    public DailyNoteKind Kind { get; }

    /// <summary>The daily note's name (<c>yyyy-MM-dd</c>).</summary>
    public string Name { get; }
}

/// <summary>
/// The daily note: the top-level note <c>yyyy-MM-dd</c> for a day. When it is missing it is created with a title,
/// the day's appointments and the due todos; an existing one (plain or sensitive) is never read, changed or replaced.
/// </summary>
internal static class DailyNote
{
    private const string NameFormat = "yyyy-MM-dd";

    /// <summary>The daily note's name for <paramref name="day"/>: <c>yyyy-MM-dd</c>, in any culture.</summary>
    public static string NameFor(DateOnly day) => day.ToString(NameFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// The text of a new daily note: <c># &lt;name&gt;</c>, then <c>## Appointments</c> with one line per
    /// appointment (or <c>- No appointments.</c>) and one per calendar problem, then <c>## Todos</c> with one line per
    /// due todo (or <c>- Nothing due.</c>, or <c>- Can't read todos.json.</c>). Every inserted text is cleaned to one
    /// line, so it can't start a heading or a line of its own. <c>\n</c> line ends, ending in <c>\n</c>.
    /// </summary>
    public static string Format(
        DateOnly day,
        IReadOnlyList<Appointment> appointments,
        IReadOnlyList<string> unreadableCalendarFiles,
        CalendarFeedStatus status,
        string? reason,
        bool todosUnreadable,
        IReadOnlyList<DueTodo> due)
    {
        StringBuilder text = new StringBuilder();
        text.Append("# ").Append(NameFor(day)).Append('\n');
        AppendAppointments(text, day, appointments, unreadableCalendarFiles, status, reason);
        AppendTodos(text, todosUnreadable, due);
        return text.ToString();
    }

    /// <summary>
    /// The text of a new daily note for <paramref name="today"/>: the open todos due on or before it
    /// (<see cref="TodoStore.List"/> order), the day's appointments from the calendar files and the feed (fetched
    /// once, like <see cref="CurrentView.Build"/>) and their problems. Never touches <c>context.db</c>.
    /// </summary>
    public static string Compose(TodoStore todos, CalendarStore calendar, CalendarFeed? feed, DateOnly today)
    {
        StringBuilder text = new StringBuilder();
        text.Append("# ").Append(NameFor(today)).Append('\n');
        AppendSections(text, todos, calendar, feed, today);
        return text.ToString();
    }

    /// <summary>
    /// Appends the daily note's <c>## Appointments</c> and <c>## Todos</c> sections for <paramref name="today"/>
    /// (each after a blank line), read like <see cref="Compose"/>: the feed is fetched once and <c>context.db</c> is
    /// never opened. Shared with the morning briefing, so both hold the same bytes for the same data.
    /// </summary>
    internal static void AppendSections(
        StringBuilder text, TodoStore todos, CalendarStore calendar, CalendarFeed? feed, DateOnly today)
    {
        CalendarFeedResult result = CurrentView.Fetch(feed);
        CalendarDay day = calendar.Day(today, CurrentView.FeedEvents(result, calendar.Zone, today));

        IReadOnlyList<DueTodo> due = [];
        bool todosUnreadable = false;
        try
        {
            due = CurrentView.DueOn(todos.List(), today);
        }
        catch (TodoStoreException)
        {
            todosUnreadable = true;
        }

        AppendAppointments(text, today, day.Appointments, day.Unreadable, result.Status, result.Reason);
        AppendTodos(text, todosUnreadable, due);
    }

    /// <summary>
    /// Makes sure the daily note for <paramref name="today"/> exists. An existing plain note gives
    /// <see cref="DailyNoteKind.Existing"/> and a sensitive one <see cref="DailyNoteKind.Sensitive"/>, both without
    /// calling <paramref name="compose"/> or writing anything. Otherwise the top-level note is created with
    /// <paramref name="compose"/>'s text through <see cref="NoteStore.CreateWithText"/> (temp file and a move that
    /// never overwrites); a note that appeared in between counts as existing. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when the folder can't be read or written.
    /// </summary>
    public static DailyNoteResult Ensure(NoteStore notes, DateOnly today, Func<string> compose)
    {
        string name = NameFor(today);
        if (Existing(notes, name) is { } found)
        {
            return found;
        }

        try
        {
            NoteInfo created = notes.CreateWithText(name, compose());
            return new DailyNoteResult(DailyNoteKind.Created, created.Name);
        }
        catch (NoteExistsException)
        {
            return Existing(notes, name) ?? new DailyNoteResult(DailyNoteKind.Existing, name);
        }
    }

    // The existing daily note called name, plain first, or null when there is none.
    private static DailyNoteResult? Existing(NoteStore notes, string name)
    {
        if (notes.TryFind(name, out NoteInfo? plain) && plain is not null)
        {
            return new DailyNoteResult(DailyNoteKind.Existing, plain.Name);
        }

        if (notes.TryFindSensitive(name, out NoteInfo? sealedNote) && sealedNote is not null)
        {
            return new DailyNoteResult(DailyNoteKind.Sensitive, sealedNote.Name);
        }

        return null;
    }

    // The blank line and the ## Appointments section: one line per appointment (or "- No appointments."), then one per
    // unreadable calendar file and one for a failed feed.
    private static void AppendAppointments(
        StringBuilder text,
        DateOnly day,
        IReadOnlyList<Appointment> appointments,
        IReadOnlyList<string> unreadableCalendarFiles,
        CalendarFeedStatus status,
        string? reason)
    {
        text.Append("\n## Appointments\n");
        if (appointments.Count == 0)
        {
            text.Append("- No appointments.\n");
        }

        AppendAppointmentLines(text, day, appointments, unreadableCalendarFiles, status, reason);
    }

    /// <summary>
    /// The <c>## Appointments</c> lines without the heading and without <c>- No appointments.</c>: one per
    /// appointment, then one per unreadable calendar file and one for a failed feed. Shared with the standup.
    /// </summary>
    internal static void AppendAppointmentLines(
        StringBuilder text,
        DateOnly day,
        IReadOnlyList<Appointment> appointments,
        IReadOnlyList<string> unreadableCalendarFiles,
        CalendarFeedStatus status,
        string? reason)
    {
        DateTime from = day.ToDateTime(TimeOnly.MinValue);
        foreach (Appointment appointment in appointments)
        {
            string title = appointment.Title.Length == 0 ? "(no title)" : OneLine(appointment.Title);
            text.Append("- ").Append(CurrentView.Time(appointment, from, from.AddDays(1))).Append(' ').Append(title);
            if (!string.IsNullOrEmpty(appointment.Location))
            {
                text.Append(" (").Append(OneLine(appointment.Location)).Append(')');
            }

            text.Append('\n');
        }

        foreach (string file in unreadableCalendarFiles)
        {
            text.Append("- Can't read ").Append(CalendarStore.FolderName).Append('/').Append(OneLine(file)).Append(".\n");
        }

        if (status == CalendarFeedStatus.Failed)
        {
            text.Append("- Can't download the Exchange calendar (").Append(OneLine(reason ?? string.Empty)).Append(").\n");
        }
    }

    // The blank line and the ## Todos section: one line per due todo, "- Nothing due." or "- Can't read todos.json.".
    private static void AppendTodos(StringBuilder text, bool todosUnreadable, IReadOnlyList<DueTodo> due)
    {
        text.Append("\n## Todos\n");
        if (todosUnreadable)
        {
            text.Append("- Can't read todos.json.\n");
        }
        else if (due.Count == 0)
        {
            text.Append("- Nothing due.\n");
        }
        else
        {
            foreach (DueTodo todo in due)
            {
                AppendTodoLine(text, todo);
            }
        }
    }

    /// <summary>One <c>## Todos</c> line: <c>- &lt;text&gt;</c>, <c> (due yyyy-MM-dd)</c> when overdue. Shared with the standup.</summary>
    internal static void AppendTodoLine(StringBuilder text, DueTodo todo)
    {
        text.Append("- ").Append(OneLine(todo.Text));
        if (todo.Overdue)
        {
            text.Append(" (due ").Append(NameFor(todo.Due)).Append(')');
        }

        text.Append('\n');
    }

    private static string OneLine(string text) => SafeText.Clean(text).Replace('\n', ' ');
}
