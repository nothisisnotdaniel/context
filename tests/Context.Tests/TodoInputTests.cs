namespace Context.Tests;

public sealed class TodoInputTests
{
    private static readonly DateOnly Today = new(2026, 11, 7);

    [Theory]
    [InlineData("today", 2026, 11, 7)]
    [InlineData("TODAY", 2026, 11, 7)]
    [InlineData("Tomorrow", 2026, 11, 8)]
    [InlineData("2026-12-24", 2026, 12, 24)]
    [InlineData("2020-01-01", 2020, 1, 1)]
    public void TryParseDate_AcceptsDatesAndWords(string token, int year, int month, int day)
    {
        Assert.True(TodoInput.TryParseDate(token, Today, out DateOnly date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("2026-13-01")]
    [InlineData("24.12.2026")]
    [InlineData("2026-1-2")]
    public void TryParseDate_RefusesAnythingElse(string token)
    {
        Assert.False(TodoInput.TryParseDate(token, Today, out _));
    }

    [Fact]
    public void MaxTextLength_Is200()
    {
        Assert.Equal(200, TodoInput.MaxTextLength);
    }
}
