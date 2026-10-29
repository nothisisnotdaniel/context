using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Context;

/// <summary>
/// The <c>/current</c> view: today's appointments (from <see cref="CalendarStore"/>), every todo due today or
/// earlier, and the notes linked to them. A todo links to every note that has one of the tags in its text
/// (parsed with <see cref="TagParser.Parse"/>).
/// </summary>
internal static class CurrentCommands
{
    private const string DateFormat = "yyyy-MM-dd";

    private const string TimeFormat = "HH:mm";

    private const string Outside = "…";

    /// <summary>At most this many appointment rows; the last reads <c>… N more</c> when there are more.</summary>
    internal const int MaxAppointmentRows = 15;

    private const string TodosUnreadable = "Can't read todos.json. Fix or move the file and try again.";

    private const string IndexUnreadable =
        "Can't read context.db. Move it out of the Context folder and it is rebuilt from your notes.";

    /// <summary>Adds <c>/current</c> to <paramref name="registry"/>.</summary>
    public static void Register(
        CommandRegistry registry,
        TodoStore todos,
        TagIndex index,
        NoteStore notes,
        CalendarStore calendar,
        Func<DateOnly> today)
    {
        registry.Register(
            "/current",
            "Today's appointments, due todos and linked notes",
            _ => Current(todos, index, notes, calendar, today()));
    }

    private static CommandResult Current(
        TodoStore store, TagIndex index, NoteStore notes, CalendarStore calendar, DateOnly today)
    {
        List<IRenderable> parts =
        [
            new Markup($"[steelblue1]Today, {today.ToString(DateFormat, CultureInfo.InvariantCulture)}[/]"),
        ];
        parts.AddRange(Appointments(calendar, today));

        IReadOnlyList<TodoItem> todos;
        try
        {
            todos = store.List();
        }
        catch (TodoStoreException)
        {
            return new CommandResult(new Rows(parts), hint: new Markup($"[red]{TodosUnreadable}[/]"));
        }

        List<TodoItem> due = [];
        Table dueTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[grey]Due[/]")
            .AddColumn("[grey]#[/]")
            .AddColumn("[grey]Date[/]")
            .AddColumn("[grey]Todo[/]");
        for (int i = 0; i < todos.Count; i++)
        {
            TodoItem todo = todos[i];
            if (todo.Due > today)
            {
                continue;
            }

            due.Add(todo);
            string colour = todo.Due < today ? "red" : "steelblue1";
            dueTable.AddRow(
                new Markup((i + 1).ToString(CultureInfo.InvariantCulture)),
                new Markup($"[{colour}]{todo.Due.ToString(DateFormat, CultureInfo.InvariantCulture)}[/]"),
                new Markup(Markup.Escape(TodoSidebar.OneLine(todo.Text))));
        }

        if (due.Count == 0)
        {
            parts.Add(new Markup("[grey]Nothing due today. Add one with /todo today <text>.[/]"));
            return new CommandResult(new Rows(parts));
        }

        parts.Add(dueTable);
        parts.Add(LinkedNotes(index, notes, due));
        return new CommandResult(new Rows(parts));
    }

    private static IRenderable LinkedNotes(TagIndex index, NoteStore notes, IReadOnlyList<TodoItem> due)
    {
        Dictionary<string, SortedSet<string>> linked = new(StringComparer.Ordinal);
        try
        {
            index.Sync(notes);
            IEnumerable<string> tags = due
                .SelectMany(t => TagParser.Parse(t.Text))
                .Distinct(StringComparer.Ordinal);
            foreach (string tag in tags)
            {
                foreach (string note in index.NotesWithTag(tag))
                {
                    if (!linked.TryGetValue(note, out SortedSet<string>? noteTags))
                    {
                        noteTags = new SortedSet<string>(StringComparer.Ordinal);
                        linked[note] = noteTags;
                    }

                    noteTags.Add(tag);
                }
            }
        }
        catch (TagIndexException)
        {
            return new Markup($"[red]{IndexUnreadable}[/]");
        }

        if (linked.Count == 0)
        {
            return new Markup("[grey]No linked notes. Add the same #tag to a todo and a note to link them.[/]");
        }

        Table table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[grey]Linked notes[/]")
            .AddColumn("[grey]Note[/]")
            .AddColumn("[grey]Tags[/]");
        IEnumerable<string> names = linked.Keys
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n, StringComparer.Ordinal);
        foreach (string name in names)
        {
            table.AddRow(
                new Markup($"[steelblue1]{Shown(name)}[/]"),
                new Markup($"[grey]{Shown(string.Join(", ", linked[name].Select(TagParser.Format)))}[/]"));
        }

        return table;
    }

    /// <summary>The <c>Appointments</c> table (or the empty line), then a red line per unreadable file.</summary>
    private static List<IRenderable> Appointments(CalendarStore calendar, DateOnly today)
    {
        CalendarDay day = calendar.Day(today);
        List<IRenderable> parts = [];
        IReadOnlyList<Appointment> appointments = day.Appointments;
        if (appointments.Count == 0)
        {
            parts.Add(new Markup(
                $"[grey]No appointments today. Put .ics files in {Shown(calendar.Folder)} to see them here.[/]"));
        }
        else
        {
            Table table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .Title("[grey]Appointments[/]")
                .AddColumn("[grey]Time[/]")
                .AddColumn("[grey]Appointment[/]")
                .AddColumn("[grey]Where[/]");
            bool more = appointments.Count > MaxAppointmentRows;
            int shown = more ? MaxAppointmentRows - 1 : appointments.Count;
            DateTime from = today.ToDateTime(TimeOnly.MinValue);
            DateTime to = from.AddDays(1);
            for (int i = 0; i < shown; i++)
            {
                Appointment appointment = appointments[i];
                string title = appointment.Title.Length == 0 ? "(no title)" : appointment.Title;
                string where = appointment.Location is null
                    ? string.Empty
                    : $"[grey]{Markup.Escape(TodoSidebar.OneLine(appointment.Location))}[/]";
                table.AddRow(
                    new Markup($"[steelblue1]{Time(appointment, from, to)}[/]"),
                    new Markup(Markup.Escape(TodoSidebar.OneLine(title))),
                    new Markup(where));
            }

            if (more)
            {
                string count = (appointments.Count - shown).ToString(CultureInfo.InvariantCulture);
                table.AddRow(
                    new Markup(string.Empty),
                    new Markup($"[grey]{Outside} {count} more[/]"),
                    new Markup(string.Empty));
            }

            parts.Add(table);
        }

        foreach (string name in day.Unreadable)
        {
            string file = Markup.Escape(TodoSidebar.OneLine(name));
            parts.Add(new Markup($"[red]Can't read {CalendarStore.FolderName}/{file}[/]"));
        }

        return parts;
    }

    /// <summary><c>all day</c>, or <c>HH:mm-HH:mm</c> with <c>…</c> for a side outside <c>[from, to]</c>.</summary>
    internal static string Time(Appointment appointment, DateTime from, DateTime to)
    {
        if (appointment.AllDay)
        {
            return "all day";
        }

        string start = appointment.Start < from
            ? Outside
            : appointment.Start.ToString(TimeFormat, CultureInfo.InvariantCulture);
        string end = appointment.End > to
            ? Outside
            : appointment.End.ToString(TimeFormat, CultureInfo.InvariantCulture);
        return $"{start}-{end}";
    }

    // Text from disk or the user, safe for markup.
    private static string Shown(string text) => Markup.Escape(SafeText.Clean(text));
}
