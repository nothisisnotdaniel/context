namespace Context.Tests;

public sealed class NoteLinkParserTests
{
    // AC-1
    [Fact]
    public void Parse_SpecExample_TrimsAndKeepsOrder()
    {
        Assert.Equal(["Project Alpha", "b.md"], NoteLinkParser.Parse("See [[Project Alpha]], and [[ b.md ]]."));
    }

    // AC-1
    [Theory]
    [InlineData("[[a]]", "a")]
    [InlineData("x[[a]]", "a")]
    [InlineData("x[[a]]y", "a")]
    [InlineData("[[a  b]]", "a  b")]
    [InlineData("[[\ta b ]]", "a b")]
    [InlineData("[[a]]]", "a")]
    [InlineData("[[[a]]", "a")]
    [InlineData("[[Note|label]]", "Note|label")]
    [InlineData("[[Note#Heading]]", "Note#Heading")]
    [InlineData("![[embed]]", "embed")]
    [InlineData("line\r\n[[a]]\r\n", "a")]
    [InlineData("[[a]] `code`", "a")]
    public void Parse_FindsLink(string markdown, string target)
    {
        Assert.Equal([target], NoteLinkParser.Parse(markdown));
    }

    // AC-1: distinct ignoring case, first spelling wins.
    [Fact]
    public void Parse_DistinctIgnoringCase_FirstSpellingKept()
    {
        Assert.Equal(["Beta", "a"], NoteLinkParser.Parse("[[Beta]] [[a]] [[BETA]]\n[[ beta ]] [[A]]"));
    }

    // AC-1: 103 characters is the longest target.
    [Fact]
    public void Parse_LengthLimit()
    {
        string longest = new string('x', NoteLinkParser.MaxTargetLength);
        string tooLong = new string('y', NoteLinkParser.MaxTargetLength + 1);

        Assert.Equal(403, NoteLinkParser.MaxTargetLength);
        Assert.Equal([longest], NoteLinkParser.Parse($"[[{longest}]]"));
        Assert.Equal([longest], NoteLinkParser.Parse($"[[  {longest}  ]]"));
        Assert.Empty(NoteLinkParser.Parse($"[[{tooLong}]]"));
    }

    // AC-2
    [Theory]
    [InlineData("[[]]")]
    [InlineData("[[   ]]")]
    [InlineData("[[a]")]
    [InlineData("[[a] ]")]
    [InlineData("[[a\n]]")]
    [InlineData("[[a [b]]")]
    [InlineData("[a]]")]
    [InlineData("[ [a]]")]
    [InlineData("#[a b]")]
    [InlineData("#[[a]]")]
    [InlineData("`[[a]]`")]
    [InlineData("x ``[[a]]`` y")]
    [InlineData("```\n[[a]]\n```")]
    [InlineData("  ```md\n[[a]]")]
    [InlineData("")]
    public void Parse_NoLink(string markdown)
    {
        Assert.Empty(NoteLinkParser.Parse(markdown));
    }

    // AC-2: fences and code spans hide links, text around them still counts.
    [Fact]
    public void Parse_SkipsCodeButNotTheRest()
    {
        string markdown = "[[before]]\n```\n[[inside]]\n```\n`[[span]]` [[after]]";

        Assert.Equal(["before", "after"], NoteLinkParser.Parse(markdown));
    }

    // AC-2: an unmatched backtick is plain text, so the link after it is found.
    [Fact]
    public void Parse_UnmatchedBacktick_IsPlainText()
    {
        Assert.Equal(["a"], NoteLinkParser.Parse("` [[a]]"));
    }

    // AC-2: links and tags never mix.
    [Fact]
    public void Parse_TagsAndLinksStaySeparate()
    {
        const string Markdown = "#[a b] [[c d]] #work";

        Assert.Equal(["c d"], NoteLinkParser.Parse(Markdown));
        Assert.Equal(["a b", "work"], TagParser.Parse(Markdown));
    }

    // Security: a line full of "[[" (no ']' at all, or with many '[') is scanned in linear time.
    [Theory]
    [InlineData("[[")]
    [InlineData("[[a")]
    [InlineData("[[a]")]
    [InlineData("[[a `")]
    public void Parse_LongHostileLine_IsFast(string unit)
    {
        string line = string.Concat(Enumerable.Repeat(unit, 200_000)) + "]]";
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

        NoteLinkParser.Parse(line);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    // 156 AC-2: a mention is no link, "@[[x]]" is neither, and a link is no mention.
    [Fact]
    public void Parse_Mention_IsNoLink()
    {
        Assert.Empty(NoteLinkParser.Parse("@[Anna] and @[[Anna]]"));
        Assert.Empty(PersonParser.Parse("[[Anna]] and @[[Anna]]"));
    }

    // 163 AC-3: a backtick run that opens a matched code span inside the target means no link; the span is skipped.
    [Theory]
    [InlineData("[[a `b]] c` d")]
    [InlineData("[[x `]]` y]]")]
    [InlineData("[[a ``b]] c`` d")]
    [InlineData("[[a ` x ``b]] c`` d")]
    public void Parse_TargetRunsIntoCodeSpan_IsNoLink(string markdown)
    {
        Assert.Empty(NoteLinkParser.Parse(markdown));
    }

    // 163 AC-3: scanning goes on after the skipped span.
    [Theory]
    [InlineData("[[a `b]] c` [[real]]", "real")]
    [InlineData("[[x `]]` [[real]]", "real")]
    [InlineData("[[a `[[b]]` c]] [[real]]", "real")]
    public void Parse_AfterSpanInTarget_FindsLaterLink(string markdown, string target)
    {
        Assert.Equal([target], NoteLinkParser.Parse(markdown));
    }

    // 163 AC-4: a backtick with no match inside a target is plain text, and a span after the link changes nothing.
    [Theory]
    [InlineData("[[a`b]]", "a`b")]
    [InlineData("[[a]] `code`", "a")]
    [InlineData("[[a ``b]] c` d", "a ``b")]
    [InlineData("`x` [[a]] `y`", "a")]
    public void Parse_UnmatchedBacktickInTarget_IsPlainText(string markdown, string target)
    {
        Assert.Equal([target], NoteLinkParser.Parse(markdown));
    }

    // 163 AC-1: nested and mixed run lengths.
    [Theory]
    [InlineData("`` a ` [[x]] `` [[real]]", "real")]
    [InlineData("` `` ` [[real]] ``", "real")]
    public void Parse_MixedRunLengths_SkipsSpans(string markdown, string target)
    {
        Assert.Equal([target], NoteLinkParser.Parse(markdown));
    }

    // 163 AC-2: many unmatched backtick runs of different lengths are parsed in linear time.
    [Fact]
    public void Parse_ManyUnmatchedRuns_IsFast()
    {
        string line = string.Concat(Enumerable.Range(1, 3000).Select(k => new string('`', k) + "a #t [[x]] @[P] "));
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

        IReadOnlyList<string> targets = NoteLinkParser.Parse(line);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
        Assert.Equal(["x"], targets);
    }

    // 205 AC-1: split at the first #, both parts trimmed; no heading without text after the #.
    [Theory]
    [InlineData("Plan#Next steps ", "Plan", "Next steps")]
    [InlineData(" Plan # Next steps#2", "Plan", "Next steps#2")]
    [InlineData("Plan", "Plan", null)]
    [InlineData("Plan#", "Plan", null)]
    [InlineData("Plan#   ", "Plan", null)]
    [InlineData("#Intro", "", "Intro")]
    public void Split_NoteAndHeading(string target, string note, string? heading)
    {
        NoteLinkTarget split = NoteLinkParser.Split(target);

        Assert.Equal(note, split.Note);
        Assert.Equal(heading, split.Heading);
    }

    // 205 AC-1: Parse keeps the target as written.
    [Fact]
    public void Parse_HeadingLink_KeepsTarget()
    {
        Assert.Equal(["Plan#Goals"], NoteLinkParser.Parse("see [[Plan#Goals]]"));
    }

    // 205: the record never prints raw note text.
    [Fact]
    public void Split_ToString_HidesRawText()
    {
        Assert.Equal(nameof(NoteLinkTarget), NoteLinkParser.Split("Secret#Goal").ToString());
    }
}
