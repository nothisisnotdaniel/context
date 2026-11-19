namespace Context.Tests;

// 282: Times' project quicklist for the command bar (AC-3, AC-6). Each test has its own Times folder; nothing is written
// by the code under test.
public sealed class TimesProjectsTests : IDisposable
{
    private readonly string folder;

    public TimesProjectsTests()
    {
        folder = Path.Combine(Path.GetTempPath(), "context-timesprojects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private string FilePath => Path.Combine(folder, TimesProjects.FileName);

    private void Write(string json) => File.WriteAllText(FilePath, json);

    [Fact]
    public void Read_Rows_InFileOrder()
    {
        Write("""{"projects":[{"workItem":4711,"title":"Billing"},{"workItem":1234,"title":"Review","extra":true}]}""");

        Assert.Equal([(4711, "Billing"), (1234, "Review")], TimesProjects.Read(folder));
    }

    [Fact]
    public void Read_Titles_AreCleanedToOneLine()
    {
        Write("""{"projects":[{"workItem":1,"title":"a\nb\u0007c"}]}""");

        (int workItem, string title) = Assert.Single(TimesProjects.Read(folder));
        Assert.Equal(1, workItem);
        Assert.Equal("a b" + SafeText.Placeholder + "c", title);
    }

    [Theory]
    [InlineData("""{"projects":[{"workItem":1,"title":"a"},{"workItem":0,"title":"b"}]}""")]
    [InlineData("""{"projects":[{"workItem":1,"title":"a"},{"workItem":-3,"title":"b"}]}""")]
    [InlineData("""{"projects":[{"workItem":1.5,"title":"a"}]}""")]
    [InlineData("""{"projects":[{"workItem":"1","title":"a"}]}""")]
    [InlineData("""{"projects":[{"workItem":1}]}""")]
    [InlineData("""{"projects":[{"workItem":1,"title":2}]}""")]
    [InlineData("""{"projects":[{"workItem":99999999999,"title":"a"}]}""")]
    [InlineData("""{"projects":[1]}""")]
    [InlineData("""{"projects":{}}""")]
    [InlineData("""{"other":[]}""")]
    [InlineData("""[]""")]
    [InlineData("""not json""")]
    [InlineData("")]
    public void Read_InvalidFile_IsEmpty(string json)
    {
        Write(json);

        Assert.Empty(TimesProjects.Read(folder));
    }

    [Fact]
    public void Read_MissingFolderOrFile_IsEmpty()
    {
        Assert.Empty(TimesProjects.Read(null));
        Assert.Empty(TimesProjects.Read(string.Empty));
        Assert.Empty(TimesProjects.Read(folder));
        Assert.Empty(TimesProjects.Read(Path.Combine(folder, "missing")));
    }

    [Fact]
    public void Read_OverTheCap_IsEmpty()
    {
        string row = """{"workItem":1,"title":"x"},""";
        string body = string.Concat(Enumerable.Repeat(row, (int)(TimesProjects.MaxBytes / row.Length) + 1));
        Write("{\"projects\":[" + body + "{\"workItem\":2,\"title\":\"y\"}]}");

        Assert.True(new FileInfo(FilePath).Length > TimesProjects.MaxBytes);
        Assert.Empty(TimesProjects.Read(folder));
    }

    [Fact]
    public void Read_WritesNothing()
    {
        Write("""{"projects":[{"workItem":1,"title":"a"}]}""");
        DateTime before = File.GetLastWriteTimeUtc(FilePath);

        TimesProjects.Read(folder);

        Assert.Equal([TimesProjects.FileName], Directory.GetFiles(folder).Select(Path.GetFileName));
        Assert.Equal(before, File.GetLastWriteTimeUtc(FilePath));
    }
}
