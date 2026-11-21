using System.Text.Json;

namespace Context.Tests;

// 349 AC-1, AC-3, AC-4, AC-7 to AC-9 (and 343's replay): the editor bundle's decisions.js mirrors DecisionParser,
// DecisionNumbers and DecisionFolder, and the picker's and Copy reference's pure rules, run on the bundled editor.js in
// Jint (no browser, no DOM).
public sealed class EditorDecisionBundleTests : IClassFixture<EditorBundle>
{
    private const string Guid = "11111111-2222-3333-4444-555555555555";

    private static readonly DecisionFolder Folder = new("ab12", new Guid(Guid), true);

    private readonly EditorBundle bundle;

    public EditorDecisionBundleTests(EditorBundle bundle)
    {
        this.bundle = bundle;
    }

    public static TheoryData<string> Texts()
    {
        TheoryData<string> data = [];
        foreach (string file in Fixtures())
        {
            data.Add(File.ReadAllText(file));
        }

        data.Add("﻿!d ab12-3 One\r\ntwo\r\n\r\n!d next\r\n");
        data.Add("# Plan\n\nstatus:: open\n!d ab12-7 x\n");
        data.Add("!d One\ntwo\n- !d 5 other\n  * !d AB12-6 sixth\n# Head\n!d after heading");
        data.Add("!d " + new string('a', 999) + "\U0001F600tail");
        data.Add("!done\n\n!dx\n\n!D x\n\ntext !d mid\n\n#!d x\n\n#!d12 x\n\n!d\t4 tab\n\n!d 0 zero\n\n!d ai12-5 bad\n\n!d ab12-5x glued");
        data.Add("see #!d12. (x) #!dAB12-3, `#!d4` x#!d5 #!d6- #!d7_ #!d8# #!d0 #!d1234567890 #!d999999999 #!dao12-5 #!dzz99-1");
        data.Add("```\n!d ab12-1 x\n```\n!d ab12-2 y\n\n!d ab12-2\n\n!d\nOne\n\n- !d\n");
        return data;
    }

    // AC-1, AC-9: decisionsJson finds exactly DecisionParser.Parse's decisions (line, prefix, number, text, marker and
    // token indexes, and the marker end where the note's Copy reference widget sits).
    [Theory]
    [MemberData(nameof(Texts))]
    public void Decisions_MatchCoreParser(string markdown)
    {
        using JsonDocument found = JsonDocument.Parse(bundle.Call("decisionsJson", markdown));

        Assert.Equal(
            DecisionParser.Parse(markdown)
                .Select(d => (d.Line, d.Prefix, d.Number, d.Text, d.MarkerIndex, d.TokenIndex, d.TokenLength, d.MarkerEnd))
                .ToArray(),
            found.RootElement.EnumerateArray().Select(d => (
                d.GetProperty("line").GetInt32(),
                d.GetProperty("prefix").ValueKind == JsonValueKind.Null ? null : d.GetProperty("prefix").GetString(),
                d.GetProperty("number").ValueKind == JsonValueKind.Null ? (int?)null : d.GetProperty("number").GetInt32(),
                d.GetProperty("text").GetString()!,
                d.GetProperty("markerIndex").GetInt32(),
                d.GetProperty("tokenIndex").GetInt32(),
                d.GetProperty("tokenLength").GetInt32(),
                d.GetProperty("markerEnd").GetInt32())).ToArray());
    }

    // AC-7: decisionReferences finds exactly DecisionParser.References, in the same order and forms.
    [Theory]
    [MemberData(nameof(Texts))]
    public void References_MatchCoreParser(string markdown)
    {
        Assert.Equal(
            JsonSerializer.Serialize(DecisionParser.References(markdown).Select(r => r.Text)),
            bundle.Call("decisionReferences", markdown));
    }

    // AC-3, AC-4: applyDecisionEditsText replays DecisionNumbers.Apply's edits (tokens and the folder line) onto the
    // same text exactly.
    [Theory]
    [InlineData("!d one\n\n- !d two\n\n!d ab12-7 kept\n\n!d three", "7")]
    [InlineData("# Plan\n!d 12 plain\n\n!d zz99-3 other\n\n!d ab12-9 refused", "")]
    [InlineData("!d ab12-5 first\n\n!d ab12-5 first\n\n!d ab12-5 second\n\n!d new", "5")]
    [InlineData("﻿!d\r\nOne\r\n\r\n!d\t two  \r\n\r\n!d\r\n", "")]
    [InlineData("# T\n\nstatus:: open\n\n!d x", "")]
    [InlineData("decision-folder:: " + Guid + "\n!d AB12-8 kept\n\n!d new", "8")]
    public void ApplyEdits_MatchCore(string markdown, string keep)
    {
        HashSet<int> kept = keep.Length == 0 ? [] : keep.Split(',').Select(int.Parse).ToHashSet();
        DecisionEdits edits = DecisionNumbers.Apply(markdown, Folder, (n, _) => kept.Contains(n), _ => 40);

        Assert.True(edits.Changed);
        Assert.Equal(edits.Text, Edited(markdown, NoteEditor.DecisionEditsJson(edits)));
    }

    // AC-4: withFolderLine places the line exactly as DecisionFolder.WithFolderLine does.
    [Theory]
    [InlineData("# Plan\n!d x\n")]
    [InlineData("# Plan")]
    [InlineData("!d x\n")]
    [InlineData("")]
    [InlineData("﻿# T\r\n\r\nkey:: v\r\n")]
    [InlineData("# T\n\n\ntext\n")]
    [InlineData("#nope\n!d x")]
    public void WithFolderLine_MatchesCore(string markdown)
    {
        string call = "ContextEditor.withFolderLine(" + JsonSerializer.Serialize(markdown) + ", " + JsonSerializer.Serialize(Guid) + ")";

        Assert.Equal(DecisionFolder.WithFolderLine(markdown, new Guid(Guid)), JsonDocument.Parse(bundle.Json(call)).RootElement.GetString());
    }

    // Malformed edits change nothing (null).
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"set\":[[0,null,\"1\"]],\"folder\":null}")]
    [InlineData("{\"set\":[[0,null,\"AB12-1\"]],\"folder\":null}")]
    [InlineData("{\"set\":[[\"0\",null,\"ab12-1\"]],\"folder\":null}")]
    [InlineData("{\"set\":[[0,null]],\"folder\":null}")]
    [InlineData("{\"set\":[],\"folder\":\"nope\"}")]
    [InlineData("{\"folder\":null}")]
    [InlineData("[1]")]
    public void ApplyEdits_Malformed_Null(string json)
    {
        Assert.Equal("null", bundle.Json($"ContextEditor.applyDecisionEditsText({JsonSerializer.Serialize("!d x\n")}, {JsonSerializer.Serialize(json)})"));
    }

    // Replayed in the editor, the edits number the paragraph the cursor is in without moving it out, add the folder line
    // to the hidden attribute block, and are not in the undo history: one undo removes only the typed "x".
    [Fact]
    public void EditorReplay_KeepsCursorParagraph_NotInUndo()
    {
        string markdown = "# Plan\n\n!d one\n\n!d 5 two\n\n!d three\n";
        string json = "{\"set\":[[0,null,\"ab12-6\"],[1,\"5\",\"ab12-7\"],[2,null,\"ab12-8\"]],\"folder\":\"" + Guid + "\"}";
        string call = "ContextEditor.decisionEditsMarkdown(" + JsonSerializer.Serialize(markdown) + ", "
            + JsonSerializer.Serialize(json) + ", 4, \"Plan\")";
        using JsonDocument result = JsonDocument.Parse(JsonDocument.Parse(bundle.Json(call)).RootElement.GetString()!);
        string edited = result.RootElement.GetProperty("markdown").GetString()!;

        string expected = $"# Plan\ndecision-folder:: {Guid}\n\n!d ab12-6 one\n\n!d ab12-7 two\n\n!d ab12-8 three\n";
        Assert.Equal(1, Count(edited, "x"));
        Assert.Equal(expected, edited.Replace("x", string.Empty, StringComparison.Ordinal));
        Assert.Contains("x", result.RootElement.GetProperty("paragraph").GetString()!, StringComparison.Ordinal);
        Assert.StartsWith("!d ab12-6 ", result.RootElement.GetProperty("paragraph").GetString()!, StringComparison.Ordinal);
        Assert.Equal(expected, result.RootElement.GetProperty("undo").GetString());
    }

    // AC-7, AC-9: a known reference is a decision-ref link with its headline as title, an unknown one grey with the
    // "No decision" title, in both forms; code and a paragraph's own marker get none; the text is unchanged.
    [Fact]
    public void ReferenceDecorations_KnownUnknownNotInCode()
    {
        string markdown = "!d ab12-5 own marker, see #!d5, #!dZZ99-2 and #!d9.\n\n`#!d5` code\n\n```\n#!d5\n```\n";
        string call = "ContextEditor.decisionRefsJson(" + JsonSerializer.Serialize(markdown) + ", "
            + JsonSerializer.Serialize("[[\"5\",\"We ship\"],[\"zz99-2\",\"Theirs\"],[\"9\",null]]") + ")";
        using JsonDocument refs = JsonDocument.Parse(JsonDocument.Parse(bundle.Json(call)).RootElement.GetString()!);

        Assert.Equal(
            [
                ("#!d5", "decision-ref", "We ship", "5"), ("#!dZZ99-2", "decision-ref", "Theirs", "zz99-2"),
                ("#!d9", "decision-ref-unknown", "No decision #!d9.", (string?)null),
            ],
            refs.RootElement.EnumerateArray().Select(r => (
                r.GetProperty("text").GetString()!,
                r.GetProperty("class").GetString()!,
                r.GetProperty("title").GetString()!,
                r.GetProperty("decision").ValueKind == JsonValueKind.Null ? null : r.GetProperty("decision").GetString())).ToArray());
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
    }

    // AC-9: with the prefixes set, every prefixed marker gets one Copy reference widget at its marker end: an own
    // marker as the plain number, another prefix as p-n, the own prefix in a clashing note as the alias; bare and plain
    // markers get none.
    [Theory]
    [InlineData(null, "1234", "1234")]
    [InlineData("\"q7r8\"", "q7r8-1234", "q7r8-1234")]
    public void CopyWidgets_AtMarkerEnd(string? alias, string ownLabel, string ownRef)
    {
        string markdown = "!d ab12-1234 we ship\n\n!d zz99-3 theirs\n\n!d bare\n\n!d 7 plain\n";
        string prefixes = "{\"own\":\"ab12\",\"alias\":" + (alias ?? "null") + "}";
        string call = "ContextEditor.decisionRefsJson(" + JsonSerializer.Serialize(markdown) + ", \"[]\", "
            + JsonSerializer.Serialize(prefixes) + ")";
        using JsonDocument result = JsonDocument.Parse(JsonDocument.Parse(bundle.Json(call)).RootElement.GetString()!);
        JsonElement[] widgets = result.RootElement.GetProperty("widgets").EnumerateArray().ToArray();

        IReadOnlyList<ParsedDecision> parsed = DecisionParser.Parse(markdown);
        Assert.Equal(
            [(ownLabel, ownRef, parsed[0].MarkerEnd - parsed[0].MarkerIndex), ("zz99-3", "zz99-3", parsed[1].MarkerEnd - parsed[1].MarkerIndex)],
            widgets.Select(w => (w.GetProperty("label").GetString()!, w.GetProperty("ref").GetString()!, w.GetProperty("offset").GetInt32())).ToArray());
        Assert.Equal("Copy reference to Decision " + ownLabel, widgets[0].GetProperty("attrs").GetProperty("aria-label").GetString());
        Assert.Equal("#!d" + ownRef, widgets[0].GetProperty("attrs").GetProperty("copy").GetString());
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
    }

    // AC-8: pickerQuery gives the text typed after a #!d that ends at pos, or null in code, a fence or with none.
    [Theory]
    [InlineData("see #!d", 7, "")]
    [InlineData("see #!d12", 9, "12")]
    [InlineData("see #!dab1", 10, "ab1")]
    [InlineData("#!dab12-3", 9, "ab12-3")]
    [InlineData("see #!d12", 8, "1")]
    [InlineData("see #!d12 more", 14, null)]
    [InlineData("x#!d12", 6, null)]
    [InlineData("see ` #!d12", 11, "12")]
    [InlineData("see `#!d12` x", 10, null)]
    [InlineData("```\n#!d12", 9, null)]
    [InlineData("```\nx\n```\n#!d12", 15, "12")]
    [InlineData("no reference", 5, null)]
    [InlineData("see #!d12!", 10, null)]
    public void PickerQuery(string text, int pos, string? expected)
    {
        string call = $"ContextEditor.pickerQuery({JsonSerializer.Serialize(text)}, {pos})";

        Assert.Equal(JsonSerializer.Serialize(expected), bundle.Json(call));
    }

    // AC-8: pickerRows filters by headline (ignoring case), reference or label start; at most 20; the no-match row.
    [Theory]
    [InlineData("", "1234,zz99-5,77")]
    [InlineData("SHIP", "1234")]
    [InlineData("12", "1234")]
    [InlineData("zz", "zz99-5")]
    [InlineData("7", "77")]
    [InlineData("nothing", "")]
    public void PickerRows_Filter(string query, string refs)
    {
        string list = "[{\"label\":\"1234\",\"ref\":\"1234\",\"headline\":\"We ship Friday\"},"
            + "{\"label\":\"zz99-5\",\"ref\":\"zz99-5\",\"headline\":\"Theirs\"},"
            + "{\"label\":\"77\",\"ref\":\"77\",\"headline\":\"Hire two\"}]";
        using JsonDocument rows = JsonDocument.Parse(Rows(list, query));

        if (refs.Length == 0)
        {
            JsonElement row = Assert.Single(rows.RootElement.EnumerateArray().ToArray());
            Assert.Equal("No matching decisions.", row.GetProperty("text").GetString());
            Assert.False(row.GetProperty("selectable").GetBoolean());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("ref").ValueKind);
            return;
        }

        Assert.Equal(refs.Split(','), rows.RootElement.EnumerateArray().Select(r => r.GetProperty("ref").GetString()!));
        Assert.All(rows.RootElement.EnumerateArray(), r => Assert.True(r.GetProperty("selectable").GetBoolean()));
    }

    // AC-8: the row text and the cap of 20.
    [Fact]
    public void PickerRows_TextAndCap()
    {
        string list = "[" + string.Join(",", Enumerable.Range(1, 25).Select(n => $"{{\"label\":\"{n}\",\"ref\":\"{n}\",\"headline\":\"h{n}\"}}")) + "]";
        using JsonDocument rows = JsonDocument.Parse(Rows(list, string.Empty));

        Assert.Equal(20, rows.RootElement.GetArrayLength());
        Assert.Equal("Decision 1  h1", rows.RootElement[0].GetProperty("text").GetString());
    }

    // AC-8: each key in the open picker.
    [Theory]
    [InlineData("ArrowDown", 0, "{\"action\":\"move\",\"selected\":1}")]
    [InlineData("ArrowDown", 1, "{\"action\":\"move\",\"selected\":1}")]
    [InlineData("ArrowUp", 1, "{\"action\":\"move\",\"selected\":0}")]
    [InlineData("ArrowUp", 0, "{\"action\":\"move\",\"selected\":0}")]
    [InlineData("ArrowUp", 9, "{\"action\":\"move\",\"selected\":0}")]
    [InlineData("Enter", 1, "{\"action\":\"insert\",\"from\":4,\"to\":9,\"insert\":\"#!dzz99-5 \"}")]
    [InlineData("Tab", 0, "{\"action\":\"insert\",\"from\":4,\"to\":9,\"insert\":\"#!d1234 \"}")]
    [InlineData("Escape", 0, "{\"action\":\"close\"}")]
    [InlineData(" ", 0, "{\"action\":\"close\"}")]
    [InlineData("a", 0, "{\"action\":\"pass\"}")]
    public void PickerKey_Keys(string key, int selected, string expected)
    {
        string state = "{\"rows\":[{\"text\":\"Decision 1234  a\",\"ref\":\"1234\",\"selectable\":true},"
            + "{\"text\":\"Decision zz99-5  b\",\"ref\":\"zz99-5\",\"selectable\":true}],\"selected\":" + selected + ",\"from\":4,\"to\":9}";

        Assert.Equal(expected, Key(state, key));
    }

    // AC-8: Enter on the no-match row does nothing.
    [Fact]
    public void PickerKey_NoMatchEnter_None()
    {
        string state = "{\"rows\":[{\"text\":\"No matching decisions.\",\"ref\":null,\"selectable\":false}],\"selected\":0,\"from\":0,\"to\":3}";

        Assert.Equal("{\"action\":\"none\"}", Key(state, "Enter"));
    }

    // AC-8, AC-9: the popup's and the Copy reference button's attributes.
    [Fact]
    public void PickerAndCopyAttrs()
    {
        Assert.Equal("{\"class\":\"decision-picker\",\"role\":\"listbox\"}", Unquote(bundle.Json("ContextEditor.pickerAttrsJson()")));
        Assert.Equal(
            "{\"class\":\"decision-copy\",\"aria-label\":\"Copy reference to Decision ab12-7\",\"title\":\"Copy reference to Decision ab12-7\",\"text\":\"Copy\",\"copy\":\"#!dab12-7\"}",
            Unquote(bundle.Json("ContextEditor.copyButtonAttrsJson(\"ab12-7\", \"ab12-7\")")));
    }

    // AC-1: the outline finds no heading in a marker or a reference.
    [Theory]
    [InlineData("!d")]
    [InlineData("#!d12")]
    [InlineData("- !d ab12-5 x")]
    public void Outline_FindsNothing(string markdown)
    {
        string call = "ContextEditor.outline(" + JsonSerializer.Serialize("# Plan\n\n" + markdown + "\n") + ", \"Plan\")";

        Assert.Equal(JsonSerializer.Serialize("[]"), bundle.Json(call));
    }

    private string Rows(string list, string query) =>
        Unquote(bundle.Json($"ContextEditor.pickerRowsJson({JsonSerializer.Serialize(list)}, {JsonSerializer.Serialize(query)})"));

    private string Key(string state, string key) =>
        Unquote(bundle.Json($"ContextEditor.pickerKeyJson({JsonSerializer.Serialize(state)}, {JsonSerializer.Serialize(key)})"));

    private static string Unquote(string json) => JsonDocument.Parse(json).RootElement.GetString()!;

    private string Edited(string markdown, string json)
    {
        string call = "ContextEditor.applyDecisionEditsText(" + JsonSerializer.Serialize(markdown) + ", " + JsonSerializer.Serialize(json) + ")";
        return JsonDocument.Parse(bundle.Json(call)).RootElement.GetString()!;
    }

    private static int Count(string text, string part) =>
        (text.Length - text.Replace(part, string.Empty, StringComparison.Ordinal).Length) / part.Length;

    private static IEnumerable<string> Fixtures()
    {
        string folder = Path.Combine(RepoFiles.Root(), "tests", "Context.Desktop.Tests", "RoundTrip");
        return Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal);
    }
}
