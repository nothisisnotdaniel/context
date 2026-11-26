using System.Text.Json;
using System.Text.RegularExpressions;
using Jint;

namespace Context.Tests;

// 283: inline code and code blocks in the page editor, run on the committed editor.js in Jint (no browser, no DOM):
// the real keymap bindings and input rules through DOM-free hooks (⟦ ⟧ mark the selection, a lone ⟦ is a caret),
// syntax tokens, the changed-block rule, the language picker's rules and the copy fallback chain.
public sealed class CodeBlockBundleTests : IClassFixture<EditorBundle>
{
    private readonly EditorBundle bundle;

    public CodeBlockBundleTests(EditorBundle bundle)
    {
        this.bundle = bundle;
    }

    // AC-1: Mod-e and Mod-` toggle the code mark on the selection, on and off.
    [Theory]
    [InlineData("a⟦bc⟧d", "Mod-e", "a`bc`d")]
    [InlineData("a⟦bc⟧d", "Mod-`", "a`bc`d")]
    [InlineData("a⟦`bc`⟧d", "Mod-e", "abcd")]
    [InlineData("⟦one two⟧", "Mod-e", "`one two`")]
    public void InlineCode_TogglesTheSelection(string markdown, string key, string expected)
    {
        Assert.Equal(expected, Command(markdown, key));
    }

    // AC-1: with an empty selection Mod-e toggles the stored mark: the next typed text is code, or plain again.
    [Theory]
    [InlineData("ab⟦", "x", "ab`x`")]
    [InlineData("`ab⟦`", "x", "`ab`x")]
    public void InlineCode_EmptySelection_TogglesTheStoredMark(string markdown, string typed, string expected)
    {
        Assert.Equal(expected, Command(markdown, "Mod-e", typed));
    }

    // AC-2: Mod-Alt-c makes the touched paragraphs and headings one code block (one line each, no language), an empty
    // paragraph an empty block, and a code block paragraphs again.
    [Theory]
    [InlineData("para ⟦A\n\npara B⟧", "```\npara A\npara B\n```")]
    [InlineData("para A⟦", "```\npara A\n```")]
    [InlineData("## Head⟦ing", "```\nHeading\n```")]
    [InlineData("⟦", "```\n\n```")]
    [InlineData("before\n\n⟦x\n\n## y⟧\n\nafter", "before\n\n```\nx\ny\n```\n\nafter")]
    [InlineData("```\nline 1\nli⟦ne 2\n```", "line 1\n\nline 2")]
    // The empty middle line becomes an empty paragraph, which markdown writes as nothing.
    [InlineData("```js\n⟦a\n\nb⟧\n```", "a\n\nb")]
    [InlineData("- item\n\n  pa⟦ra", "- item\n\n  ```\n  para\n  ```")]
    public void CodeBlock_Toggles(string markdown, string expected)
    {
        Assert.Equal(expected, Command(markdown, "Mod-Alt-c"));
    }

    // AC-2: the cursor stays on the same character, so typing after the toggle lands where it was.
    [Theory]
    [InlineData("para A\n\npa⟦ra B", "X", "para A\n\n```\npaXra B\n```")]
    [InlineData("para ⟦A\n\npa⟧ra B", "X", "```\npara Xra B\n```")]
    [InlineData("```\nline 1\nli⟦ne 2\n```", "X", "line 1\n\nliXne 2")]
    public void CodeBlock_KeepsTheCursor(string markdown, string typed, string expected)
    {
        Assert.Equal(expected, Command(markdown, "Mod-Alt-c", typed));
    }

    // AC-2: a chip in a block keeps its raw text in the code.
    [Fact]
    public void CodeBlock_ChipsBecomeTheirText()
    {
        Assert.Equal("```\na #tag [[Note]] b\n```", Command("a #tag [[Note]] ⟦b", "Mod-Alt-c"));
    }

    // AC-4: typing in a code block keeps the exact characters: no list, quote, heading, nested block or chip.
    [Theory]
    [InlineData("- ")]
    [InlineData("> ")]
    [InlineData("1. ")]
    [InlineData("# ")]
    [InlineData("```")]
    [InlineData("#tag ")]
    [InlineData("[[Note]]")]
    [InlineData("@[Ann Lee]")]
    public void Typing_InCodeBlock_KeepsTheCharacters(string typed)
    {
        string markdown = Typed("```\n⟦\n```", typed);

        Assert.Equal(typed, Body(markdown));
        Assert.Equal("[\"code_block\"]", BlockTypes(markdown));
    }

    // AC-4: inline code at the start of a paragraph makes no list, quote, heading, block or chip either.
    [Theory]
    [InlineData("`-⟦x`", " ", "`- x`")]
    [InlineData("`>⟦x`", " ", "`> x`")]
    [InlineData("`1.⟦x`", " ", "`1. x`")]
    [InlineData("`#⟦x`", " ", "`# x`")]
    [InlineData("`#tag⟦x`", " ", "`#tag x`")]
    [InlineData("`[[Note]⟦x`", "]", "`[[Note]]x`")]
    [InlineData("`@[Ann Lee⟦x`", "]", "`@[Ann Lee]x`")]
    public void Typing_InInlineCode_KeepsTheCharacters(string markdown, string typed, string expected)
    {
        Assert.Equal(expected, Typed(markdown, typed));
        Assert.Equal("[\"paragraph\"]", BlockTypes(Typed(markdown, typed)));
    }

    // AC-4: outside code the same rules still fire (the hook types through the real rules).
    [Fact]
    public void Typing_OutsideCode_StillMakesAList()
    {
        Assert.Equal("[\"bullet_list\"]", BlockTypes(Typed("⟦", "- x")));
    }

    // AC-5: Enter in a code block inserts a newline, also in a list item, which it never splits.
    [Theory]
    [InlineData("```\nco⟦de\n```", "```\nco\nde\n```")]
    [InlineData("- item\n\n  ```\n  co⟦de\n  ```", "- item\n\n  ```\n  co\n  de\n  ```")]
    public void Enter_InCodeBlock_InsertsANewline(string markdown, string expected)
    {
        Assert.Equal(expected, Command(markdown, "Enter"));
    }

    // AC-5: Mod-Enter leaves the block for a new paragraph after it.
    [Fact]
    public void ModEnter_InCodeBlock_LeavesTheBlock()
    {
        Assert.Equal("```\ncode\n```\n\nx", Command("```\nco⟦de\n```", "Mod-Enter", "x"));
    }

    // AC-6: a registered language or alias in any ASCII case gives hljs tokens with a keyword.
    [Theory]
    [InlineData("public class A { }", "csharp")]
    [InlineData("public class A { }", "CS")]
    [InlineData("function f() { return 1; }", "JavaScript")]
    [InlineData("if ($a) { Write-Host 'x' }", "ps1")]
    [InlineData("key: true", "yml")]
    [InlineData("<p class=\"x\">Hi</p>", "html")]
    [InlineData("SELECT a FROM b", "sql")]
    [InlineData("def f():\n    return 1", "py")]
    [InlineData("echo hi && exit", "sh")]
    public void Tokens_KnownLanguage_HasHljsClasses(string text, string lang)
    {
        List<(int From, int To, string Class)> tokens = Tokens(text, lang);

        Assert.NotEmpty(tokens);
        Assert.All(tokens, t => Assert.Matches("^hljs-[a-z_]+( hljs-[a-z_]+)*$", t.Class));
        Assert.Contains(tokens, t => t.Class.Split(' ').Any(c => c is "hljs-keyword" or "hljs-tag" or "hljs-attr" or "hljs-built_in" or "hljs-literal"));
        AssertSortedAndApart(tokens, text.Length);
    }

    // AC-6: the keyword class falls on the keyword's own characters.
    [Fact]
    public void Tokens_OffsetsPointIntoTheText()
    {
        const string Text = "public class A { }";

        List<(int From, int To, string Class)> tokens = Tokens(Text, "csharp");

        Assert.Contains(tokens, t => t.Class == "hljs-keyword" && Text[t.From..t.To] == "public");
    }

    // AC-6: an empty, unknown or Mermaid language, a text over 20,000 chars and an empty text give no tokens.
    [Theory]
    [InlineData("fn main() {}", "rust")]
    [InlineData("public class A { }", "")]
    [InlineData("graph TD\n  A --> B", "mermaid")]
    [InlineData("", "csharp")]
    public void Tokens_NoLanguage_Empty(string text, string lang)
    {
        Assert.Equal("[]", bundle.Json($"JSON.parse(ContextEditor.tokensJson({Js(text)}, {Js(lang)}))"));
    }

    [Fact]
    public void Tokens_OverTheLimit_Empty()
    {
        string text = string.Concat(Enumerable.Repeat("var a = 1;", 2000)) + "x";
        Assert.Equal(20001, text.Length);

        Assert.Equal("[]", bundle.Json($"JSON.parse(ContextEditor.tokensJson({Js(text)}, \"csharp\"))"));
        Assert.NotEqual("[]", bundle.Json($"JSON.parse(ContextEditor.tokensJson({Js(text[..20000])}, \"csharp\"))"));
    }

    // AC-6: a value that is not text never throws.
    [Fact]
    public void Tokens_NotText_NeverThrows()
    {
        Assert.Equal("[]", bundle.Json("JSON.parse(ContextEditor.tokensJson(null, \"csharp\"))"));
        Assert.Equal("[]", bundle.Json("JSON.parse(ContextEditor.tokensJson(\"x\", 5))"));
    }

    // AC-7: only the code block that typing changed is highlighted again; typing in a paragraph changes none.
    [Theory]
    [InlineData("```\na\n```\n\n```\nb⟦\n```\n\n```\nc\n```", "zz", "[1]")]
    [InlineData("```js\na⟦\n```\n\n```\nb\n```", "zz", "[0]")]
    [InlineData("p⟦\n\n```\na\n```\n\n```\nb\n```", "zz", "[]")]
    public void ChangedCodeBlocks_OnlyTheTypedOne(string markdown, string typed, string expected)
    {
        Assert.Equal(expected, bundle.Json($"JSON.parse(ContextEditor.changedCodeBlocksJson({Js(markdown)}, {Js(typed)}))"));
    }

    // AC-7: the language fixture comes back byte for byte (highlighting never touches the document).
    [Fact]
    public void CodeLanguagesFixture_RoundTrips()
    {
        string path = Path.Combine(RepoFiles.Root(), "tests", "Context.Desktop.Tests", "RoundTrip", "lossless", "code-languages.md");
        string markdown = File.ReadAllText(path).ReplaceLineEndings("\n");

        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
    }

    // AC-8: the picker shows Plain text, every language by name, and keeps an unknown word as its own option.
    [Theory]
    [InlineData("", "")]
    [InlineData("csharp", "csharp")]
    [InlineData("CS title=x", "csharp")]
    [InlineData("rust", "rust")]
    public void LanguageOptions_SelectTheBlocksLanguage(string parameters, string selected)
    {
        using JsonDocument options = JsonDocument.Parse(bundle.Json($"JSON.parse(ContextEditor.languageOptionsJson({Js(parameters)}))"));
        List<JsonElement> list = options.RootElement.EnumerateArray().ToList();

        Assert.Equal(("", "Plain text"), (list[0].GetProperty("value").GetString(), list[0].GetProperty("label").GetString()));
        Assert.Equal(
            ["Bash", "C#", "CSS", "JavaScript", "JSON", "PowerShell", "Python", "SQL", "TypeScript", "XML / HTML", "YAML"],
            list.Skip(1).Take(11).Select(o => o.GetProperty("label").GetString()));
        Assert.Equal(selected, Assert.Single(list, o => o.GetProperty("selected").GetBoolean()).GetProperty("value").GetString());
        Assert.Equal(selected == "rust" ? 13 : 12, list.Count);
    }

    // AC-8: a choice replaces only the first params word; Plain text clears it and keeps the rest.
    [Theory]
    [InlineData("", "csharp", "csharp")]
    [InlineData("js", "python", "python")]
    [InlineData("rust title=\"x\"", "python", "python title=\"x\"")]
    [InlineData("js title=\"x\"", "", "title=\"x\"")]
    [InlineData("js", "", "")]
    [InlineData("", "", "")]
    public void WithLanguage_ReplacesTheFirstWord(string parameters, string choice, string expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.withLanguage({Js(parameters)}, {Js(choice)})")));
    }

    // AC-9: writeText first; execCopy only when it is missing or rejects; "Copied" or "Can't copy", never a rejection.
    [Theory]
    [InlineData("resolves", "true", "Copied", "w")]
    [InlineData("resolves", "throws", "Copied", "w")]
    [InlineData("rejects", "true", "Copied", "w,e")]
    [InlineData("rejects", "false", "Can't copy", "w,e")]
    [InlineData("rejects", "throws", "Can't copy", "w,e")]
    [InlineData("throws", "true", "Copied", "w,e")]
    [InlineData("missing", "true", "Copied", "e")]
    [InlineData("missing", "false", "Can't copy", "e")]
    [InlineData("missing", "throws", "Can't copy", "e")]
    [InlineData("missing", "missing", "Can't copy", "")]
    public void CopyText_FallsBackAndNeverRejects(string writeText, string execCopy, string label, string calls)
    {
        const string Text = "line 1\n  \"two\" `x`";
        string write = writeText switch
        {
            "resolves" => "t => { calls.push(\"w\"); texts.push(t); return Promise.resolve(); }",
            "rejects" => "t => { calls.push(\"w\"); texts.push(t); return Promise.reject(new Error(\"denied\")); }",
            "throws" => "t => { calls.push(\"w\"); texts.push(t); throw new Error(\"denied\"); }",
            _ => "undefined",
        };
        string exec = execCopy switch
        {
            "true" => "t => { calls.push(\"e\"); texts.push(t); return true; }",
            "false" => "t => { calls.push(\"e\"); texts.push(t); return false; }",
            "throws" => "t => { calls.push(\"e\"); texts.push(t); throw new Error(\"no\"); }",
            _ => "undefined",
        };

        using JsonDocument result = JsonDocument.Parse(bundle.Settled($$"""
            (() => {
              const calls = [];
              const texts = [];
              return ContextEditor.copyText({{Js(Text)}}, { writeText: {{write}}, execCopy: {{exec}} })
                .then(label => JSON.stringify({ label, calls: calls.join(","), texts }), () => JSON.stringify({ label: "rejected" }));
            })()
            """));

        Assert.Equal(label, result.RootElement.GetProperty("label").GetString());
        Assert.Equal(calls, result.RootElement.GetProperty("calls").GetString());
        Assert.All(result.RootElement.GetProperty("texts").EnumerateArray(), t => Assert.Equal(Text, t.GetString()));
    }

    // 438 AC-1, AC-2: long code lines wrap in the source and in the committed bundle's CSS; no horizontal scroll bar.
    [Theory]
    [InlineData("src")]
    [InlineData("bundle")]
    public void CodeBlocks_Wrap_InSourceAndBundle(string which)
    {
        string desktop = Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder);
        string path = which == "src"
            ? Path.Combine(desktop, "editor", "src", "editor.css")
            : Path.Combine(desktop, "wwwroot", "editor", "editor.css");
        string css = File.ReadAllText(path);

        Match code = Regex.Match(css, @"\.note-editor-body pre > code\s*\{([^}]*)\}");
        Assert.True(code.Success);
        Assert.Matches(@"white-space:\s*pre-wrap", code.Groups[1].Value);
        Assert.Matches(@"overflow-wrap:\s*(anywhere|break-word)", code.Groups[1].Value);
        Match pre = Regex.Match(css, @"\.note-editor-body pre\s*\{([^}]*)\}");
        Assert.True(pre.Success);
        Assert.DoesNotMatch(@"overflow-x:\s*auto", pre.Groups[1].Value);
    }

    // 438 AC-4: wrapping is display only: a long line and an indented line round-trip byte for byte, and Copy gets them whole.
    [Fact]
    public void CodeBlock_LongAndIndentedLines_RoundTripAndCopyUnchanged()
    {
        string longLine = new('x', 500);
        string markdown = "```\n" + longLine + "\n    indented\n\ttabbed\n```";

        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));

        string text = longLine + "\n    indented";
        using JsonDocument result = JsonDocument.Parse(bundle.Settled($$"""
            (() => {
              const texts = [];
              return ContextEditor.copyText({{Js(text)}}, { writeText: t => { texts.push(t); return Promise.resolve(); } })
                .then(() => JSON.stringify({ texts }));
            })()
            """));
        Assert.Equal(text, result.RootElement.GetProperty("texts")[0].GetString());
    }

    // AC-10: inline code and code blocks use the monospace stack on --code-bg, the hljs classes are coloured through
    // --code-* variables, and every --code-* variable editor.css uses is defined in app.css's light and dark blocks.
    [Fact]
    public void CodeColours_AreThemeVariables()
    {
        string desktop = Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder);
        string editor = File.ReadAllText(Path.Combine(desktop, "editor", "src", "editor.css"));
        string app = File.ReadAllText(Path.Combine(desktop, "wwwroot", "app.css"));

        Assert.Matches(@"\.note-editor-body :not\(pre\) > code \{[^}]*background: var\(--code-bg\)", editor);
        Assert.Matches(@"\.note-editor-body pre \{[^}]*background: var\(--code-bg\)", editor);
        Assert.Matches(@"\.note-editor-body code \{[^}]*font-family: ui-monospace", editor);
        foreach (string name in new[] { "keyword", "string", "comment", "number", "title", "built_in", "literal", "attr" })
        {
            Assert.Matches(@"\.hljs-" + name + @"[^{]*\{[^}]*color: var\(--code-", editor);
        }

        string light = Block(app, ":root {");
        string dark = Block(app, "@media (prefers-color-scheme: dark) {");
        string chosenDark = Block(app, ":root[data-theme=\"dark\"] {");
        List<string> used = Regex.Matches(editor, @"var\((--code-[a-z-]+)\)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(used.Count >= 9, string.Join(", ", used));
        foreach (string name in used)
        {
            Assert.Contains(name + ":", light, StringComparison.Ordinal);
            Assert.Contains(name + ":", dark, StringComparison.Ordinal);
            Assert.Contains(name + ":", chosenDark, StringComparison.Ordinal);
        }
    }

    // The text of the first block that opens with start, up to its matching brace.
    // ---- 290: typing backticks, and text saved as typed (no escapes added) ----

    // 290 AC-1: text typed through the input rules is saved character for character, with no backslash added.
    [Theory]
    [InlineData("*a*")]
    [InlineData("**b**")]
    [InlineData("_c_")]
    [InlineData("a_b")]
    [InlineData("[x](y)")]
    [InlineData("[x]")]
    [InlineData("<http://a>")]
    [InlineData("&amp;")]
    [InlineData("\\")]
    [InlineData("\\*")]
    [InlineData("|")]
    [InlineData("~~~")]
    [InlineData("a # b")]
    [InlineData("1)")]
    [InlineData("a ` b")]
    [InlineData("x *a* **b** _c_ a_b [x](y) <http://a> &amp; \\ \\* | ~~~ # 1) ` end")]
    public void Typed_SavedAsTyped(string typed)
    {
        Assert.Equal(typed + "\n", TypedMd(typed));
    }

    // 290 AC-2: text typed at a line start is not escaped, nor are the characters around typed text.
    [Theory]
    [InlineData("+ x")]
    [InlineData("---x")]
    [InlineData("=")]
    [InlineData("[a]: b")]
    public void TypedAtLineStart_SavedAsTyped(string typed)
    {
        Assert.Equal(typed + "\n", Typed("⟦\n", typed));
    }

    // 290 AC-2: typing into an existing note escapes nothing around the typed text.
    [Fact]
    public void TypedIntoNote_NeighboursNotEscaped()
    {
        Assert.Equal("a *b\n", Typed("a ⟦b\n", "*"));
    }

    // 290 AC-3: a parsed escape or entity is text that keeps its source characters.
    [Fact]
    public void Parsed_EscapesStayAsText()
    {
        string markdown = "\\*a\\* \\` \\_ \\# \\[ \\\\ &amp; &#35; &lt;\n";

        Assert.Equal(["text|\\*a\\* \\` \\_ \\# \\[ \\\\ &amp; &#35; &lt;"], Inline(Doc(markdown)));
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
    }

    // 290 AC-4: the closing backtick makes the span inline code without its backticks; the next character is plain.
    [Fact]
    public void InlineCodeRule_MakesCode()
    {
        Assert.Equal(["text|code|code"], Inline(TypedDoc("`code`")));
        Assert.Equal("`code`\n", TypedMd("`code`"));
        Assert.Equal(["text|a ", "text|b|code", "text| c"], Inline(TypedDoc("a `b` c")));
        Assert.Equal("a `b` c\n", TypedMd("a `b` c"));
    }

    // 290 AC-4: the closing backtick fires the rule, not the next character.
    [Fact]
    public void InlineCodeRule_FiresOnTheClosingBacktick()
    {
        Assert.Equal(["text|`x"], Inline(TypedDoc("`x")));
        Assert.Equal(["text|x|code"], Inline(TypedDoc("`x`")));
    }

    // 290 AC-5: no inline code for an empty span, a span after a backtick or a backslash, or a span over a chip; the
    // text stays plain and saves as typed.
    [Theory]
    [InlineData("``")]
    [InlineData("``a`")]
    [InlineData("\\`a`")]
    [InlineData("` #tag x`")]
    [InlineData("`[[Note]]`")]
    [InlineData("` @[Anna] x`")]
    public void InlineCodeRule_DoesNotFire(string typed)
    {
        Assert.DoesNotContain(Inline(TypedDoc(typed)), c => c.EndsWith("|code", StringComparison.Ordinal));
        Assert.Equal(typed + "\n", TypedMd(typed));
    }

    // 290 AC-5: no inline code in a code block.
    [Fact]
    public void InlineCodeRule_NotInCodeBlock()
    {
        Assert.Equal(["code_block||\n`a`"], Blocks(TypedDoc("``` \n`a`")));
    }

    // 290 AC-5: no rule over text that already has the code mark: the backticks stay in the code text.
    [Fact]
    public void InlineCodeRule_NotInCodeText()
    {
        string markdown = Typed("`` `a⟦ ``\n", "`");

        Assert.Equal("`` `a` ``\n", markdown);
        Assert.Equal(["text|`a`|code"], Inline(Doc(markdown)));
    }

    // 290 AC-6: ``` and an optional language, then Space, at the start of an empty paragraph give an empty code block.
    [Theory]
    [InlineData("``` ", "")]
    [InlineData("```js ", "js")]
    [InlineData("```c# ", "c#")]
    public void FenceRule_Space_MakesCodeBlock(string typed, string language)
    {
        Assert.Equal([$"code_block|{language}|"], Blocks(TypedDoc(typed)));
        string markdown = "```" + language + "\n\n```\n";
        Assert.Equal(markdown, TypedMd(typed));
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
    }

    // 290 AC-6: ``` alone stays paragraph text.
    [Fact]
    public void FenceRule_BackticksAlone_StayText()
    {
        Assert.Equal(["paragraph|```"], Blocks(TypedDoc("```")));
        Assert.Equal(["paragraph|```js"], Blocks(TypedDoc("```js")));
    }

    // 290 AC-7: Enter on a fence line makes it a code block with that language, caret inside, nothing split off.
    [Fact]
    public void FenceEnter_MakesCodeBlock()
    {
        string markdown = Command("⟦\n", "Enter", "x = 1", "```py");

        Assert.Equal("```py\nx = 1\n```\n", markdown);
        Assert.Equal("[\"code_block\"]", BlockTypes(markdown));
        Assert.Equal("```\n\n```\n", Command("⟦\n", "Enter", "", "```"));
        Assert.Equal(["code_block||"], Blocks(bundle.Call("docJson", Command("⟦\n", "Enter", "", "```"))));
        Assert.Equal("text\n\n```c#\nint x;\n```\n", Command("text\n\n⟦\n", "Enter", "int x;", "```c#"));
    }

    // 290 AC-7: Enter on any other paragraph splits it as before.
    [Theory]
    [InlineData("a⟦b\n", "", "", "a\n\nb\n")]
    [InlineData("⟦\n", "x", "a```", "a```\n\nx\n")]
    [InlineData("⟦\n", "x", "``", "``\n\nx\n")]
    [InlineData("⟦\n", "x", "```a`", "```a`\n\nx\n")]
    public void FenceEnter_OtherParagraph_Splits(string markdown, string typed, string before, string expected)
    {
        Assert.Equal(expected, Command(markdown, "Enter", typed, before));
    }

    // 290 AC-7: Enter on ``` with the code mark splits the paragraph, it makes no code block.
    [Fact]
    public void FenceEnter_InlineCode_Splits()
    {
        Assert.Equal("[\"paragraph\",\"paragraph\"]", BlockTypes(Command("`` ```⟦ ``\n", "Enter", "x")));
    }

    // 290 AC-8: the fence rule does not fire mid-line, in a code block or in inline code.
    [Fact]
    public void FenceRule_DoesNotFire()
    {
        Assert.Equal(["paragraph|a ``` x"], Blocks(TypedDoc("a ``` x")));
        Assert.Equal(["code_block||\n``` "], Blocks(TypedDoc("``` \n``` ")));
        Assert.Equal("[\"paragraph\"]", BlockTypes(Command("⟦", "Mod-e", "``` x")));
    }

    private static string Block(string css, string start)
    {
        int at = css.IndexOf(start, StringComparison.Ordinal);
        Assert.True(at >= 0, start);
        int depth = 0;
        for (int i = at; i < css.Length; i++)
        {
            if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}' && --depth == 0)
            {
                return css[at..(i + 1)];
            }
        }

        throw new InvalidOperationException("Unclosed block " + start);
    }

    // 447 AC-1, AC-2, AC-8: Tab indents by two spaces: at the caret, or at the start of each touched line.
    [Theory]
    [InlineData("```\nab⟦c\n```", "```\nab  c\n```")]
    [InlineData("```\n⟦a\nb⟧\nc\n```", "```\n  a\n  b\nc\n```")]
    [InlineData("```\na\n⟦b⟧\nc\n```", "```\na\n  b\nc\n```")]
    [InlineData("```\na\nb⟦c\nd⟧e\n```", "```\na\n  bc\n  de\n```")]
    [InlineData("```\n⟦a\n⟧b\n```", "```\n  a\nb\n```")]
    [InlineData("```mermaid\ngraph TD\n⟦A-->B\n```", "```mermaid\ngraph TD\n  A-->B\n```")]
    public void Tab_IndentsInCodeBlock(string markdown, string expected)
    {
        Assert.Equal(expected, Command(markdown, "Tab"));
    }

    // 447 AC-1: the caret ends after the inserted spaces.
    [Fact]
    public void Tab_CaretEndsAfterSpaces()
    {
        Assert.Equal("```\nab  Xc\n```", Command("```\nab⟦c\n```", "Tab", "X"));
    }

    // 447 AC-2: the selection stays over the same text.
    [Fact]
    public void Tab_KeepsSelectionOverSameText()
    {
        Assert.Equal("```\n  X\n```", Command("```\n⟦a\nb⟧\n```", "Tab", "X"));
        Assert.Equal("```\na\nb\n```", Keys("```\n⟦a\nb⟧\n```", "Tab", "Shift-Tab").Markdown);
    }

    // 447 AC-3: Shift-Tab removes up to two spaces or one tab; unchanged lines stay, and the key is handled.
    [Theory]
    [InlineData("```\n    a⟦b\n```", "```\n  ab\n```")]
    [InlineData("```\n a⟦b\n```", "```\nab\n```")]
    [InlineData("```\n\t\ta⟦b\n```", "```\n\tab\n```")]
    [InlineData("```\n  ⟦a\n    b⟧\nc\n```", "```\na\n  b\nc\n```")]
    [InlineData("```\na⟦b\n```", "```\nab\n```")]
    [InlineData("```mermaid\n  ⟦A\n```", "```mermaid\nA\n```")]
    public void ShiftTab_OutdentsInCodeBlock(string markdown, string expected)
    {
        Assert.Equal(expected, Command(markdown, "Shift-Tab"));
        Assert.True(Keys(markdown, "Shift-Tab").Handled[0]);
    }

    // 447 AC-4: in a list item's code block, Tab indents the code and does not sink the item.
    [Fact]
    public void Tab_InListCodeBlock_DoesNotSink()
    {
        const string Item = "- a\n\n  ```\n  x⟦y\n  ```";
        Assert.Equal("- a\n\n  ```\n  x  y\n  ```", Command(Item, "Tab"));
        Assert.Equal("- a\n\n  ```\n  xy\n  ```", Command("- a\n\n  ```\n    x⟦y\n  ```", "Shift-Tab"));
        Assert.Equal("- a\n\n  ```\n  xy\n  ```", Command("- a\n\n  ```\n  ⟦xy\n  ```", "Shift-Tab"));
        Assert.Equal([true], Keys(Item, "Tab").Handled);
    }

    // 447 AC-5: outside code blocks the list keys and the paragraph behaviour are unchanged.
    [Fact]
    public void Tab_OutsideCode_ListSinksAndLifts_ParagraphUnhandled()
    {
        Assert.Equal("- a\n  - b", Command("- a\n- ⟦b", "Tab"));
        Assert.Equal("- a\n- b", Command("- a\n  - ⟦b", "Shift-Tab"));
        Assert.Equal([false, false], Keys("⟦x", "Tab", "Shift-Tab").Handled);
        Assert.Equal("x", Keys("⟦x", "Tab").Markdown);
    }

    // 447 AC-6: Esc, then Tab (or Shift-Tab) is left to the browser and changes nothing; the next Tab indents.
    [Fact]
    public void Escape_ThenTab_IsUnhandled_ThenIndentsAgain()
    {
        const string Md = "```\n⟦a\n```";
        KeyResult one = Keys(Md, "Escape", "Tab");
        Assert.Equal([false, false], one.Handled);
        Assert.Equal("```\na\n```", one.Markdown);
        KeyResult two = Keys(Md, "Escape", "Tab", "Tab");
        Assert.Equal([false, false, true], two.Handled);
        Assert.Equal("```\n  a\n```", two.Markdown);
        KeyResult back = Keys("```\n  ⟦a\n```", "Escape", "Shift-Tab");
        Assert.Equal([false, false], back.Handled);
        Assert.Equal("```\n  a\n```", back.Markdown);
    }

    // 447 AC-6: any other key in between restores indenting; Esc outside a code block sets nothing.
    [Fact]
    public void Escape_ThenOtherKey_RestoresIndent()
    {
        KeyResult other = Keys("```\n⟦a\n```", "Escape", "Left", "Tab");
        Assert.True(other.Handled[2]);
        Assert.Equal("```\n  a\n```", other.Markdown);
        Assert.True(Keys("p⟦", "Escape", "Tab").Handled[1] == false);
        KeyResult list = Keys("- a\n- ⟦b", "Escape", "Tab");
        Assert.True(list.Handled[1]);
        KeyResult inList = Keys("- ```\n  ⟦a\n  ```", "Escape", "Tab");
        Assert.False(inList.Handled[1]);
    }

    // 447 AC-7: one Tab is one undo step; the saved Markdown holds the spaces and round-trips.
    [Fact]
    public void Tab_IsOneUndoStep_AndRoundTrips()
    {
        const string Md = "```js\n⟦a\n```";
        KeyResult result = Keys(Md, "Tab");
        Assert.Equal("```js\n  a\n```", result.Markdown);
        Assert.Equal("```js\na\n```", result.Undone);
        Assert.Equal(result.Markdown, Command(result.Markdown.Replace("  a", "  ⟦a"), "Shift-Mod-z"));
    }

    private sealed record KeyResult(List<bool> Handled, string Markdown, string Undone);

    private KeyResult Keys(string markdown, params string[] keys)
    {
        using JsonDocument doc = JsonDocument.Parse(bundle.Json(
            $"ContextEditor.keySequenceMarkdown({Js(markdown)}, {JsonSerializer.Serialize(keys)})"));
        JsonElement root = doc.RootElement;
        return new KeyResult(
            root.GetProperty("handled").EnumerateArray().Select(e => e.GetBoolean()).ToList(),
            root.GetProperty("markdown").GetString()!,
            root.GetProperty("undone").GetString()!);
    }

    private string Command(string markdown, string key, string typed = "") =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.commandMarkdown({Js(markdown)}, {Js(key)}, {Js(typed)})"))!;

    private string Command(string markdown, string key, string typed, string before) =>
        JsonSerializer.Deserialize<string>(bundle.Json(
            $"ContextEditor.commandMarkdown({Js(markdown)}, {Js(key)}, {Js(typed)}, {Js(before)})"))!;

    private string TypedMd(string typed) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.typedMarkdown({Js(typed)})"))!;

    private string TypedDoc(string typed) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.typedBlock({Js(typed)})"))!;

    private string Doc(string markdown) => bundle.Call("docJson", markdown);

    // The inline nodes of every block of a doc JSON as "text|<text>" (with "|<mark>" per mark) or "<type>".
    private static List<string> Inline(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        List<string> list = [];
        foreach (JsonElement block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (!block.TryGetProperty("content", out JsonElement content))
            {
                continue;
            }

            foreach (JsonElement node in content.EnumerateArray())
            {
                string type = node.GetProperty("type").GetString()!;
                if (type != "text")
                {
                    list.Add(type);
                    continue;
                }

                string marks = node.TryGetProperty("marks", out JsonElement m)
                    ? string.Concat(m.EnumerateArray().Select(x => "|" + x.GetProperty("type").GetString()))
                    : "";
                list.Add("text|" + node.GetProperty("text").GetString() + marks);
            }
        }

        return list;
    }

    // The blocks of a doc JSON as "code_block|<params>|<text>" or "<type>|<text>".
    private static List<string> Blocks(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("content").EnumerateArray().Select(block =>
        {
            string type = block.GetProperty("type").GetString()!;
            string text = block.TryGetProperty("content", out JsonElement content)
                ? string.Concat(content.EnumerateArray().Select(n => n.TryGetProperty("text", out JsonElement t) ? t.GetString() : "\ufffc"))
                : "";
            return type == "code_block" ? $"{type}|{block.GetProperty("attrs").GetProperty("params").GetString()}|{text}" : $"{type}|{text}";
        }).ToList();
    }

    private string Typed(string markdown, string typed) =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.typedAtMarkdown({Js(markdown)}, {Js(typed)})"))!;

    private string BlockTypes(string markdown) =>
        bundle.Json($"JSON.parse(ContextEditor.docJson({Js(markdown)})).content.map(b => b.type)");

    // The text inside the only fence of markdown.
    private static string Body(string markdown)
    {
        Match match = Regex.Match(markdown, "^(`{3,})\n(.*)\n\\1$", RegexOptions.Singleline);
        Assert.True(match.Success, markdown);
        return match.Groups[2].Value;
    }

    private List<(int From, int To, string Class)> Tokens(string text, string lang)
    {
        using JsonDocument document = JsonDocument.Parse(bundle.Json($"JSON.parse(ContextEditor.tokensJson({Js(text)}, {Js(lang)}))"));
        return document.RootElement.EnumerateArray()
            .Select(t => (t[0].GetInt32(), t[1].GetInt32(), t[2].GetString()!))
            .ToList();
    }

    private static void AssertSortedAndApart(List<(int From, int To, string Class)> tokens, int length)
    {
        int end = 0;
        foreach ((int from, int to, string _) in tokens)
        {
            Assert.True(from >= end && to > from && to <= length, $"{from}-{to} after {end}");
            end = to;
        }
    }

    private static string Js(string text) => JsonSerializer.Serialize(text);
}
