using System.Text.Json;

namespace Context.Tests;

// 343 AC-1, AC-2, AC-5 to AC-8: the editor bundle's decisions.js mirrors DecisionParser and DecisionNumbers, and the
// editor's replay, paste and reference decorations, run on the bundled editor.js in Jint (no browser, no DOM).
public sealed class EditorDecisionBundleTests : IClassFixture<EditorBundle>
{
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

        data.Add("﻿#!d3 One\r\ntwo\r\n\r\n#!d next\r\n");
        data.Add("# Plan\n\nstatus:: open\n#!d7 x\n");
        data.Add("#!d One\ntwo\n- #!d5 other\n  * #!d6 sixth\n# Head\n#!d after heading");
        data.Add("#!d " + new string('a', 999) + "\U0001F600tail");
        data.Add("see #!d12. (x) #!d3, `#!d4` x#!d5 #!d6- #!d7_ #!d8# #!d0 #!d1234567890 #!d999999999");
        data.Add("```\n#!d1 x\n```\n#!d2 y\n\n#!d2\n\n#!d\nOne\n\n- #!d\n");
        return data;
    }

    // AC-1: decisionsJson finds exactly DecisionParser.Parse's decisions (line, number, text, marker index and lengths).
    [Theory]
    [MemberData(nameof(Texts))]
    public void Decisions_MatchCoreParser(string markdown)
    {
        using JsonDocument found = JsonDocument.Parse(bundle.Call("decisionsJson", markdown));

        Assert.Equal(
            DecisionParser.Parse(markdown).Select(d => (d.Line, d.Number, d.Text, d.MarkerIndex, d.MarkerLength, d.StripLength)).ToArray(),
            found.RootElement.EnumerateArray().Select(d => (
                d.GetProperty("line").GetInt32(),
                d.GetProperty("number").ValueKind == JsonValueKind.Null ? (int?)null : d.GetProperty("number").GetInt32(),
                d.GetProperty("text").GetString()!,
                d.GetProperty("markerIndex").GetInt32(),
                d.GetProperty("markerLength").GetInt32(),
                d.GetProperty("stripLength").GetInt32())).ToArray());
    }

    // AC-2: decisionReferences finds exactly DecisionParser.References' numbers, in the same order.
    [Theory]
    [MemberData(nameof(Texts))]
    public void References_MatchCoreParser(string markdown)
    {
        Assert.Equal(
            JsonSerializer.Serialize(DecisionParser.References(markdown)),
            bundle.Call("decisionReferences", markdown));
    }

    // AC-7: stripDecisionMarkers gives exactly DecisionParser.StripMarkers' text.
    [Theory]
    [MemberData(nameof(Texts))]
    public void StripMarkers_MatchCore(string markdown)
    {
        Assert.Equal(DecisionParser.StripMarkers(markdown), bundle.Call("stripDecisionMarkers", markdown));
    }

    // AC-5, AC-6: applyDecisionEditsText replays DecisionNumbers.Apply's edits onto the same text exactly.
    [Theory]
    [InlineData("#!d one\n\n- #!d two\n\n#!d7 kept\n\n#!d three", "")]
    [InlineData("#!d1234 x\nsee #!d1234\n\n#!d9 mine", "1234")]
    [InlineData("#!d5 first\n\n#!d5 second\n\n#!d new", "")]
    [InlineData("﻿#!d\r\nOne\r\n\r\n#!d\t two  \r\n\r\n#!d\r\n", "")]
    [InlineData("#!d12\nWe ship.\n\n#!d3 x", "12,3")]
    public void ApplyEdits_MatchCore(string markdown, string ownedElsewhere)
    {
        HashSet<int> owned = ownedElsewhere.Length == 0 ? [] : ownedElsewhere.Split(',').Select(int.Parse).ToHashSet();
        DecisionEdits edits = DecisionNumbers.Apply(markdown, (n, _) => owned.Contains(n) ? DecisionHolder.Copy : DecisionHolder.None, _ => 40);
        string json = JsonSerializer.Serialize(new { bare = edits.Bare, strip = edits.Strip });

        Assert.Equal(edits.Text, Edited(markdown, json));
    }

    // AC-6: malformed edits change nothing (null).
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"bare\":[0],\"strip\":[]}")]
    [InlineData("{\"bare\":[\"1\"],\"strip\":[]}")]
    [InlineData("{\"bare\":[1]}")]
    [InlineData("{\"bare\":[1000000000],\"strip\":[]}")]
    [InlineData("[1]")]
    public void ApplyEdits_Malformed_Null(string json)
    {
        Assert.Equal("null", bundle.Json($"ContextEditor.applyDecisionEditsText({JsonSerializer.Serialize("#!d x\n")}, {JsonSerializer.Serialize(json)})"));
    }

    // AC-6: replayed in the editor, the edits number the paragraph the cursor is in without moving it out, and they are
    // not in the undo history: one undo removes only the typed "x".
    [Fact]
    public void EditorReplay_KeepsCursorParagraph_NotInUndo()
    {
        string markdown = "# Plan\n\n#!d one\n\n#!d5 two\n\n#!d three\n";
        string call = "ContextEditor.decisionEditsMarkdown(" + JsonSerializer.Serialize(markdown) + ", "
            + JsonSerializer.Serialize("{\"bare\":[6,7],\"strip\":[5]}") + ", 12)";
        using JsonDocument result = JsonDocument.Parse(JsonDocument.Parse(bundle.Json(call)).RootElement.GetString()!);
        string edited = result.RootElement.GetProperty("markdown").GetString()!;

        Assert.Equal(1, Count(edited, "x"));
        Assert.Equal("# Plan\n\n#!d6 one\n\ntwo\n\n#!d7 three\n", edited.Replace("x", string.Empty, StringComparison.Ordinal));
        Assert.Contains("x", result.RootElement.GetProperty("paragraph").GetString()!, StringComparison.Ordinal);
        Assert.StartsWith("#!d6 ", result.RootElement.GetProperty("paragraph").GetString()!, StringComparison.Ordinal);
        Assert.Equal("# Plan\n\n#!d6 one\n\ntwo\n\n#!d7 three\n", result.RootElement.GetProperty("undo").GetString());
    }

    // AC-7: a pasted slice loses its numbered markers (paragraph and references kept, bare markers kept); this
    // editor's last cut keeps them.
    [Theory]
    [InlineData("#!d12 We ship.\n\nsee #!d12\n\n- #!d34 listed\n\n#!d bare\n", false, "We ship.\n\nsee #!d12\n\n- listed\n\n#!d bare\n")]
    [InlineData("#!d12 We ship.\n\nsee #!d12\n", true, "#!d12 We ship.\n\nsee #!d12\n")]
    [InlineData("`#!d12` code\n\n```\n#!d12 fence\n```\n", false, "`#!d12` code\n\n```\n#!d12 fence\n```\n")]
    public void PastedSlice_StripsNumberedMarkers(string markdown, bool keep, string expected)
    {
        string call = "ContextEditor.pastedSliceMarkdown(" + JsonSerializer.Serialize(markdown) + ", " + (keep ? "true" : "false") + ")";

        Assert.Equal(expected, JsonDocument.Parse(bundle.Json(call)).RootElement.GetString());
    }

    // AC-7: pasted plain text follows the same rule: stripped unless it equals the last cut.
    [Theory]
    [InlineData("#!d12 We ship.\nsee #!d12", null, "We ship.\nsee #!d12")]
    [InlineData("#!d12 We ship.", "#!d12 We ship.", "#!d12 We ship.")]
    [InlineData("#!d12 We ship.", "other", "We ship.")]
    [InlineData("#!d bare", null, "#!d bare")]
    public void PastedText_StripsUnlessLastCut(string text, string? lastCut, string expected)
    {
        string call = "ContextEditor.pastedDecisionMarkdown(" + JsonSerializer.Serialize(text) + ", "
            + (lastCut is null ? "null" : JsonSerializer.Serialize(lastCut)) + ")";

        Assert.Equal(expected, JsonDocument.Parse(bundle.Json(call)).RootElement.GetString());
    }

    // AC-8: a known reference is a decision-ref link with its headline as title; an unknown one is grey with the
    // "No decision" title; code and a paragraph's own marker get none; the text is unchanged (decorations only).
    [Fact]
    public void ReferenceDecorations_KnownUnknownNotInCode()
    {
        string markdown = "#!d5 own marker, see #!d5 and #!d9.\n\n`#!d5` code\n\n```\n#!d5\n```\n";
        string call = "ContextEditor.decisionRefsJson(" + JsonSerializer.Serialize(markdown) + ", "
            + JsonSerializer.Serialize("[[5,\"We ship\"],[9,null]]") + ")";
        using JsonDocument refs = JsonDocument.Parse(JsonDocument.Parse(bundle.Json(call)).RootElement.GetString()!);

        Assert.Equal(
            [("#!d5", "decision-ref", "We ship", "5"), ("#!d9", "decision-ref-unknown", "No decision #!d9.", (string?)null)],
            refs.RootElement.EnumerateArray().Select(r => (
                r.GetProperty("text").GetString()!,
                r.GetProperty("class").GetString()!,
                r.GetProperty("title").GetString()!,
                r.GetProperty("decision").ValueKind == JsonValueKind.Null ? null : r.GetProperty("decision").GetString())).ToArray());
        Assert.Equal(markdown, bundle.Call("roundTrip", markdown));
    }

    // AC-2: the outline finds no heading in a marker or a reference.
    [Theory]
    [InlineData("#!d")]
    [InlineData("#!d12")]
    [InlineData("- #!d5 x")]
    public void Outline_FindsNothing(string markdown)
    {
        string call = "ContextEditor.outline(" + JsonSerializer.Serialize("# Plan\n\n" + markdown + "\n") + ", \"Plan\")";

        Assert.Equal(JsonSerializer.Serialize("[]"), bundle.Json(call));
    }

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
