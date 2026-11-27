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

    /// <summary>
    /// Evaluates a JavaScript expression that gives a promise of a string and returns the string it settles to; Jint
    /// runs the promise's jobs until it has settled (no delay, no timer), and a rejection throws.
    /// </summary>
    public string Settled(string expression)
    {
        lock (engine)
        {
            return engine.Evaluate(expression).UnwrapIfPromise().AsString();
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

    // 489 AC-4: the built bundle reports the editor's top scroll margin (the sticky bar's 44px plus 5).
    [Fact]
    public void EditorBarMargin_Is49()
    {
        Assert.Equal("49", bundle.Json("ContextEditor.EditorBarMargin"));
    }

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
            // 290 AC-3: escapes.md and escaped-pipe.md keep their escapes and entities.
            "\\*", "\\`", "\\_", "\\#", "\\[", "\\\\", "&amp;", "a \\| b",
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

    // Job 384 AC-8: isDrawingSrc matches only a stored .excalidraw name right under .attachments/.
    [Theory]
    [InlineData(".attachments/d.excalidraw", true)]
    [InlineData(".attachments/My-Sketch_1.excalidraw", true)]
    [InlineData(".attachments/d.excalidraw.png", false)]
    [InlineData(".attachments/d.png", false)]
    [InlineData(".attachments/a/d.excalidraw", false)]
    [InlineData(".attachments/a\\d.excalidraw", false)]
    [InlineData("other/d.excalidraw", false)]
    [InlineData("x.attachments/d.excalidraw", false)]
    [InlineData(".attachments/d.excalidraw ", false)]
    [InlineData(".attachments/.excalidraw", false)]
    [InlineData(".attachments/d.EXCALIDRAW", false)]
    public void IsDrawingSrc_OnlyStoredDrawings(string src, bool expected)
    {
        Assert.Equal(expected ? "true" : "false", bundle.Json($"ContextEditor.isDrawingSrc({Js(src)})"));
    }

    // Job 384 AC-8/AC-11: a drawing image link is an image node and round-trips byte for byte.
    [Theory]
    [InlineData("![x](.attachments/d.excalidraw)\n")]
    [InlineData("# Plan\n\nSee ![My-Sketch.excalidraw](.attachments/My-Sketch.excalidraw) here.\n")]
    public void Drawing_IsImageNode_AndRoundTrips(string markdown)
    {
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", markdown));

        JsonElement image = Assert.Single(Nodes(doc.RootElement), n => Type(n) == "image");
        Assert.EndsWith(".excalidraw", image.GetProperty("attrs").GetProperty("src").GetString(), StringComparison.Ordinal);
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
    }

    // Job 384 AC-8: the image node view of a drawing with a URL is the img plus an "Open in Excalidraw" button whose
    // click only asks .NET to open the source; without a URL it is the placeholder; another image is the plain img.
    [Theory]
    [InlineData(".attachments/d.excalidraw", true)]
    [InlineData(".attachments/d.excalidraw", false)]
    [InlineData(".attachments/shot.png", true)]
    public void ImageView_Drawing_OpenButton(string src, bool hasUrl)
    {
        string json = bundle.Json($$"""
            (() => {
              const element = tag => ({
                tagName: tag.toUpperCase(), className: "", textContent: "", children: [], listeners: [],
                appendChild(child) { this.children.push(child); return child; },
                addEventListener(type, listener) { this.listeners.push({ type, listener }); },
                setAttribute(name, value) { this.attrs = { ...(this.attrs || {}), [name]: String(value) }; },
              });
              globalThis.document = { createElement: element };
              const calls = [];
              const dispatches = [];
              const images = new Map();
              if ({{(hasUrl ? "true" : "false")}}) images.set({{Js(src)}}, "blob:drawing");
              const editor = {
                images,
                dotnet: { invokeMethodAsync(...args) { calls.push(args); return undefined; } },
                view: { dispatch(tr) { dispatches.push(tr); } },
              };
              try {
                const view = ContextEditor.imageViewTest(editor)({ attrs: { src: {{Js(src)}}, alt: "Sketch" } });
                const dom = view.dom;
                const describe = e => ({ tag: e.tagName, cls: e.className, text: e.textContent, alt: e.alt ?? null,
                  src: e.src ?? null, type: e.type ?? null, label: (e.attrs || {})["aria-label"] ?? null,
                  listeners: e.listeners.map(l => l.type) });
                const result = { dom: describe(dom), children: dom.children.map(describe) };
                const button = dom.children.find(c => c.className === "drawing-open");
                if (button) {
                  const event = { target: button, prevented: 0, stopped: 0,
                    preventDefault() { this.prevented++; }, stopPropagation() { this.stopped++; } };
                  button.listeners.find(l => l.type === "click").listener(event);
                  result.prevented = event.prevented;
                  result.stopped = event.stopped;
                  result.stopsButton = view.stopEvent({ target: button });
                  result.stopsImage = view.stopEvent({ target: dom.children[0] });
                }
                result.calls = calls;
                result.dispatches = dispatches.length;
                return result;
              } finally {
                delete globalThis.document;
              }
            })()
            """);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        JsonElement dom = root.GetProperty("dom");
        Assert.Equal(0, root.GetProperty("dispatches").GetInt32());
        Assert.Equal("\"undefined\"", bundle.Json("typeof document"));
        if (!hasUrl)
        {
            Assert.Equal(("SPAN", "image-placeholder grey", "[image: Sketch]"),
                (dom.GetProperty("tag").GetString(), dom.GetProperty("cls").GetString(), dom.GetProperty("text").GetString()));
            Assert.Empty(root.GetProperty("children").EnumerateArray());
            Assert.Empty(root.GetProperty("calls").EnumerateArray());
            return;
        }

        if (!src.EndsWith(".excalidraw", StringComparison.Ordinal))
        {
            Assert.Equal(("IMG", "blob:drawing", "Sketch"),
                (dom.GetProperty("tag").GetString(), dom.GetProperty("src").GetString(), dom.GetProperty("alt").GetString()));
            Assert.Empty(root.GetProperty("children").EnumerateArray());
            return;
        }

        Assert.Equal(("SPAN", "drawing"), (dom.GetProperty("tag").GetString(), dom.GetProperty("cls").GetString()));
        JsonElement[] children = root.GetProperty("children").EnumerateArray().ToArray();
        Assert.Equal(2, children.Length);
        Assert.Equal(("IMG", "blob:drawing", "Sketch"),
            (children[0].GetProperty("tag").GetString(), children[0].GetProperty("src").GetString(), children[0].GetProperty("alt").GetString()));
        Assert.Equal(("BUTTON", "drawing-open", "Open in Excalidraw", "button"),
            (children[1].GetProperty("tag").GetString(), children[1].GetProperty("cls").GetString(),
                children[1].GetProperty("text").GetString(), children[1].GetProperty("type").GetString()));
        Assert.Equal("Open Sketch in Excalidraw", children[1].GetProperty("label").GetString());
        Assert.Equal(1, root.GetProperty("prevented").GetInt32());
        Assert.Equal(1, root.GetProperty("stopped").GetInt32());
        Assert.True(root.GetProperty("stopsButton").GetBoolean());
        Assert.False(root.GetProperty("stopsImage").GetBoolean());
        JsonElement call = Assert.Single(root.GetProperty("calls").EnumerateArray());
        Assert.Equal(["OnAttachment", src], call.EnumerateArray().Select(a => a.GetString()));
    }

    // 177 AC-8/AC-12: a link whose name a move rewrote keeps its old label as written.
    [Theory]
    [InlineData("[a_b.pdf](.attachments/a_b-2.pdf)\n")]
    [InlineData("![_x_.png](.attachments/_x_-2.png) and [b_.pdf](.attachments/b_.pdf)\n")]
    public void Attachments_LabelsRoundTrip(string markdown)
    {
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
    }

    // 177 AC-10, 224 AC-1/AC-4: at load the bundle registers dragover, drop and contextmenu on the window, one listener
    // each (364: and keydown, see KeyDown_CtrlOrCmdP_NeverPrints), and dragover and drop are still blocked, so a dropped
    // file never navigates.
    [Fact]
    public void Bundle_RegistersWindowListeners_AndBlocksDragAndDrop()
    {
        Engine engine = LoadWithRecordedListeners();

        string types = engine.Evaluate("JSON.stringify(added.map(function (a) { return a[0]; }))").AsString();
        string drag = engine.Evaluate(
            "JSON.stringify(added.filter(function (a) { return a[0] === 'dragover' || a[0] === 'drop'; }).map(function (a) {"
            + " var e = { prevented: 0, preventDefault: function () { this.prevented++; } }; a[1](e); return [a[0], e.prevented]; }))")
            .AsString();

        Assert.Equal("[\"dragover\",\"drop\",\"keydown\",\"contextmenu\"]", types);
        Assert.Equal("[[\"dragover\",1],[\"drop\",1]]", drag);
    }

    // 364 AC-10: the window keydown listener prevents the default of Ctrl/Cmd+P (no Alt or Shift) only, so the web
    // view never prints; every other key keeps its default.
    [Theory]
    [InlineData("{ key: 'p', ctrlKey: true }", 1)]
    [InlineData("{ key: 'p', metaKey: true }", 1)]
    [InlineData("{ key: 'P', ctrlKey: true }", 1)]
    [InlineData("{ key: 'p', ctrlKey: true, shiftKey: true }", 0)]
    [InlineData("{ key: 'p', metaKey: true, altKey: true }", 0)]
    [InlineData("{ key: 'p' }", 0)]
    [InlineData("{ key: 'k', ctrlKey: true }", 0)]
    [InlineData("{ ctrlKey: true }", 0)]
    public void KeyDown_CtrlOrCmdP_NeverPrints(string fields, double expected)
    {
        Engine engine = LoadWithRecordedListeners();

        double prevented = engine.Evaluate(
            "(function () { var e = " + fields + "; e.prevented = 0; e.preventDefault = function () { this.prevented++; };"
            + " added.filter(function (a) { return a[0] === 'keydown'; }).forEach(function (a) { a[1](e); }); return e.prevented; })()")
            .AsNumber();

        Assert.Equal(expected, prevented);
    }

    // 224 AC-2: a right click on a plain element, on a target without closest, or with no target shows no menu.
    [Theory]
    [InlineData("{ closest: function (s) { seen = s; return null; } }")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("undefined")]
    public void ContextMenu_PreventedOutsideTextEditing(string target)
    {
        Engine engine = LoadWithRecordedListeners();

        double prevented = engine.Evaluate(ContextMenuCall(target)).AsNumber();

        Assert.Equal(1, prevented);
    }

    // 224 AC-3: in an input or inside the ProseMirror editor the platform's text menu stays, and the selector is exact.
    [Theory]
    [InlineData("{ tagName: 'INPUT' }")]
    [InlineData("{ className: 'ProseMirror', contentEditable: 'true' }")]
    public void ContextMenu_AllowedInTextEditing(string match)
    {
        Engine engine = LoadWithRecordedListeners();

        double prevented = engine.Evaluate(
            ContextMenuCall("{ closest: function (s) { seen = s; return " + match + "; } }")).AsNumber();

        Assert.Equal(0, prevented);
        Assert.Equal("input, [contenteditable=\"true\"]", engine.Evaluate("seen").AsString());
    }

    // A fresh engine per test with a recording globalThis.addEventListener, then the committed bundle.
    private static Engine LoadWithRecordedListeners()
    {
        string path = Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "editor", "editor.js");
        Engine engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(30)));
        engine.Execute("var seen = null; var added = []; globalThis.addEventListener = function (type, handler) { added.push([type, handler]); };");
        engine.Execute(File.ReadAllText(path));
        return engine;
    }

    // Calls every recorded contextmenu listener with a fake event for the target; gives the preventDefault count.
    private static string ContextMenuCall(string target) =>
        "(function () { var e = { target: " + target + ", prevented: 0, preventDefault: function () { this.prevented++; } };"
        + " added.filter(function (a) { return a[0] === 'contextmenu'; }).forEach(function (a) { a[1](e); }); return e.prevented; })()";

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
        Assert.Equal("[\"h2\",{\"class\":\"heading-level-1\"},0]", bundle.Json("ContextEditor.headingDom(1)"));
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

    // 202 AC-4, 215 AC-1: every heading level is drawn at h2 or below; level 1 is an h2 with the class heading-level-1.
    [Theory]
    [InlineData(1, "[\"h2\",{\"class\":\"heading-level-1\"},0]")]
    [InlineData(2, "[\"h2\",0]")]
    [InlineData(3, "[\"h3\",0]")]
    [InlineData(4, "[\"h4\",0]")]
    [InlineData(5, "[\"h5\",0]")]
    [InlineData(6, "[\"h6\",0]")]
    public void HeadingDom_NeverH1(int level, string expected)
    {
        string dom = bundle.Json($"ContextEditor.headingDom({level})");
        Assert.Equal(expected, dom);
        Assert.DoesNotContain("\"h1\"", dom, StringComparison.Ordinal);
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
        string[] files = ["decisions.js", "glossary.js", "highlight.js", "index.js", "markdown.js", "mermaid-frame.js", "mermaid.js", "paste.js", "schema.js", "theme.js", "view.js"];
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

    // 236 AC-1: the leading block's rows (key as written, value trimmed), the body without it, later lines kept.
    [Fact]
    public void AttributeRows_LeadingBlockOnly()
    {
        string markdown = "# P\n\naliases:: A, B\ntype:: person\nrole::\n\nBody\nrole:: x\n";

        Assert.Equal(
            [("aliases", "A, B"), ("type", "person"), ("role", string.Empty)],
            Rows(bundle.Json($"ContextEditor.attributeRows({Js(markdown)}, {Js("P")})")));
        (string head, string attrs, string body) = Split(markdown, "P");
        Assert.Equal(("# P\n\n", "aliases:: A, B\ntype:: person\nrole::\n\n", "Body\nrole:: x\n"), (head, attrs, body));
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", body));
        JsonElement[] blocks = doc.RootElement.GetProperty("content").EnumerateArray().ToArray();
        string first = string.Concat(Nodes(blocks[0]).Select(Text));
        Assert.StartsWith("Body", first, StringComparison.Ordinal);
        Assert.Contains("role:: x", string.Concat(blocks.SelectMany(Nodes).Select(Text)), StringComparison.Ordinal);
    }

    // 236 AC-1: without a title head the block starts at the very first line; a key shape per KeyEnd, at most 50 chars.
    [Theory]
    [InlineData("role:: x\nText\n", "role", "x")]
    [InlineData("Start-Date_2::   2026  \n", "Start-Date_2", "2026")]
    [InlineData("\u00e9t\u00e9:: v", "\u00e9t\u00e9", "v")]
    [InlineData("a::b\n", "a", "b")]
    public void AttributeRows_NoTitle_FirstLine(string markdown, string key, string value)
    {
        Assert.Equal([(key, value)], Rows(bundle.Json($"ContextEditor.attributeRows({Js(markdown)}, {Js("T")})")));
    }

    // 236 AC-1: a first body line that is not attribute-shaped gives no rows and leaves the body as it is.
    [Theory]
    [InlineData("# T\n\nText\nrole:: x\n")]
    [InlineData("\nrole:: x\n")]
    [InlineData("```\nrole:: x\n```\n")]
    [InlineData(" role:: x\n")]
    [InlineData("# T\n\n role:: x\n")]
    [InlineData("1role:: x\n")]
    [InlineData("role : x\n")]
    [InlineData("role: x\n")]
    [InlineData("-role:: x\n")]
    [InlineData("# Other\n\nrole:: x\n")]
    public void AttributeRows_NoBlock_BodyUnchanged(string markdown)
    {
        Assert.Empty(Rows(bundle.Json($"ContextEditor.attributeRows({Js(markdown)}, {Js("T")})")));
        (string head, string attrs, string body) = Split(markdown, "T");
        Assert.Equal(string.Empty, attrs);
        Assert.Equal(markdown, head + body);
        Assert.Equal(JsonDocument.Parse(SplitTitle(markdown, "T")).RootElement.GetProperty("body").GetString(), body);
    }

    // 236 AC-1: a key over 50 chars is no attribute line; 50 is.
    [Fact]
    public void AttributeRows_KeyLength()
    {
        string fifty = "k" + new string('a', 49);

        Assert.Single(Rows(bundle.Json($"ContextEditor.attributeRows({Js(fifty + ":: v\n")}, {Js("T")})")));
        Assert.Empty(Rows(bundle.Json($"ContextEditor.attributeRows({Js(fifty + "a:: v\n")}, {Js("T")})")));
    }

    // 236 AC-2: for every lossless fixture (with its own title as the name, and with T) head + attrs + body is the input.
    [Theory]
    [MemberData(nameof(Lossless))]
    public void AttributeSplit_JoinsBackExactly(string file)
    {
        string markdown = Read(file);
        foreach (string name in new[] { OwnTitle(markdown) ?? "T", "T" })
        {
            (string head, string attrs, string body) = Split(markdown, name);
            Assert.Equal(markdown, head + attrs + body);
        }
    }

    // 236 AC-2: the new fixture holds a title, a block with empty values and [[Link]] values, and later attribute lines;
    // its block is split off.
    [Fact]
    public void AttributeBlockFixture_CoversTheSpec()
    {
        string markdown = Read(Path.Combine("lossless", "attribute-block.md"));
        (string head, string attrs, string body) = Split(markdown, "Attribute block");

        Assert.Equal("# Attribute block\n\n", head);
        Assert.DoesNotContain('\r', markdown);
        Assert.Contains("role::\n", attrs, StringComparison.Ordinal);
        Assert.Contains("[[ACME]]", attrs, StringComparison.Ordinal);
        Assert.Contains("later:: ", body, StringComparison.Ordinal);
        Assert.Equal(5, Rows(bundle.Json($"ContextEditor.attributeRows({Js(markdown)}, {Js("Attribute block")})")).Count);
        Assert.Equal(markdown, RoundTrip(markdown, "Attribute block"));
    }

    // 236 AC-3: a changed value rewrites only its line; the other rows keep their bytes (LF and CRLF).
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void EditAttributes_ChangedValue_OnlyThatLine(string eol)
    {
        string attrs = $"aliases::  A,  B {eol}type:: person{eol}role::{eol}{eol}";

        string? edited = Edit(attrs, Edits((0, "aliases", "A,  B"), (1, "type", "company"), (2, "role", string.Empty)));

        Assert.Equal($"aliases::  A,  B {eol}type:: company{eol}role::{eol}{eol}", edited);
        AssertParsed(edited!, ("aliases", "A, B"), ("type", "company"));
    }

    // 236 AC-3: an empty value is written "key::"; nothing changed keeps every byte.
    [Fact]
    public void EditAttributes_EmptyValueAndUnchanged()
    {
        string attrs = "Type::\tperson\nrole::\n";

        Assert.Equal(attrs, Edit(attrs, Edits((0, "Type", "person"), (1, "role", string.Empty))));
        Assert.Equal("Type::\nrole::\n", Edit(attrs, Edits((0, "Type", string.Empty), (1, "role", string.Empty))));
    }

    // 236 AC-3: an added row goes after the last row, before the trailing blank lines, with the block's line ending.
    [Theory]
    [InlineData("type:: person\n\n\n", "type:: person\nstart:: 2026\n\n\n")]
    [InlineData("type:: person\r\n\r\n", "type:: person\r\nstart:: 2026\r\n\r\n")]
    [InlineData("type:: person", "type:: person\nstart:: 2026\n")]
    [InlineData("type:: person\r\n  ", "type:: person\r\nstart:: 2026\r\n  ")]
    public void EditAttributes_Add_BeforeTrailingBlanks(string attrs, string expected)
    {
        string? edited = Edit(attrs, Edits((0, "type", "person"), (-1, "start", "2026")));

        Assert.Equal(expected, edited);
        AssertParsed(edited!, ("type", "person"), ("start", "2026"));
    }

    // 236 AC-3: removing a row drops its line; removing all drops the block and its trailing blank lines.
    [Fact]
    public void EditAttributes_Remove()
    {
        string attrs = "aliases:: A\ntype:: person\nrole:: lead\n\n";

        string? edited = Edit(attrs, Edits((0, "aliases", "A"), (2, "role", "lead")));
        Assert.Equal("aliases:: A\nrole:: lead\n\n", edited);
        AssertParsed(edited!, ("aliases", "A"), ("role", "lead"));
        Assert.Equal(string.Empty, Edit(attrs, "[]"));
        Assert.Equal("aliases:: A\n\n", Edit("aliases:: A\ntype:: x", Edits((0, "aliases", "A"))) + "\n");
    }

    // 236 AC-3: a key or value with a line break or another control character can never add a line.
    [Fact]
    public void EditAttributes_ControlCharacters_StayOnOneLine()
    {
        string? edited = Edit("type:: person\n", Edits((0, "type", "a\nb\r\u2028c\u0000"), (-1, "role", "x\ninjected:: y")));

        Assert.Equal("type:: a b  c\nrole:: x injected:: y\n", edited);
        AssertParsed(edited!, ("type", "a b c"), ("role", "x injected:: y"));
    }

    // 236 AC-3: malformed edits change nothing (null).
    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[{\"line\":5,\"key\":\"a\",\"value\":\"b\"}]")]
    [InlineData("[{\"line\":-2,\"key\":\"a\",\"value\":\"b\"}]")]
    [InlineData("[{\"line\":0.5,\"key\":\"a\",\"value\":\"b\"}]")]
    [InlineData("[{\"line\":-1,\"key\":1,\"value\":\"b\"}]")]
    [InlineData("[{\"line\":-1,\"key\":\"a\"}]")]
    [InlineData("[{\"line\":-1,\"key\":\"1a\",\"value\":\"b\"}]")]
    [InlineData("[{\"line\":-1,\"key\":\"a b\",\"value\":\"b\"}]")]
    [InlineData("[{\"line\":-1,\"key\":\"\",\"value\":\"b\"}]")]
    [InlineData("[null]")]
    public void EditAttributes_Malformed_Null(string edits)
    {
        Assert.Null(Edit("type:: person\n", edits));
    }

    // 236 AC-3: an edited block parses back to the same rows, and the C# parser reads the same pairs from the whole note.
    [Fact]
    public void EditAttributes_WholeNote_ParsesAsExpected()
    {
        string markdown = "# P\n\naliases:: A, B\ntype:: person\nrole::\n\nBody\nlater:: z\n";
        (string head, string attrs, string body) = Split(markdown, "P");

        string edited = Edit(attrs, Edits((0, "aliases", "A, B"), (2, "role", "Lead"), (-1, "affiliation", "[[ACME]]")))!;

        Assert.Equal(
            [("aliases", "A, B"), ("role", "Lead"), ("affiliation", "[[ACME]]")],
            Rows(bundle.Json($"ContextEditor.attributeRows({Js(head + edited + body)}, {Js("P")})")));
        Assert.Equal(
            [("aliases", "A, B"), ("role", "Lead"), ("affiliation", "[[ACME]]"), ("later", "z")],
            AttributeParser.Parse(head + edited + body).Select(a => (a.Key, a.Value)));
    }

    private (string Head, string Attrs, string Body) Split(string markdown, string name)
    {
        using JsonDocument title = JsonDocument.Parse(SplitTitle(markdown, name));
        string head = title.RootElement.GetProperty("head").GetString()!;
        using JsonDocument split = JsonDocument.Parse(JsonSerializer.Deserialize<string>(
            bundle.Json($"ContextEditor.attributeSplitJson({Js(title.RootElement.GetProperty("body").GetString()!)})"))!);
        return (head, split.RootElement.GetProperty("attrs").GetString()!, split.RootElement.GetProperty("body").GetString()!);
    }

    private static List<(string Key, string Value)> Rows(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(JsonSerializer.Deserialize<string>(json)!);
        return doc.RootElement.EnumerateArray()
            .Select(r => (r.GetProperty("key").GetString()!, r.GetProperty("value").GetString()!)).ToList();
    }

    private string? Edit(string attrs, string edits) =>
        JsonSerializer.Deserialize<string?>(bundle.Json($"ContextEditor.editAttributes({Js(attrs)}, {Js(edits)})"));

    private static string Edits(params (int Line, string Key, string Value)[] edits) =>
        JsonSerializer.Serialize(edits.Select(e => new { line = e.Line, key = e.Key, value = e.Value }));

    private static void AssertParsed(string text, params (string Key, string Value)[] expected) =>
        Assert.Equal(expected.Select(e => (e.Key.ToLowerInvariant(), e.Value)), AttributeParser.Parse(text).Select(a => (a.Key, a.Value)));

    private string SplitTitle(string markdown, string name) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.splitTitle({Js(markdown)}, {Js(name)})"))!;

    private string RoundTrip(string markdown, string name) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.roundTrip({Js(markdown)}, {Js(name)})"))!;

    private string Outline(string markdown, string name) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.outline({Js(markdown)}, {Js(name)})"))!;

    private static List<string> Headings(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(h => $"{h.GetProperty("level").GetInt32()}|{h.GetProperty("text").GetString()}")
            .ToList();
    }

    // 205 AC-11: the body's headings without the hidden title, chips as raw text, marks dropped, code left out.
    [Fact]
    public void Outline_BodyHeadings_NoTitle_ChipsRaw()
    {
        string markdown = "# Plan\n\n## A\ntext\n### **B** #x\n```\n# not\n```\n";

        Assert.Equal(["2|A", "3|B #x"], Headings(Outline(markdown, "Plan")));
    }

    // 205 AC-11: without a matching name the first line is a heading of the body.
    [Fact]
    public void Outline_OtherName_KeepsFirstHeading()
    {
        Assert.Equal(["1|Plan", "2|A"], Headings(Outline("# Plan\n\n## A\n", "Other")));
        Assert.Equal([], Headings(Outline("", "Plan")));
    }

    // 205 AC-11: a heading link survives the round trip.
    [Theory]
    [InlineData("[[Plan#Goals]]\n")]
    [InlineData("See [[Plan#Next steps]] and [[#Intro]].\n")]
    public void RoundTrip_HeadingLink_Unchanged(string markdown)
    {
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
    }

    // 205: the page functions exist in the bundle.
    [Fact]
    public void Bundle_HasHeadingFunctions()
    {
        Assert.Equal("[\"function\",\"function\",\"function\"]", bundle.Json(
            "[typeof ContextEditor.outline, typeof ContextEditor.headings, typeof ContextEditor.scrollToHeading]"));
    }

    // 232 AC-1: a typed @[Name] is the same mention node the parser makes.
    [Fact]
    public void TypedBlock_Mention_SameAsParsed()
    {
        string typed = "@[Anna Schmidt]";

        Assert.Equal(["mention|@[Anna Schmidt]|Anna Schmidt"], Children(TypedBlock(typed)));
        using JsonDocument typedDoc = JsonDocument.Parse(TypedBlock(typed));
        using JsonDocument parsedDoc = JsonDocument.Parse(bundle.Call("docJson", typed));
        JsonElement typedNode = Nodes(typedDoc.RootElement).Single(n => Type(n) == "mention");
        JsonElement parsedNode = Nodes(parsedDoc.RootElement).Single(n => Type(n) == "mention");
        Assert.Equal(parsedNode.GetRawText(), typedNode.GetRawText());
    }

    // 232 AC-2, AC-3: the chip appears on ']', between the text around it; the name is normalized, raw kept.
    [Theory]
    [InlineData("@[Anna", new[] { "text|@[Anna" })]
    [InlineData("Met @[Anna] today", new[] { "text|Met ", "mention|@[Anna]|Anna", "text| today" })]
    [InlineData("@[  Anna   Schmidt ]", new[] { "mention|@[  Anna   Schmidt ]|Anna Schmidt" })]
    [InlineData("a\t@[Anna]", new[] { "text|a\t", "mention|@[Anna]|Anna" })]
    public void TypedBlock_Mention_Children(string typed, string[] children)
    {
        Assert.Equal(children, Children(TypedBlock(typed)));
    }

    public static TheoryData<string> NoMentionTyped() => new()
    {
        "a@[Anna]",
        "@[]",
        "@[ ]",
        "@[a/b]",
        "@[.Anna]",
        "@[Anna.]",
        "@[CON]",
        "@[" + new string('a', 101) + "]",
    };

    // 232 AC-4: what the parser refuses stays plain text.
    [Theory]
    [MemberData(nameof(NoMentionTyped))]
    public void TypedBlock_NoMention_PlainText(string typed)
    {
        using JsonDocument doc = JsonDocument.Parse(TypedBlock(typed));

        Assert.DoesNotContain(Nodes(doc.RootElement), n => Type(n) == "mention");
        Assert.Equal(typed, string.Concat(Nodes(doc.RootElement).Select(Text)));
    }

    // 232 AC-4: no chip in a code block.
    [Fact]
    public void TypedBlock_NoMention_InCodeBlock()
    {
        using JsonDocument doc = JsonDocument.Parse(TypedBlock("``` \n@[Anna]"));

        Assert.Equal("code_block", Type(Assert.Single(doc.RootElement.GetProperty("content").EnumerateArray())));
        Assert.DoesNotContain(Nodes(doc.RootElement), n => Type(n) == "mention");
        Assert.Equal("\n@[Anna]", string.Concat(Nodes(doc.RootElement).Select(Text)));
    }

    // 232 AC-5: '(' straight after the chip turns it back into text; any other character keeps it.
    [Theory]
    [InlineData("@[Anna](", new[] { "text|@[Anna](" })]
    [InlineData("Met @[Anna](", new[] { "text|Met @[Anna](" })]
    [InlineData("@[Anna] ", new[] { "mention|@[Anna]|Anna", "text| " })]
    [InlineData("@[Anna].", new[] { "mention|@[Anna]|Anna", "text|." })]
    [InlineData("@[Anna] (", new[] { "mention|@[Anna]|Anna", "text| (" })]
    [InlineData("#tag(", new[] { "text|#tag(" })]
    [InlineData("[[Plan]](", new[] { "noteLink|[[Plan]]", "text|(" })]
    public void TypedBlock_Mention_Paren(string typed, string[] children)
    {
        Assert.Equal(children, Children(TypedBlock(typed)));
    }

    public static TheoryData<string> MentionTyped()
    {
        TheoryData<string> data = new()
        {
            // "@[Anna] x", not "@[Anna] ": the parser drops a paragraph's trailing space whatever comes before it.
            "@[Anna Schmidt]", "@[Anna", "Met @[Anna] today", "@[  Anna   Schmidt ]", "@[Anna](", "@[Anna] x", "@[Anna].",
            "@[Anna] (",
        };
        foreach (string typed in NoMentionTyped())
        {
            data.Add(typed);
        }

        return data;
    }

    // 232 AC-6: saving a typed note writes what a saved and reopened note gives (a saved note ends with "\n").
    [Theory]
    [MemberData(nameof(MentionTyped))]
    public void TypedMarkdown_EqualsRoundTrip(string typed)
    {
        Assert.Equal(bundle.Call("roundTrip", typed + "\n"), TypedMarkdown(typed));
    }

    // 232 AC-6: in a code block the typed text is saved as code (typedBlock types "\n" as text inside the block).
    [Fact]
    public void TypedMarkdown_CodeBlock_AsTyped()
    {
        Assert.Equal("```\n\n@[Anna]\n```\n", TypedMarkdown("``` \n@[Anna]"));
        Assert.Equal("```\n\n@[Anna]\n```\n", bundle.Call("roundTrip", "```\n\n@[Anna]\n```\n"));
    }

    // 232 AC-6: the typed text is saved as typed.
    [Theory]
    [InlineData("@[Anna Schmidt]")]
    [InlineData("Met @[Anna] today")]
    public void TypedMarkdown_AsTyped(string typed)
    {
        Assert.Equal(typed + "\n", TypedMarkdown(typed));
    }

    // 232 AC-7: the saved text lists the person as a mention.
    [Fact]
    public void TypedMarkdown_ListsMention()
    {
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("inlineNodes", TypedMarkdown("@[Anna Schmidt]")));

        Assert.Equal(["Anna Schmidt"], doc.RootElement.GetProperty("mentions").EnumerateArray().Select(m => m.GetString()!).ToArray());
    }

    private string TypedMarkdown(string text) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.typedMarkdown({Js(text)})"))!;

    // The first block's children as "type|text" or "type|raw|name" (mention) or "type|raw" (other chips).
    private static string[] Children(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement block = Assert.Single(doc.RootElement.GetProperty("content").EnumerateArray());
        if (!block.TryGetProperty("content", out JsonElement content))
        {
            return [];
        }

        return content.EnumerateArray().Select(n => Type(n) switch
        {
            "text" => "text|" + Text(n),
            "mention" => $"mention|{n.GetProperty("attrs").GetProperty("raw").GetString()}|{n.GetProperty("attrs").GetProperty("name").GetString()}",
            _ => $"{Type(n)}|{n.GetProperty("attrs").GetProperty("raw").GetString()}",
        }).ToArray();
    }

    private string TypedBlock(string text) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.typedBlock({Js(text)})"))!;

    // 263 AC-1: a fence is a Mermaid fence when the first word of its info is "mermaid" in any ASCII case.
    [Theory]
    [InlineData("mermaid", true)]
    [InlineData(" Mermaid title", true)]
    [InlineData("MERMAID", true)]
    [InlineData("mermaid\ttitle=\"x\"", true)]
    [InlineData("mermaidx", false)]
    [InlineData("", false)]
    [InlineData("js", false)]
    [InlineData("mer maid", false)]
    [InlineData("x mermaid", false)]
    public void Mermaid_IsMermaidFence(string info, bool expected)
    {
        Assert.Equal(expected ? "true" : "false", bundle.Json($"ContextEditor.isMermaidFence({Js(info)})"));
    }

    // 263 AC-2: only a matching "rendered" reply with a bounded string that starts with <svg is drawn.
    [Theory]
    [InlineData("""{"type":"rendered","id":7,"svg":"<svg viewBox=\"0 0 1 1\"></svg>"}""", "svg")]
    [InlineData("""{"type":"rendered","id":7,"svg":"  \n<svg></svg>"}""", "svg")]
    [InlineData("""{"type":"failed","id":7}""", "failed")]
    [InlineData("""{"type":"rendered","id":7}""", "failed")]
    [InlineData("""{"type":"rendered","id":7,"svg":null}""", "failed")]
    [InlineData("""{"type":"rendered","id":7,"svg":42}""", "failed")]
    [InlineData("""{"type":"rendered","id":7,"svg":"<img src=x onerror=alert(1)>"}""", "failed")]
    [InlineData("""{"type":"rendered","id":7,"svg":"text <svg></svg>"}""", "failed")]
    [InlineData("""{"type":"rendered","id":8,"svg":"<svg></svg>"}""", "ignore")]
    [InlineData("""{"type":"failed","id":"7"}""", "ignore")]
    [InlineData("""{"type":"rendered","svg":"<svg></svg>"}""", "ignore")]
    [InlineData("""{"type":"other","id":7,"svg":"<svg></svg>"}""", "ignore")]
    [InlineData("""{"id":7,"svg":"<svg></svg>"}""", "ignore")]
    [InlineData("null", "ignore")]
    [InlineData("7", "ignore")]
    [InlineData("\"<svg></svg>\"", "ignore")]
    [InlineData("""[{"type":"rendered","id":7,"svg":"<svg></svg>"}]""", "ignore")]
    [InlineData("not json", "ignore")]
    public void Mermaid_ResultKind(string json, string kind)
    {
        Assert.Equal(Js(kind), bundle.Json($"ContextEditor.mermaidResultJson({Js(json)}, 7).kind"));
    }

    // 263 AC-2: a drawn result carries the svg unchanged, and a failed one carries nothing else.
    [Fact]
    public void Mermaid_ResultShapes()
    {
        string svg = """{"type":"rendered","id":3,"svg":"<svg><text>x</text></svg>"}""";
        Assert.Equal("""{"kind":"svg","svg":"<svg><text>x</text></svg>"}""", bundle.Json($"ContextEditor.mermaidResultJson({Js(svg)}, 3)"));
        Assert.Equal("""{"kind":"failed"}""", bundle.Json($"ContextEditor.mermaidResultJson({Js("""{"type":"failed","id":3,"error":"Parse error"}""")}, 3)"));
    }

    // 263 AC-2: the svg may be at most 5,000,000 chars.
    [Theory]
    [InlineData(5_000_000, "svg")]
    [InlineData(5_000_001, "failed")]
    public void Mermaid_ResultSvgSizeLimit(int length, string kind)
    {
        string expression = "ContextEditor.mermaidResultJson(JSON.stringify({ type: \"rendered\", id: 1, svg: \"<svg\" + \"a\".repeat("
            + (length - 4) + ") }), 1).kind";
        Assert.Equal(Js(kind), bundle.Json(expression));
    }

    // 263 AC-3: text over 50,000 chars, empty or whitespace-only text is never sent to the frame.
    [Theory]
    [InlineData(1, true)]
    [InlineData(50_000, true)]
    [InlineData(50_001, false)]
    public void Mermaid_ShouldRenderLimit(int length, bool expected)
    {
        Assert.Equal(expected ? "true" : "false", bundle.Json($"ContextEditor.shouldRender(\"a\".repeat({length}))"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\n\t  \n")]
    public void Mermaid_ShouldRender_NotForBlankText(string text)
    {
        Assert.Equal("false", bundle.Json($"ContextEditor.shouldRender({Js(text)})"));
    }

    // 263 AC-5: the Mermaid fixture covers backtick and tilde fences, longer fences and info words after "mermaid", and
    // round-trips byte for byte (Lossless_RoundTripsByteForByte runs it too).
    [Fact]
    public void Mermaid_FixtureRoundTrips()
    {
        string markdown = Read(Path.Combine("lossless", "mermaid.md"));
        string[] needed = ["```mermaid\n", "```Mermaid title=", "~~~mermaid\n", "~~~~mermaid\n", "````mermaid\n", "```mermaidx\n"];

        Assert.All(needed, n => Assert.Contains(n, markdown, StringComparison.Ordinal));
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
        Assert.Equal("true", bundle.Call("isLossless", markdown));
    }

    // 263 AC-5: tilde and longer fences are kept for every code block, and a fence grows past a run of its own character.
    [Theory]
    [InlineData("~~~\ncode\n~~~\n")]
    [InlineData("~~~js\ncode\n~~~\n")]
    [InlineData("~~~~~mermaid\ngraph TD\n~~~~~\n")]
    [InlineData("````\n```\ninner\n```\n````\n")]
    [InlineData("~~~mermaid\nA --> B\n~~~")]
    public void Fences_RoundTrip(string markdown)
    {
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
    }

    // 263 AC-5: a CRLF note with Mermaid fences reaches the editor as LF (NoteStore.ReadForEdit), round-trips byte for
    // byte there, and saving that unchanged text writes nothing, so the file keeps its CRLF bytes.
    [Fact]
    public void Mermaid_CrlfNote_RoundTripsAndStaysUnchanged()
    {
        string root = Path.Combine(Path.GetTempPath(), "context-mermaid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string lf = Read(Path.Combine("lossless", "mermaid.md"));
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(lf.Replace("\n", "\r\n", StringComparison.Ordinal));
            string path = Path.Combine(root, "Diagrams.md");
            File.WriteAllBytes(path, bytes);
            NoteStore store = new(root);

            EditableNote opened = store.ReadForEdit(new NoteInfo("Diagrams", File.GetLastWriteTime(path), path));
            string edited = bundle.Call("roundTrip", opened.Text);

            Assert.Equal(lf, opened.Text);
            Assert.True(opened.HadCrlf);
            Assert.Equal(opened.Text, edited);
            Assert.Equal(NoteSaveResult.Unchanged, store.SaveIfUnchanged(opened, edited, out _));
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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

    // 329 AC-5: both task kinds round-trip byte for byte, also nested, numbered and next to plain items.
    [Theory]
    [InlineData("- { } a\n")]
    [InlineData("- {x} a\n")]
    [InlineData("- [ ] a\n")]
    [InlineData("- [x] a\n")]
    [InlineData("- { } a\n- [ ] b\n- {x} c\n- [x] d\n- e\n")]
    [InlineData("- { } a\n  - [ ] nested\n")]
    [InlineData("1. { } a\n2. [ ] b\n")]
    public void TaskKinds_RoundTripByteForByte(string markdown)
    {
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
        Assert.Equal("true", bundle.Call("isLossless", markdown));
    }

    // 329 AC-5: docJson gives each list item's checked and kind: "global" for braces, "local" for brackets, null for
    // a plain item.
    [Fact]
    public void DocJson_TaskItems_HaveCheckedAndKind()
    {
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", "- { } a\n- {x} b\n- [ ] c\n- [X] d\n- e\n"));

        string[] items = Nodes(doc.RootElement)
            .Where(n => Type(n) == "list_item")
            .Select(n => $"{Attr(n, "checked")}|{Attr(n, "kind")}")
            .ToArray();

        Assert.Equal(["False|global", "True|global", "False|local", "True|local", "null|null"], items);
    }

    // 329 AC-5: ticking a task item writes the box of its own kind, and unticking it back.
    [Theory]
    [InlineData("- { } a\n", 0, "- {x} a\n")]
    [InlineData("- {x} a\n", 0, "- { } a\n")]
    [InlineData("- [ ] a\n", 0, "- [x] a\n")]
    [InlineData("- [x] a\n", 0, "- [ ] a\n")]
    [InlineData("- [ ] a\n- { } b\n", 1, "- [ ] a\n- {x} b\n")]
    public void Toggle_KeepsTheKind(string markdown, int index, string expected)
    {
        string json = bundle.Json($"ContextEditor.toggledMarkdown({Js(markdown)}, {index})");

        Assert.Equal(expected, JsonSerializer.Deserialize<string>(json));
    }

    // 329 AC-5: Enter in a task item makes an unchecked item of the same kind.
    [Theory]
    [InlineData("- { } a\u27e6", "- { } a\n- { } b")]
    [InlineData("- {x} a\u27e6", "- {x} a\n- { } b")]
    [InlineData("- [ ] a\u27e6", "- [ ] a\n- [ ] b")]
    [InlineData("- [x] a\u27e6", "- [x] a\n- [ ] b")]
    [InlineData("- a\u27e6", "- a\n- b")]
    public void Enter_InTaskItem_SameKindUnchecked(string markdown, string expected)
    {
        string json = bundle.Json($"ContextEditor.commandMarkdown({Js(markdown)}, \"Enter\", \"b\")");

        Assert.Equal(expected, JsonSerializer.Deserialize<string>(json));
    }

    // 329 AC-6: typing a box after -, * or + at a paragraph start, or a box in an empty list item, makes that kind of
    // task item at once, and the next typed text lands right after the box (the caret did not move).
    [Theory]
    [InlineData("\u27e6", "- [ ] abc", "- [ ] abc")]
    [InlineData("\u27e6", "- [x] abc", "- [x] abc")]
    [InlineData("\u27e6", "- { } abc", "- { } abc")]
    [InlineData("\u27e6", "- {x} abc", "- {x} abc")]
    [InlineData("\u27e6", "* { } abc", "- { } abc")]
    [InlineData("\u27e6", "+ [ ] abc", "- [ ] abc")]
    [InlineData("\u27e6rest", "* {x} ", "- {x} rest")]
    [InlineData("\u27e6rest", "+ [ ] ", "- [ ] rest")]
    [InlineData("- \u27e6", "{ } abc", "- { } abc")]
    [InlineData("- \u27e6", "[x] abc", "- [x] abc")]
    [InlineData("1. \u27e6", "{ } abc", "1. { } abc")]
    public void Typing_Box_MakesTaskItem_CaretAfterBox(string markdown, string typed, string expected)
    {
        Assert.Equal(expected, TypedAt(markdown, typed));
    }

    // 329 AC-6: the item made by typing has the typed kind and checked state in the doc, not text.
    [Fact]
    public void Typing_Box_GivesTaskNodeNotText()
    {
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", TypedAt("\u27e6", "* { } abc")));

        JsonElement item = Nodes(doc.RootElement).Single(n => Type(n) == "list_item");
        Assert.Equal(("False", "global"), (Attr(item, "checked"), Attr(item, "kind")));
        Assert.Equal("abc", Nodes(item).Where(n => Type(n) == "text").Select(Text).Single());
    }

    // 329 AC-6: the input rules never fire in a code block, inline code, mid-line, in a task item or for {X}.
    [Theory]
    [InlineData("```\n\u27e6\n```", "- { } x", "```\n- { } x\n```")]
    [InlineData("`\u27e6x`", "- { } ", "`- { } x`")]
    [InlineData("a \u27e6", "- { } x", "a - { } x")]
    [InlineData("- [ ] \u27e6", "{ } x", "- [ ] { } x")]
    [InlineData("\u27e6", "* {X} x", "* {X} x")]
    public void Typing_Box_NotInCodeOrMidLine(string markdown, string typed, string expected)
    {
        Assert.Equal(expected, TypedAt(markdown, typed));
    }

    // 329 AC-6: pasted lines of both kinds become task items of their kind.
    [Fact]
    public void Paste_BothKinds_GivesTaskItems()
    {
        string json = bundle.Json($"ContextEditor.pastedMarkdown({Js("- { } a\n- [ ] b\n- {x} c\n")}, false, \"\")");
        using JsonDocument pasted = JsonDocument.Parse(JsonSerializer.Deserialize<string>(json)!);
        string markdown = pasted.RootElement.GetProperty("markdown").GetString()!;

        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", markdown));
        Assert.Equal(
            ["False|global", "False|local", "True|global"],
            Nodes(doc.RootElement).Where(n => Type(n) == "list_item").Select(n => $"{Attr(n, "checked")}|{Attr(n, "kind")}"));
        Assert.Equal("- { } a\n- [ ] b\n- {x} c", markdown.TrimEnd('\n'));

        // After text in a paragraph: the item follows that text's paragraph; other pasted text stays the default paste.
        string after = bundle.Json($"ContextEditor.pastedMarkdown({Js("- { } x\r\n")}, false, {Js("intro")})");
        using JsonDocument afterDoc = JsonDocument.Parse(JsonSerializer.Deserialize<string>(after)!);
        Assert.Equal("intro\n\n- { } x", afterDoc.RootElement.GetProperty("markdown").GetString()!.TrimEnd('\n'));
        string mixed = bundle.Json($"ContextEditor.pastedMarkdown({Js("- { } x\nplain")}, false, \"\")");
        using JsonDocument mixedDoc = JsonDocument.Parse(JsonSerializer.Deserialize<string>(mixed)!);
        Assert.Equal(JsonValueKind.Null, mixedDoc.RootElement.GetProperty("markdown").ValueKind);
    }

    // 330 AC-5: a "[>]" item is an unchecked task of kind "carry"; it round-trips byte for byte; in code it is text.
    [Theory]
    [InlineData("- [>] a\n")]
    [InlineData("- a\n  - [>] nested\n")]
    [InlineData("- [>] a\n- [ ] b\n- { } c\n")]
    [InlineData("```\n- [>] a\n```\n")]
    public void CarryItem_RoundTripsByteForByte(string markdown)
    {
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
        Assert.Equal("true", bundle.Call("isLossless", markdown));
    }

    // 330 AC-5: a "*" list keeps its "[>]" box; the editor writes every bullet as "-" (house style, as for "* [ ]").
    [Fact]
    public void CarryItem_StarBullet_KeepsTheBox()
    {
        Assert.Equal("- [>] a\n", bundle.Call("roundTrip", "* [>] a\n"));
        Assert.Equal(bundle.Call("roundTrip", "* [ ] a\n").Replace("[ ]", "[>]", StringComparison.Ordinal), bundle.Call("roundTrip", "* [>] a\n"));
    }

    [Fact]
    public void CarryItem_DocJson_UncheckedCarry_TextInCode()
    {
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", "- [>] a\n"));
        JsonElement item = Nodes(doc.RootElement).Single(n => Type(n) == "list_item");
        Assert.Equal(("False", "carry"), (Attr(item, "checked"), Attr(item, "kind")));
        Assert.Equal("a", Nodes(item).Where(n => Type(n) == "text").Select(Text).Single());

        using JsonDocument code = JsonDocument.Parse(bundle.Call("docJson", "```\n- [>] a\n```\n"));
        Assert.DoesNotContain(Nodes(code.RootElement), n => Type(n) == "list_item");
        Assert.Equal("- [>] a", Nodes(code.RootElement).Where(n => Type(n) == "text").Select(Text).Single());
    }

    // 330 AC-6: carriedMarkdown turns "[ ]" into "[>]" and back; other boxes stay as they are.
    [Theory]
    [InlineData("- [ ] a\n", 0, "- [>] a\n")]
    [InlineData("- [>] a\n", 0, "- [ ] a\n")]
    [InlineData("- [x] a\n", 0, "- [x] a\n")]
    [InlineData("- { } a\n", 0, "- { } a\n")]
    [InlineData("- {x} a\n", 0, "- {x} a\n")]
    [InlineData("- { } a\n- [ ] b\n", 1, "- { } a\n- [>] b\n")]
    [InlineData("1. [ ] a\n", 0, "1. [>] a\n")]
    public void Carried_TogglesOnlyOpenCheckboxes(string markdown, int index, string expected)
    {
        string json = bundle.Json($"ContextEditor.carriedMarkdown({Js(markdown)}, {index})");

        Assert.Equal(expected, JsonSerializer.Deserialize<string>(json));
    }

    // 330 AC-7: ticking a "[>]" item gives "[x]" (and unticking that "[ ]"); Enter in it makes a "[ ]" item.
    [Fact]
    public void CarryItem_TickAndEnter()
    {
        string ticked = JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.toggledMarkdown({Js("- [>] a\n")}, 0)"))!;
        Assert.Equal("- [x] a\n", ticked);
        Assert.Equal("- [ ] a\n", JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.toggledMarkdown({Js(ticked)}, 0)")));

        string json = bundle.Json($"ContextEditor.commandMarkdown({Js("- [>] a\u27e6")}, \"Enter\", \"b\")");
        Assert.Equal("- [>] a\n- [ ] b", JsonSerializer.Deserialize<string>(json));
    }

    // 330 AC-8: typing "- [>] " at a paragraph start, or "[>] " in an empty plain item, makes a carry item at once.
    [Theory]
    [InlineData("\u27e6", "- [>] abc", "- [>] abc")]
    [InlineData("\u27e6", "* [>] abc", "- [>] abc")]
    [InlineData("\u27e6rest", "+ [>] ", "- [>] rest")]
    [InlineData("- \u27e6", "[>] abc", "- [>] abc")]
    public void Typing_CarryBox_MakesCarryItem(string markdown, string typed, string expected)
    {
        Assert.Equal(expected, TypedAt(markdown, typed));
        using JsonDocument doc = JsonDocument.Parse(bundle.Call("docJson", TypedAt(markdown, typed)));
        JsonElement item = Nodes(doc.RootElement).Single(n => Type(n) == "list_item");
        Assert.Equal(("False", "carry"), (Attr(item, "checked"), Attr(item, "kind")));
    }

    [Fact]
    public void Typing_CarryBox_CommandMarkdown_GivesCarryItem()
    {
        string json = bundle.Json($"ContextEditor.commandMarkdown({Js("- \u27e6")}, \"Mod-Alt-n\", \"[>] abc\")");

        Assert.Equal("- [>] abc", JsonSerializer.Deserialize<string>(json));
    }

    // 330 AC-9: Mod-Alt-n toggles the innermost task item around the caret; elsewhere the document stays the same.
    [Theory]
    [InlineData("- [ ] a\u27e6b", "- [>] ab")]
    [InlineData("- [>] \u27e6ab", "- [ ] ab")]
    [InlineData("- [ ] outer\n  - [ ] in\u27e6ner", "- [ ] outer\n  - [>] inner")]
    [InlineData("- [ ] outer\n  - { } in\u27e6ner", "- [ ] outer\n  - { } inner")]
    [InlineData("- [x] a\u27e6b", "- [x] ab")]
    [InlineData("- { } a\u27e6b", "- { } ab")]
    [InlineData("- {x} a\u27e6b", "- {x} ab")]
    [InlineData("- a\u27e6b", "- ab")]
    [InlineData("a\u27e6b", "ab")]
    public void CarryKey_TogglesInnermostOpenCheckbox(string markdown, string expected)
    {
        string json = bundle.Json($"ContextEditor.commandMarkdown({Js(markdown)}, \"Mod-Alt-n\")");

        Assert.Equal(expected, JsonSerializer.Deserialize<string>(json));
    }

    // ---- 341: the theme ----

    // 341 AC-6: setTheme sets data-theme on the root element for "light" and "dark", and removes it for "system" and
    // any other value; it starts from a set attribute so a removal shows.
    [Theory]
    [InlineData("\"light\"", "light")]
    [InlineData("\"dark\"", "dark")]
    [InlineData("\"system\"", null)]
    [InlineData("\"Dark\"", null)]
    [InlineData("\"blue\"", null)]
    [InlineData("\"\"", null)]
    [InlineData("null", null)]
    [InlineData("undefined", null)]
    [InlineData("1", null)]
    public void SetTheme_SetsOrRemovesDataTheme(string argument, string? expected)
    {
        string json = bundle.Json($$"""
            (() => {
              const attrs = { "data-theme": "dark" };
              const root = {
                setAttribute(name, value) { attrs[name] = String(value); },
                removeAttribute(name) { delete attrs[name]; },
              };
              globalThis.document = { documentElement: root };
              try {
                ContextEditor.setTheme({{argument}});
              } finally {
                delete globalThis.document;
              }
              return Object.prototype.hasOwnProperty.call(attrs, "data-theme") ? attrs["data-theme"] : null;
            })()
            """);

        Assert.Equal(expected is null ? "null" : Js(expected), json);
        Assert.Equal("\"undefined\"", bundle.Json("typeof document"));
    }

    // 341 AC-6: without a document setTheme does nothing and doesn't throw.
    [Fact]
    public void SetTheme_NoDocument_DoesNothing()
    {
        Assert.Equal("true", bundle.Json("ContextEditor.setTheme(\"dark\") === undefined"));
    }

    // 341 AC-8: dark when data-theme is dark, or when there is none and the system prefers dark; otherwise default.
    [Theory]
    [InlineData("\"dark\"", "true", "dark")]
    [InlineData("\"dark\"", "false", "dark")]
    [InlineData("\"light\"", "true", "default")]
    [InlineData("\"light\"", "false", "default")]
    [InlineData("null", "true", "dark")]
    [InlineData("null", "false", "default")]
    [InlineData("undefined", "true", "dark")]
    [InlineData("\"\"", "true", "dark")]
    [InlineData("\"\"", "false", "default")]
    [InlineData("\"blue\"", "true", "default")]
    [InlineData("\"Dark\"", "false", "default")]
    public void MermaidTheme_EveryCombination(string dataTheme, string prefersDark, string expected)
    {
        Assert.Equal(Js(expected), bundle.Json($"ContextEditor.mermaidTheme({dataTheme}, {prefersDark})"));
    }

    // 341 AC-8: every render message carries the page's theme; the frame reconfigures Mermaid with the message's theme
    // for each render and takes only "dark", anything else as "default" (source and committed frame bundle).
    [Fact]
    public void Mermaid_RenderMessageCarriesTheme_FrameFallsBackToDefault()
    {
        string desktop = Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder);
        string mermaid = File.ReadAllText(Path.Combine(desktop, "editor", "src", "mermaid.js"));
        string frame = File.ReadAllText(Path.Combine(desktop, "editor", "src", "mermaid-frame.js"));
        string frameBundle = File.ReadAllText(Path.Combine(desktop, "wwwroot", "mermaid", "mermaid-frame.js"));
        string theme = File.ReadAllText(Path.Combine(desktop, "editor", "src", "theme.js"));

        Assert.Contains("const message = { type: \"render\", id, text, theme: currentMermaidTheme() };", mermaid, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(mermaid, @"type: ""render"""));
        Assert.Contains("return mermaidTheme(dataTheme, prefersDark);", theme, StringComparison.Ordinal);
        Assert.Contains("getAttribute(\"data-theme\")", theme, StringComparison.Ordinal);
        Assert.Contains("return value === \"dark\" ? \"dark\" : \"default\";", frame, StringComparison.Ordinal);
        Assert.Matches(@"async function render\(id, text, theme\) \{[^}]*configure\(theme\);", frame);
        Assert.Contains("render(data.id, data.text, theme)", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("matchMedia", frame, StringComparison.Ordinal);
        Assert.Matches(@"return [a-zA-Z_$][\w$]*===""dark""\?""dark"":""default""", frameBundle);
        Assert.DoesNotContain("prefers-color-scheme", frameBundle, StringComparison.Ordinal);
    }

    private string TypedAt(string markdown, string typed) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.typedAtMarkdown({Js(markdown)}, {Js(typed)})"))!;

    // An attribute of a node as text: "True"/"False" for a boolean, "null" for null or missing, else the string.
    private static string Attr(JsonElement node, string name)
    {
        if (!node.TryGetProperty("attrs", out JsonElement attrs) || !attrs.TryGetProperty(name, out JsonElement value))
        {
            return "null";
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Null => "null",
            _ => value.GetString()!,
        };
    }
}
