using System.Text.Json;
using Jint;

namespace Context.Tests;

/// <summary>Loads the committed editor bundle (<c>wwwroot/editor/editor.js</c>) once into a Jint engine.</summary>
public sealed class EditorBundle
{
    private readonly Engine engine;

    public EditorBundle()
    {
        string path = Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "editor", "editor.js");
        engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(30)));
        engine.Execute(File.ReadAllText(path));
    }

    /// <summary>Calls <c>ContextEditor.&lt;function&gt;(markdown)</c> and returns its result as a string.</summary>
    public string Call(string function, string markdown)
    {
        lock (engine)
        {
            engine.SetValue("input", markdown);
            return engine.Evaluate($"String(ContextEditor.{function}(input))").AsString();
        }
    }

    /// <summary>Evaluates a JavaScript expression and returns its result as JSON text.</summary>
    public string Json(string expression)
    {
        lock (engine)
        {
            return engine.Evaluate($"JSON.stringify({expression})").AsString();
        }
    }
}

// 094 AC-1 to AC-4: the editor's Markdown round trip, its tags and links, raw HTML and lossy notes, run on the
// bundled editor.js in Jint (no browser, no DOM).
public sealed class EditorBundleTests : IClassFixture<EditorBundle>
{
    private readonly EditorBundle bundle;

    public EditorBundleTests(EditorBundle bundle)
    {
        this.bundle = bundle;
    }

    public static TheoryData<string> Lossless() => new(FixtureFiles("lossless"));

    public static TheoryData<string> Lossy() => new(FixtureFiles("lossy"));

    public static TheoryData<string> All() => new(FixtureFiles("lossless").Concat(FixtureFiles("lossy")));

    // AC-1: every lossless fixture comes back byte for byte (LF, as committed) and is reported lossless.
    [Theory]
    [MemberData(nameof(Lossless))]
    public void Lossless_RoundTripsByteForByte(string file)
    {
        string markdown = Read(file);

        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
        Assert.Equal("true", bundle.Call("isLossless", markdown));
    }

    // AC-1: the fixtures cover what the spec lists.
    [Fact]
    public void Lossless_FixturesCoverTheSpec()
    {
        string all = string.Concat(FixtureFiles("lossless").Select(Read));
        string[] needed =
        [
            "\n# ", "\n## ", "soft\nwrapped", "\n- ", "\n  - ", "\n1. ", "- [ ] ", "- [x] ", "*em*", "**strong**", "`inline code`",
            "```\n#not-a-tag [[Not a link]]", "\n> ", "[link](https://example.com/page)", "#tag", "#[tag with spaces]",
            "[[Note]]", "[[Note|alias]]", "C#", "#1", "a_b", "Literal [ bracket",
        ];

        Assert.All(needed, n => Assert.Contains(n, all, StringComparison.Ordinal));
        Assert.Contains(FixtureFiles("lossless"), file => !Read(file).EndsWith('\n'));
        Assert.Contains(FixtureFiles("lossless"), file => Read(file).EndsWith('\n'));
    }

    // AC-1: a few small cases beyond the fixtures.
    [Theory]
    [InlineData("")]
    [InlineData("x\n")]
    [InlineData("#tag\n")]
    [InlineData("#[tag with spaces]\n")]
    [InlineData("[[Note|alias]] and #x.\n")]
    [InlineData("1) item\n")]
    [InlineData("a * b and 2 * 3\n")]
    [InlineData("C# and F# and #1 and #\n")]
    [InlineData("![alt](pic.png)\n")]
    public void Small_RoundTrips(string markdown)
    {
        if (bundle.Call("isLossless", markdown) == "true")
        {
            Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
        }
        else
        {
            Assert.NotEqual(markdown, bundle.Call("roundTrip", markdown));
        }
    }

    // AC-2: the editor's tag and link nodes are exactly what TagParser and NoteLinkParser find, fixture by fixture.
    [Theory]
    [MemberData(nameof(All))]
    public void InlineNodes_MatchCoreParsers(string file)
    {
        string markdown = Read(file);
        using JsonDocument nodes = JsonDocument.Parse(bundle.Call("inlineNodes", markdown));

        Assert.Equal(
            TagParser.Parse(markdown).ToArray(),
            nodes.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToArray());
        Assert.Equal(
            NoteLinkParser.Parse(markdown).ToArray(),
            nodes.RootElement.GetProperty("links").EnumerateArray().Select(t => t.GetString()!).ToArray());
    }

    // AC-2: nothing inside a fenced block or a code span becomes a tag or link node.
    [Fact]
    public void InlineNodes_NothingInCode()
    {
        string markdown = Read("lossless/code.md");
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", markdown));

        List<JsonElement> code = Nodes(doc.RootElement).Where(n => Type(n) == "code_block").ToList();
        Assert.Equal(2, code.Count);
        Assert.All(code, c => Assert.DoesNotContain(Nodes(c).Skip(1), n => Type(n) is "tag" or "noteLink"));
        Assert.DoesNotContain(Nodes(doc.RootElement), n => Type(n) is "tag" or "noteLink");
        Assert.Contains(Nodes(doc.RootElement), n => Type(n) == "text" && Text(n) == "#code" && HasMark(n, "code"));
        Assert.Equal("{\"tags\":[],\"links\":[]}", bundle.Call("inlineNodes", "`#x [[y]]`\n\n```\n#z\n```\n"));
    }

    // AC-3: raw HTML is parsed as literal text, never as an html node, and round-trips unchanged.
    [Theory]
    [InlineData("<script>alert(1)</script>\n")]
    [InlineData("Inline <b>x</b> here.\n")]
    [InlineData("<img src=x onerror=alert(1)>\n")]
    [InlineData("a <img src=x onerror=alert(1)> b\n")]
    public void RawHtml_IsLiteralText(string markdown)
    {
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", markdown));

        Assert.DoesNotContain(Nodes(doc.RootElement), n => Type(n).Contains("html", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Nodes(doc.RootElement), n => Type(n) == "image");
        string text = string.Concat(Nodes(doc.RootElement).Where(n => Type(n) == "text").Select(Text));
        Assert.Equal(markdown.TrimEnd('\n'), text);
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
    }

    // AC-3: the html fixture has no html node either.
    [Fact]
    public void RawHtml_FixtureHasNoHtmlNode()
    {
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", Read("lossless/html.md")));

        Assert.DoesNotContain(Nodes(doc.RootElement), n => Type(n).Contains("html", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Nodes(doc.RootElement), n => Type(n) == "text" && Text(n).Contains("<b>x</b>", StringComparison.Ordinal));
    }

    // AC-4: the lossy fixtures are reported lossy, and they really change on a round trip.
    [Theory]
    [MemberData(nameof(Lossy))]
    public void Lossy_IsReportedLossy(string file)
    {
        string markdown = Read(file);

        Assert.Equal("false", bundle.Call("isLossless", markdown));
        Assert.NotEqual(markdown, bundle.Call("roundTrip", markdown));
    }

    // AC-4: the four lossy cases the spec names are fixtures.
    [Fact]
    public void Lossy_FixturesCoverTheSpec()
    {
        string all = string.Concat(FixtureFiles("lossy").Select(Read));

        Assert.Contains("* star item", all, StringComparison.Ordinal);
        Assert.Contains("\n=====", all, StringComparison.Ordinal);
        Assert.Contains("__bold__", all, StringComparison.Ordinal);
        Assert.Contains("|---|", all, StringComparison.Ordinal);
    }

    // Images are an image node (shown as a grey placeholder by the schema), not text the editor drops.
    [Fact]
    public void Image_IsKeptAsImageNode()
    {
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", "![alt](pic.png)\n"));

        JsonElement image = Assert.Single(Nodes(doc.RootElement), n => Type(n) == "image");
        Assert.Equal("pic.png", image.GetProperty("attrs").GetProperty("src").GetString());
    }

    private static List<string> FixtureFiles(string kind)
    {
        string folder = Path.Combine(RepoFiles.Root(), "tests", "Context.Desktop.Tests", "RoundTrip", kind);
        return Directory.EnumerateFiles(folder, "*.md")
            .Select(f => kind + "/" + Path.GetFileName(f))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    // 142 AC-1, AC-2: a chip is a span the keyboard can reach, with a role that says what Enter does.
    [Theory]
    [InlineData("tag", "#work", "chip tag accent", "button")]
    [InlineData("noteLink", "[[A|b]]", "chip note-link accent", "link")]
    public void ChipDom_IsAFocusableSpanWithARole(string kind, string raw, string cssClass, string role)
    {
        string call = $"ContextEditor.chipDom({JsonSerializer.Serialize(kind)}, {JsonSerializer.Serialize(raw)})";
        using JsonDocument spec = JsonDocument.Parse(bundle.Json(call));
        JsonElement[] parts = spec.RootElement.EnumerateArray().ToArray();

        Assert.Equal(3, parts.Length);
        Assert.Equal("span", parts[0].GetString());
        Dictionary<string, string?> attributes = parts[1].EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        Assert.Equal(4, attributes.Count);
        Assert.Equal(cssClass, attributes["class"]);
        Assert.Equal(raw, attributes["title"]);
        Assert.Equal("0", attributes["tabindex"]);
        Assert.Equal(role, attributes["role"]);
        Assert.Equal(raw, parts[2].GetString());
    }

    // 142 AC-3: only a bare Enter opens a focused chip.
    [Theory]
    [InlineData("{ key: 'Enter' }", true)]
    [InlineData("{ key: 'Enter', ctrlKey: false, metaKey: false, altKey: false, shiftKey: false }", true)]
    [InlineData("{ key: 'Tab' }", false)]
    [InlineData("{ key: ' ' }", false)]
    [InlineData("{ key: 'Escape' }", false)]
    [InlineData("{ key: 'Backspace' }", false)]
    [InlineData("{ key: 'a' }", false)]
    [InlineData("{ key: 'Enter', ctrlKey: true }", false)]
    [InlineData("{ key: 'Enter', metaKey: true }", false)]
    [InlineData("{ key: 'Enter', altKey: true }", false)]
    [InlineData("{ key: 'Enter', shiftKey: true }", false)]
    public void OpensChip_OnlyForEnterWithoutModifiers(string keyEvent, bool expected)
    {
        Assert.Equal(expected ? "true" : "false", bundle.Json($"ContextEditor.opensChip({keyEvent})"));
    }

    private static string Read(string file)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(RepoFiles.Root(), "tests", "Context.Desktop.Tests", "RoundTrip", file));
        return new System.Text.UTF8Encoding(false, true).GetString(bytes);
    }

    private static IEnumerable<JsonElement> Nodes(JsonElement node)
    {
        yield return node;
        if (node.TryGetProperty("content", out JsonElement content))
        {
            foreach (JsonElement child in content.EnumerateArray())
            {
                foreach (JsonElement nested in Nodes(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string Type(JsonElement node) => node.GetProperty("type").GetString()!;

    private static string Text(JsonElement node) => node.TryGetProperty("text", out JsonElement text) ? text.GetString()! : string.Empty;

    private static bool HasMark(JsonElement node, string mark) =>
        node.TryGetProperty("marks", out JsonElement marks) && marks.EnumerateArray().Any(m => m.GetProperty("type").GetString() == mark);
}
