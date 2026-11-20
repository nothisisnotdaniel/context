namespace Context.Tests;

// 236 AC-4: the editor's attribute rows are read all or nothing.
public sealed class NoteAttributeTests
{
    [Fact]
    public void Parse_ValidRows_InOrder()
    {
        IReadOnlyList<NoteAttribute> rows =
            NoteAttribute.Parse("[{\"key\":\"aliases\",\"value\":\"A, B\"},{\"key\":\"Role\",\"value\":\"\"}]");

        Assert.Equal([("aliases", "A, B"), ("Role", string.Empty)], rows.Select(r => (r.Key, r.Value)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"key\":\"a\",\"value\":\"b\"}")]
    [InlineData("[{\"key\":\"a\",\"value\":\"b\"},1]")]
    [InlineData("[{\"key\":\"a\"}]")]
    [InlineData("[{\"value\":\"b\"}]")]
    [InlineData("[{\"key\":1,\"value\":\"b\"}]")]
    [InlineData("[{\"key\":\"a\",\"value\":null}]")]
    [InlineData("[{\"key\":\"a\",\"value\":\"b\"},{\"key\":\"c\",\"value\":2}]")]
    [InlineData("[{\"key\":\"a\",\"value\":\"b\"}")]
    public void Parse_Malformed_None(string? json)
    {
        Assert.Empty(NoteAttribute.Parse(json));
    }

    [Fact]
    public void Parse_AtMostMaxRows()
    {
        string Rows(int count) => "[" + string.Join(",", Enumerable.Repeat("{\"key\":\"a\",\"value\":\"b\"}", count)) + "]";

        Assert.Equal(NoteAttribute.MaxRows, NoteAttribute.Parse(Rows(NoteAttribute.MaxRows)).Count);
        Assert.Empty(NoteAttribute.Parse(Rows(NoteAttribute.MaxRows + 1)));
    }

    [Fact]
    public void ToString_NeverShowsTheText()
    {
        Assert.Equal("NoteAttribute", new NoteAttribute("secret", "value").ToString());
    }
}
