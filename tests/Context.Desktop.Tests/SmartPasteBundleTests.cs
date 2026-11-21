using System.Text.Json;

namespace Context.Tests;

// 244 AC-1 to AC-4, AC-6 (JS part) and AC-8: smart paste in the bundled editor.js, run in Jint through the DOM-free
// pastedMarkdown and titledMarkdown hooks.
public sealed class SmartPasteBundleTests : IClassFixture<EditorBundle>
{
    private const string NoPasteJson = "{\"markdown\":null,\"link\":null}";
    private static readonly (string? Markdown, string? Link) NoPaste = (null, null);
    private const string Table = "| a | b |\n| --- | --- |\n| 1 | 2 |";

    private readonly EditorBundle bundle;

    public SmartPasteBundleTests(EditorBundle bundle)
    {
        this.bundle = bundle;
    }

    // AC-1: CRLF cells in an empty paragraph become exactly the table, header first.
    [Fact]
    public void Table_InEmptyParagraph_IsExactly()
    {
        Assert.Equal(Result(Table, null), Paste("a\tb\r\n1\t2\r\n"));
    }

    // AC-1: CR line ends, trimmed cells, an empty cell written "|  |", and only one trailing newline dropped.
    [Fact]
    public void Table_CrTrimmedAndEmptyCells()
    {
        Assert.Equal(
            Result("| a | b |\n| --- | --- |\n| 1 |  |", null),
            Paste(" a \t b\r1\t  \r"));
        Assert.Equal(NoPaste, Paste("a\tb\n1\t2\n\n"));
    }

    // AC-1: 500 lines of 49 tabs is still a table.
    [Fact]
    public void Table_AtTheLimits_IsATable()
    {
        string line = string.Join('\t', Enumerable.Repeat("c", 50));
        string text = string.Join('\n', Enumerable.Repeat(line, 500));

        string? markdown = Paste(text).Markdown;

        Assert.NotNull(markdown);
        Assert.Equal(501, markdown.Split('\n').Length);
        Assert.StartsWith("| c | c |", markdown, StringComparison.Ordinal);
        Assert.Equal("| " + string.Join(" | ", Enumerable.Repeat("---", 50)) + " |", markdown.Split('\n')[1]);
    }

    // AC-1: typed "a | b" is no longer escaped, and the table fixture round-trips byte for byte.
    [Fact]
    public void Pipe_IsNotEscaped_TableRoundTrips()
    {
        Assert.Equal("a | b\n", JsonSerializer.Deserialize<string>(bundle.Json("ContextEditor.typedMarkdown('a | b')")));
        string table = File.ReadAllText(Path.Combine(RoundTripFolder(), "lossless", "table.md"));
        Assert.Equal(table, bundle.Call("roundTrip", table));
        Assert.Equal("true", bundle.Call("isLossless", table));
    }

    // AC-2: text before and after the cursor keeps its own paragraphs.
    [Fact]
    public void Table_BetweenText_OwnParagraph()
    {
        Assert.Equal(Result("x\n\n" + Table + "\n\ny", null), Paste("a\tb\n1\t2", start: "xy", cursor: 1));
    }

    // AC-2: a non-empty selection is replaced first.
    [Fact]
    public void Table_ReplacesTheSelection()
    {
        Assert.Equal(
            Result("x\n\nh\n\n" + Table + "\n\nworld\n", null),
            Paste("a\tb\n1\t2", start: "x\n\nhello world\n", cursor: 3, end: 8));
    }

    // AC-2 (job 290): a cell such as *x* is saved as pasted, with no backslash added, like typed text.
    [Fact]
    public void Table_CellsSavedAsPasted()
    {
        string? markdown = Paste("*x*\t[[n]]\n#t\t`c`").Markdown;

        Assert.Equal("| *x* | [[n]] |\n| --- | --- |\n| #t | `c` |", markdown);
    }

    // AC-3: the hook's answer for the default paste is exactly the null pair.
    [Fact]
    public void NoTable_JsonIsTheNullPair()
    {
        Assert.Equal(NoPasteJson, PasteJson("a\tb", false, "", null, null));
    }

    // AC-3: text that is no table takes the default paste.
    [Theory]
    [InlineData("a\tb")]
    [InlineData("a\tb\n")]
    [InlineData("a\tb\n1\t2\t3")]
    [InlineData("a\tb\n1\n")]
    [InlineData("a\tb|c\n1\t2")]
    [InlineData("a\tb\n1\t|")]
    [InlineData("just text\nmore text")]
    [InlineData("")]
    public void NoTable_DefaultPaste(string text)
    {
        Assert.Equal(NoPaste, Paste(text));
    }

    // AC-3: more than 500 lines or more than 49 tabs per line is no table.
    [Fact]
    public void OverTheLimits_DefaultPaste()
    {
        Assert.Equal(NoPaste, Paste(string.Join('\n', Enumerable.Repeat("a\tb", 501))));
        string wide = string.Join('\t', Enumerable.Repeat("c", 51));
        Assert.Equal(NoPaste, Paste(wide + "\n" + wide));
    }

    // AC-3: a cursor in a heading, a code block or inline code takes the default paste, for a table and a link.
    [Theory]
    [InlineData("# Head\n", 2)]
    [InlineData("```\ncode\n```\n", 2)]
    [InlineData("a `code` b\n", 4)]
    public void InHeadingOrCode_DefaultPaste(string start, int cursor)
    {
        Assert.Equal(NoPaste, Paste("a\tb\n1\t2", start: start, cursor: cursor));
        Assert.Equal(NoPaste, Paste("https://e.x/a", start: start, cursor: cursor));
    }

    // AC-4: a lone link goes in as unmarked text and is reported once.
    [Theory]
    [InlineData("https://e.x/a", "https://e.x/a")]
    [InlineData("  http://example.com:8080/p?q=1#f\n", "http://example.com:8080/p?q=1#f")]
    [InlineData("HTTPS://[::1]/x", "HTTPS://[::1]/x")]
    public void LoneLink_PlainText_Reported(string text, string url)
    {
        Assert.Equal(Result(url, url), Paste(text));
        Assert.Equal(Result("see " + url + " now", url), Paste(text, start: "see  now", cursor: 4));
        Assert.Equal(Result("x" + url, url), Paste(text, start: "x"));
    }

    // AC-4: a link pasted into bold text gets no mark.
    [Fact]
    public void LoneLink_InBold_StaysUnmarked()
    {
        Assert.Equal(Result("**a**https://e.x/a**b**", "https://e.x/a"), Paste("https://e.x/a", start: "**ab**", cursor: 1));
    }

    // AC-4: anything but one absolute http(s) URL with a host is no link: no transaction, no call.
    [Theory]
    [InlineData("ftp://e.x/a")]
    [InlineData("https://")]
    [InlineData("https:///path")]
    [InlineData("https://e.x/a https://e.x/b")]
    [InlineData("see https://e.x/a")]
    [InlineData("https://e.x/a\nb")]
    [InlineData("mailto:a@e.x")]
    [InlineData("e.x/a")]
    [InlineData("javascript:alert(1)")]
    public void NoLink_DefaultPaste(string text)
    {
        Assert.Equal(NoPaste, Paste(text));
    }

    // AC-4: at most 2048 characters.
    [Fact]
    public void LongLink_Limit()
    {
        string url = "https://e.x/" + new string('a', 2048 - 12);
        Assert.Equal(Result(url, url), Paste(url));
        Assert.Equal(NoPaste, Paste(url + "a"));
    }

    // AC-6: the title link text parses back to the same text and href, never HTML.
    [Theory]
    [InlineData("A [b] page")]
    [InlineData("<b>x</b> & y")]
    [InlineData("Issue #12 - C# tips")]
    public void TitledMarkdown_ParsesBack(string title)
    {
        string markdown = JsonSerializer.Deserialize<string>(
            bundle.Json($"ContextEditor.titledMarkdown('https://e.x/a', {JsonSerializer.Serialize(title)})"))!;

        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", markdown));
        JsonElement paragraph = Assert.Single(doc.RootElement.GetProperty("content").EnumerateArray());
        JsonElement text = Assert.Single(paragraph.GetProperty("content").EnumerateArray());
        Assert.Equal(title, text.GetProperty("text").GetString());
        JsonElement mark = Assert.Single(text.GetProperty("marks").EnumerateArray());
        Assert.Equal("link", mark.GetProperty("type").GetString());
        Assert.Equal("https://e.x/a", mark.GetProperty("attrs").GetProperty("href").GetString());
    }

    // AC-6: an empty title, or one that would not open again as the same link (a tag, a note link, a link end, and
    // since job 290 markup, as the text is no longer escaped), leaves the URL text alone.
    [Theory]
    [InlineData("<b>*x*</b> & `y` _z_")]
    [InlineData("  ")]
    [InlineData("Read #later")]
    [InlineData("See [[Note]]")]
    [InlineData("a](b) [c")]
    public void TitledMarkdown_EmptyOrUnsafeTitle_KeepsUrl(string title)
    {
        Assert.Equal(
            "\"https://e.x/a\"",
            bundle.Json($"ContextEditor.titledMarkdown('https://e.x/a', {JsonSerializer.Serialize(title)})"));
    }

    // AC-6: the pending range is mapped: text typed next to the URL keeps the title; a URL typed into, marked or
    // removed is left alone.
    [Theory]
    [InlineData("before", "x [T](https://e.x/a)")]
    [InlineData("after", "[T](https://e.x/a) y")]
    [InlineData("inside", "hxttps://e.x/a")]
    [InlineData("bold", "**https://e.x/a**")]
    [InlineData("delete", "")]
    public void TitledMarkdown_ChangedRange_LeftAlone(string edit, string expected)
    {
        Assert.Equal(
            JsonSerializer.Serialize(expected),
            bundle.Json($"ContextEditor.titledMarkdown('https://e.x/a', 'T', '{edit}')"));
    }

    // AC-4, AC-6, AC-8: the page wires it up: handlePaste runs smartPaste with ProseMirror's plain rule (Shift held, the
    // key not Insert), reports a pasted link through OnPasteLink, and setLinkTitle is on ContextEditor.
    [Fact]
    public void View_WiresSmartPaste()
    {
        string view = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "editor", "src", "view.js"));

        Assert.Contains(
            "smartPaste(view.state, data.getData(\"text/plain\"), editor.shift && editor.lastKey !== 45)",
            view,
            StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(view, "invokeMethodAsync\\(\"OnPasteLink\""));
        Assert.Equal("\"function\"", bundle.Json("typeof ContextEditor.setLinkTitle"));
    }

    // AC-8: a plain paste (Ctrl/Cmd+Shift+V) takes ProseMirror's plain-text paste for a table and a link.
    [Fact]
    public void Plain_DefaultPaste()
    {
        Assert.Equal(NoPasteJson, PasteJson("a\tb\r\n1\t2\r\n", true, "", null, null));
        Assert.Equal(NoPasteJson, PasteJson("https://e.x/a", true, "", null, null));
    }

    private (string? Markdown, string? Link) Paste(string text, bool plain = false, string start = "", int? cursor = null, int? end = null)
    {
        using JsonDocument doc = JsonDocument.Parse(PasteJson(text, plain, start, cursor, end));
        return (doc.RootElement.GetProperty("markdown").GetString(), doc.RootElement.GetProperty("link").GetString());
    }

    private string PasteJson(string text, bool plain = false, string start = "", int? cursor = null, int? end = null)
    {
        string args = string.Join(
            ", ",
            JsonSerializer.Serialize(text),
            plain ? "true" : "false",
            JsonSerializer.Serialize(start),
            cursor is int c ? c.ToString(System.Globalization.CultureInfo.InvariantCulture) : "undefined",
            end is int e ? e.ToString(System.Globalization.CultureInfo.InvariantCulture) : "undefined");
        return JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.pastedMarkdown({args})"))!;
    }

    private static (string? Markdown, string? Link) Result(string markdown, string? link) => (markdown, link);

    private static string RoundTripFolder() => Path.Combine(RepoFiles.Root(), "tests", "Context.Desktop.Tests", "RoundTrip");
}
