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
        await StaleClick.KeyDownAsync(cut, ".layout", new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await StaleClick.InputAsync(cut, ".palette-input", new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "/donelog" });
        await StaleClick.KeyDownAsync(cut, ".palette-input", new KeyboardEventArgs { Key = "Enter" });
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

        await StaleClick.ClickAsync(cut, ".done-panel button.close");
        Assert.Empty(cut.FindAll(".done-panel"));
    }

    // AC-9: the panel's rows are styled like the review's: a plain list, flex rows, fixed date and due, a cut text; no
    // colour literals.
    [Fact]
    public void AppCss_DonePanelRules()
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        Assert.Contains("list-style: none;", Block(css, ".done-entries {"), StringComparison.Ordinal);
        Assert.Contains("display: flex;", Block(css, ".done-entry {"), StringComparison.Ordinal);
        Assert.Contains("margin: 12px 0 4px;", Block(css, ".done-panel h3.done-week {"), StringComparison.Ordinal);
        Assert.Contains("flex: none;", Block(css, ".done-entry .due {"), StringComparison.Ordinal);
        Assert.Contains("min-width: 0;", Block(css, ".done-entry .text {"), StringComparison.Ordinal);
        Assert.Contains("border-bottom: 1px solid var(--line);", Block(css, ".done-panel {"), StringComparison.Ordinal);
        Assert.DoesNotMatch(
            @"#[0-9a-fA-F]{3,8}\b|\b(rgb|rgba|hsl|hsla)\(|\b(white|black|red|green|blue|gray|grey|transparent)\b",
            string.Concat(new[] { ".done-panel {", ".done-panel h3.done-week {", ".done-entries {", ".done-entry {", ".done-entry .due {", ".done-entry .text {" }.Select(s => Block(css, s))));
    }

    private static string Block(string css, string selector)
    {
        int at = css.IndexOf("\n" + selector, StringComparison.Ordinal);
        Assert.True(at >= 0, selector);
        return css[at..css.IndexOf('}', at)];
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
