namespace Context.Tests;

public sealed class DesktopTextTests
{
    // AC-5: SafeText.Clean, then every line break becomes a space.
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a\nb", "a b")]
    [InlineData("a\r\nb", "a b")]
    [InlineData("a\tb", "a    b")]
    [InlineData("bell\u0007 esc\u001b del\u007f c1\u0085", "bell� esc� del� c1�")]
    [InlineData("<script>alert(1)</script>", "<script>alert(1)</script>")]
    public void OneLine_CleansAndJoinsLines(string raw, string expected)
    {
        Assert.Equal(expected, DesktopText.OneLine(raw));
    }
}
