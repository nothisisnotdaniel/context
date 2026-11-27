using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 380: the glossary page, opened through Main's /glossary.
public sealed class GlossaryPanelTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private async Task<IRenderedComponent<Main>> OpenGlossary(string command = "/glossary")
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        await RunPalette(cut, command);
        return cut;
    }

    private static async Task RunPalette(IRenderedComponent<Main> cut, string command)
    {
        await StaleClick.KeyDownAsync(cut, ".layout", new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await StaleClick.InputAsync(cut, ".palette-input", new ChangeEventArgs { Value = command });
        await StaleClick.KeyDownAsync(cut, ".palette-input", new KeyboardEventArgs { Key = "Enter" });
    }

    private static void WaitLoaded(IRenderedComponent<Main> cut) =>
        cut.WaitForAssertion(
            () => Assert.DoesNotContain("Loading…", cut.Find(".glossary-panel").TextContent, StringComparison.Ordinal),
            Wait);

    private static string[] Terms(IRenderedComponent<Main> cut) =>
        cut.FindAll(".glossary-panel dt.glossary-term").Select(t => t.TextContent).ToArray();

    private static int Reads(DesktopState state)
    {
        TagIndex index = (TagIndex)typeof(DesktopState)
            .GetField("index", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(state)!;
        return index.Reads;
    }

    // AC-4: the page, its heading, Close, the filter, one dt per term and one dd per meaning with a note button.
    [Fact]
    public async Task Glossary_TermsWithMeanings_NoteButtons()
    {
        data.WriteNote("A", "API:: first\n");
        data.WriteNote("b", "Api:: second\u0007<b>x</b>\nZed:: z\n");

        IRenderedComponent<Main> cut = await OpenGlossary();
        WaitLoaded(cut);

        Assert.Single(cut.FindAll(".page"));
        IElement section = cut.Find("section.page.glossary-panel");
        Assert.Equal("Glossary", section.GetAttribute("aria-label"));
        Assert.Equal("Glossary", cut.Find(".glossary-panel h2").TextContent);
        Assert.Equal("Close", cut.Find(".glossary-panel button.close").GetAttribute("aria-label"));
        IElement filter = cut.Find("input.glossary-filter");
        Assert.Equal("Filter the glossary", filter.GetAttribute("aria-label"));
        Assert.Equal("200", filter.GetAttribute("maxlength"));
        Assert.Equal(["API", "Zed"], Terms(cut));

        IReadOnlyList<IElement> children = cut.Find("dl.glossary-rows").Children.ToList();
        Assert.Equal(["DT", "DD", "DD", "DT", "DD"], children.Select(c => c.TagName).ToArray());
        Assert.Equal("first", children[1].QuerySelector(".glossary-text")!.TextContent);
        Assert.Equal("A", children[1].QuerySelector("button.note-button")!.TextContent);
        Assert.Equal("second�<b>x</b>", children[2].QuerySelector(".glossary-text")!.TextContent);
        Assert.Equal("b", children[2].QuerySelector("button.note-button")!.TextContent);
        Assert.DoesNotContain("<b>", section.InnerHtml, StringComparison.Ordinal);
        Assert.All(cut.FindAll(".glossary-panel button"), b => Assert.Equal("button", b.GetAttribute("type")));

        await StaleClick.ClickAsync(cut, ".glossary-panel button.close");
        Assert.Empty(cut.FindAll(".glossary-panel"));
    }

    // AC-4, AC-5: a meaning's note button opens the note and closes the page.
    [Fact]
    public async Task NoteButton_OpensNote_ClosesPage()
    {
        data.WriteNote("Plan", "# Plan\nAPI:: first\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await OpenGlossary();
        WaitLoaded(cut);

        await StaleClick.ClickAsync(cut, ".glossary-panel button.note-button");

        cut.WaitForAssertion(() => Assert.Equal("Plan", cut.Find(".note-title").TextContent), Wait);
        Assert.Empty(cut.FindAll(".glossary-panel"));
        Assert.NotNull(js);
    }

    // AC-5: /today closes the page as well.
    [Fact]
    public async Task Today_ClosesPage()
    {
        data.WriteNote("Plan", "API:: first\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await OpenGlossary();
        WaitLoaded(cut);

        await RunPalette(cut, "/today");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".glossary-panel")), Wait);
        Assert.NotNull(js);
    }

    // AC-4: at most 200 terms, then the grey "… n more" line.
    [Fact]
    public async Task Rows_CappedAt200_MoreLine()
    {
        string text = string.Concat(Enumerable.Range(0, 203).Select(i => $"Term{i:D3}:: m\n"));
        data.WriteNote("Many", text);

        IRenderedComponent<Main> cut = await OpenGlossary();
        WaitLoaded(cut);

        Assert.Equal(200, cut.FindAll(".glossary-panel dt.glossary-term").Count);
        IElement more = cut.Find(".glossary-panel p.more");
        Assert.Equal("… 3 more", more.TextContent);
        Assert.True(more.ClassList.Contains("grey"));
    }

    // AC-5: Loading… while the index is held, then the grey empty line.
    [Fact]
    public async Task Loading_ThenEmpty()
    {
        data.WriteNote("Plan", "type:: person\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await data.State!.ReloadsSettled().WaitAsync(Wait);

        // The gate is held on a thread of its own: this test awaits while holding it (job 491: a Monitor entered here
        // and left after the await could be left on another thread, which kept the gate held and hung the run).
        using (GateHold.Take(DesktopFixture.IndexGate(data.State!)))
        {
            await RunPalette(cut, "/glossary");
            IElement loading = cut.Find(".glossary-panel p[role=status]");
            Assert.Equal("Loading…", loading.TextContent);
            Assert.True(loading.ClassList.Contains("grey"));
        }

        cut.WaitForAssertion(
            () => Assert.Equal("No terms yet. Write Term:: meaning in a note.", cut.Find(".glossary-panel p[role=status]").TextContent),
            Wait);
        Assert.True(cut.Find(".glossary-panel p[role=status]").ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll("dl.glossary-rows"));
    }

    // AC-5: a context.db that is not a database shows the red line and is left alone.
    [Fact]
    public async Task BadIndex_RedLine()
    {
        data.WriteNote("Plan", "API:: first\n");
        data.WriteFile(TagIndex.FileName, NotADatabase);

        IRenderedComponent<Main> cut = await OpenGlossary();
        WaitLoaded(cut);

        IElement status = cut.Find(".glossary-panel p[role=status]");
        Assert.Equal("Can't read the glossary.", status.TextContent);
        Assert.True(status.ClassList.Contains("red"));
        Assert.Equal(NotADatabase, File.ReadAllText(Path.Combine(data.Root, TagIndex.FileName)));
    }

    // AC-5: typing narrows the loaded rows without reading the index; nothing left is the grey no-match line.
    [Fact]
    public async Task Typing_Narrows_NoIndexRead()
    {
        data.WriteNote("A", "API:: first\nZed:: second\n");
        IRenderedComponent<Main> cut = await OpenGlossary();
        WaitLoaded(cut);
        cut.WaitForAssertion(() => Assert.Equal(["API", "Zed"], Terms(cut)), Wait);
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        int before = Reads(data.State!);

        await StaleClick.InputAsync(cut, "input.glossary-filter", new ChangeEventArgs { Value = "seco" });
        Assert.Equal(["Zed"], Terms(cut));

        await StaleClick.InputAsync(cut, "input.glossary-filter", new ChangeEventArgs { Value = "zz<b>" });
        IElement status = cut.Find(".glossary-panel p[role=status]");
        Assert.Equal("No terms match \"zz<b>\".", status.TextContent);
        Assert.True(status.ClassList.Contains("grey"));

        Assert.Equal(before, Reads(data.State!));
    }

    // AC-5: /glossary API opens with the filter API.
    [Fact]
    public async Task Command_WithFilter_Prefills()
    {
        data.WriteNote("A", "API:: first\nZed:: second\n");

        IRenderedComponent<Main> cut = await OpenGlossary("/glossary API");
        WaitLoaded(cut);

        Assert.Equal("API", cut.Find("input.glossary-filter").GetAttribute("value"));
        cut.WaitForAssertion(() => Assert.Equal(["API"], Terms(cut)), Wait);
    }

    // AC-5: after a note changed on disk and the state changed, the new term shows without reopening.
    [Fact]
    public async Task StateChanged_ShowsNewTerm()
    {
        data.WriteNote("A", "API:: first\n");
        IRenderedComponent<Main> cut = await OpenGlossary();
        WaitLoaded(cut);
        cut.WaitForAssertion(() => Assert.Equal(["API"], Terms(cut)), Wait);

        data.WriteNote("A", "API:: first\nFoo:: bar\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "A.md"), new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(["API", "Foo"], Terms(cut)), Wait);
    }

    // AC-4: the rows use the theme variables and the note button style; no colour literals.
    [Fact]
    public void AppCss_GlossaryRules()
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));
        string[] selectors = [".glossary-rows {", ".glossary-rows dt.glossary-term {", ".glossary-rows dd.glossary-meaning {"];

        Assert.Contains(".glossary-rows .note-button", css, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            @"#[0-9a-fA-F]{3,8}\b|\b(rgb|rgba|hsl|hsla)\(|\b(white|black|red|green|blue|gray|grey|transparent)\b",
            string.Concat(selectors.Select(s => Block(css, s))));
    }

    private static string Block(string css, string selector)
    {
        int at = css.IndexOf("\n" + selector, StringComparison.Ordinal);
        Assert.True(at >= 0, selector);
        return css[at..css.IndexOf('}', at)];
    }
}
