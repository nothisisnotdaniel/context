namespace Context.Tests;

// 243 AC-1: reading a waiting-for todo's text.
public sealed class WaitingForTests
{
    [Theory]
    [InlineData("waiting: @[Anna Schmidt] send the slides", "Anna Schmidt", "send the slides")]
    [InlineData("  WAITING:@[Anna]x", "Anna", "x")]
    [InlineData("Waiting:\t \t@[ anna   schmidt ]  rest  ", "anna schmidt", "rest")]
    [InlineData("\twaiting: @[Bob]", "Bob", "")]
    [InlineData("waiting: @[Bob] a\r\nb\t", "Bob", "a\r\nb")]
    [InlineData("waiting: @[Bob] mentions @[Cleo] too", "Bob", "mentions @[Cleo] too")]
    public void TryParse_Matches(string text, string person, string rest)
    {
        WaitingItem? item = WaitingFor.TryParse(text);

        Assert.NotNull(item);
        Assert.Equal((person, rest), (item.Person, item.Rest));
    }

    [Theory]
    [InlineData("waiting @[A] x")]
    [InlineData("waiting: A x")]
    [InlineData("waiting: @[a/b] x")]
    [InlineData("x waiting: @[A]")]
    [InlineData("waiting: `@[A]`")]
    [InlineData("waiting: @[A](link) x")]
    [InlineData("waiting: @[A x")]
    [InlineData("waiting: @[A\r\n] x")]
    [InlineData("waiting: @[] x")]
    [InlineData("waiting: @[a|b] x")]
    [InlineData("waiting:\n@[A] x")]
    [InlineData("waitıng: @[A] x")]
    [InlineData("waiting：@[A] x")]
    [InlineData("waiting:")]
    [InlineData("waiting: @")]
    [InlineData("waiting: @[")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_NoMatch(string? text) => Assert.Null(WaitingFor.TryParse(text));

    // A long or odd text never throws.
    [Fact]
    public void TryParse_LongText_NeverThrows()
    {
        Assert.Null(WaitingFor.TryParse("waiting: @[" + new string('a', 100_000)));
        Assert.Null(WaitingFor.TryParse("waiting: @[" + new string('a', 1000) + "] x"));
        Assert.Equal("x", WaitingFor.TryParse("waiting: @[😀] x")?.Rest);
    }

    [Fact]
    public void Compose_ReadsBack()
    {
        string text = WaitingFor.Compose("Anna Schmidt", "send the slides");

        Assert.Equal("waiting: @[Anna Schmidt] send the slides", text);
        Assert.Equal(new WaitingItem("Anna Schmidt", "send the slides"), WaitingFor.TryParse(text));
    }
}
