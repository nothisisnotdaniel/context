namespace Context.Tests;

// 157 AC-1 / AC-2: attribute lines (key:: value) and how values are matched.
public sealed class AttributeParserTests
{
    private static string Describe(IReadOnlyList<PageAttribute> attributes) =>
        string.Join(" | ", attributes.Select(a => $"{a.Key}={a.Value}"));

    // AC-1: what is and isn't an attribute line.
    [Theory]
    [InlineData("role:: Bereichsleiter", "role=Bereichsleiter")]
    [InlineData("Role::Bereichsleiter", "role=Bereichsleiter")]
    [InlineData("role::   Head   of\tSales  ", "role=Head of Sales")]
    [InlineData("affiliation:: [[ACME]]", "affiliation=[[ACME]]")]
    [InlineData("a-b_c1:: x", "a-b_c1=x")]
    [InlineData("Größe:: 1,80", "größe=1,80")]
    [InlineData("role::: x", "role=: x")]
    [InlineData("role: x", "")]
    [InlineData(" role:: x", "")]
    [InlineData("\trole:: x", "")]
    [InlineData("`role:: x`", "")]
    [InlineData("- role:: x", "")]
    [InlineData("1role:: x", "")]
    [InlineData("role :: x", "")]
    [InlineData("ro le:: x", "")]
    [InlineData("role::", "")]
    [InlineData("role::   ", "")]
    [InlineData("::x", "")]
    [InlineData("# Title", "")]
    public void Parse_Line(string line, string expected)
    {
        Assert.Equal(expected, Describe(AttributeParser.Parse(line)));
    }

    // AC-1: lines inside fenced code blocks are no attributes; CRLF line ends are fine.
    [Fact]
    public void Parse_SkipsFences_AndReadsCrlf()
    {
        string text = "# P\r\nrole:: a\r\n```\r\nx:: y\r\n```\r\n  ```js\r\nz:: w\r\n  ```\r\nkind:: b\r\n";

        Assert.Equal("role=a | kind=b", Describe(AttributeParser.Parse(text)));
    }

    // AC-1: the key is at most 50 chars, the value 1-400 chars after collapsing.
    [Fact]
    public void Parse_Lengths()
    {
        string key50 = "k" + new string('e', 49);
        Assert.Single(AttributeParser.Parse(key50 + ":: x"));
        Assert.Empty(AttributeParser.Parse(key50 + "y:: x"));

        string value400 = new string('v', 400);
        Assert.Equal(value400, Assert.Single(AttributeParser.Parse("k:: " + value400)).Value);
        Assert.Empty(AttributeParser.Parse("k:: " + value400 + "v"));
        Assert.Single(AttributeParser.Parse("k:: " + new string('v', 200) + "     " + new string('v', 199)));
    }

    // AC-1: a key may repeat; pairs are distinct by key + value key, the first spelling kept.
    [Fact]
    public void Parse_RepeatedKeys_DistinctByValueKey_FirstSpellingKept()
    {
        string text = "role:: Chef\nROLE:: chef\nrole:: Vater\naffiliation:: [[ACME]]\naffiliation:: acme\naffiliation:: Beta";

        Assert.Equal("role=Chef | role=Vater | affiliation=[[ACME]] | affiliation=Beta", Describe(AttributeParser.Parse(text)));
    }

    // AC-2: a single [[link]] matches by its trimmed target; anything else by the value; both lower-cased.
    [Theory]
    [InlineData("[[ACME]]", "acme")]
    [InlineData("[[ ACME  Corp ]]", "acme corp")]
    [InlineData("ACME", "acme")]
    [InlineData("acme", "acme")]
    [InlineData("[[A]] and [[B]]", "[[a]] and [[b]]")]
    [InlineData("[[A]]x", "[[a]]x")]
    [InlineData("[[]]", "[[]]")]
    [InlineData("[[ ]]", "[[ ]]")]
    [InlineData("[[a[b]]", "[[a[b]]")]
    [InlineData("  Head   of Sales ", "head of sales")]
    public void ValueKey_Cases(string value, string expected)
    {
        Assert.Equal(expected, AttributeParser.ValueKey(value));
    }

    // AC-1: a typed key is trimmed and lower-cased, or refused.
    [Theory]
    [InlineData("Role", "role")]
    [InlineData("  role  ", "role")]
    [InlineData("a-b_1", "a-b_1")]
    [InlineData("", null)]
    [InlineData("1a", null)]
    [InlineData("a b", null)]
    [InlineData("role::", null)]
    [InlineData("a/b", null)]
    public void NormalizeKey_Cases(string key, string? expected)
    {
        Assert.Equal(expected, AttributeParser.NormalizeKey(key));
    }

    [Fact]
    public void PageAttribute_ToString_IsTheTypeName()
    {
        Assert.Equal(nameof(PageAttribute), new PageAttribute("k", "secret").ToString());
    }
}
