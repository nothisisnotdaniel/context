using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 293: the done log page, opened through Main's /donelog.
public sealed class DonePanelTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private async Task<IRenderedComponent<Main>> OpenDoneLog()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "/donelog" });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        return cut;
    }

    // AC-9: one section per week headed by its Monday; each entry shows date, cleaned text and, when set, the due date.
    [Fact]
    public async Task DoneLog_WeeksNewestFirst_WithDue()
    {
        data.WriteNote("done", "# done\n- 2026-11-16 mon\n- 2026-11-22 a<b>\u0007c (due 2026-11-20)\n- 2026-11-23 next\n");

        IRenderedComponent<Main> cut = await OpenDoneLog();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".done-panel h3.done-week").Count), Wait);
        Assert.Single(cut.FindAll(".page"));
        Assert.Equal("Done log", cut.Find(".done-panel h2").TextContent);
        Assert.Equal(
            ["Week of 2026-11-23", "Week of 2026-11-16"],
            cut.FindAll(".done-panel h3.done-week").Select(h => h.TextContent).ToArray());
        IReadOnlyList<IElement> entries = cut.FindAll(".done-panel li.done-entry");
        Assert.Equal(
            ["2026-11-23 next", "2026-11-22 a<b>�c", "2026-11-16 mon"],
            entries.Select(e => e.QuerySelector(".date")!.TextContent + " " + e.QuerySelector(".text")!.TextContent).ToArray());
        Assert.Equal("due 2026-11-20", entries[1].QuerySelector(".due")!.TextContent);
        Assert.Null(entries[0].QuerySelector(".due"));
        Assert.DoesNotContain("<b>", cut.Find(".done-panel").InnerHtml, StringComparison.Ordinal);
        Assert.All(cut.FindAll(".done-panel button"), b => Assert.Equal("button", b.GetAttribute("type")));

        await cut.Find(".done-panel button.close").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll(".done-panel"));
    }

    // AC-9: no done.md, or one with no entries, shows the grey empty line.
    [Theory]
    [InlineData(null)]
    [InlineData("# done\nfree text\n")]
    public async Task DoneLog_Empty_NothingDoneYet(string? text)
    {
        if (text is not null)
        {
            data.WriteNote("done", text);
        }

        IRenderedComponent<Main> cut = await OpenDoneLog();

        cut.WaitForAssertion(() => Assert.Equal("Nothing done yet.", cut.Find(".done-panel p[role=status]").TextContent), Wait);
        Assert.Empty(cut.FindAll(".done-panel h3"));
    }

    // AC-9: an unreadable done.md shows the red line.
    [Fact]
    public async Task DoneLog_Unreadable_RedLine()
    {
        Directory.CreateDirectory(data.Root);
        using (FileStream big = File.Create(Path.Combine(data.Root, "done.md")))
        {
            big.SetLength(NoteStore.MaxNoteBytes + 1);
        }

        IRenderedComponent<Main> cut = await OpenDoneLog();

        cut.WaitForAssertion(() => Assert.Equal("Can't read the done log.", cut.Find(".done-panel p.red").TextContent), Wait);
        Assert.Empty(cut.FindAll(".done-panel h3"));
    }

    // AC-9: the panel reloads when a todo is done meanwhile.
    [Fact]
    public async Task DoneLog_ReloadsAfterCompletion()
    {
        data.AddTodo("Send report", DesktopFixture.Today);
        IRenderedComponent<Main> cut = await OpenDoneLog();
        cut.WaitForAssertion(() => Assert.Equal("Nothing done yet.", cut.Find(".done-panel p[role=status]").TextContent), Wait);

        ActionResult result = await data.Actions!.CompleteTodo(data.State!.Snapshot!.Todos[0], new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        cut.WaitForAssertion(() => Assert.Equal("Send report", cut.Find(".done-panel li.done-entry .text").TextContent), Wait);
    }
}
