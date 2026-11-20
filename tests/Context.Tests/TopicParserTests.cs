namespace Context.Tests;

// 253 AC-1: TopicParser finds open "next: @[Name] text" lines outside fences.
public sealed class TopicParserTests
{
    [Theory]
    [InlineData("next: @[Ann Lee] budget")]
    [InlineData("  - next:@[Ann Lee] budget")]
    [InlineData("- [ ] NEXT: @[Ann Lee] budget")]
    [InlineData("* next: @[Ann Lee]   budget  ")]
    [InlineData("\t+ [ ] Next:\t@[ Ann  Lee ] budget")]
    public void Parse_TopicLine_GivesPersonAndText(string line)
    {
        ParsedTopic topic = Assert.Single(TopicParser.Parse("# Plan\n\n" + line + "\nmore"));

        Assert.Equal(3, topic.Line);
        Assert.Equal("Ann Lee", topic.Person);
        Assert.Equal("budget", topic.Text);
        Assert.Equal(line, topic.Raw);
    }

    [Theory]
    [InlineData("- [x] next: @[Ann Lee] budget")]
    [InlineData("- [X] next: @[Ann Lee] budget")]
    [InlineData("next: @[Ann Lee]")]
    [InlineData("next: @[Ann Lee]    ")]
    [InlineData("next: budget @[Ann Lee]")]
    [InlineData("see next: @[Ann Lee] x")]
    [InlineData("next: @[a/b] x")]
    [InlineData("next: @[] x")]
    [InlineData("next: @[Ann Lee](https://x) x")]
    [InlineData("next: @[Ann Lee x")]
    [InlineData("next @[Ann Lee] x")]
    [InlineData("-  next: @[Ann Lee] x")]
    [InlineData("1. next: @[Ann Lee] x")]
    [InlineData("- [ ]next: @[Ann Lee] x")]
    [InlineData("ｎext: @[Ann Lee] x")]
    public void Parse_NotATopic_GivesNothing(string line)
    {
        Assert.Empty(TopicParser.Parse(line));
    }

    [Fact]
    public void Parse_InsideFence_GivesNothing()
    {
        IReadOnlyList<ParsedTopic> topics = TopicParser.Parse(
            "```\nnext: @[Ann Lee] fenced\n```\nnext: @[Bob] outside\r\n");

        ParsedTopic topic = Assert.Single(topics);
        Assert.Equal(4, topic.Line);
        Assert.Equal("Bob", topic.Person);
        Assert.Equal("outside", topic.Text);
        Assert.Equal("next: @[Bob] outside", topic.Raw);
    }

    [Fact]
    public void Parse_LongText_CutTo1000_NeverInsideSurrogatePair()
    {
        string plain = new('a', 1500);
        Assert.Equal(1000, Assert.Single(TopicParser.Parse("next: @[Ann] " + plain)).Text.Length);

        string pair = new string('a', 999) + "\U0001F600" + "b";
        string text = Assert.Single(TopicParser.Parse("next: @[Ann] " + pair)).Text;
        Assert.Equal(new string('a', 999), text);
    }

    [Theory]
    [InlineData("- [ ] next: @[Ann] x", "- [x] next: @[Ann] x")]
    [InlineData("  * [ ] NEXT: @[Ann] x", "  * [x] NEXT: @[Ann] x")]
    [InlineData("- next: @[Ann] x", "- [x] next: @[Ann] x")]
    [InlineData("\t+ next:@[Ann] x", "\t+ [x] next:@[Ann] x")]
    [InlineData("next: @[Ann] x", "- [x] next: @[Ann] x")]
    [InlineData("   next: @[Ann] x", "   - [x] next: @[Ann] x")]
    public void DoneForm_EachOpenForm(string line, string done)
    {
        Assert.Equal(done, TopicParser.DoneForm(line));
        Assert.Empty(TopicParser.Parse(done));
    }

    [Fact]
    public void ToString_PrintsNoContent()
    {
        ParsedTopic topic = Assert.Single(TopicParser.Parse("next: @[Ann Lee] secret"));
        Assert.Equal("ParsedTopic", topic.ToString());
        Assert.Equal("NoteTopic", new NoteTopic("n", 1, "Ann Lee", "secret", "raw").ToString());
    }

    // 329 AC-1: a topic keeps the "[ ]" box (DoneForm still writes "[x]"); a "{ }" line is a todo, never a topic.
    [Fact]
    public void ParseLine_BracketIsTopic_BraceIsNot()
    {
        Assert.NotNull(TopicParser.ParseLine(1, "- [ ] next: @[X] t"));
        Assert.Equal("- [x] next: @[X] t", TopicParser.DoneForm("- [ ] next: @[X] t"));
        Assert.Null(TopicParser.ParseLine(1, "- { } next: @[X] t"));
    }
}
