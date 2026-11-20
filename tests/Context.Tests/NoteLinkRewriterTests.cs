using System.Reflection;

namespace Context.Tests;

// 206: rewriting [[links]] when a note is renamed. Pure string work against a fixed list of names.
public sealed class NoteLinkRewriterTests
{
    private static readonly string[] Names = ["Plan", "Plan/Child", "Planet", "Other", "C", "C# notes", "Doc.md"];

    // AC-1: the note part is replaced with the new full name; spaces, heading, alias, embed and .md stay as written.
    [Theory]
    [InlineData("[[Plan]]", "[[Project]]")]
    [InlineData("see [[ Plan ]] here", "see [[ Project ]] here")]
    [InlineData("[[plan]]", "[[Project]]")]
    [InlineData("[[Plan|the plan]]", "[[Project|the plan]]")]
    [InlineData("[[Plan#Next steps]]", "[[Project#Next steps]]")]
    [InlineData("[[Plan  #  Next steps | x ]]", "[[Project  #  Next steps | x ]]")]
    [InlineData("[[Plan.md]]", "[[Project.md]]")]
    [InlineData("[[plan.MD#H|a]]", "[[Project.MD#H|a]]")]
    [InlineData("![[Plan]]", "![[Project]]")]
    [InlineData("[[Plan/Child]]", "[[Project/Child]]")]
    [InlineData("[[plan/child#H|a]]", "[[Project/Child#H|a]]")]
    [InlineData("[[Plan]] and [[Other]] and [[Plan]]", "[[Project]] and [[Other]] and [[Project]]")]
    [InlineData("`code` [[Plan]] `more`", "`code` [[Project]] `more`")]
    [InlineData("```\n[[Plan]]\n```\n[[Plan]]\n", "```\n[[Plan]]\n```\n[[Project]]\n")]
    public void Rewrite_RenamedNote(string text, string expected)
    {
        Assert.Equal(expected, NoteLinkRewriter.Rewrite(text, new NameLookup(Names), "Plan", "Project"));
    }

    // AC-1: CRLF line endings and a BOM stay byte for byte.
    [Fact]
    public void Rewrite_CrlfAndBom_Kept()
    {
        string text = "\uFEFF# Notes\r\nsee [[Plan]]\r\n\r\nand [[Plan#H|x]] too\r\n";

        Assert.Equal(
            "\uFEFF# Notes\r\nsee [[Project]]\r\n\r\nand [[Project#H|x]] too\r\n",
            NoteLinkRewriter.Rewrite(text, new NameLookup(Names), "Plan", "Project"));
    }

    // AC-1: renaming a subpage keeps its parent; a subpage of a subpage moves along.
    [Fact]
    public void Rewrite_Subpage_KeepsParent()
    {
        NameLookup before = new(["Plan", "Plan/Child", "Plan/Child/Deep"]);

        Assert.Equal(
            "[[Plan]] [[Plan/Kid]] [[Plan/Kid/Deep#H]]",
            NoteLinkRewriter.Rewrite("[[Plan]] [[Plan/Child]] [[Plan/Child/Deep#H]]", before, "Plan/Child", "Plan/Kid"));
    }

    // AC-1: a title with a # resolves as a whole first, and is renamed as a whole.
    [Fact]
    public void Rewrite_WholeTargetWithHash()
    {
        Assert.Equal(
            "[[Sharp]] [[C#H]]",
            NoteLinkRewriter.Rewrite("[[C# notes]] [[C#H]]", new NameLookup(Names), "C# notes", "Sharp"));
    }

    // AC-2: code, own-note headings, other notes, missing notes and look-alike names are never changed.
    [Theory]
    [InlineData("`[[Plan]]`")]
    [InlineData("x ``[[Plan]]`` y")]
    [InlineData("```\n[[Plan]]\n```")]
    [InlineData("  ```md\n[[Plan]]")]
    [InlineData("[[#Intro]]")]
    [InlineData("[[Other]] [[Planet]] [[Missing]] [[Plan/Missing]]")]
    [InlineData("[[Plan `x]] y`")]
    [InlineData("#[[Plan]] @[[Plan]]")]
    [InlineData("no links at all")]
    [InlineData("")]
    public void Rewrite_NothingToChange_SameInstance(string text)
    {
        Assert.Same(text, NoteLinkRewriter.Rewrite(text, new NameLookup(Names), "Plan", "Project"));
    }

    // AC-2: [[C# notes]] names another note, so renaming C leaves it; [[C#H]] is C's heading.
    [Fact]
    public void Rewrite_OtherNoteWithHash_Unchanged()
    {
        Assert.Equal(
            "[[C# notes]] [[D#H]] [[D]]",
            NoteLinkRewriter.Rewrite("[[C# notes]] [[C#H]] [[C]]", new NameLookup(Names), "C", "D"));
    }

    // AC-3: a case-only or same-name rename changes no link.
    [Theory]
    [InlineData("Plan")]
    [InlineData("plan")]
    [InlineData("PLAN")]
    public void Rewrite_CaseOnly_SameInstance(string newName)
    {
        string text = "[[Plan]] [[plan#H]]";

        Assert.Same(text, NoteLinkRewriter.Rewrite(text, new NameLookup(Names), "Plan", newName));
    }

    // AC-6: a valid rewrite passes the round trip and gives the rewritten text.
    [Fact]
    public void TryRewrite_Valid_ReturnsRewritten()
    {
        NameLookup before = new(Names);
        NameLookup after = NoteLinkRewriter.Renamed(Names, "Plan", "Project");

        Assert.True(NoteLinkRewriter.TryRewrite(
            "[[Plan#H|a]] [[Plan/Child]] [[Other]]", before, after, "Plan", "Project", out string rewritten));
        Assert.Equal("[[Project#H|a]] [[Project/Child]] [[Other]]", rewritten);
    }

    // AC-6: no link to change means true and the same instance, whatever the new title holds.
    [Fact]
    public void TryRewrite_NoLink_SameInstance()
    {
        string text = "[[Other]]";
        NameLookup before = new(Names);

        Assert.True(NoteLinkRewriter.TryRewrite(
            text, before, NoteLinkRewriter.Renamed(Names, "Plan", "Pro#ject"), "Plan", "Pro#ject", out string rewritten));
        Assert.Same(text, rewritten);
    }

    // AC-6: a new title that changes how the link parses or resolves fails, and the text is given back unchanged.
    [Theory]
    [InlineData("Pro#ject")]
    [InlineData("Pro|ject")]
    [InlineData("Pro[ject")]
    [InlineData("Pro]ject")]
    [InlineData("Pro`ject")]
    public void TryRewrite_UnsafeTitle_Fails(string newName)
    {
        string text = "see [[Plan]]";
        NameLookup before = new(Names);

        Assert.False(NoteLinkRewriter.TryRewrite(
            text, before, NoteLinkRewriter.Renamed(Names, "Plan", newName), "Plan", newName, out string rewritten));
        Assert.Same(text, rewritten);
    }

    // AC-6: a rewritten target over 403 characters is no link any more, so it fails.
    [Fact]
    public void TryRewrite_TargetTooLong_Fails()
    {
        string newName = new string('p', 395);
        string text = "[[Plan/Child#Heading]]";
        NameLookup before = new(Names);

        Assert.False(NoteLinkRewriter.TryRewrite(
            text, before, NoteLinkRewriter.Renamed(Names, "Plan", newName), "Plan", newName, out string rewritten));
        Assert.Same(text, rewritten);
    }

    // AC-6: a link that would resolve to another note after the rename fails.
    [Fact]
    public void TryRewrite_ResolvesElsewhere_Fails()
    {
        NameLookup before = new(["Plan", "Project"]);
        NameLookup after = new(["Project", "Project"]);

        Assert.False(NoteLinkRewriter.TryRewrite("[[Plan.md]]", before, new NameLookup(["Other"]), "Plan", "Project", out _));
        Assert.True(NoteLinkRewriter.TryRewrite("[[Plan.md]]", before, after, "Plan", "Project", out string ok));
        Assert.Equal("[[Project.md]]", ok);
    }

    // AC-1/AC-6: Moved gives the name after the rename for the note and its subpages, else null.
    [Theory]
    [InlineData("Plan", "Project")]
    [InlineData("Plan/Child", "Project/Child")]
    [InlineData("Planet", null)]
    [InlineData("Other", null)]
    [InlineData("Plan/", null)]
    public void Moved_NoteAndSubpages(string name, string? expected)
    {
        Assert.Equal(expected, NoteLinkRewriter.Moved(name, "Plan", "Project"));
    }

    // Scope: FindSpans finds what Parse finds, over every input of the parser tests; each span's text is its target.
    [Fact]
    public void FindSpans_MatchesParse_OnParserTestInputs()
    {
        List<string> inputs = typeof(NoteLinkParserTests)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(m => m.GetCustomAttributes<InlineDataAttribute>().SelectMany(a => a.GetData(m)))
            .Select(row => row.FirstOrDefault())
            .OfType<string>()
            .ToList();
        inputs.AddRange([
            "See [[Project Alpha]], and [[ b.md ]].",
            "[[Beta]] [[a]] [[BETA]]\n[[ beta ]] [[A]]",
            "[[before]]\n```\n[[inside]]\n```\n`[[span]]` [[after]]",
            "\uFEFFx [[a]]\r\n[[b]] [[a]]\r\n",
        ]);
        Assert.True(inputs.Count > 40);

        foreach (string markdown in inputs)
        {
            IReadOnlyList<NoteLinkSpan> spans = NoteLinkParser.FindSpans(markdown);
            List<string> distinct = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (NoteLinkSpan span in spans)
            {
                Assert.Equal(span.Target, markdown.Substring(span.Offset, span.Target.Length));
                if (seen.Add(span.Target))
                {
                    distinct.Add(span.Target);
                }
            }

            Assert.Equal(NoteLinkParser.Parse(markdown), distinct);
        }
    }

    // Scope: spans carry the 1-based line and the absolute offset, repeats included.
    [Fact]
    public void FindSpans_LineAndOffset()
    {
        IReadOnlyList<NoteLinkSpan> spans = NoteLinkParser.FindSpans("a [[ x ]]\r\n```\n[[y]]\n```\n[[x]] [[z|w]]");

        Assert.Equal(
            [(1, 5, "x"), (5, 27, "x"), (5, 33, "z|w")],
            spans.Select(s => (s.Line, s.Offset, s.Target)).ToArray());
        Assert.Equal(nameof(NoteLinkSpan), spans[0].ToString());
    }
}
