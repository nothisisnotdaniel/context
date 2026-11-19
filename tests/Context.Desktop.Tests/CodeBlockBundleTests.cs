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
        List<string> used = Regex.Matches(editor, @"var\((--code-[a-z-]+)\)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(used.Count >= 9, string.Join(", ", used));
        foreach (string name in used)
        {
            Assert.Contains(name + ":", light, StringComparison.Ordinal);
            Assert.Contains(name + ":", dark, StringComparison.Ordinal);
        }
    }

    // The text of the first block that opens with start, up to its matching brace.
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

    private string Command(string markdown, string key, string typed = "") =>
        JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.commandMarkdown({Js(markdown)}, {Js(key)}, {Js(typed)})"))!;

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
