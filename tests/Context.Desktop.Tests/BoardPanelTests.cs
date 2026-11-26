using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 242: a tag's board, one column per board column of settings.json, cards moved by drag and drop or move buttons.
public sealed class BoardPanelTests : IDisposable
{
    private const string Board = """{"boards":[{"tag":"launch","columns":["todo","doing","done"]}]}""";

    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public BoardPanelTests()
    {
        data.WriteFile(SettingsStore.FileName, Board);
    }

    private IRenderedComponent<StatusBarView>? bar;

    // 455: feedback goes to the status bar, which Main renders; tests render the bar next to the panel.
    private IRenderedComponent<StatusBarView> Bar => bar ??= data.Bunit.Render<StatusBarView>();

    public void Dispose() => data.Dispose();

    private string PlanPath => Path.Combine(data.Root, "Plan.md");

    // AC-6: the heading, one section per column (No status first, as it has a card), draggable cards whose name opens
    // the note, a todo card's text, move buttons for the columns on either side, and the close button.
    [Fact]
    public async Task Render_ShowsColumnsCardsAndMoveButtons()
    {
        data.WriteNote("Plan", "#launch #status/doing\n- { } ship it #launch\n");
        data.WriteNote("Idea", "#launch\n");
        await data.RefreshAsync();
        List<string> opened = [];
        int closed = 0;

        IRenderedComponent<BoardPanel> cut = data.Bunit.Render<BoardPanel>(p => p
            .Add(c => c.Tag, "launch")
            .Add(c => c.OnOpen, (string name) => opened.Add(name))
            .Add(c => c.OnClose, () => closed++));
        WaitForBoard(cut);

        Assert.Equal("Board", cut.Find("section.page.board-panel").GetAttribute("aria-label"));
        Assert.Equal("Board #launch", cut.Find(".board-panel h2").TextContent);
        Assert.Equal([BoardPanel.NoStatus, "todo", "doing", "done"], cut.FindAll("section.board-column h3").Select(h => h.TextContent));
        Assert.Equal(["Idea", "Plan"], CardNames(cut, BoardPanel.NoStatus));
        Assert.Equal(["Plan"], CardNames(cut, "doing"));
        Assert.Empty(CardNames(cut, "todo"));
        Assert.All(cut.FindAll("li.board-card"), li => Assert.Equal("true", li.GetAttribute("draggable")));
        Assert.Equal("ship it #launch", Assert.Single(cut.FindAll("span.board-todo")).TextContent);
        Assert.Equal(["Move to todo"], Moves(Card(cut, BoardPanel.NoStatus, 0)));
        Assert.Equal(["Move to todo"], Moves(Card(cut, BoardPanel.NoStatus, 1)));
        Assert.Equal(["Move to todo", "Move to done"], Moves(Card(cut, "doing", 0)));

        await Card(cut, "doing", 0).QuerySelector("button.note-button")!.ClickAsync(new MouseEventArgs());
        await cut.Find(".board-panel .close").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Plan"], opened);
        Assert.Equal(1, closed);
    }

    // AC-6: /board launch opens the panel; a card's name opens the note; the close button closes it; TagPanel's Board
    // button opens it too.
    [Fact]
    public async Task Main_BoardCommandAndTagPanelButton_OpenThePanel()
    {
        data.WriteNote("Plan", "#launch #status/todo\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/board launch");
        WaitForBoard(cut);
        Assert.Equal("Board #launch", cut.Find(".board-panel h2").TextContent);
        Assert.Equal(["Plan"], CardNames(cut, "todo"));

        await Card(cut, "todo", 0).QuerySelector("button.note-button")!.ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Plan", cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);

        await RunPalette(cut, "/board launch");
        WaitForBoard(cut);
        await cut.Find(".board-panel .close").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll(".board-panel"));

        await RunPalette(cut, "/tags launch");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tag-panel")), Wait);
        await cut.Find(".tag-panel button.board-open").ClickAsync(new MouseEventArgs());
        WaitForBoard(cut);
        Assert.Equal("Board #launch", cut.Find(".board-panel h2").TextContent);
        Assert.Empty(cut.FindAll(".tag-panel"));
    }

    // AC-6: /board without a valid tag is the red usage line and opens nothing.
    [Theory]
    [InlineData("/board")]
    [InlineData("/board   ")]
    [InlineData("/board #a!b")]
    public async Task Main_BoardWithoutATag_RedUsage(string line)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line);

        Assert.Equal("Type /board <tag>.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll(".board-panel"));
    }

    // AC-6: the panel given a bad tag shows the red usage line and reads nothing.
    [Fact]
    public async Task Render_InvalidTag_RedUsage()
    {
        await data.RefreshAsync();

        IRenderedComponent<BoardPanel> cut = data.Bunit.Render<BoardPanel>(p => p.Add(c => c.Tag, "#a!b"));

        IElement line = cut.Find(".board-panel p[role=status]");
        Assert.Equal("Type /board <tag>.", line.TextContent);
        Assert.True(line.ClassList.Contains("red"));
    }

    // AC-7: dragging a card onto another column rewrites its token and reloads; a drop on its own column, on No
    // status or with no dragged card writes nothing.
    [Fact]
    public async Task Drop_OnAnotherColumn_MovesTheCard()
    {
        data.WriteNote("Plan", "# Plan\n#launch #status/todo\n");
        IRenderedComponent<BoardPanel> cut = await RenderBoard();

        await Column(cut, "doing").DropAsync(new DragEventArgs());
        await Card(cut, "todo", 0).DragStartAsync(new DragEventArgs());
        await Column(cut, "todo").DropAsync(new DragEventArgs());
        Assert.Equal("# Plan\n#launch #status/todo\n", File.ReadAllText(PlanPath));

        await Card(cut, "todo", 0).DragStartAsync(new DragEventArgs());
        await Column(cut, "doing").DropAsync(new DragEventArgs());

        Assert.Equal("# Plan\n#launch #status/doing\n", File.ReadAllText(PlanPath));
        cut.WaitForAssertion(() => Assert.Equal(["Plan"], CardNames(cut, "doing")), Wait);
        Assert.Empty(CardNames(cut, "todo"));
        Assert.Equal(string.Empty, data.StatusBar.Text);
    }

    // AC-7: a move button moves a todo card by its own token (the keyboard path), leaving the note's token alone.
    [Fact]
    public async Task MoveButton_MovesTheTodoCard()
    {
        data.WriteNote("Plan", "#launch #status/done\n- { } ship it #launch\n");
        IRenderedComponent<BoardPanel> cut = await RenderBoard();

        await MoveButton(cut, BoardPanel.NoStatus, 0, "Move to todo").ClickAsync(new MouseEventArgs());

        Assert.Equal("#launch #status/done\n- { } ship it #launch #status/todo\n", File.ReadAllText(PlanPath));
        cut.WaitForAssertion(() => Assert.Equal(["Plan"], CardNames(cut, "todo")), Wait);
        Assert.Empty(cut.FindAll("section.board-column.no-status"));
        Assert.Equal(["Plan"], CardNames(cut, "done"));
    }

    // AC-7: a note changed, removed or unreadable since the board was shown gives its red line and writes nothing.
    [Theory]
    [InlineData("changed", "Plan changed. Reload the board and try again.")]
    [InlineData("missing", "Plan no longer exists. Nothing was moved.")]
    [InlineData("failed", "Can't write Plan.")]
    public async Task Move_NoteNotAsShown_RedLine(string change, string text)
    {
        data.WriteNote("Plan", "#launch #status/todo\n");
        IRenderedComponent<BoardPanel> cut = await RenderBoard();
        string? after = change switch
        {
            "changed" => "#launch #status/done\n",
            "failed" => "#launch #status/todo\n" + new string('x', (int)NoteStore.MaxEditBytes),
            _ => null,
        };
        if (after is null)
        {
            File.Delete(PlanPath);
        }
        else
        {
            File.WriteAllText(PlanPath, after);
        }

        await MoveButton(cut, "todo", 0, "Move to doing").ClickAsync(new MouseEventArgs());

        IElement line = Bar.Find(".main-status");
        Assert.Equal(text, line.TextContent);
        Assert.True(line.ClassList.Contains("red"));
        Assert.Equal(after is not null, File.Exists(PlanPath));
        if (after is not null)
        {
            Assert.Equal(after, File.ReadAllText(PlanPath));
        }
    }

    // AC-7: nothing moves while another action runs: the busy line, and the file is not written.
    [Fact]
    public async Task Move_WhileBusy_WritesNothing()
    {
        data.WriteNote("Plan", "#launch #status/todo\n");
        IRenderedComponent<BoardPanel> cut = await RenderBoard();
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        Task<ActionResult> first = data.Actions.CaptureText("first");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        try
        {
            await MoveButton(cut, "todo", 0, "Move to doing").ClickAsync(new MouseEventArgs());
            await Card(cut, "todo", 0).DragStartAsync(new DragEventArgs());
            await Column(cut, "done").DropAsync(new DragEventArgs());
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(ActionKind.Done, (await first).Kind);
        Assert.Equal(DesktopActions.BusyText, data.StatusBar.Text);
        Assert.Equal("red", data.StatusBar.Css);
        Assert.Equal("#launch #status/todo\n", File.ReadAllText(PlanPath));
    }

    // AC-8: no board for the tag, a broken settings file, an unreadable index and a board without cards each show
    // their line.
    [Theory]
    [InlineData("noboard", "No board for #launch. Add it under \"boards\" in settings.json.", "grey")]
    [InlineData("settings", "Can't read settings.json.", "red")]
    [InlineData("index", "Can't read the board.", "red")]
    [InlineData("empty", "No notes or todos have #launch.", "grey")]
    public async Task Render_NothingToShow_Line(string state, string text, string color)
    {
        if (state != "empty")
        {
            data.WriteNote("Plan", "#launch #status/todo\n");
        }

        if (state == "noboard")
        {
            data.WriteFile(SettingsStore.FileName, """{"boards":[{"tag":"work","columns":["todo"]}]}""");
        }
        else if (state == "settings")
        {
            data.WriteFile(SettingsStore.FileName, "{");
        }
        else if (state == "index")
        {
            data.WriteFile(TagIndex.FileName, NotADatabase);
        }

        IRenderedComponent<BoardPanel> cut = await RenderBoard();

        IElement line = cut.Find(".board-panel p[role=status]");
        Assert.Equal(text, line.TextContent);
        Assert.True(line.ClassList.Contains(color));
        Assert.Empty(cut.FindAll("section.board-column"));
    }

    // AC-8: the panel reloads when the state changes (settings.json rewritten, then Refresh).
    [Fact]
    public async Task StateChanged_Reloads()
    {
        data.WriteNote("Plan", "#launch #status/review\n");
        IRenderedComponent<BoardPanel> cut = await RenderBoard();
        Assert.Equal(["Plan"], CardNames(cut, BoardPanel.NoStatus));

        data.WriteFile(SettingsStore.FileName, """{"boards":[{"tag":"launch","columns":["todo","review"]}]}""");
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(["Plan"], CardNames(cut, "review")), Wait);
        Assert.Equal(["todo", "review"], cut.FindAll("section.board-column h3").Select(h => h.TextContent));
    }

    // AC-8: while a reload waits for the index, the shown cards stay.
    [Fact]
    public async Task StateChanged_KeepsTheCardsWhileItReloads()
    {
        data.WriteNote("Plan", "#launch #status/todo\n");
        IRenderedComponent<BoardPanel> cut = await RenderBoard();
        data.WriteNote("Plan", "#launch #status/done\n");
        object gate = DesktopFixture.IndexGate(data.State!);

        Monitor.Enter(gate);
        try
        {
            typeof(DesktopState).GetMethod("RaiseChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(data.State, null);
            Assert.Equal(["Plan"], CardNames(cut, "todo"));
            Assert.Empty(cut.FindAll(".board-panel p.grey"));
        }
        finally
        {
            Monitor.Exit(gate);
        }

        cut.WaitForAssertion(() => Assert.Equal(["Plan"], CardNames(cut, "done")), Wait);
        Assert.Empty(CardNames(cut, "todo"));
    }

    private static async Task RunPalette(IRenderedComponent<Main> cut, string line)
    {
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = line });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    }

    private static void WaitForBoard(IRenderedComponent<IComponent> cut) =>
        cut.WaitForAssertion(
            () => Assert.DoesNotContain("Loading…", cut.Find(".board-panel").TextContent, StringComparison.Ordinal), Wait);

    private static IElement Column(IRenderedComponent<IComponent> cut, string name) =>
        cut.FindAll("section.board-column").Single(s => s.GetAttribute("aria-label") == name);

    private static IElement Card(IRenderedComponent<IComponent> cut, string column, int at) =>
        Column(cut, column).QuerySelectorAll("li.board-card")[at];

    private static IElement MoveButton(IRenderedComponent<IComponent> cut, string column, int at, string label) =>
        Card(cut, column, at).QuerySelectorAll("button.board-move").Single(b => b.GetAttribute("aria-label") == label);

    private static List<string> CardNames(IRenderedComponent<IComponent> cut, string column)
    {
        IElement? lane = cut.FindAll("section.board-column").SingleOrDefault(s => s.GetAttribute("aria-label") == column);
        return lane is null ? [] : [.. lane.QuerySelectorAll("li.board-card button.note-button").Select(b => b.TextContent)];
    }

    private static List<string> Moves(IElement card) =>
        [.. card.QuerySelectorAll("button.board-move").Select(b => b.GetAttribute("aria-label")!)];

    private async Task<IRenderedComponent<BoardPanel>> RenderBoard()
    {
        await data.RefreshAsync();
        IRenderedComponent<BoardPanel> cut = data.Bunit.Render<BoardPanel>(p => p.Add(c => c.Tag, "launch"));
        WaitForBoard(cut);
        return cut;
    }
}
