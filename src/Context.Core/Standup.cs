using System.Text;

namespace Context;

/// <summary>
/// The standup (job 265): plain text with three blocks for pasting into a chat, <c>Yesterday (yyyy-MM-dd)</c> (the
/// last workday's Times groups and the todos done since then), <c>Today</c> (today's appointments and due todos) and
/// <c>Blocked</c> (every open waiting-for todo). It only reads: Times' folder, <c>todos.json</c>, the calendar files and
/// the feed (fetched once, like the daily note). It never opens <c>context.db</c> or <c>settings.json</c>.
/// </summary>
internal static class Standup
{
    /// <summary>The line in place of the todo lines when <c>todos.json</c> can't be read.</summary>
    public const string TodosUnreadableLine = "- Can't read todos.json.\n";

    /// <summary>
    /// The standup for <paramref name="today"/>; see <see cref="Compose(TodoStore, CalendarStore, CalendarFeed?, string?, DateOnly, TimeZoneInfo, out bool)"/>.
    /// </summary>
    public static string Compose(
        TodoStore todos,
        CalendarStore calendar,
        CalendarFeed? feed,
        string? timesFolder,
        DateOnly today,
        TimeZoneInfo zone) =>
        Compose(todos, calendar, feed, timesFolder, today, zone, out _);

    /// <summary>
    /// The standup for <paramref name="today"/>: the blocks <c>Yesterday (yyyy-MM-dd)</c>, <c>Today</c> and
    /// <c>Blocked</c>, each a label line and <c>- </c> lines, with a blank line between blocks. Done times are read in
    /// <paramref name="zone"/>. Every inserted text is cleaned to one line. <c>\n</c> line ends, ending in <c>\n</c>.
    /// <paramref name="todosUnreadable"/> is true when <c>todos.json</c> can't be read (its lines then say so).
    /// </summary>
    public static string Compose(
        TodoStore todos,
        CalendarStore calendar,
        CalendarFeed? feed,
        string? timesFolder,
        DateOnly today,
        TimeZoneInfo zone,
        out bool todosUnreadable)
    {
        IReadOnlyList<TodoItem> open = [];
        IReadOnlyList<TodoItem> done = [];
        todosUnreadable = false;
        try
        {
            open = todos.List();
            done = todos.ListDone();
        }
        catch (TodoStoreException)
        {
            todosUnreadable = true;
        }

        StringBuilder text = new StringBuilder();
        AppendYesterday(text, timesFolder, today, zone, todosUnreadable, done);
        text.Append('\n');
        AppendToday(text, calendar, feed, today, todosUnreadable, open);
        text.Append('\n');
        AppendBlocked(text, todosUnreadable, open);
        return text.ToString();
    }

    /// <summary>
    /// The last workday before <paramref name="today"/> and its Times file: the latest day from today-1 back to
    /// today-<see cref="Briefing.LookBackDays"/> whose file has entries, or an unreadable file met first. With none,
    /// the latest Monday to Friday before today and a <c>null</c> record.
    /// </summary>
    internal static (DateOnly Day, TimesDay? Record) LastWorkday(string? timesFolder, DateOnly today)
    {
        for (int back = 1; back <= Briefing.LookBackDays; back++)
        {
            DateOnly day = today.AddDays(-back);
            TimesDay record = TimesDay.Read(timesFolder, day);
            if (record.Kind == TimesDayKind.Unreadable
                || (record.Kind == TimesDayKind.Entries && record.Entries.Count > 0))
            {
                return (day, record);
            }
        }

        DateOnly weekday = today.AddDays(-1);
        while (weekday.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            weekday = weekday.AddDays(-1);
        }

        return (weekday, null);
    }

    // Yesterday (yyyy-MM-dd): the last workday's groups (or why there are none), then the todos done from that day up
    // to the day before today, oldest first.
    private static void AppendYesterday(
        StringBuilder text,
        string? timesFolder,
        DateOnly today,
        TimeZoneInfo zone,
        bool todosUnreadable,
        IReadOnlyList<TodoItem> done)
    {
        (DateOnly day, TimesDay? record) = LastWorkday(timesFolder, today);
        string date = DailyNote.NameFor(day);
        text.Append("Yesterday (").Append(date).Append(")\n");
        int start = text.Length;
        if (record is null)
        {
            text.Append("- No Times bookings in the last ").Append(Briefing.LookBackDays).Append(" days.\n");
        }
        else if (record.Kind == TimesDayKind.Unreadable)
        {
            text.Append("- Can't read Times' day file for ").Append(date).Append(".\n");
        }
        else
        {
            foreach (TimesGroup group in record.Groups)
            {
                text.Append(DayEnd.FormatGroup(group));
            }
        }

        if (todosUnreadable)
        {
            text.Append(TodosUnreadableLine);
        }
        else
        {
            // ListDone is latest first; a stable sort by the done time gives oldest first and keeps its tie order.
            foreach (TodoItem todo in done.OrderBy(t => t.Done))
            {
                DateOnly local = LocalDate(todo.Done!.Value, zone);
                if (local >= day && local < today)
                {
                    text.Append("- Done: ").Append(OneLine(todo.Text)).Append('\n');
                }
            }
        }

        if (text.Length == start)
        {
            text.Append("- Nothing logged.\n");
        }
    }

    // Today: the daily note's appointment lines (without "- No appointments."), then the open todos due on or before
    // today that are not waiting-for todos.
    private static void AppendToday(
        StringBuilder text,
        CalendarStore calendar,
        CalendarFeed? feed,
        DateOnly today,
        bool todosUnreadable,
        IReadOnlyList<TodoItem> open)
    {
        text.Append("Today\n");
        int start = text.Length;
        CalendarFeedResult result = CurrentView.Fetch(feed);
        CalendarDay day = calendar.Day(today, CurrentView.FeedEvents(result, calendar.Zone, today));
        DailyNote.AppendAppointmentLines(text, today, day.Appointments, day.Unreadable, result.Status, result.Reason);
        if (todosUnreadable)
        {
            text.Append(TodosUnreadableLine);
        }
        else
        {
            List<TodoItem> plain = open.Where(t => WaitingFor.TryParse(t.Text) is null).ToList();
            foreach (DueTodo todo in CurrentView.DueOn(plain, today))
            {
                DailyNote.AppendTodoLine(text, todo);
            }
        }

        if (text.Length == start)
        {
            text.Append("- Nothing planned.\n");
        }
    }

    // Blocked: every open waiting-for todo, whatever its due date, in List() order, with its follow-up date.
    private static void AppendBlocked(StringBuilder text, bool todosUnreadable, IReadOnlyList<TodoItem> open)
    {
        text.Append("Blocked\n");
        if (todosUnreadable)
        {
            text.Append(TodosUnreadableLine);
            return;
        }

        bool any = false;
        foreach (TodoItem todo in open)
        {
            if (WaitingFor.TryParse(todo.Text) is not { } item)
            {
                continue;
            }

            any = true;
            text.Append("- ").Append(OneLine(item.Person));
            string rest = OneLine(item.Rest);
            text.Append(rest.Length == 0 ? string.Empty : ": " + rest);
            text.Append(" (follow up ").Append(DailyNote.NameFor(todo.Due)).Append(")\n");
        }

        if (!any)
        {
            text.Append("- Nothing blocked.\n");
        }
    }

    // The local date of a stored UTC time in zone.
    private static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));

    private static string OneLine(string text) => SafeText.Clean(text).Replace('\n', ' ');
}
