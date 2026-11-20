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

    // 235 AC-3: a note's name is cleaned like OneLine, and every bidi control becomes one placeholder.
    [Theory]
    [InlineData("a/b/c", "a/b/c")]
    [InlineData("c\u202Aa\u202Bb\u202Cc\u202Dd\u202Ee", "c\uFFFDa\uFFFDb\uFFFDc\uFFFDd\uFFFDe")]
    [InlineData("\u2066x\u2067y\u2068z\u2069", "\uFFFDx\uFFFDy\uFFFDz\uFFFD")]
    [InlineData("bell\u0007\nnext\u202E", "bell\uFFFD next\uFFFD")]
    [InlineData("keep \u200F\u2065\u2070", "keep \u200F\u2065\u2070")]
    public void NoteName_CleansLikeOneLine_AndReplacesBidiControls(string raw, string expected)
    {
        Assert.Equal(expected, DesktopText.NoteName(raw));
    }
}
