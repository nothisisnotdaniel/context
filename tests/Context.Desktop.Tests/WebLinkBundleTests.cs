using System.Text.Json;

namespace Context.Tests;

// 354 AC-1 to AC-7: web links in the bundled editor.js (parse, typed input rule, paste, lossless round trips, rename,
// Cmd/Ctrl+click and the link element), run in Jint through the DOM-free hooks.
public sealed class WebLinkBundleTests : IClassFixture<EditorBundle>
{
    private readonly EditorBundle bundle;

    public WebLinkBundleTests(EditorBundle bundle)
    {
        this.bundle = bundle;
    }

    // AC-1: only "Name" carries the link, and the text after it is plain, in a paragraph, a list item and a heading.
    [Theory]
    [InlineData("[Name](https://url): Some more text", ": Some more text")]
    [InlineData("[Name](https://url). x", ". x")]
    [InlineData("[Name](https://url)) x", ") x")]
    [InlineData("[Name](https://url):x", ":x")]
    [InlineData("- [Name](https://url): Some more text", ": Some more text")]
    [InlineData("## [Name](https://url): Some more text", ": Some more text")]
    public void Parse_LinkOnlyOnTheName(string markdown, string rest)
    {
        Assert.Equal(["Name|https://url", rest + "|"], Runs(bundle.Call("docJson", markdown)));
    }

    // AC-2: typing the link turns it into a link on ")", the brackets and URL are gone, and ": more" stays plain.
    [Fact]
    public void Typed_LinkOnCloseParen_RestPlain()
    {
        Assert.Equal(["Name|https://url"], Runs(Typed("[Name](https://url)")));
        Assert.Equal(["Name|https://url", ": more|"], Runs(Typed("[Name](https://url): more")));
        Assert.Equal(["Name|mailto:a@b.c"], Runs(Typed("[Name](mailto:a@b.c)")));
    }

    // AC-2: a URL that fails webHref, or empty text, stays plain typed text.
    [Theory]
    [InlineData("[Name](javascript:x)")]
    [InlineData("[Name](file:///a)")]
    [InlineData("[Name](notes/a.md)")]
    [InlineData("[](https://url)")]
    public void Typed_Refused_StaysPlain(string typed)
    {
        Assert.Equal([typed + "|"], Runs(Typed(typed)));
        Assert.Equal(typed + "\n", TypedMarkdown(typed));
    }

    // AC-2: in inline code or a code block the typed link stays the code's plain text.
    [Fact]
    public void Typed_InCode_StaysPlain()
    {
        Assert.Equal("`a[x](https://a)`", TypedAt("`a⟦`", "[x](https://a)"));
        Assert.Equal("```\n[x](https://a)\n```", TypedAt("```\n⟦\n```", "[x](https://a)"));
    }

    // AC-3: a normal paste links "Name" and keeps the rest plain; a plain paste keeps the text unchanged.
    [Fact]
    public void Paste_NormalLinks_PlainDoesNot()
    {
        const string Line = "See [Name](https://url): x";

        Assert.Equal(["See |", "Name|https://url", ": x|"], Runs(Pasted(Line, plain: false)));
        Assert.Equal([Line + "|"], Runs(Pasted(Line, plain: true)));
    }

    // AC-3: a link that fails webHref or an image is not turned into a link by paste.
    [Fact]
    public void Paste_RefusedLinks_StayText()
    {
        Assert.Equal(["b [y](javascript:z) ![i](https://i)|"], Runs(Pasted("b [y](javascript:z) ![i](https://i)", plain: false)));
    }

    // AC-4: every listed line is lossless and the round trip adds no "\".
    [Theory]
    [InlineData("[Name](https://url): Some more text")]
    [InlineData("[Name](https://example.com/a_(b)?q=1&x=*y*): more")]
    [InlineData("[Name](https://x.com/ä?a=&amp;b)")]
    [InlineData("[Name](<https://a b>)")]
    [InlineData("[https://x.com](https://x.com)")]
    [InlineData("<https://x.com>")]
    [InlineData("[**Bold** x](https://x)")]
    [InlineData("[Name](https://x.com/a\\_b)")]
    [InlineData("[Name](https://url \"T\")")]
    [InlineData("[Name](mailto:a@b.c)")]
    [InlineData("\\[Name](https://x)")]
    [InlineData("a_b *c* [x] (y) # : \\*")]
    public void Lossless_ListedLines(string markdown)
    {
        Assert.Equal("true", bundle.Call("isLossless", markdown));
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
    }

    // AC-4: a typed link saves as exactly the typed characters.
    [Theory]
    [InlineData("[Name](https://url): more")]
    [InlineData("[Name](https://example.com/a_(b)?q=1&x=*y*)")]
    [InlineData("See [a_b](https://x.com/a_b) now")]
    public void Typed_SavesAsTyped(string typed)
    {
        Assert.Equal(typed + "\n", TypedMarkdown(typed));
    }

    // AC-5: renaming the link text writes the new text with the destination as written, and adds no "\".
    [Theory]
    [InlineData("[Na⟦me](https://url \"T\")", "[NaXme](https://url \"T\")")]
    [InlineData("[Na⟦me](<https://a b>)", "[NaXme](<https://a b>)")]
    [InlineData("[Na⟦me](https://x.com/a\\_b)", "[NaXme](https://x.com/a\\_b)")]
    [InlineData("[Na⟦me](https://example.com/a_(b)?q=1&x=*y*): more", "[NaXme](https://example.com/a_(b)?q=1&x=*y*): more")]
    public void Rename_KeepsTheDestinationAsWritten(string markdown, string expected)
    {
        Assert.Equal(expected, TypedAt(markdown, "X"));
    }

    // AC-5: attachment PDF links still round-trip and still open on a plain click.
    [Fact]
    public void Attachment_StillRoundTripsAndOpensOnPlainClick()
    {
        const string Markdown = "The plan as [plan.pdf](.attachments/plan.pdf).";

        Assert.Equal(Markdown, bundle.Call("roundTrip", Markdown));
        Assert.Equal("true", bundle.Call("isLossless", Markdown));
        Assert.Equal(
            "{\"kind\":\"attachment\",\"href\":\".attachments/plan.pdf\"}",
            Clicked("The plan as [pl⟦an.pdf](.attachments/plan.pdf).", mod: false));
    }

    // AC-6: Cmd/Ctrl+click on a web link opens it; a plain click only places the caret.
    [Theory]
    [InlineData("[Na⟦me](https://url): x", "https://url")]
    [InlineData("[m⟦x](mailto:a@b.c)", "mailto:a@b.c")]
    [InlineData("See [x⟦y](https://x.com/a\\_b).", "https://x.com/a_b")]
    public void Click_WebLink_OnlyWithMod(string markdown, string href)
    {
        Assert.Equal("{\"kind\":\"web\",\"href\":" + JsonSerializer.Serialize(href) + "}", Clicked(markdown, mod: true));
        Assert.Equal("null", Clicked(markdown, mod: false));
    }

    // AC-6: a link that fails webHref gives null with or without Mod.
    [Theory]
    [InlineData("[x⟦y](javascript:alert(1))")]
    [InlineData("[x⟦y](file:///etc/passwd)")]
    [InlineData("[x⟦y](data:text/html,x)")]
    [InlineData("[x⟦y](other.md)")]
    public void Click_NonWebLink_Null(string markdown)
    {
        Assert.Equal("null", Clicked(markdown, mod: true));
        Assert.Equal("null", Clicked(markdown, mod: false));
    }

    // AC-6: webHref accepts only http, https and mailto without whitespace or control chars.
    [Theory]
    [InlineData("https://x", true)]
    [InlineData("HTTPS://x", true)]
    [InlineData("http://x", true)]
    [InlineData("mailto:a@b.c", true)]
    [InlineData("https://a b", false)]
    [InlineData("https://a\tb", false)]
    [InlineData("https://a\u0085b", false)]
    [InlineData("ftp://x", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("other.md", false)]
    [InlineData("", false)]
    public void WebHref_Rules(string href, bool expected)
    {
        Assert.Equal(expected ? "true" : "false", bundle.Json($"ContextEditor.webHref({Js(href)})"));
        Assert.Equal("false", bundle.Json($"ContextEditor.webHref({Js("https://" + new string('a', 2041))})"));
    }

    // AC-6: only the "web" kind makes the page call OnWebLink; everything else stays on OnAttachment.
    [Fact]
    public void Click_OnlyWebCallsOnWebLink()
    {
        string view = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "editor", "src", "view.js"));

        Assert.Single(view.Split("invokeMethodAsync(\"OnWebLink\"")[1..]);
        Assert.Contains(
            "if (link.kind === \"web\") {\n        dotnet.invokeMethodAsync(\"OnWebLink\", link.href)",
            view.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    // AC-7: the link element is a span.link whose title is the href as written, with no live href.
    [Theory]
    [InlineData("[Name](https://url): x", "https://url")]
    [InlineData("[Name](https://x.com/ä?a=&amp;b)", "https://x.com/ä?a=&b")]
    public void LinkDom_SpanWithTitleNoHref(string markdown, string title)
    {
        string json = bundle.Json($"ContextEditor.linkDomJson({Js(markdown)})");
        string dom = JsonSerializer.Deserialize<string>(json)!;

        Assert.Equal("[\"span\",{\"class\":\"link\",\"title\":" + JsonSerializer.Serialize(title) + "},0]", Normal(dom));
        Assert.DoesNotContain("\"href\"", dom, StringComparison.Ordinal);
    }

    private static string Normal(string json) => JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);

    private string Typed(string text) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.typedBlock({Js(text)})"))!;

    private string TypedMarkdown(string text) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.typedMarkdown({Js(text)})"))!;

    private string TypedAt(string markdown, string text) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.typedAtMarkdown({Js(markdown)}, {Js(text)})"))!;

    private string Pasted(string text, bool plain) =>
        JsonSerializer.Deserialize<string>(
            bundle.Json($"ContextEditor.pastedDocJson({Js(text)}, {(plain ? "true" : "false")}, \"\")"))!;

    private string Clicked(string markdown, bool mod) =>
        JsonSerializer.Deserialize<string>(
            bundle.Json($"ContextEditor.clickedLinkJson({Js(markdown)}, {(mod ? "true" : "false")})"))!;

    private static string Js(string text) => JsonSerializer.Serialize(text);

    // Every text node of the doc in order, as "text|href" (href empty without a link mark).
    private static List<string> Runs(string docJson)
    {
        List<string> runs = [];
        Collect(JsonDocument.Parse(docJson).RootElement, runs);
        return runs;
    }

    private static void Collect(JsonElement node, List<string> runs)
    {
        if (node.TryGetProperty("text", out JsonElement text))
        {
            string href = "";
            if (node.TryGetProperty("marks", out JsonElement marks))
            {
                foreach (JsonElement mark in marks.EnumerateArray())
                {
                    if (mark.GetProperty("type").GetString() == "link")
                    {
                        href = mark.GetProperty("attrs").GetProperty("href").GetString()!;
                    }
                }
            }

            runs.Add(text.GetString() + "|" + href);
        }

        if (node.TryGetProperty("content", out JsonElement content))
        {
            foreach (JsonElement child in content.EnumerateArray())
            {
                Collect(child, runs);
            }
        }
    }
}
