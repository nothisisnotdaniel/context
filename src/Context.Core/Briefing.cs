using System.Globalization;
using System.Text;

namespace Context;

/// <summary>What <see cref="Briefing.Ensure"/> found or did.</summary>
internal enum BriefingKind
{
    /// <summary>There was no briefing for the day; it was created.</summary>
    Created,

    /// <summary>The day's briefing exists (also when another writer created it first); nothing was written.</summary>
    Existing,

    /// <summary>The top-level <c>briefings</c> note is sensitive; nothing was read or written.</summary>
    Sensitive,
}

/// <summary>The answer of <see cref="Briefing.Ensure"/>: what happened and the page's name.</summary>
internal sealed record BriefingResult
{
    public BriefingResult(BriefingKind kind, string name)
    {
        Kind = kind;
        Name = name;
    }

    public BriefingKind Kind { get; }

    /// <summary>The page's full name (<c>briefings/yyyy-MM-dd</c>).</summary>
    public string Name { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(BriefingResult);
}

/// <summary>
/// The morning briefing: the stored page <c>briefings/yyyy-MM-dd</c> with today's appointments and due todos (the
/// daily note's sections), the previous Times day's open checkpoints and the flexitime balance through yesterday. It
/// is written once per day and never refreshed. Times' folder is only ever read.
/// </summary>
internal static class Briefing
{
    /// <summary>The top-level note the briefings are subpages of.</summary>
    public const string FolderName = "briefings";

    /// <summary>How far back <see cref="Compose"/> looks for the previous Times day.</summary>
    public const int LookBackDays = 7;

    /// <summary>The page's name for <paramref name="day"/>: <c>briefings/yyyy-MM-dd</c>, in any culture.</summary>
    public static string NameFor(DateOnly day) => FolderName + "/" + DailyNote.NameFor(day);

    /// <summary>
    /// The briefing's text for <paramref name="today"/>: <c># yyyy-MM-dd</c>, the daily note's <c>## Appointments</c>
    /// and <c>## Todos</c> sections (<see cref="DailyNote.AppendSections"/>), <c>## Follow-ups</c>, <c>## To review</c>
    /// (job 256: the notes of <paramref name="reviews"/> due by <see cref="NoteReviews.Due"/>; <c>null</c> when the
    /// index can't be read), <c>## Open checkpoint</c> and <c>## Flexitime balance</c>, each after a blank line. Every
    /// Times name and note name is cleaned to one line. <c>\n</c> line ends, ending in <c>\n</c>. Never writes.
    /// </summary>
    public static string Compose(
        TodoStore todos,
        CalendarStore calendar,
        CalendarFeed? feed,
        string? timesFolder,
        DateOnly today,
        IReadOnlyList<NoteReview>? reviews)
    {
        StringBuilder text = new StringBuilder();
        text.Append("# ").Append(DailyNote.NameFor(today)).Append('\n');
        DailyNote.AppendSections(text, todos, calendar, feed, today);
        AppendFollowUps(text, todos, today);
        AppendReviews(text, reviews, today);
        AppendOpenCheckpoint(text, timesFolder, today);
        AppendBalance(text, TimesBalance.Compute(timesFolder, today.AddDays(-1)), today.AddDays(-1));
        return text.ToString();
    }

    /// <summary>
    /// True when the briefing should be written now: today's Times file has an arrival, or this is the first run
    /// (<paramref name="atStart"/>) on Monday to Friday and today is not an absence day.
    /// </summary>
    public static bool ShouldCreate(DateOnly today, TimesDay timesToday, bool atStart)
    {
        if (timesToday.Arrival is not null)
        {
            return true;
        }

        return atStart
            && today.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
            && timesToday.Kind != TimesDayKind.Absence;
    }

    /// <summary>
    /// Makes sure <c>briefings/&lt;day&gt;</c> exists. A sensitive top-level <c>briefings</c> gives
    /// <see cref="BriefingKind.Sensitive"/> with nothing read or written; an existing page gives
    /// <see cref="BriefingKind.Existing"/> without calling <paramref name="compose"/>. Otherwise the top-level
    /// <c>briefings</c> note (<c># briefings\n</c>) is created when it is missing, then the page through
    /// <see cref="NoteStore.CreateChild(string, string, string)"/> with <paramref name="compose"/>'s text; a page or
    /// <c>briefings</c> that appeared in between counts as existing. An existing page or <c>briefings</c> is never
    /// changed. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the folder can't be
    /// read or written.
    /// </summary>
    public static BriefingResult Ensure(NoteStore notes, DateOnly day, Func<string> compose)
    {
        string title = DailyNote.NameFor(day);
        string name = NameFor(day);
        if (notes.TryFindSensitive(FolderName, out _))
        {
            return new BriefingResult(BriefingKind.Sensitive, name);
        }

        if (!notes.TryFind(FolderName, out NoteInfo? folder) || folder is null)
        {
            try
            {
                folder = notes.Create(FolderName);
            }
            catch (NoteExistsException)
            {
                // Created by someone else between the check and the write.
                if (notes.TryFindSensitive(FolderName, out _))
                {
                    return new BriefingResult(BriefingKind.Sensitive, name);
                }

                if (!notes.TryFind(FolderName, out folder) || folder is null)
                {
                    throw new IOException("The briefings note can't be found.");
                }
            }
        }

        if (notes.TryFind(folder.Name + "/" + title, out NoteInfo? page) && page is not null)
        {
            return new BriefingResult(BriefingKind.Existing, page.Name);
        }

        string text = compose();
        string heading = "# " + title + "\n";
        string body = text.StartsWith(heading, StringComparison.Ordinal) ? text[heading.Length..] : text;
        try
        {
            NoteInfo created = notes.CreateChild(folder.Name, title, body);
            return new BriefingResult(BriefingKind.Created, created.Name);
        }
        catch (NoteExistsException)
        {
            return new BriefingResult(BriefingKind.Existing, folder.Name + "/" + title);
        }
    }

    // ## Follow-ups: the open waiting-for todos due today or earlier, in List() order; an overdue one with its date.
    private static void AppendFollowUps(StringBuilder text, TodoStore todos, DateOnly today)
    {
        text.Append("\n## Follow-ups\n");
        IReadOnlyList<TodoItem> open;
        try
        {
            open = todos.List();
        }
        catch (TodoStoreException)
        {
            text.Append("- Can't read todos.json.\n");
            return;
        }

        bool any = false;
        foreach (TodoItem todo in open)
        {
            if (todo.Due > today || WaitingFor.TryParse(todo.Text) is not { } item)
            {
                continue;
            }

            any = true;
            text.Append("- ").Append(OneLine(item.Person)).Append(": ").Append(OneLine(item.Rest));
            if (todo.Due < today)
            {
                text.Append(" (due ").Append(DailyNote.NameFor(todo.Due)).Append(')');
            }

            text.Append('\n');
        }

        if (!any)
        {
            text.Append("- No follow-ups due.\n");
        }
    }

    // ## To review: one link per note whose review date is today or earlier; an overdue one with its date.
    private static void AppendReviews(StringBuilder text, IReadOnlyList<NoteReview>? reviews, DateOnly today)
    {
        text.Append("\n## To review\n");
        if (reviews is null)
        {
            text.Append("- Can't read the review dates.\n");
            return;
        }

        IReadOnlyList<NoteReview> due = NoteReviews.Due(reviews, today);
        if (due.Count == 0)
        {
            text.Append("- Nothing to review.\n");
            return;
        }

        foreach (NoteReview review in due)
        {
            text.Append("- [[").Append(OneLine(review.Note)).Append("]]");
            if (review.Due < today)
            {
                text.Append(" (due ").Append(DailyNote.NameFor(review.Due)).Append(')');
            }

            text.Append('\n');
        }
    }

    // ## Open checkpoint: the open groups of the latest day from today-1 back to today-7 with at least one entry.
    private static void AppendOpenCheckpoint(StringBuilder text, string? timesFolder, DateOnly today)
    {
        text.Append("\n## Open checkpoint\n");
        for (int back = 1; back <= LookBackDays; back++)
        {
            DateOnly day = today.AddDays(-back);
            string date = DailyNote.NameFor(day);
            TimesDay record = TimesDay.Read(timesFolder, day);
            if (record.Kind == TimesDayKind.Unreadable)
            {
                text.Append("- Can't read Times' day file for ").Append(date).Append(".\n");
                return;
            }

            if (record.Kind != TimesDayKind.Entries || record.Entries.Count == 0)
            {
                continue;
            }

            List<TimesGroup> open = record.Groups.Where(g => g.Open).ToList();
            if (open.Count == 0)
            {
                text.Append("- None open on ").Append(date).Append(".\n");
                return;
            }

            foreach (TimesGroup group in open)
            {
                text.Append("- ").Append(date).Append(": ");
                if (group.WorkItem is { } item)
                {
                    text.Append('#').Append(item.ToString(CultureInfo.InvariantCulture)).Append(' ');
                }

                text.Append(string.Join(", ", group.Names.Select(OneLine))).Append('\n');
            }

            return;
        }

        text.Append("- No Times bookings in the last 7 days.\n");
    }

    // ## Flexitime balance: Times' signed balance through the day before today, and how many files were skipped.
    private static void AppendBalance(StringBuilder text, TimesBalance balance, DateOnly through)
    {
        text.Append("\n## Flexitime balance\n");
        if (balance.Kind == TimesBalanceKind.Unlistable)
        {
            text.Append("- Can't read Times' folder.\n");
            return;
        }

        if (balance.Kind == TimesBalanceKind.NoFolder || balance.Counted == 0)
        {
            text.Append("- No Times bookings.\n");
            return;
        }

        text.Append("- ").Append(FormatSigned(balance.Total)).Append(" through ").Append(DailyNote.NameFor(through))
            .Append(".\n");
        if (balance.Skipped > 0)
        {
            text.Append("- Skipped ").Append(balance.Skipped.ToString(CultureInfo.InvariantCulture))
                .Append(" Times day file(s) that can't be read.\n");
        }
    }

    /// <summary>A balance as <c>+H:mm</c> or <c>-H:mm</c> (whole minutes toward zero; zero is <c>+0:00</c>).</summary>
    internal static string FormatSigned(TimeSpan value)
    {
        long minutes = value.Ticks / TimeSpan.TicksPerMinute;
        long size = Math.Abs(minutes);
        string sign = minutes < 0 ? "-" : "+";
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{size / 60}:{size % 60:00}");
    }

    private static string OneLine(string text) => SafeText.Clean(text).Replace('\n', ' ');
}
