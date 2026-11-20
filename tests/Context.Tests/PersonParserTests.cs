namespace Context.Tests;

public sealed class PersonParserTests
{
    // 156 AC-1: at the start of a line or after whitespace; trimmed, whitespace runs collapsed, in order.
    [Fact]
    public void Parse_FindsMentions_TrimmedAndCollapsed_InOrder()
    {
        Assert.Equal(
            ["Anna Schmidt", "Bob", "Carl"],
            PersonParser.Parse("@[ Anna \t Schmidt ] met\n@[Bob], then @[Carl]."));
    }

    // 156 AC-1
    [Theory]
    [InlineData("@[Anna]", "Anna")]
    [InlineData("x @[Anna] y", "Anna")]
    [InlineData("\t@[Anna]", "Anna")]
    [InlineData("- @[Anna]", "Anna")]
    [InlineData("@[Anna]]", "Anna")]
    [InlineData("@[Anna] (later)", "Anna")]
    [InlineData("@[Anna]x", "Anna")]
    [InlineData("@[Anna #x]", "Anna #x")]
    [InlineData("@[Dr. Anna]", "Dr. Anna")]
    [InlineData("@[Anna-Lena O'Brien]", "Anna-Lena O'Brien")]
    [InlineData("line\r\n@[Anna]\r\n", "Anna")]
    [InlineData("@[a @[Anna]", "Anna")]
    [InlineData("@[Anna] `@[Bob]`", "Anna")]
    public void Parse_FindsMention(string markdown, string name)
    {
        Assert.Equal([name], PersonParser.Parse(markdown));
    }

    // 156 AC-1: what gives nothing.
    [Theory]
    [InlineData("a@[X]")]
    [InlineData("`@[X]`")]
    [InlineData("``a `@[X]` b``")]
    [InlineData("@[X](u)")]
    [InlineData("@[a/b]")]
    [InlineData("@[a\\b]")]
    [InlineData("@[a:b]")]
    [InlineData("@[..]")]
    [InlineData("@[.hidden]")]
    [InlineData("@[Anna.]")]
    [InlineData("@[]")]
    [InlineData("@[   ]")]
    [InlineData("@[CON]")]
    [InlineData("@[nul.txt]")]
    [InlineData("@[a?b]")]
    [InlineData("@[a<b]")]
    [InlineData("@[a|b]")]
    [InlineData("@[a\"b]")]
    [InlineData("@[a*b]")]
    [InlineData("@[a\u0001b]")]
    [InlineData("@[[Anna]]")]
    [InlineData("@[Anna\n]")]
    [InlineData("@ [Anna]")]
    [InlineData("#[Anna]")]
    [InlineData("[[Anna]]")]
    [InlineData("@Anna")]
    public void Parse_NoMention(string markdown)
    {
        Assert.Empty(PersonParser.Parse(markdown));
    }

    // 156 AC-1: longer than a title can be gives nothing; exactly the limit is a mention.
    [Fact]
    public void Parse_LongName_OnlyUpToTheTitleLimit()
    {
        string max = new string('a', NoteStore.MaxTitleLength);

        Assert.Equal([max], PersonParser.Parse("@[" + max + "]"));
        Assert.Empty(PersonParser.Parse("@[" + max + "a]"));
    }

    // 156 AC-1: fenced code is skipped.
    [Fact]
    public void Parse_SkipsFencedCode()
    {
        Assert.Equal(["Bob"], PersonParser.Parse("```\n@[Anna]\n```\n  ```js\n@[Carl]\n```\n@[Bob]\n"));
    }

    // 156 AC-1: distinct ignoring case, the first spelling kept.
    [Fact]
    public void Parse_DistinctIgnoringCase_FirstSpellingKept()
    {
        Assert.Equal(["anna SCHMIDT", "Bob"], PersonParser.Parse("@[anna SCHMIDT] @[Anna  Schmidt] @[Bob] @[BOB]"));
    }

    // 156 AC-1: the key is the lower-cased name.
    [Fact]
    public void Key_IsLowerCase()
    {
        Assert.Equal("anna schmidt", PersonParser.Key("Anna SCHMIDT"));
    }

    // 156 AC-7: a typed person may be bare or wrapped as a mention.
    [Theory]
    [InlineData("Anna Schmidt", "Anna Schmidt")]
    [InlineData("  @[ Anna   Schmidt ] ", "Anna Schmidt")]
    [InlineData("a/b", null)]
    [InlineData("@[]", null)]
    [InlineData("", null)]
    public void NormalizeTyped_BareOrWrapped(string typed, string? name)
    {
        Assert.Equal(name, PersonParser.NormalizeTyped(typed));
    }

    // 159 AC-4: each expected segment is "plain text" or "raw text=>Name" for a mention.
    public static TheoryData<string, string[]> SegmentCases() => new()
    {
        { "", [] },
        { "Buy milk", ["Buy milk"] },
        { "@[Anna]", ["@[Anna]=>Anna"] },
        { "Call @[Anna] about @[ Bob  Berg ].", ["Call ", "@[Anna]=>Anna", " about ", "@[ Bob  Berg ]=>Bob Berg", "."] },
        { "@[Anna] and @[anna] again", ["@[Anna]=>Anna", " and ", "@[anna]=>anna", " again"] },
        { "@[Anna]@[Bob]", ["@[Anna]=>Anna", "@[Bob]"] },
        { "`@[Anna]` @[Bob]", ["`@[Anna]` ", "@[Bob]=>Bob"] },
        { "#[Anna] [[Anna]] @[[Anna]]", ["#[Anna] [[Anna]] @[[Anna]]"] },
        { "x @[a/b] y", ["x @[a/b] y"] },
        { "@[Anna](u) x", ["@[Anna](u) x"] },
        { "a@[Anna]", ["a@[Anna]"] },
        { "@[a @[Anna]", ["@[a ", "@[Anna]=>Anna"] },
        { "@[Anna]] (later)", ["@[Anna]=>Anna", "] (later)"] },
        { "@[Anna", ["@[Anna"] },
    };

    // 159 AC-4: plain and mention segments by the rules of Parse; joined, they give the input exactly.
    [Theory]
    [MemberData(nameof(SegmentCases))]
    public void Segments_SplitPlainAndMentions_RoundTrip(string line, string[] expected)
    {
        IReadOnlyList<PersonSegment> segments = PersonParser.Segments(line);

        Assert.Equal(expected, segments.Select(s => s.Name is null ? s.Text : s.Text + "=>" + s.Name));
        Assert.Equal(line, string.Concat(segments.Select(s => s.Text)));
        Assert.All(segments, s => Assert.Equal(s.Name is not null, s.IsMention));
        Assert.All(segments, s => Assert.NotEqual(string.Empty, s.Text));

        // Every mention segment is one Parse finds in the line, and Parse finds no other.
        Assert.Equal(
            PersonParser.Parse(line).Select(PersonParser.Key),
            segments.Where(s => s.IsMention).Select(s => PersonParser.Key(s.Name!)).Distinct());
    }

    // 156 AC-2: a mention gives no tag and no link; tags and links give no mention.
    [Fact]
    public void Parse_TagsLinksAndMentions_NeverMatchEachOther()
    {
        const string Text = "@[Anna] #[Bob] [[Carl]] @[[Dora]] #[[Eve]]";

        Assert.Equal(["Anna"], PersonParser.Parse(Text));
        Assert.Equal(["bob"], TagParser.Parse(Text));
        Assert.Equal(["Carl"], NoteLinkParser.Parse(Text));
        Assert.Empty(TagParser.Parse("@[Anna]"));
        Assert.Empty(NoteLinkParser.Parse("@[Anna]"));
    }

    // 156 Security: a line full of "@[" with no ']' is scanned in linear time.
    [Theory]
    [InlineData("@[")]
    [InlineData("@[a ")]
    [InlineData(" @[a [")]
    public void Parse_LongHostileLine_IsFast(string unit)
    {
        string line = string.Concat(Enumerable.Repeat(unit, 200_000)) + "]";
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

        PersonParser.Parse(line);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    // 156: person pages are the direct subpages of the top-level people note, in any case.
    [Theory]
    [InlineData("people/Anna", "Anna")]
    [InlineData("PEOPLE/Anna Schmidt", "Anna Schmidt")]
    [InlineData("people", null)]
    [InlineData("people/Anna/notes", null)]
    [InlineData("work/people/Anna", null)]
    [InlineData("peoples/Anna", null)]
    public void PersonOf_DirectSubpagesOfPeople(string note, string? person)
    {
        Assert.Equal(person, People.PersonOf(note));
        Assert.Equal("people/Anna", People.PersonNote("Anna"));
    }

    // 163 AC-1: nested and mixed run lengths.
    [Theory]
    [InlineData("`` a ` @[X] `` @[Real]", "Real")]
    [InlineData("` `` ` @[Real] ``", "Real")]
    [InlineData("an unclosed ` @[Real]", "Real")]
    public void Parse_MixedRunLengths_SkipsSpans(string markdown, string name)
    {
        Assert.Equal([name], PersonParser.Parse(markdown));
    }

    // 163 AC-2: many unmatched backtick runs of different lengths are parsed in linear time.
    [Fact]
    public void Parse_ManyUnmatchedRuns_IsFast()
    {
        string line = string.Concat(Enumerable.Range(1, 3000).Select(k => new string('`', k) + "a #t [[x]] @[P] "));
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

        IReadOnlyList<string> names = PersonParser.Parse(line);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
        Assert.Equal(["P"], names);
    }
}
