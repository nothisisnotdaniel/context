namespace Context.Tests;

public sealed class NoteHistoryTests : IDisposable
{
    private static readonly DateTime Start = new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private readonly string parent;
    private readonly string root;
    private readonly NoteStore store;
    private DateTime now = Start;

    public NoteHistoryTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-history-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(root);
        store = new NoteStore(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    // AC-1: a top-level note keeps its versions in <root>/.history/<Title>/, byte for byte.
    [Fact]
    public void Keep_TopLevel_WritesTheBytesUnchanged()
    {
        NoteInfo note = store.Create("Plan");
        byte[] bytes = [0xEF, 0xBB, 0xBF, (byte)'#', (byte)'\r', (byte)'\n'];

        string? id = History().Keep(note.Path, bytes);

        Assert.Equal("20261029T090000000Z", id);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, ".history", "Plan", id + ".md")));
    }

    // AC-1: a subpage's versions sit next to it, in <its folder>/.history/<Title>/.
    [Fact]
    public void Keep_Subpage_WritesNextToTheNote()
    {
        store.Create("Work");
        store.CreateChild("Work", "Projects");
        NoteInfo note = store.CreateChild("Work/Projects", "N");

        string? id = History().Keep(note.Path, "old\n"u8.ToArray());

        Assert.True(File.Exists(Path.Combine(root, "Work", "Projects", ".history", "N", id + ".md")));
    }

    // AC-1: the same bytes as the newest version write nothing.
    [Fact]
    public void Keep_SameAsNewest_WritesNothing()
    {
        NoteInfo note = store.Create("Plan");
        NoteHistory history = History();
        history.Keep(note.Path, "a\n"u8.ToArray());
        now = now.AddSeconds(1);

        Assert.Null(history.Keep(note.Path, "a\n"u8.ToArray()));
        Assert.Single(history.Versions(note));
    }

    // AC-2: two versions in the same millisecond get -1, -2; nothing is overwritten.
    [Fact]
    public void Keep_SameMillisecond_CountsUp()
    {
        NoteInfo note = store.Create("Plan");
        NoteHistory history = History();

        string? first = history.Keep(note.Path, "a"u8.ToArray());
        string? second = history.Keep(note.Path, "b"u8.ToArray());
        string? third = history.Keep(note.Path, "c"u8.ToArray());

        Assert.Equal("20261029T090000000Z", first);
        Assert.Equal("20261029T090000000Z-1", second);
        Assert.Equal("20261029T090000000Z-2", third);
        Assert.Equal(["20261029T090000000Z-2", "20261029T090000000Z-1", "20261029T090000000Z"], history.Versions(note).Select(v => v.Id));
        Assert.Equal("a"u8.ToArray(), File.ReadAllBytes(Path.Combine(root, ".history", "Plan", first + ".md")));
    }

    // AC-2: a version file only the owner can read and write.
    [Fact]
    public void Keep_File_IsPrivate()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        NoteInfo note = store.Create("Plan");
        string? id = History().Keep(note.Path, "a"u8.ToArray());

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(Path.Combine(root, ".history", "Plan", id + ".md")));
    }

    // AC-3: at most 50 versions; the oldest go first.
    [Fact]
    public void Keep_OverFifty_RemovesTheOldest()
    {
        NoteInfo note = store.Create("Plan");
        NoteHistory history = History();
        for (int i = 0; i < 52; i++)
        {
            history.Keep(note.Path, System.Text.Encoding.UTF8.GetBytes("v" + i));
            now = now.AddSeconds(1);
        }

        IReadOnlyList<NoteVersion> versions = history.Versions(note);
        Assert.Equal(NoteHistory.MaxVersionsPerNote, versions.Count);
        Assert.Equal(Start.AddSeconds(51), versions[0].SavedUtc);
        Assert.Equal(Start.AddSeconds(2), versions[^1].SavedUtc);
    }

    // AC-3: at most 25 MB in total; the version just written is never removed, even when it alone is large.
    [Fact]
    public void Keep_OverTheByteCap_RemovesTheOldest_NeverTheNewOne()
    {
        NoteInfo note = store.Create("Plan");
        NoteHistory history = History();
        byte[] big = new byte[5 * 1024 * 1024];
        for (int i = 0; i < 6; i++)
        {
            big[0] = (byte)i;
            history.Keep(note.Path, big);
            now = now.AddSeconds(1);
        }

        IReadOnlyList<NoteVersion> versions = history.Versions(note);
        Assert.Equal(5, versions.Count);
        Assert.Equal(Start.AddSeconds(5), versions[0].SavedUtc);
        Assert.True(versions.Sum(v => v.Length) <= NoteHistory.MaxBytesPerNote);
    }

    // AC-4: a file named .history blocks the version, so nothing is written.
    [Fact]
    public void Keep_HistoryIsAFile_Throws()
    {
        NoteInfo note = store.Create("Plan");
        File.WriteAllText(Path.Combine(root, ".history"), "x");

        Assert.Throws<NoteHistoryException>(() => History().Keep(note.Path, "a"u8.ToArray()));
        Assert.Equal("x", File.ReadAllText(Path.Combine(root, ".history")));
    }

    // AC-4: a .history that is a link is never followed.
    [Fact]
    public void Keep_HistoryIsALink_Throws_AndWritesNothing()
    {
        NoteInfo note = store.Create("Plan");
        string elsewhere = Path.Combine(parent, "elsewhere");
        Directory.CreateDirectory(Path.Combine(elsewhere, "Plan"));
        Directory.CreateSymbolicLink(Path.Combine(root, ".history"), elsewhere);

        Assert.Throws<NoteHistoryException>(() => History().Keep(note.Path, "a"u8.ToArray()));
        Assert.Empty(Directory.EnumerateFiles(elsewhere, "*", SearchOption.AllDirectories));
        Assert.Throws<NoteHistoryException>(() => History().Versions(note));
        Assert.False(History().TryRead(note, "20261029T090000000Z", out _));
    }

    // AC-4: Versions lists only regular files with a valid name; a link to a file is left out.
    [Fact]
    public void Versions_OnlyValidRegularFiles()
    {
        NoteInfo note = store.Create("Plan");
        string folder = Path.Combine(root, ".history", "Plan");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "20261029T090000000Z.md"), "ok");
        File.WriteAllText(Path.Combine(folder, "notes.md"), "x");
        File.WriteAllText(Path.Combine(folder, "20261329T090000000Z.md"), "x");
        File.WriteAllText(Path.Combine(folder, "20261029T090000000Z-0.md"), "x");
        File.WriteAllText(Path.Combine(parent, "target.md"), "x");
        File.CreateSymbolicLink(Path.Combine(folder, "20261029T100000000Z.md"), Path.Combine(parent, "target.md"));
        Directory.CreateDirectory(Path.Combine(folder, "20261029T110000000Z.md"));

        Assert.Equal(["20261029T090000000Z"], History().Versions(note).Select(v => v.Id));
        Assert.False(History().TryRead(note, "20261029T100000000Z", out _));
    }

    // AC-4: a sealed version needs a title that passes ValidateTitle; a plain note lists no sealed versions.
    [Fact]
    public void Versions_Sealed_NeedAValidTitle()
    {
        File.WriteAllBytes(Path.Combine(root, "Plan.ctxs"), [1]);
        NoteInfo note = new NoteInfo("Plan", DateTime.Now, Path.Combine(root, "Plan.ctxs"));
        string folder = Path.Combine(root, ".history", "Plan");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "20261029T090000000Z~Old.ctxs"), [2]);
        File.WriteAllBytes(Path.Combine(folder, "20261029T100000000Z~.bad.ctxs"), [3]);
        File.WriteAllBytes(Path.Combine(folder, "20261029T110000000Z.md"), [4]);

        IReadOnlyList<NoteVersion> versions = History().Versions(note);

        NoteVersion only = Assert.Single(versions);
        Assert.Equal("Old", only.SealedTitle);
        Assert.True(History().TryRead(note, only.Id, out byte[]? bytes));
        Assert.Equal([2], bytes);
    }

    // AC-4: Keep refuses an invalid sealed title and writes nothing.
    [Fact]
    public void Keep_InvalidSealedTitle_Throws_AndWritesNothing()
    {
        File.WriteAllBytes(Path.Combine(root, "Plan.ctxs"), [1]);

        Assert.Throws<ArgumentException>(() => History().Keep(Path.Combine(root, "Plan.ctxs"), [1], "a/b"));
        Assert.False(Directory.Exists(Path.Combine(root, ".history")));
    }

    // AC-7: a sealed version keeps the bytes under <id>~<title>.ctxs.
    [Fact]
    public void Keep_Sealed_WritesIdAndTitle()
    {
        string path = Path.Combine(root, "Plan.ctxs");
        File.WriteAllBytes(path, [1]);

        string? id = History().Keep(path, [9, 8], "Plan");

        Assert.Equal([9, 8], File.ReadAllBytes(Path.Combine(root, ".history", "Plan", id + "~Plan.ctxs")));
    }

    // AC-6: RemoveHistory removes one kind, then the folders it emptied.
    [Fact]
    public void RemoveHistory_OneKind_ThenEmptyFolders()
    {
        NoteInfo note = store.Create("Plan");
        NoteHistory history = History();
        history.Keep(note.Path, "a"u8.ToArray());
        history.KeepSealedCopy(note.Path, "20261029T080000000Z", "Plan", [1]);

        history.RemoveHistory(note.Path, ".md");
        Assert.Equal(["20261029T080000000Z~Plan.ctxs"], Directory.GetFiles(Path.Combine(root, ".history", "Plan")).Select(Path.GetFileName));

        history.RemoveHistory(note.Path, ".ctxs");
        Assert.False(Directory.Exists(Path.Combine(root, ".history")));
    }

    // AC-6: an unknown file in the history folder keeps it.
    [Fact]
    public void RemoveHistory_OtherFile_KeepsTheFolder()
    {
        NoteInfo note = store.Create("Plan");
        History().Keep(note.Path, "a"u8.ToArray());
        File.WriteAllText(Path.Combine(root, ".history", "Plan", "readme.txt"), "mine");

        History().RemoveHistory(note.Path);

        Assert.Equal(["readme.txt"], Directory.GetFiles(Path.Combine(root, ".history", "Plan")).Select(Path.GetFileName));
    }

    // AC-9: a sealed copy keeps the id it is given and never overwrites.
    [Fact]
    public void KeepSealedCopy_KeepsTheId_NeverOverwrites()
    {
        NoteInfo note = store.Create("Plan");

        string path = History().KeepSealedCopy(note.Path, "20261028T070000000Z-3", "Plan", [5]);

        Assert.Equal(Path.Combine(root, ".history", "Plan", "20261028T070000000Z-3~Plan.ctxs"), path);
        Assert.Throws<NoteHistoryException>(() => History().KeepSealedCopy(note.Path, "20261028T070000000Z-3", "Plan", [6]));
        Assert.Equal([5], File.ReadAllBytes(path));
        Assert.Throws<ArgumentException>(() => History().KeepSealedCopy(note.Path, "yesterday", "Plan", [6]));
    }

    // AC-1: a path outside the data folder is refused.
    [Fact]
    public void Keep_OutsideTheDataFolder_Throws()
    {
        string outside = Path.Combine(parent, "Other.md");
        File.WriteAllText(outside, "x");

        Assert.Throws<NoteHistoryException>(() => History().Keep(outside, "a"u8.ToArray()));
        Assert.False(Directory.Exists(Path.Combine(parent, ".history")));
    }

    private NoteHistory History() => new NoteHistory(store, () => now);
}
