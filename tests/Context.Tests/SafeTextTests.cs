namespace Context.Tests;

public class SafeTextTests
{
    // AC-1
    [Theory]
    [InlineData("a\u001b[2Jb", "a�[2Jb")]
    [InlineData("\u001b]0;pwned\u0007", "�]0;pwned�")]
    [InlineData("x\by", "x�y")]
    [InlineData("\u0000\u007F\u0080\u009B\u009D\u009F", "������")]
    public void Clean_ReplacesControlCharacters(string input, string expected)
    {
        Assert.Equal(expected, SafeText.Clean(input));
    }

    // AC-1, AC-2
    [Fact]
    public void Clean_MapsEveryCodePointUpTo009F()
    {
        for (int code = 0; code <= 0x9F; code++)
        {
            string input = "a" + (char)code + "b";
            string expected = code switch
            {
                '\t' => "a    b",
                '\n' => "a\nb",
                <= 0x1F or >= 0x7F => "a�b",
                _ => input,
            };

            Assert.True(expected == SafeText.Clean(input), $"U+{code:X4}");
        }
    }

    // AC-2
    [Theory]
    [InlineData("a\nb", "a\nb")]
    [InlineData("a\r\nb\r\n", "a\nb\n")]
    [InlineData("a\rb", "a�b")]
    [InlineData("a\r\r\nb", "a�\nb")]
    [InlineData("\r", "�")]
    [InlineData("\ta\tb", "    a    b")]
    public void Clean_HandlesLineEndingsAndTabs(string input, string expected)
    {
        Assert.Equal(expected, SafeText.Clean(input));
    }

    // AC-3
    [Theory]
    [InlineData("Grüße — [red]x[/] 日本")]
    [InlineData("")]
    [InlineData("plain text\nsecond line")]
    [InlineData(" ÿ�")]
    public void Clean_LeavesTextWithoutControlsUnchanged(string input)
    {
        Assert.Equal(input, SafeText.Clean(input));
    }

    [Fact]
    public void Placeholder_IsReplacementCharacter()
    {
        Assert.Equal('�', SafeText.Placeholder);
    }
}
