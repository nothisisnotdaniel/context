using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 168: recurring todos on the desktop: /todo every and /done in the palette, the repeat marker in TodoPanel and the
// next-due result line. The day is DesktopFixture.Today (Thursday 2026-10-29).
public sealed class RecurringTodoTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-5: /todo every <unit> <text> adds the todo with the repeat and its first due date.
    [Theory]
    [InlineData("/todo every monday Water plants", "monday", "2026-11-02")]
    [InlineData("/todo EVERY Day Water plants", "day", "2026-10-29")]
    [InlineData("/todo every month   Water plants  ", "month-29", "2026-10-29")]
    [InlineData("/todo every thursday Water plants", "thursday", "2026-10-29")]
    public async Task Palette_TodoEvery_Adds(string line, string stored, string due)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line);

        TodoItem added = Assert.Single(data.Todos!.List());
        Assert.Equal("Water plants", added.Text);
        Assert.Equal(stored, added.Repeat!.Stored);
        Assert.Equal(DateOnly.Parse(due, System.Globalization.CultureInfo.InvariantCulture), added.Due);
        IElement status = cut.Find(".main-status");
        Assert.Equal("Added: Water plants", status.TextContent);
        Assert.True(status.ClassList.Contains("accent"));
    }

    // AC-5: a missing or bad unit, or missing text, shows the red hint and writes nothing.
    [Theory]
    [InlineData("/todo every")]
    [InlineData("/todo every day")]
    [InlineData("/todo every day   ")]
    [InlineData("/todo every week Water plants")]
    [InlineData("/todo every mon Water plants")]
    [InlineData("/todo every 2 Water plants")]
    public async Task Palette_TodoEveryBad_RedHint_WritesNothing(string line)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line);

        Assert.False(File.Exists(Path.Combine(data.Root, TodoStore.FileName)));
        IElement status = cut.Find(".main-status");
        Assert.Equal("Type /todo every <weekday|day|month> <text>.", status.TextContent);
        Assert.True(status.ClassList.Contains("red"));
    }

    // AC-5: the same 200-character rule as any todo; a bad date keeps the old hint.
    [Fact]
    public async Task Palette_TodoEvery_TooLong_Refused_BadDateUnchanged()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/todo every day " + new string('a', 201));

        Assert.Equal(DesktopActions.TodoTooLongText, cut.Find(".main-status").TextContent);
        Assert.False(File.Exists(Path.Combine(data.Root, TodoStore.FileName)));

        await RunPalette(cut, "/todo everyday Water plants");

        Assert.Equal("Type /todo <yyyy-MM-dd|today|tomorrow> <text>.", cut.Find(".main-status").TextContent);
        Assert.False(File.Exists(Path.Combine(data.Root, TodoStore.FileName)));
    }

    // AC-6: /done is listed and ranked by CommandMatcher.
    [Fact]
    public async Task Palette_Done_Listed()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "k", CtrlKey = true });

        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = "/don" });

        Assert.Equal("/done <text>", cut.FindAll(".palette-row .usage")[0].TextContent);
        Assert.Equal("Mark the todo with that text done", cut.FindAll(".palette-row .description")[0].TextContent);
    }

    // AC-6: the same red texts as context todo done, and nothing is written.
    [Fact]
    public async Task Palette_Done_RedTexts_WriteNothing()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/done Call");
        AssertRed(cut, "There are no todos to mark done.");

        data.AddTodo("Call Anna", DesktopFixture.Today);
        data.AddTodo("Call Ben", DesktopFixture.Today.AddDays(1));
        await data.RefreshAsync();
        SortedDictionary<string, string> files = Files();

        await RunPalette(cut, "/done");
        AssertRed(cut, "Type /done <text>.");
        await RunPalette(cut, "/done    ");
        AssertRed(cut, "Type /done <text>.");
        await RunPalette(cut, "/done milk\u001b");
        AssertRed(cut, "No todo matches \"milk�\".");
        await RunPalette(cut, "/done call");
        AssertRed(cut, "2 todos match \"call\". Type more of the todo's text.");

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(files, Files());
    }

    // AC-6: a single pick opens the Mark this todo done? dialog; cancelling writes nothing.
    [Fact]
    public async Task Palette_Done_OpensDialog_CancelWritesNothing()
    {
        data.AddTodo("Call Anna", DesktopFixture.Today);
        data.AddTodo("Buy milk", DesktopFixture.Today);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        SortedDictionary<string, string> files = Files();

        await RunPalette(cut, "/done anna");

        Assert.Equal("Mark this todo done? Call Anna", cut.Find("[role=dialog] .question").TextContent);
        Assert.Equal(string.Empty, cut.Find(".main-status").TextContent);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(files, Files());
        Assert.Equal(2, data.Todos!.List().Count);
    }

    // AC-6, AC-7: confirming /done on a recurring todo marks it done, shows the next due date and lists the new todo.
    [Fact]
    public async Task Palette_Done_Recurring_ConfirmShowsNextDue()
    {
        data.AddTodo("Water plants", DesktopFixture.Today, TodoRepeat.Weekly(DayOfWeek.Thursday));
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/done water plants");
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(
            () =>
            {
                Assert.Empty(cut.FindAll("[role=dialog]"));
                IElement result = cut.Find(".todos-region p.result");
                Assert.Equal("Done: Water plants. Next due 2026-11-05.", result.TextContent);
                Assert.True(result.ClassList.Contains("accent"));
                Assert.Equal(["2026-11-05 Water plants every Thursday"], cut.FindAll("li.todo").Select(Line).ToArray());
            },
            Wait);
        Assert.Single(data.Todos!.ListDone());
    }

    // AC-7: the checkbox on a recurring todo shows the next due date; a plain todo still shows Done: <text>.
    [Fact]
    public async Task Tick_Recurring_ShowsNextDue_PlainUnchanged()
    {
        data.AddTodo("Stretch", DesktopFixture.Today.AddDays(-3), TodoRepeat.Daily);
        data.AddTodo("Buy milk", DesktopFixture.Today);
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        Box(cut, 0).Change(true);
        Assert.Equal("Mark this todo done? Stretch", cut.Find("[role=dialog] .question").TextContent);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(
            () =>
            {
                IElement result = cut.Find("p.result");
                Assert.Equal("Done: Stretch. Next due 2026-10-30.", result.TextContent);
                Assert.True(result.ClassList.Contains("accent"));
                Assert.Equal(
                    ["2026-10-29 Buy milk", "2026-10-30 Stretch every day"],
                    cut.FindAll("li.todo").Select(Line).ToArray());
            },
            Wait);

        Box(cut, 0).Change(true);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Done: Buy milk", cut.Find("p.result").TextContent), Wait);
    }

    // AC-8: a recurring todo has a span.repeat after its text with the same title; a plain one has none.
    [Fact]
    public async Task Render_RepeatMarker()
    {
        data.AddTodo("Stretch", DesktopFixture.Today, TodoRepeat.Daily);
        data.AddTodo("Rent", DesktopFixture.Today.AddDays(1), TodoRepeat.Monthly(30));
        data.AddTodo("Water plants", DesktopFixture.Today.AddDays(2), TodoRepeat.Weekly(DayOfWeek.Monday));
        data.AddTodo("Buy milk", DesktopFixture.Today.AddDays(3));

        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        IReadOnlyList<IElement> items = cut.FindAll("li.todo");
        Assert.Equal(
            ["every day", "every month", "every Monday"],
            items.Take(3).Select(i => i.QuerySelector("span.repeat")!.TextContent).ToArray());
        Assert.All(items.Take(3), i =>
        {
            IElement repeat = i.QuerySelector("span.repeat")!;
            Assert.Equal(repeat.TextContent, repeat.GetAttribute("title"));
            Assert.Same(i.QuerySelector("span.text"), repeat.PreviousElementSibling);
        });
        Assert.Null(items[3].QuerySelector("span.repeat"));
    }

    private static void AssertRed(IRenderedComponent<Main> cut, string text)
    {
        IElement status = cut.Find(".main-status");
        Assert.Equal(text, status.TextContent);
        Assert.True(status.ClassList.Contains("red"));
    }

    private static IElement Box(IRenderedComponent<TodoPanel> cut, int index) => cut.FindAll("li.todo input.done")[index];

    private static string Line(IElement item) =>
        string.Join(" ", item.QuerySelectorAll(".date, .text, .repeat").Select(c => c.TextContent));

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

    private static async Task RunPalette(IRenderedComponent<Main> cut, string line)
    {
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = line });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    }
}
