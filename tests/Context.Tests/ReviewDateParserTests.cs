namespace Context.Tests;

// 256 AC-1: ReviewDateParser finds the first "review:yyyy-MM-dd" line outside fences.
public sealed class ReviewDateParserTests
{
    [Theory]
    [InlineData("review:2026-12-01")]
    [InlineData("  - Review: 2026-12-01  ")]
    [InlineData("* REVIEW:\t2026-12-01")]
    [InlineData("\t+ review:  2026-12-01\t")]
    public void Parse_ReviewLine_GivesLineDateAndRaw(string line)
    {
        ParsedReview? review = ReviewDateParser.Parse("# Plan\n\n" + line + "\nmore");

        Assert.NotNull(review);
        Assert.Equal(3, review.Line);
        Assert.Equal(new DateOnly(2026, 12, 1), review.Due);
        Assert.Equal(line, review.Raw);
    }

    [Theory]
    [InlineData("review:2026-02-30")]
    [InlineData("review:2026-12-1")]
    [InlineData("review:2026-12-01 later")]
    [InlineData("x review:2026-12-01")]
    [InlineData("review:２０２６-12-01")]
    [InlineData("review:2026-1٢-01")]
    [InlineData("review 2026-12-01")]
    [InlineData("revieſ:2026-12-01")]
    [InlineData("-review:2026-12-01")]
    [InlineData("-  review:2026-12-01")]
    [InlineData("review:")]
    [InlineData("review:2026/12/01")]
    public void Parse_NoReviewLine_GivesNull(string line)
    {
        Assert.Null(ReviewDateParser.Parse("# Plan\n" + line + "\n"));
    }

    [Fact]
    public void Parse_InsideFence_IsSkipped()
    {
        Assert.Null(ReviewDateParser.Parse("```\nreview:2026-12-01\n```\n"));

        ParsedReview? after = ReviewDateParser.Parse("```\nreview:2026-12-01\n```\nreview:2027-01-02");
        Assert.NotNull(after);
        Assert.Equal(4, after.Line);
        Assert.Equal(new DateOnly(2027, 1, 2), after.Due);
    }

    [Fact]
    public void Parse_TwoReviewLines_OnlyFirstCounts()
    {
        ParsedReview? review = ReviewDateParser.Parse("review:2026-12-01\r\nreview:2025-01-01\r\n");

        Assert.NotNull(review);
        Assert.Equal(1, review.Line);
        Assert.Equal(new DateOnly(2026, 12, 1), review.Due);
        Assert.Equal("review:2026-12-01", review.Raw);
    }

    [Theory]
    [InlineData("review:2026-12-01", "review:2027-03-05")]
    [InlineData("  - Review: 2026-12-01  ", "  - Review: 2027-03-05  ")]
    [InlineData("* REVIEW:\t2026-12-01\t", "* REVIEW:\t2027-03-05\t")]
    public void SetDate_ReplacesOnlyTheDate(string raw, string expected)
    {
        Assert.Equal(expected, ReviewDateParser.SetDate(raw, new DateOnly(2027, 3, 5)));
    }

    [Fact]
    public void SetDate_NoReviewLine_GivesNull()
    {
        Assert.Null(ReviewDateParser.SetDate("review:2026-02-30", new DateOnly(2027, 3, 5)));
    }

    [Fact]
    public void ToString_NeverPrintsTheLine()
    {
        Assert.Equal(nameof(ParsedReview), ReviewDateParser.Parse("review:2026-12-01")!.ToString());
    }
}
