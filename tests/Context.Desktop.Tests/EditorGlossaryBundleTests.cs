using System.Text.Json;

namespace Context.Tests;

// 380 AC-6, AC-7: the editor bundle's glossary.js matching and the view's glossary decorations, run on the bundled
// editor.js in Jint (no browser, no DOM).
public sealed class EditorGlossaryBundleTests : IClassFixture<EditorBundle>
{
    private readonly EditorBundle bundle;

    public EditorGlossaryBundleTests(EditorBundle bundle)
    {
        this.bundle = bundle;
    }

    private string Matches(string text, params string[] terms) =>
        bundle.Json("JSON.parse(ContextEditor.glossaryMatches(" + JsonSerializer.Serialize(text) + ", "
            + JsonSerializer.Serialize(JsonSerializer.Serialize(terms)) + "))");

    // 380 AC-6: [from, to, termIndex] triples, case-sensitive, whole words, left to right, no overlaps, longest wins.
    [Theory]
    [InlineData("Context window size", new[] { "Context", "Context window" }, "[[0,14,1]]")]
    [InlineData("Context and Context window", new[] { "Context window", "Context" }, "[[0,7,1],[12,26,0]]")]
    [InlineData("it ITEM IT", new[] { "IT" }, "[[8,10,0]]")]
    [InlineData("IT, (IT) IT2 xIT", new[] { "IT" }, "[[0,2,0],[5,7,0]]")]
    [InlineData("API API", new[] { "API" }, "[[0,3,0],[4,7,0]]")]
    [InlineData("api", new[] { "API" }, "[]")]
    [InlineData("Über alles", new[] { "Über" }, "[[0,4,0]]")]
    [InlineData("ÜberX", new[] { "Über" }, "[]")]
    [InlineData("Context windows", new[] { "Context window", "Context" }, "[[0,7,1]]")]
    public void GlossaryMatches_Rules(string text, string[] terms, string expected)
    {
        Assert.Equal(expected, Matches(text, terms));
    }

    // 380 AC-6: malformed terms give no matches; [term, title] pairs are read like plain terms.
    [Fact]
    public void GlossaryMatches_MalformedTerms_None_PairsRead()
    {
        Assert.Equal("[]", bundle.Json("JSON.parse(ContextEditor.glossaryMatches(\"API\", \"nope\"))"));
        Assert.Equal("[]", bundle.Json("JSON.parse(ContextEditor.glossaryMatches(\"API\", \"[[\\\"\\\", \\\"t\\\"]]\"))"));
        Assert.Equal("[[0,3,0]]", bundle.Json("JSON.parse(ContextEditor.glossaryMatches(\"API x\", \"[[\\\"API\\\", \\\"t\\\"]]\"))"));
        string tooMany = JsonSerializer.Serialize(Enumerable.Range(0, 501).Select(i => "T" + i).ToArray());
        Assert.Equal("[]", bundle.Json("JSON.parse(ContextEditor.glossaryMatches(\"T1\", " + JsonSerializer.Serialize(tooMany) + "))"));
    }

    private JsonElement Decorations(string markdown, IEnumerable<(string Term, string Title)> terms)
    {
        string json = JsonSerializer.Serialize(terms.Select(t => new[] { t.Term, t.Title }).ToArray());
        using JsonDocument document = JsonDocument.Parse(bundle.Json(
            "JSON.parse(ContextEditor.glossaryDecorationsJson(" + JsonSerializer.Serialize(markdown) + ", "
                + JsonSerializer.Serialize(json) + "))"));
        return document.RootElement.Clone();
    }

    // 380 AC-7: every match outside code marks and code blocks is a span.glossary-term with the meanings as title, one
    // line per meaning (a term with two meanings lists both).
    [Fact]
    public void Decorations_SkipCode_TitleListsMeanings()
    {
        JsonElement found = Decorations(
            "API and `API` here\n\n```\nAPI in a block\n```\n\n- Kpi in a list, also API\n",
            [("API", "Application programming interface (Words)\nA pint (Pub)"), ("Kpi", "key figure (Words)")]);

        Assert.Equal(
            [
                ("API", "glossary-term", "Application programming interface (Words)\nA pint (Pub)"),
                ("Kpi", "glossary-term", "key figure (Words)"),
                ("API", "glossary-term", "Application programming interface (Words)\nA pint (Pub)"),
            ],
            found.EnumerateArray().Select(d => (
                d.GetProperty("text").GetString()!, d.GetProperty("class").GetString()!, d.GetProperty("title").GetString()!)));
    }

    // 380 AC-7: no terms (or malformed ones) draw nothing.
    [Fact]
    public void Decorations_NoTerms_Nothing()
    {
        Assert.Empty(Decorations("API here", []).EnumerateArray());
        Assert.Equal("[]", bundle.Json("JSON.parse(ContextEditor.glossaryDecorationsJson(\"API\", \"{}\"))"));
    }

    // 380 AC-7: setGlossary's transaction draws the spans but changes no text and adds no undo step.
    [Fact]
    public void SetGlossaryTransaction_KeepsTextAndUndo()
    {
        string json = JsonSerializer.Serialize(new[] { new[] { "API", "t" } });
        using JsonDocument result = JsonDocument.Parse(bundle.Json(
            "JSON.parse(ContextEditor.glossaryMetaJson(\"API and API\", " + JsonSerializer.Serialize(json) + "))"));
        JsonElement root = result.RootElement;

        Assert.Equal(2, root.GetProperty("spans").GetInt32());
        Assert.True(root.GetProperty("sameDoc").GetBoolean());
        Assert.False(root.GetProperty("changed").GetBoolean());
        Assert.Equal(root.GetProperty("undoBefore").GetInt32(), root.GetProperty("undoAfter").GetInt32());
    }
}
