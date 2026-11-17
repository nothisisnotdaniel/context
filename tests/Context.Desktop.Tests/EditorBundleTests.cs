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

    // 157 AC-11: the attributes fixture holds attribute lines, a [[link]] value, an empty placeholder and a fenced
    // x:: y, and the C# parser finds exactly the attributes outside the fence.
    [Fact]
    public void Lossless_AttributesFixture()
    {
        string markdown = Read("lossless/attributes.md");

        Assert.Contains("affiliation:: [[ACME]]\n", markdown, StringComparison.Ordinal);
        Assert.Contains("\nrole::\n", markdown, StringComparison.Ordinal);
        Assert.Contains("```\nx:: y\n```", markdown, StringComparison.Ordinal);
        Assert.Equal(
            ["affiliation", "role", "type", "start-date_2"],
            AttributeParser.Parse(markdown).Select(a => a.Key).ToArray());
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
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
    // 156 AC-9: so are its mention nodes and PersonParser.
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
        Assert.Equal(
            PersonParser.Parse(markdown).ToArray(),
            nodes.RootElement.GetProperty("mentions").EnumerateArray().Select(t => t.GetString()!).ToArray());
    }

    // 156 AC-9: the mentions fixture finds exactly the AC-1 mentions, and none of the cases that are no mention.
    [Fact]
    public void Mentions_Fixture_HasEveryCase()
    {
        string markdown = Read("lossless/mentions.md");

        Assert.Equal(["Anna Schmidt", "Bob Jones", "Frida", "Greta"], PersonParser.Parse(markdown));
        using JsonDocument nodes = JsonDocument.Parse(bundle.Call("inlineNodes", markdown));
        Assert.Equal(
            ["Anna Schmidt", "Bob Jones", "Frida", "Greta"],
            nodes.RootElement.GetProperty("mentions").EnumerateArray().Select(t => t.GetString()!).ToArray());
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
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
        Assert.Equal(
            "{\"tags\":[],\"links\":[],\"mentions\":[]}",
            bundle.Call("inlineNodes", "`#x [[y]] @[w]`\n\n```\n#z @[v]\n```\n"));
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

    // 177 AC-8: an attachment image keeps its stored name as alt and its source exactly, a PDF link its href, and the
    // fixture round-trips byte for byte (checked by Lossless_RoundTripsByteForByte too).
    [Fact]
    public void Attachments_FixtureKeepsNamesAndSources()
    {
        string markdown = Read("lossless/attachments.md");
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", markdown));

        List<JsonElement> images = Nodes(doc.RootElement).Where(n => Type(n) == "image").ToList();
        Assert.Equal(
            ["shot.png", "a_b-2.png", "_lead.jpg", "two.webp", "x.gif"],
            images.Select(i => i.GetProperty("attrs").GetProperty("alt").GetString()));
        Assert.All(images, i => Assert.Equal(
            ".attachments/" + i.GetProperty("attrs").GetProperty("alt").GetString(),
            i.GetProperty("attrs").GetProperty("src").GetString()));
        Assert.Equal(
            NoteAttachments.ImageSources(markdown),
            images.Select(i => i.GetProperty("attrs").GetProperty("src").GetString()!).ToArray());
        string[] hrefs = Nodes(doc.RootElement)
            .Where(n => n.TryGetProperty("marks", out _))
            .SelectMany(n => n.GetProperty("marks").EnumerateArray())
            .Where(m => m.GetProperty("type").GetString() == "link")
            .Select(m => m.GetProperty("attrs").GetProperty("href").GetString()!)
            .ToArray();
        Assert.Equal([".attachments/plan.pdf", ".attachments/a_b-3.pdf"], hrefs);
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
        Assert.Equal("true", bundle.Call("isLossless", markdown));
    }

    // 177 AC-8: every name Add can store round-trips as an image and as a PDF link.
    [Theory]
    [InlineData("shot.png")]
    [InlineData("_a_.png")]
    [InlineData("a__b.jpeg")]
    [InlineData("A-2.gif")]
    [InlineData("x_y_z-99.pdf")]
    public void Attachments_StoredNamesRoundTrip(string name)
    {
        Assert.True(NoteAttachments.IsStoredName(name));
        string image = $"![{name}](.attachments/{name})\n";
        string link = $"[{name}](.attachments/{name})\n";

        Assert.Equal(image, bundle.Call("roundTrip", image));
        Assert.Equal(link, bundle.Call("roundTrip", link));
    }

    // 177 AC-8/AC-12: a link whose name a move rewrote keeps its old label as written.
    [Theory]
    [InlineData("[a_b.pdf](.attachments/a_b-2.pdf)\n")]
    [InlineData("![_x_.png](.attachments/_x_-2.png) and [b_.pdf](.attachments/b_.pdf)\n")]
    public void Attachments_LabelsRoundTrip(string markdown)
    {
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
    }

    // 177 AC-10: at load the bundle blocks dragover and drop for the whole window, so a dropped file never navigates.
    [Fact]
    public void Bundle_BlocksWindowDragAndDrop()
    {
        string path = Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "editor", "editor.js");
        Engine engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(30)));
        engine.Execute("var added = []; globalThis.addEventListener = function (type, handler) { added.push([type, handler]); };");
        engine.Execute(File.ReadAllText(path));

        string result = engine.Evaluate(
            "JSON.stringify(added.map(function (a) { var e = { prevented: 0, preventDefault: function () { this.prevented++; } };"
            + " a[1](e); return [a[0], e.prevented]; }))").AsString();

        Assert.Equal("[[\"dragover\",1],[\"drop\",1]]", result);
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
    [InlineData("mention", "@[Anna]", "chip mention accent", "link")]
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

    // 202 AC-1: the first line "# <name>" (case and surrounding spaces ignored, or the name's last segment) is the
    // head, with its newline and the blank lines straight after it; head + body is the input.
    [Theory]
    [InlineData("# Ideas\n\nText\n", "Ideas", "# Ideas\n\n", "Text\n")]
    [InlineData("# ideas\nText\n", "Ideas", "# ideas\n", "Text\n")]
    [InlineData("#   Ideas  \n\n \t\nText\n", "IDEAS", "#   Ideas  \n\n \t\n", "Text\n")]
    [InlineData("# Anna\n\nNotes\n", "people/Anna", "# Anna\n\n", "Notes\n")]
    [InlineData("# people/Anna\n", "people/Anna", "# people/Anna\n", "")]
    [InlineData("# T", "T", "# T", "")]
    [InlineData("# T\n\n", "T", "# T\n\n", "")]
    [InlineData("# T\n\n\nBody\n", "T", "# T\n\n\n", "Body\n")]
    [InlineData("## Ideas\n\nText\n", "Ideas", "", "## Ideas\n\nText\n")]
    [InlineData("# Other\n\nText\n", "Ideas", "", "# Other\n\nText\n")]
    [InlineData("Ideas\n===\n\nText\n", "Ideas", "", "Ideas\n===\n\nText\n")]
    [InlineData("Ideas\n\n# Ideas\n", "Ideas", "", "Ideas\n\n# Ideas\n")]
    [InlineData("#Ideas\n", "Ideas", "", "#Ideas\n")]
    [InlineData("# Anna\n", "people/Ann", "", "# Anna\n")]
    [InlineData("\n# Ideas\n", "Ideas", "", "\n# Ideas\n")]
    [InlineData("", "Ideas", "", "")]
    public void SplitTitle_HidesOnlyAMatchingFirstLine(string markdown, string name, string head, string body)
    {
        using JsonDocument split = JsonDocument.Parse(SplitTitle(markdown, name));

        Assert.Equal(head, split.RootElement.GetProperty("head").GetString());
        Assert.Equal(body, split.RootElement.GetProperty("body").GetString());
    }

    public static TheoryData<string> LosslessWithTitles() => new(FixtureFiles("lossless").Select(Read).Concat(
        ["# T\n", "# T", "# T\n\n\nBody\n", "# T\n\n", "# T\n\n# T\n\nText\n", "# t\n- [ ] todo\n"]));

    // 202 AC-2: with the name T, every lossless fixture and every titled note round-trips byte for byte.
    [Theory]
    [MemberData(nameof(LosslessWithTitles))]
    public void TitleName_RoundTripsByteForByte(string markdown)
    {
        Assert.Equal(markdown, RoundTrip(markdown, "T"));
        Assert.Equal("true", bundle.Json($"ContextEditor.isLossless({Js(markdown)}, {Js("T")})"));
    }

    // 202 AC-2: a fixture with its own title as the name keeps its bytes too, and the title fixture hides its title.
    [Theory]
    [MemberData(nameof(Lossless))]
    public void OwnTitleName_RoundTripsByteForByte(string file)
    {
        string markdown = Read(file);
        string name = OwnTitle(markdown) ?? "T";

        Assert.Equal(markdown, RoundTrip(markdown, name));
        Assert.Equal("true", bundle.Json($"ContextEditor.isLossless({Js(markdown)}, {Js(name)})"));
    }

    // 202 AC-2: without a name nothing changes: "# T\n\n\nBody\n" is still lossy and keeps today's result.
    [Fact]
    public void NoName_BehavesAsBefore()
    {
        string markdown = "# T\n\n\nBody\n";

        Assert.Equal("false", bundle.Call("isLossless", markdown));
        Assert.Equal("true", bundle.Json($"ContextEditor.isLossless({Js(markdown)}, {Js("T")})"));
    }

    // 202 AC-3: the body the editor mounts never holds the hidden title.
    [Theory]
    [InlineData("# Ideas\n\nText\n", "Ideas")]
    [InlineData("# Title line\n\nText\n", "title line")]
    [InlineData("# Anna\n\nAnna notes\n", "people/Anna")]
    [InlineData("# T", "T")]
    public void MountedBody_HasNoHiddenTitle(string markdown, string name)
    {
        using JsonDocument split = JsonDocument.Parse(SplitTitle(markdown, name));
        string head = split.RootElement.GetProperty("head").GetString()!;
        string body = split.RootElement.GetProperty("body").GetString()!;
        Assert.NotEqual(string.Empty, head);

        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", body));

        Assert.DoesNotContain(Nodes(doc.RootElement), n => Type(n) == "heading" && n.GetProperty("attrs").GetProperty("level").GetInt32() == 1);
        Assert.DoesNotContain(head.TrimEnd('\n'), body, StringComparison.Ordinal);
    }

    // 202 AC-3: the title fixture's mounted body starts with its text, not its title.
    [Fact]
    public void TitleFixture_BodyStartsAfterTheTitle()
    {
        string markdown = Read("lossless/title-line.md");
        using JsonDocument split = JsonDocument.Parse(SplitTitle(markdown, "Title line"));
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", split.RootElement.GetProperty("body").GetString()!));

        JsonElement first = doc.RootElement.GetProperty("content")[0];
        Assert.Equal("paragraph", Type(first));
        Assert.DoesNotContain(Nodes(doc.RootElement), n => Type(n) == "heading" && string.Concat(Nodes(n).Select(Text)) == "Title line");
    }

    // 202 AC-4: a level-1 heading that is not hidden keeps level 1, is drawn as h2 and is saved as "# ".
    [Theory]
    [InlineData("Other")]
    [InlineData(null)]
    public void UnhiddenLevelOne_KeptAndDrawnAsH2(string? name)
    {
        string markdown = Read("lossless/headings.md");
        string nameArgument = name is null ? "undefined" : Js(name);
        using JsonDocument split = JsonDocument.Parse(bundle.Json($"JSON.parse(ContextEditor.splitTitle({Js(markdown)}, {nameArgument}))"));
        Assert.Equal(string.Empty, split.RootElement.GetProperty("head").GetString());
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", markdown));

        JsonElement heading = doc.RootElement.GetProperty("content")[0];
        Assert.Equal("heading", Type(heading));
        Assert.Equal(1, heading.GetProperty("attrs").GetProperty("level").GetInt32());
        Assert.Equal("[\"h2\",0]", bundle.Json("ContextEditor.headingDom(1)"));
        string saved = JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.roundTrip({Js(markdown)}, {nameArgument})"))!;
        Assert.StartsWith("# Project notes\n", saved, StringComparison.Ordinal);
        Assert.Equal(markdown, saved);
    }

    // 202 AC-4: a level-1 heading below a hidden title stays level 1 in the doc and is saved as "# ".
    [Fact]
    public void LevelOneBelowTitle_StaysLevelOne()
    {
        string markdown = Read("lossless/title-line.md");
        using JsonDocument split = JsonDocument.Parse(SplitTitle(markdown, "Title line"));
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", split.RootElement.GetProperty("body").GetString()!));

        JsonElement heading = Assert.Single(Nodes(doc.RootElement), n => Type(n) == "heading");
        Assert.Equal(1, heading.GetProperty("attrs").GetProperty("level").GetInt32());
        Assert.Contains("\n# Another level one\n", RoundTrip(markdown, "Title line"), StringComparison.Ordinal);
    }

    // 202 AC-4: every heading level is drawn at h2 or below.
    [Theory]
    [InlineData(1, "h2")]
    [InlineData(2, "h2")]
    [InlineData(3, "h3")]
    [InlineData(6, "h6")]
    public void HeadingDom_NeverH1(int level, string tag)
    {
        Assert.Equal($"[\"{tag}\",0]", bundle.Json($"ContextEditor.headingDom({level})"));
    }

    // 202 AC-5: typing "# " or "## " makes a level-2 heading, "### " level 3; no typed input gives level 1.
    [Theory]
    [InlineData("# ", 2)]
    [InlineData("## ", 2)]
    [InlineData("### ", 3)]
    [InlineData("#### ", 4)]
    [InlineData("###### ", 6)]
    [InlineData("# Title", 2)]
    public void TypedBlock_HeadingLevels(string typed, int level)
    {
        using JsonDocument doc = JsonDocument.Parse(TypedBlock(typed));

        JsonElement heading = Assert.Single(doc.RootElement.GetProperty("content").EnumerateArray());
        Assert.Equal("heading", Type(heading));
        Assert.Equal(level, heading.GetProperty("attrs").GetProperty("level").GetInt32());
    }

    // 202 AC-5: other input stays a paragraph or another block, never a level-1 heading.
    [Theory]
    [InlineData("#")]
    [InlineData("####### ")]
    [InlineData("#tag ")]
    [InlineData("text # ")]
    [InlineData("- # ")]
    [InlineData("> # ")]
    [InlineData("Ideas")]
    public void TypedBlock_NeverLevelOne(string typed)
    {
        using JsonDocument doc = JsonDocument.Parse(TypedBlock(typed));

        Assert.DoesNotContain(Nodes(doc.RootElement), n => Type(n) == "heading" && n.GetProperty("attrs").GetProperty("level").GetInt32() < 2);
    }

    // 202 AC-6: no keymap, input rule or command in the editor sources sets a heading to level 1.
    [Fact]
    public void Sources_NeverSetLevelOne()
    {
        string folder = Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "editor", "src");
        string[] files = ["index.js", "markdown.js", "schema.js", "view.js"];
        Assert.Equal(files, Directory.EnumerateFiles(folder, "*.js").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray());

        foreach (string file in files)
        {
            string text = File.ReadAllText(Path.Combine(folder, file));
            Assert.DoesNotContain("level: 1", text, StringComparison.Ordinal);
            Assert.DoesNotContain("{ level: m[1].length }", text, StringComparison.Ordinal);
        }

        Assert.Contains("level: { default: 1 }", File.ReadAllText(Path.Combine(folder, "schema.js")), StringComparison.Ordinal);
        Assert.Contains("Math.max(m[1].length, 2)", File.ReadAllText(Path.Combine(folder, "view.js")), StringComparison.Ordinal);
    }

    private string SplitTitle(string markdown, string name) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.splitTitle({Js(markdown)}, {Js(name)})"))!;

    private string RoundTrip(string markdown, string name) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.roundTrip({Js(markdown)}, {Js(name)})"))!;

    private string TypedBlock(string text) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.typedBlock({Js(text)})"))!;

    private static string Js(string text) => JsonSerializer.Serialize(text);

    private static string? OwnTitle(string markdown)
    {
        string first = markdown.Split('\n')[0];
        return first.StartsWith("# ", StringComparison.Ordinal) ? first[2..].Trim() : null;
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
