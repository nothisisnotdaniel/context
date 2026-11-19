using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 256 AC-7 to AC-9: the To review panel, on its own and in Main (the fixture's today is 2026-10-29).
public sealed class ToReviewPanelTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();
    private readonly List<string> opened = [];
    private readonly List<string> reviewed = [];

    public void Dispose() => data.Dispose();

    private string PathOf(string name) => Path.Combine(data.Root, name + ".md");

    private async Task<IRenderedComponent<ToReviewPanel>> Render(Func<string, Task<string?>>? guard = null)
    {
        await data.RefreshAsync();
        return data.Bunit.Render<ToReviewPanel>(p => p
            .Add(t => t.Guard, guard)
            .Add(t => t.OnOpen, (string name) => opened.Add(name))
            .Add(t => t.OnReviewed, (string name) => reviewed.Add(name)));
    }

    private static IElement Item(IRenderedComponent<ToReviewPanel> cut, string name) =>
        cut.FindAll("li.review-item").Single(li => li.QuerySelector("button.review-note")!.TextContent == name);

    // AC-7, AC-9: one row per due note (overdue ones with a grey "since"), every button named for its note; future and
    // sensitive notes never show.
    [Fact]
    public async Task Render_DueRows_WithButtons()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteNote("Old", "review:2026-09-01\n");
        data.WriteNote("Plan", "review:2026-10-29\n");
        data.WriteNote("Future", "review:2026-10-30\n");
        data.WriteSensitive("Secret", "review:2026-01-01\n");

        IRenderedComponent<ToReviewPanel> cut = await Render();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("li.review-item").Count), Wait);
        IElement section = cut.Find("section.to-review");
        Assert.Equal("To review", section.GetAttribute("aria-label"));
        Assert.Equal("To review", cut.Find("section.to-review h3").TextContent);
        Assert.Equal(["Old", "Plan"], cut.FindAll("button.review-note").Select(b => b.TextContent).ToArray());

        IElement old = Item(cut, "Old");
        IElement since = old.QuerySelector(".review-since")!;
        Assert.Equal("since 2026-09-01", since.TextContent);
        Assert.True(since.ClassList.Contains("grey"));
        Assert.Null(Item(cut, "Plan").QuerySelector(".review-since"));
        Assert.Equal("3 months", old.QuerySelector("button.review-3m")!.TextContent);
        Assert.Equal("1 year", old.QuerySelector("button.review-1y")!.TextContent);
        Assert.Equal("Remove", old.QuerySelector("button.review-remove")!.TextContent);
        Assert.All(
            old.QuerySelectorAll("button"),
            b => Assert.Contains("Old", b.GetAttribute("aria-label") ?? string.Empty, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".review-unreadable"));
    }

    // AC-7: nothing due and readable: the panel is not in the page.
    [Fact]
    public async Task Render_NothingDue_NoPanel()
    {
        data.WriteNote("Future", "review:2026-10-30\n");
        data.WriteNote("Plain", "# Plain\n");

        IRenderedComponent<ToReviewPanel> cut = await Render();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("section.to-review")), Wait);
    }

    // AC-7: an unreadable index shows the red line.
    [Fact]
    public async Task Render_Unreadable_RedLine()
    {
        data.WriteNote("Plan", "review:2026-10-01\n");
        data.WriteFile(TagIndex.FileName, "not a database at all, not at all, really not");

        IRenderedComponent<ToReviewPanel> cut = await Render();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("section.to-review")), Wait);
        IElement line = cut.Find(".review-unreadable");
        Assert.Equal("Can't read the review dates.", line.TextContent);
        Assert.True(line.ClassList.Contains("red"));
        Assert.Empty(cut.FindAll("li.review-item"));
    }

    // AC-7: the name opens the note; it goes through OneLine.
    [Fact]
    public async Task NoteName_Opens()
    {
        data.WriteNote("Plan", "review:2026-10-01\n");
        IRenderedComponent<ToReviewPanel> cut = await Render();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("li.review-item")), Wait);

        await cut.Find("button.review-note").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Plan"], opened);
    }

    // AC-7, AC-8: 3 months and 1 year move the date from today; the row disappears.
    [Theory]
    [InlineData("button.review-3m", "review:2027-01-29\n")]
    [InlineData("button.review-1y", "review:2027-10-29\n")]
    public async Task Reschedule_MovesDate_RowGone(string button, string expected)
    {
        data.WriteNote("Plan", "review:2026-10-01\n");
        IRenderedComponent<ToReviewPanel> cut = await Render();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("li.review-item")), Wait);

        await cut.Find(button).ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("li.review-item")), Wait);
        Assert.Equal(expected, File.ReadAllText(PathOf("Plan")));
        Assert.Equal(["Plan"], reviewed);
    }

    // AC-7, AC-8: Remove asks first; the confirmed remove deletes the line and the row disappears.
    [Fact]
    public async Task Remove_Confirmed_DeletesLine()
    {
        data.WriteNote("Plan", "# Plan\nreview:2026-10-01\nafter\n");
        data.WriteNote("Other", "review:2026-10-02\n");
        IRenderedComponent<ToReviewPanel> cut = await Render();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("li.review-item").Count), Wait);

        await Item(cut, "Plan").QuerySelector("button.review-remove")!.ClickAsync(new MouseEventArgs());
        Assert.Equal("Remove the review date from Plan?", cut.Find("[role=dialog] .question").TextContent);
        Assert.Equal("Remove", cut.Find("[role=dialog] button.confirm").TextContent);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("li.review-item")), Wait);
        Assert.Equal("Other", cut.Find("button.review-note").TextContent);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal("# Plan\nafter\n", File.ReadAllText(PathOf("Plan")));
        Assert.Equal(["Plan"], reviewed);
    }

    // AC-7: Cancel and Esc write nothing.
    [Fact]
    public async Task Remove_CancelOrEscape_WritesNothing()
    {
        data.WriteNote("Plan", "review:2026-10-01\n");
        IRenderedComponent<ToReviewPanel> cut = await Render();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("li.review-item")), Wait);
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        await cut.Find("button.review-remove").ClickAsync(new MouseEventArgs());
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("[role=dialog]"));

        await cut.Find("button.review-remove").ClickAsync(new MouseEventArgs());
        await cut.Find("[role=dialog]").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll("[role=dialog]"));

        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
        Assert.Single(cut.FindAll("li.review-item"));
        Assert.Empty(reviewed);
    }

    // AC-8: a result that isn't Done shows its red text; the row stays.
    [Fact]
    public async Task Changed_ShowsRedResult()
    {
        data.WriteNote("Plan", "review:2026-10-01\n");
        IRenderedComponent<ToReviewPanel> cut = await Render();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("li.review-item")), Wait);
        data.WriteNote("Plan", "x\nreview:2026-10-01\n");

        await cut.Find("button.review-3m").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("p.review-result")), Wait);
        IElement result = cut.Find("p.review-result");
        Assert.Equal(NoteTodos.ChangedText("Plan"), result.TextContent);
        Assert.True(result.ClassList.Contains("red"));
        Assert.Equal("x\nreview:2026-10-01\n", File.ReadAllText(PathOf("Plan")));
        Assert.Empty(reviewed);
    }

    // AC-8: a guard's text holds the write back and shows in red.
    [Fact]
    public async Task Guard_HoldsWriteBack()
    {
        data.WriteNote("Plan", "review:2026-10-01\n");
        List<string> asked = [];
        IRenderedComponent<ToReviewPanel> cut = await Render(name =>
        {
            asked.Add(name);
            return Task.FromResult<string?>("Save Plan first.");
        });
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("li.review-item")), Wait);

        await cut.Find("button.review-1y").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Save Plan first.", cut.Find("p.review-result").TextContent), Wait);
        Assert.Equal(["Plan"], asked);
        Assert.Equal("review:2026-10-01\n", File.ReadAllText(PathOf("Plan")));
    }

    // AC-8 in Main: the open note with unsaved changes is refused with "Save <name> first."; nothing is written.
    [Fact]
    public async Task Main_DirtyOpenNote_SaveFirst()
    {
        data.WriteNote("Plan", "# Plan\nreview:2026-10-01\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("aside.todos-region li.review-item")), Wait);
        js.Edit("# Plan\nreview:2026-10-01\nTyped\n");
        // Opening Plan started a recent.json write through a temp file; wait for it so the snapshot cannot catch it.
        data.WaitForRecentWrites();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        await cut.Find("button.review-3m").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Save Plan first.", cut.Find("p.review-result").TextContent), Wait);
        data.WaitForRecentWrites();
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
        Assert.Single(cut.FindAll("li.review-item"));
    }

    // AC-8 in Main: the open, clean note is reloaded with the moved date.
    [Fact]
    public async Task Main_CleanOpenNote_Reloads()
    {
        data.WriteNote("Plan", "# Plan\nreview:2026-10-01\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("aside.todos-region li.review-item")), Wait);
        int mounts = js.Calls("ContextEditor.mount").Count;

        await cut.Find("button.review-3m").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("li.review-item")), Wait);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        Assert.Equal("# Plan\nreview:2027-01-29\n", js.MountedText());
    }

    // AC-7 in Main: the panel sits in the todos region, right after the todo panel.
    [Fact]
    public async Task Main_PanelAfterTodos()
    {
        data.WriteNote("Plan", "review:2026-10-01\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("aside.todos-region > section.to-review")), Wait);
        Assert.Equal("todos", cut.Find("aside.todos-region > section.to-review").PreviousElementSibling!.ClassName);
    }

    private static async Task Open(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NoteOpener.OpenAsync(cut, name);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
    }
}
