using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 280 AC-11: the work item page's panel: notes, todos, people, meetings, hours and commits that mention #1234.
public sealed class ItemPanelTests : IDisposable
{
    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    // The load runs DesktopState.Item on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string Repo(string name)
    {
        string folder = Path.Combine(data.Root, "..", "repos", name);
        Directory.CreateDirectory(folder);
        return Path.GetFullPath(folder);
    }

    private void SetRepositories(params string[] folders) => new SettingsStore(data.Root).SaveRepositories(folders);

    // A day with `minutes` booked on `item` from 08:00, then 30 minutes on #99.
    private void WriteBooking(DateOnly day, int item, int minutes)
    {
        Directory.CreateDirectory(data.TimesFolder);
        string end = new TimeOnly(8, 0).AddMinutes(minutes).ToString("HH:mm", CultureInfo.InvariantCulture);
        string after = new TimeOnly(8, 0).AddMinutes(minutes + 30).ToString("HH:mm", CultureInfo.InvariantCulture);
        File.WriteAllText(
            Path.Combine(data.TimesFolder, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json"),
            "{\"entries\":["
            + "{\"kind\":\"arrival\",\"time\":\"07:55\"},"
            + $"{{\"kind\":\"checkpoint\",\"time\":\"08:00\",\"name\":\"Work\",\"workItem\":{item}}},"
            + $"{{\"kind\":\"checkpoint\",\"time\":\"{end}\",\"name\":\"Other\",\"workItem\":99}},"
            + $"{{\"kind\":\"leaving\",\"time\":\"{after}\"}}]}}");
    }

    private IRenderedComponent<ItemPanel> Render(int item, List<string>? opened = null, List<string>? people = null) =>
        data.Bunit.Render<ItemPanel>(p => p
            .Add(c => c.Item, item)
            .Add(c => c.OnOpen, (string name) => opened?.Add(name))
            .Add(c => c.OnPerson, (string name) => people?.Add(name)));

    private static void WaitLoaded(IRenderedComponent<ItemPanel> cut) =>
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal), Wait);

    private static string[] Headings(IRenderedComponent<ItemPanel> cut) =>
        cut.FindAll("h2").Select(h => h.TextContent).ToArray();

    private static string[] Lines(IRenderedComponent<ItemPanel> cut, string cls) =>
        cut.FindAll("p." + cls + "[role=status]").Select(p => p.TextContent).ToArray();

    private static string Row(IElement li) => string.Join("|", li.Children.Select(c => c.TextContent));

    // AC-11: every section with rows shows, in order; buttons report the note or the person; nothing is written but
    // context.db.
    [Fact]
    public async Task Render_AllSections_ButtonsReport()
    {
        data.WriteNote("Plan", "#1234 with @[Anna]\n");
        data.WriteNote("Review", "#1234\n");
        data.WriteNote("Other", "#12345\n");
        data.AddTodo("open #1234", new DateOnly(2026, 10, 30));
        data.WriteCalendar("work.ics",
            "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Sync #1234\r\nLOCATION:Room 1\r\n"
            + "DTSTART:20261028T100000\r\nDTEND:20261028T110000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
        WriteBooking(new DateOnly(2026, 10, 26), 1234, 90);
        WriteBooking(new DateOnly(2026, 10, 27), 1234, 60);
        string repo = Repo("app");
        SetRepositories(repo);
        data.CommitLog.Set(repo, new LoggedCommit("0123456789abcdef", new DateOnly(2026, 10, 27), "Fix #1234"));
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);
        SortedDictionary<string, string> times = FileSnapshot.Take(data.TimesFolder);
        List<string> opened = [];
        List<string> people = [];

        IRenderedComponent<ItemPanel> cut = Render(1234, opened, people);

        WaitLoaded(cut);
        Assert.Equal(["Notes", "Open todos", "People", "Meetings", "Hours", "Commits"], Headings(cut));
        Assert.Equal("Work item", cut.Find("section.item-panel").GetAttribute("aria-label"));
        Assert.Equal(["Plan", "Review"], cut.FindAll(".item-notes button").Select(b => b.TextContent).ToArray());
        Assert.Equal(["2026-10-30|open #1234"], cut.FindAll(".item-open-todos li").Select(Row).ToArray());
        Assert.Equal(["Anna|1"], cut.FindAll(".item-people li").Select(Row).ToArray());
        Assert.Equal(["2026-10-28|10:00-11:00|Sync #1234|Room 1"], cut.FindAll(".item-meetings li").Select(Row).ToArray());
        Assert.Equal(["2026-W44|2:30", "Total|2:30"], cut.FindAll(".item-hours li").Select(Row).ToArray());
        Assert.Equal(["2026-10-27|app|0123456|Fix #1234"], cut.FindAll(".item-commits li").Select(Row).ToArray());
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Empty(cut.FindAll("p[role=status]"));

        await cut.FindAll(".item-notes button")[1].ClickAsync(new MouseEventArgs());
        await cut.Find(".item-people button").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Review"], opened);
        Assert.Equal(["Anna"], people);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
        Assert.Equal(times, FileSnapshot.Take(data.TimesFolder));
    }

    // AC-11: a done todo shows under Done todos with its done date.
    [Fact]
    public async Task Render_DoneTodo_DoneSection()
    {
        data.AddTodo("finished #1234", new DateOnly(2026, 10, 20));
        TodoStore store = new TodoStore(data.Root, () => new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc));
        store.Complete(store.List()[0].Id);
        await data.RefreshAsync();

        IRenderedComponent<ItemPanel> cut = Render(1234);

        WaitLoaded(cut);
        Assert.Contains("Done todos", Headings(cut));
        Assert.Equal(["2026-10-28|finished #1234"], cut.FindAll(".item-done-todos li").Select(Row).ToArray());
        Assert.DoesNotContain("Open todos", Headings(cut));
    }

    // AC-11: nothing found and nothing set up shows the three grey hints and no section.
    [Fact]
    public async Task Render_NothingSetUp_GreyHints()
    {
        data.WriteNote("Plan", "#99\n");
        await data.RefreshAsync();

        IRenderedComponent<ItemPanel> cut = Render(1234);

        WaitLoaded(cut);
        Assert.Equal(
            ["No notes, todos or meetings mention #1234.", "Times isn't set up, so no hours.",
                "Set repository folders in Settings to see commits."],
            Lines(cut, "grey"));
        Assert.Empty(Lines(cut, "red"));
        Assert.Empty(cut.FindAll("h2"));
        Assert.Empty(cut.FindAll("ul"));
        Assert.Empty(data.CommitLog.Calls);
    }

    // AC-11: a bad index, a bad todos.json, an unreadable Times day file and a failing repo each show their red line;
    // the other repo's commits still show.
    [Fact]
    public async Task Render_Failures_RedLines()
    {
        data.WriteNote("Plan", "#1234\n");
        data.WriteFile(TagIndex.FileName, NotADatabase);
        data.WriteFile("todos.json", "not json");
        WriteBooking(new DateOnly(2026, 10, 26), 1234, 60);
        File.WriteAllText(Path.Combine(data.TimesFolder, "2026-10-27.json"), "{broken");
        string good = Repo("good");
        string bad = Repo("bad");
        SetRepositories(bad, good);
        data.CommitLog.Fail(bad);
        data.CommitLog.Set(good, new LoggedCommit("abcdef0123456", new DateOnly(2026, 10, 25), "Work on #1234"));
        await data.RefreshAsync();

        IRenderedComponent<ItemPanel> cut = Render(1234);

        WaitLoaded(cut);
        Assert.Equal(
            ["Can't read the mentions.", "Can't read the todos.", "Skipped 1 unreadable Times day file(s).",
                "Can't read commits in bad."],
            Lines(cut, "red"));
        Assert.Equal(["Hours", "Commits"], Headings(cut));
        Assert.Equal(["2026-10-25|good|abcdef0|Work on #1234"], cut.FindAll(".item-commits li").Select(Row).ToArray());
        Assert.Empty(Lines(cut, "grey"));
    }

    // AC-11: git missing shows the red line and no commits.
    [Fact]
    public async Task Render_GitMissing_RedLine()
    {
        data.WriteNote("Plan", "#1234\n");
        SetRepositories(Repo("app"));
        data.CommitLog.GitMissing = true;
        await data.RefreshAsync();

        IRenderedComponent<ItemPanel> cut = Render(1234);

        WaitLoaded(cut);
        Assert.Equal(["Can't run git, so no commits."], Lines(cut, "red"));
        Assert.Empty(cut.FindAll(".item-commits"));
    }

    // AC-11: after the state changed the panel shows the new mention without reopening.
    [Fact]
    public async Task StateChanged_ShowsNewMention()
    {
        data.WriteNote("Plan", "plain\n");
        await data.RefreshAsync();
        IRenderedComponent<ItemPanel> cut = Render(1234);
        WaitLoaded(cut);
        Assert.Empty(cut.FindAll(".item-notes"));

        data.WriteNote("Plan", "now #1234\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Plan.md"), DateTime.UtcNow.AddSeconds(5));
        await data.State!.Refresh();

        cut.WaitForAssertion(
            () => Assert.Equal(["Plan"], cut.FindAll(".item-notes button").Select(b => b.TextContent).ToArray()), Wait);
    }
}
