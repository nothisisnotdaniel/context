namespace Context.Tests;

/// <summary>
/// Job 173: the command-line commands that only mirrored desktop views (links, people, person, types) are gone, and
/// README.md and CLAUDE.md no longer describe them. The strings are built in pieces so this file has no exception.
/// </summary>
public sealed class RemovedCommandsDocsTests
{
    private static readonly string[] Removed = ["links", "people", "person", "types"];

    private static string RepoFile(string name)
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Context.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, name);
    }

    // AC-7: neither file names a removed command as `context <name>`.
    [Theory]
    [InlineData("README.md")]
    [InlineData("CLAUDE.md")]
    public void Docs_NameNoRemovedCommand(string file)
    {
        string text = File.ReadAllText(RepoFile(file));

        foreach (string command in Removed)
        {
            Assert.DoesNotContain("context" + " " + command, text, StringComparison.Ordinal);
        }
    }

    // AC-7: the README JSON table has no row for a removed command.
    [Fact]
    public void Readme_JsonTable_HasNoRemovedCommandRow()
    {
        string[] lines = File.ReadAllText(RepoFile("README.md")).ReplaceLineEndings("\n").Split('\n');
        string[] rows = ["links <note>", "people", "person <name>", "types"];

        Assert.Contains(lines, l => l.StartsWith("| `version` |", StringComparison.Ordinal));
        foreach (string row in rows)
        {
            Assert.DoesNotContain(lines, l => l.StartsWith("| `" + row + "` |", StringComparison.Ordinal));
        }
    }
}
