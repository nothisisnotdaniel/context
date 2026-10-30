using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Context;

/// <summary>
/// The <c>/current</c> command: <see cref="CurrentView.Build"/> reads today's appointments, every todo due today
/// or earlier and the notes linked to them; <see cref="Render"/> draws that view with Spectre.
/// </summary>
internal static class CurrentCommands
{
    private const string DateFormat = "yyyy-MM-dd";

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
        Func<DateOnly> today,
        CalendarFeed? feed = null)
    {
        registry.Register(
            "/current",
            "Today's appointments, due todos and linked notes",
            _ => Render(CurrentView.Build(todos, index, notes, calendar, feed, today())));
    }

    /// <summary>
    /// Draws <paramref name="view"/>: the title, the appointments (at most <see cref="MaxAppointmentRows"/> rows),
    /// the due todos and the linked notes. Every text from the view is cleaned and escaped here.
    /// </summary>
    internal static CommandResult Render(CurrentView view)
    {
        List<IRenderable> parts =
        [
            new Markup($"[steelblue1]Today, {view.Today.ToString(DateFormat, CultureInfo.InvariantCulture)}[/]"),
        ];
        parts.AddRange(Appointments(view));

        if (view.TodosUnreadable)
        {
            return new CommandResult(new Rows(parts), hint: new Markup($"[red]{TodosUnreadable}[/]"));
        }

        if (view.Due.Count == 0)
        {
            parts.Add(new Markup("[grey]Nothing due today. Add one with /todo today <text>.[/]"));
            return new CommandResult(new Rows(parts));
        }

        Table dueTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[grey]Due[/]")
            .AddColumn("[grey]#[/]")
            .AddColumn("[grey]Date[/]")
            .AddColumn("[grey]Todo[/]");
        foreach (DueTodo todo in view.Due)
        {
            string colour = todo.Overdue ? "red" : "steelblue1";
            dueTable.AddRow(
                new Markup(todo.Number.ToString(CultureInfo.InvariantCulture)),
                new Markup($"[{colour}]{todo.Due.ToString(DateFormat, CultureInfo.InvariantCulture)}[/]"),
                new Markup(Markup.Escape(TodoSidebar.OneLine(todo.Text))));
        }

        parts.Add(dueTable);
        parts.Add(LinkedNotes(view));
        return new CommandResult(new Rows(parts));
    }

    private static IRenderable LinkedNotes(CurrentView view)
    {
        if (view.IndexUnreadable)
        {
            return new Markup($"[red]{IndexUnreadable}[/]");
        }

        if (view.LinkedNotes.Count == 0)
        {
            return new Markup("[grey]No linked notes. Add the same #tag to a todo and a note to link them.[/]");
        }

        Table table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[grey]Linked notes[/]")
            .AddColumn("[grey]Note[/]")
            .AddColumn("[grey]Tags[/]");
        foreach (LinkedNote note in view.LinkedNotes)
        {
            table.AddRow(
                new Markup($"[steelblue1]{Shown(note.Name)}[/]"),
                new Markup($"[grey]{Shown(string.Join(", ", note.Tags.Select(TagParser.Format)))}[/]"));
        }

        return table;
    }

    /// <summary>
    /// The <c>Appointments</c> table (or the empty line), then a red line per unreadable file, then the feed's
    /// status line (if any).
    /// </summary>
    private static List<IRenderable> Appointments(CurrentView view)
    {
        List<IRenderable> parts = [];
        IReadOnlyList<Appointment> appointments = view.Appointments;
        if (appointments.Count == 0)
        {
            parts.Add(new Markup(
                $"[grey]No appointments today. Put .ics files in {Shown(view.Folder)} to see them here.[/]"));
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
            for (int i = 0; i < shown; i++)
            {
                Appointment appointment = appointments[i];
                string title = appointment.Title.Length == 0 ? "(no title)" : appointment.Title;
                string where = appointment.Location is null
                    ? string.Empty
                    : $"[grey]{Markup.Escape(TodoSidebar.OneLine(appointment.Location))}[/]";
                table.AddRow(
                    new Markup($"[steelblue1]{view.TimeText(appointment)}[/]"),
                    new Markup(Markup.Escape(TodoSidebar.OneLine(title))),
                    new Markup(where));
            }

            if (more)
            {
                string count = (appointments.Count - shown).ToString(CultureInfo.InvariantCulture);
                table.AddRow(
                    new Markup(string.Empty),
                    new Markup($"[grey]{CurrentView.Outside} {count} more[/]"),
                    new Markup(string.Empty));
            }

            parts.Add(table);
        }

        foreach (string name in view.UnreadableCalendarFiles)
        {
            string file = Markup.Escape(TodoSidebar.OneLine(name));
            parts.Add(new Markup($"[red]Can't read {CalendarStore.FolderName}/{file}[/]"));
        }

        IRenderable? status = FeedStatus(view);
        if (status is not null)
        {
            parts.Add(status);
        }

        return parts;
    }

    /// <summary>A grey offline line for <c>Stale</c>, a red line for <c>Failed</c>, otherwise nothing.</summary>
    private static IRenderable? FeedStatus(CurrentView view)
    {
        if (view.Status == CalendarFeedStatus.Stale && view.CachedAtUtc is not null)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(view.CachedAtUtc.Value, DateTimeKind.Utc), view.Zone);
            string at = local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            return new Markup($"[grey]Exchange calendar offline, showing the copy from {at}.[/]");
        }

        if (view.Status == CalendarFeedStatus.Failed)
        {
            string reason = Shown(view.Reason ?? string.Empty);
            return new Markup($"[red]Can't download the Exchange calendar ({reason}).[/]");
        }

        return null;
    }

    // Text from disk or the user, safe for markup.
    private static string Shown(string text) => Markup.Escape(SafeText.Clean(text));
}
