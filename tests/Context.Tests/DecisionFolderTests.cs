using System.Text;

namespace Context.Tests;

// 349 AC-2, AC-4: decision-folder.json (prefix, GUID, migrated) and the decision-folder line in notes.
public sealed class DecisionFolderTests : IDisposable
{
    private static readonly Guid Own = new("11111111-2222-3333-4444-555555555555");

    private static readonly Guid Other = new("99999999-8888-7777-6666-555555555555");

    private readonly string root = Path.Combine(Path.GetTempPath(), "context-decision-folder-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string FilePath => Path.Combine(root, DecisionFolder.FileName);

    // Draws a, b, 1, 2 from the alphabet, then fails the test when drawn again.
    private static Func<int, int> Draws(params int[] values)
    {
        Queue<int> queue = new(values);
        return bound =>
        {
            Assert.Equal(32, bound);
            return queue.Dequeue();
        };
    }

    private void WriteFile(string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, text);
    }

    // AC-2: a missing file is created from the fake random and GUID, with exactly the three properties, owner-only.
    [Fact]
    public void Load_Missing_CreatesFile()
    {
        DecisionFolder? folder = DecisionFolder.Load(root, Draws(10, 11, 1, 2), () => Own);

        Assert.Equal(new DecisionFolder("ab12", Own, false), folder);
        Assert.Equal(
            "{\"prefix\":\"ab12\",\"guid\":\"11111111-2222-3333-4444-555555555555\",\"migrated\":false}",
            File.ReadAllText(FilePath));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FilePath));
        }
    }

    // AC-2: every prefix char comes from the alphabet without i, l, o, u.
    [Fact]
    public void NewPrefix_UsesAlphabet()
    {
        Assert.Equal("0zyx", DecisionFolder.NewPrefix(Draws(0, 31, 30, 29)));
        Assert.DoesNotContain('i', DecisionFolder.Alphabet);
        Assert.DoesNotContain('l', DecisionFolder.Alphabet);
        Assert.DoesNotContain('o', DecisionFolder.Alphabet);
        Assert.DoesNotContain('u', DecisionFolder.Alphabet);
        Assert.Equal(32, DecisionFolder.Alphabet.Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => DecisionFolder.NewPrefix(_ => 32));
    }

    // AC-2: an existing file is read and never rewritten; random and GUID are not drawn.
    [Fact]
    public void Load_Existing_ReadsWithoutWriting()
    {
        string text = "{\"migrated\":true,\"guid\":\"11111111-2222-3333-4444-555555555555\",\"prefix\":\"zz99\"}";
        WriteFile(text);
        DateTime before = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(FilePath, before);

        DecisionFolder? folder = DecisionFolder.Load(
            root, _ => throw new InvalidOperationException("no draw"), () => throw new InvalidOperationException("no guid"));

        Assert.Equal(new DecisionFolder("zz99", Own, true), folder);
        Assert.Equal(text, File.ReadAllText(FilePath));
        Assert.Equal(before, File.GetLastWriteTimeUtc(FilePath));
    }

    // AC-2: an invalid file gives null and is never overwritten.
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"prefix\":\"ab12\",\"guid\":\"11111111-2222-3333-4444-555555555555\"}")]
    [InlineData("{\"prefix\":\"AB12\",\"guid\":\"11111111-2222-3333-4444-555555555555\",\"migrated\":false}")]
    [InlineData("{\"prefix\":\"ai12\",\"guid\":\"11111111-2222-3333-4444-555555555555\",\"migrated\":false}")]
    [InlineData("{\"prefix\":\"ab1\",\"guid\":\"11111111-2222-3333-4444-555555555555\",\"migrated\":false}")]
    [InlineData("{\"prefix\":\"ab12\",\"guid\":\"00000000-0000-0000-0000-000000000000\",\"migrated\":false}")]
    [InlineData("{\"prefix\":\"ab12\",\"guid\":\"nope\",\"migrated\":false}")]
    [InlineData("{\"prefix\":\"ab12\",\"guid\":\"11111111-2222-3333-4444-555555555555\",\"migrated\":\"yes\"}")]
    [InlineData("{\"prefix\":\"ab12\",\"guid\":\"11111111-2222-3333-4444-555555555555\",\"migrated\":false,\"x\":1}")]
    [InlineData("{\"prefix\":\"ab12\",\"prefix\":\"cd34\",\"guid\":\"11111111-2222-3333-4444-555555555555\",\"migrated\":false}")]
    public void Load_Invalid_NullAndKept(string text)
    {
        WriteFile(text);

        Assert.Null(DecisionFolder.Load(root, Draws(10, 11, 1, 2), () => Own));
        Assert.Null(DecisionFolder.Read(root));
        Assert.Equal(text, File.ReadAllText(FilePath));
    }

    // AC-2: invalid UTF-8 and an oversized file give null and stay byte for byte.
    [Fact]
    public void Load_BadBytesOrOversized_NullAndKept()
    {
        Directory.CreateDirectory(root);
        byte[] bad = [.. Encoding.UTF8.GetBytes("{\"prefix\":\"ab12\",\"guid\":\""), 0xFF, .. Encoding.UTF8.GetBytes("\"}")];
        File.WriteAllBytes(FilePath, bad);
        Assert.Null(DecisionFolder.Load(root, Draws(10, 11, 1, 2), () => Own));
        Assert.Equal(bad, File.ReadAllBytes(FilePath));

        string big = "{\"prefix\":\"ab12\",\"guid\":\"11111111-2222-3333-4444-555555555555\",\"migrated\":false"
            + new string(' ', DecisionFolder.MaxFileBytes) + "}";
        File.WriteAllText(FilePath, big);
        Assert.Null(DecisionFolder.Load(root, Draws(10, 11, 1, 2), () => Own));
        Assert.Equal(big, File.ReadAllText(FilePath));
    }

    // AC-2: a folder at the file's path can't be read and is left alone.
    [Fact]
    public void Load_DirectoryAtPath_Null()
    {
        Directory.CreateDirectory(FilePath);

        Assert.Null(DecisionFolder.Load(root, Draws(10, 11, 1, 2), () => Own));
        Assert.True(Directory.Exists(FilePath));
    }

    // AC-2: when another process creates the file between the check and the create, its values are used.
    [Fact]
    public void Load_Race_UsesOtherProcessFile()
    {
        string theirs = "{\"prefix\":\"cd34\",\"guid\":\"99999999-8888-7777-6666-555555555555\",\"migrated\":false}";

        DecisionFolder? folder = DecisionFolder.Load(
            root,
            Draws(10, 11, 1, 2),
            () =>
            {
                WriteFile(theirs);
                return Own;
            });

        Assert.Equal(new DecisionFolder("cd34", Other, false), folder);
        Assert.Equal(theirs, File.ReadAllText(FilePath));
    }

    // AC-10: MarkMigrated sets only migrated, and only while the file holds the same prefix and GUID.
    [Fact]
    public void MarkMigrated_SetsFlag_OnlyForSameFolder()
    {
        DecisionFolder folder = DecisionFolder.Load(root, Draws(10, 11, 1, 2), () => Own)!;

        Assert.False(DecisionFolder.MarkMigrated(root, folder with { Guid = Other }));
        Assert.False(DecisionFolder.Read(root)!.Migrated);

        Assert.True(DecisionFolder.MarkMigrated(root, folder));
        Assert.Equal(folder with { Migrated = true }, DecisionFolder.Read(root));
        Assert.True(DecisionFolder.MarkMigrated(root, folder));
        Assert.Equal([DecisionFolder.FileName], Directory.GetFiles(root).Select(Path.GetFileName));
    }

    // AC-10: a broken file is never marked.
    [Fact]
    public void MarkMigrated_BrokenFile_False()
    {
        WriteFile("{broken");

        Assert.False(DecisionFolder.MarkMigrated(root, new DecisionFolder("ab12", Own, false)));
        Assert.Equal("{broken", File.ReadAllText(FilePath));
    }

    // AC-4: the note's folder GUID, Empty for a bad value, null without the line; foreign only for another GUID.
    [Theory]
    [InlineData("# T\ndecision-folder:: 11111111-2222-3333-4444-555555555555\n", "11111111-2222-3333-4444-555555555555", false)]
    [InlineData("# T\nDecision-Folder:: 99999999-8888-7777-6666-555555555555\n", "99999999-8888-7777-6666-555555555555", true)]
    [InlineData("decision-folder:: nope", "00000000-0000-0000-0000-000000000000", true)]
    [InlineData("# T\nno line\n", null, false)]
    [InlineData("```\ndecision-folder:: 99999999-8888-7777-6666-555555555555\n```", null, false)]
    public void NoteGuid_AndIsForeign(string markdown, string? guid, bool foreign)
    {
        Assert.Equal(guid is null ? null : Guid.Parse(guid), DecisionFolder.NoteGuid(markdown));
        Assert.Equal(foreign, DecisionFolder.IsForeign(markdown, Own));
    }

    // AC-4: the line goes after the title (joining an attribute block), else first; the note's ending and BOM stay.
    [Theory]
    [InlineData("# T\nbody\n", "# T\n@\nbody\n")]
    [InlineData("# T", "# T\n@\n")]
    [InlineData("# T\n\nstatus:: open\n", "# T\n\n@\nstatus:: open\n")]
    [InlineData("# T\n\nbody\n", "# T\n@\n\nbody\n")]
    [InlineData("body\n", "@\nbody\n")]
    [InlineData("", "@\n")]
    [InlineData("# T\r\nbody\r\n", "# T\r\n@\r\nbody\r\n")]
    [InlineData("﻿body\r\n", "﻿@\r\nbody\r\n")]
    [InlineData("#tag\n", "@\n#tag\n")]
    public void WithFolderLine_Places(string markdown, string expected)
    {
        string line = "decision-folder:: " + Own.ToString("D");

        Assert.Equal(expected.Replace("@", line, StringComparison.Ordinal), DecisionFolder.WithFolderLine(markdown, Own));
    }

    // The record never prints its values.
    [Fact]
    public void ToString_IsTypeName()
    {
        Assert.Equal(nameof(DecisionFolder), new DecisionFolder("ab12", Own, false).ToString());
    }
}
