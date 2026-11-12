namespace Context.Tests;

public sealed class NoteStoreTests : IDisposable
{
    private readonly string parent;
    private readonly string root;

    public NoteStoreTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-notes-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void DefaultFolder_IsContextInAppData()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Context");

        Assert.Equal(expected, NoteStore.DefaultFolder());
    }

    // AC-1
    [Fact]
    public void Create_WritesTitleHeading_Utf8NoBom_AndCreatesRoot()
    {
        NoteStore store = new NoteStore(root);
        Assert.False(Directory.Exists(root));

        NoteInfo note = store.Create("Shopping list");

        string path = Path.Combine(root, "Shopping list.md");
        Assert.True(File.Exists(path));
        Assert.Equal("Shopping list", note.Name);
        Assert.Equal("# Shopping list\n"u8.ToArray(), File.ReadAllBytes(path));
    }

    // AC-1
    [Fact]
    public void Create_KeepsNonAsciiTitle_WithoutBom()
    {
        NoteStore store = new NoteStore(root);

        store.Create("Grüße");

        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "Grüße.md"));
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("# Grüße\n", System.Text.Encoding.UTF8.GetString(bytes));
    }

    // AC-2
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a\0b")]
    [InlineData("a\tb")]
    [InlineData(".hidden")]
    [InlineData("..")]
    [InlineData("../secret")]
    public void Create_RejectsInvalidTitle_WritesNothing(string title)
    {
        NoteStore store = new NoteStore(root);

        Assert.NotNull(NoteStore.ValidateTitle(title));
        Assert.Throws<ArgumentException>(() => store.Create(title));
        Assert.False(Directory.Exists(root));
        Assert.Empty(Directory.GetFileSystemEntries(parent));
    }

    // AC-2
    [Fact]
    public void Create_RejectsEveryInvalidFileNameChar()
    {
        NoteStore store = new NoteStore(root);

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            Assert.Throws<ArgumentException>(() => store.Create($"a{c}b"));
        }

        Assert.False(Directory.Exists(root));
    }

    // AC-2
    [Fact]
    public void Create_TitleLength_100Ok_101Rejected()
    {
        NoteStore store = new NoteStore(root);

        Assert.Throws<ArgumentException>(() => store.Create(new string('a', 101)));
        Assert.False(Directory.Exists(root));

        store.Create(new string('a', 100));
        Assert.True(File.Exists(Path.Combine(root, new string('a', 100) + ".md")));
    }

    // AC-3
    [Theory]
    [InlineData("Shopping list")]
    [InlineData("SHOPPING LIST")]
    public void Create_Existing_ThrowsAndLeavesFileUnchanged(string title)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Shopping list.md");
        byte[] original = "﻿my own text [x]\r\n"u8.ToArray();
        File.WriteAllBytes(path, original);
        NoteStore store = new NoteStore(root);

        Assert.Throws<NoteExistsException>(() => store.Create(title));

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(root));
    }

    // AC-4
    [Fact]
    public void List_OnlyTopLevelMdFiles_SortedByNameIgnoringCase()
    {
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "sub", "y.md"), "y");
        File.WriteAllText(Path.Combine(root, "x.txt"), "x");
        File.WriteAllText(Path.Combine(root, "x.mdx"), "x");
        File.WriteAllText(Path.Combine(root, "beta.md"), "b");
        File.WriteAllText(Path.Combine(root, "Alpha.md"), "a");
        File.WriteAllText(Path.Combine(root, "gamma.md"), "g");
        DateTime modified = new DateTime(2026, 3, 4, 5, 6, 0, DateTimeKind.Local);
        File.SetLastWriteTime(Path.Combine(root, "beta.md"), modified);

        IReadOnlyList<NoteInfo> notes = new NoteStore(root).List();

        Assert.Equal(["Alpha", "beta", "gamma"], notes.Select(n => n.Name));
        Assert.Equal(modified, notes[1].Modified);
    }

    // AC-5
    [Fact]
    public void List_MissingRoot_IsEmpty_AndDoesNotCreateIt()
    {
        IReadOnlyList<NoteInfo> notes = new NoteStore(root).List();

        Assert.Empty(notes);
        Assert.False(Directory.Exists(root));
    }

    // AC-7
    [Theory]
    [InlineData("Shopping list")]
    [InlineData("shopping LIST")]
    [InlineData("Shopping list.md")]
    [InlineData("SHOPPING LIST.MD")]
    public void TryOpen_MatchesIgnoringCase_AndWithExtension(string name)
    {
        NoteStore store = new NoteStore(root);
        store.Create("Shopping list");
        File.AppendAllText(Path.Combine(root, "Shopping list.md"), "- milk [x]\n");

        Assert.True(store.TryOpen(name, out Note? note));
        Assert.NotNull(note);
        Assert.Equal("Shopping list", note.Name);
        Assert.Equal("# Shopping list\n- milk [x]\n", note.Text);
    }

    // AC-7
    [Fact]
    public void TryOpen_CannotEscapeRoot()
    {
        File.WriteAllText(Path.Combine(parent, "secret.md"), "secret");
        NoteStore store = new NoteStore(root);
        store.Create("note");

        Assert.False(store.TryOpen("../secret", out Note? relative));
        Assert.Null(relative);
        Assert.False(store.TryOpen("../secret.md", out _));
        Assert.False(store.TryOpen(Path.Combine(parent, "secret.md"), out _));
        Assert.False(store.TryOpen(Path.Combine(parent, "secret"), out _));
    }

    // AC-7
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".md")]
    [InlineData("missing")]
    [InlineData("sub/y")]
    public void TryOpen_UnknownName_ReturnsFalse(string name)
    {
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "sub", "y.md"), "y");
        NoteStore store = new NoteStore(root);
        store.Create("note");

        Assert.False(store.TryOpen(name, out Note? note));
        Assert.Null(note);
    }

    // AC-7
    [Fact]
    public void TryOpen_MissingRoot_ReturnsFalse()
    {
        Assert.False(new NoteStore(root).TryOpen("x", out _));
        Assert.False(Directory.Exists(root));
    }

    // 013 AC-1
    [Theory]
    [InlineData("x.md", "x")]
    [InlineData("x.MD", "x")]
    [InlineData("x.md.md", "x")]
    [InlineData(" x .md ", "x")]
    [InlineData("x.mdx", "x.mdx")]
    [InlineData("notes.md draft", "notes.md draft")]
    [InlineData(".md", "")]
    [InlineData(".md.md", "")]
    public void NormalizeTitle_StripsTrailingMd(string title, string expected)
    {
        Assert.Equal(expected, NoteStore.NormalizeTitle(title));
    }

    // 013 AC-2
    [Fact]
    public void Create_TitleEndingInMd_WritesSingleExtension()
    {
        NoteStore store = new NoteStore(root);

        NoteInfo note = store.Create("x.md");

        Assert.Equal("x", note.Name);
        Assert.Equal("# x\n"u8.ToArray(), File.ReadAllBytes(Path.Combine(root, "x.md")));
        Assert.False(File.Exists(Path.Combine(root, "x.md.md")));
        Assert.Single(Directory.GetFiles(root));
    }

    // 013 AC-6
    [Fact]
    public void Create_100CharTitlePlusMd_IsAccepted()
    {
        NoteStore store = new NoteStore(root);
        string title = new string('a', 100);

        NoteInfo note = store.Create(title + ".md");

        Assert.Equal(title, note.Name);
        Assert.True(File.Exists(Path.Combine(root, title + ".md")));
    }

    // 013 AC-5, AC-6
    [Theory]
    [InlineData(".md")]
    [InlineData(".md.md")]
    [InlineData("a/b.md")]
    [InlineData(".x.md")]
    [InlineData("..md")]
    public void Create_InvalidAfterStripping_IsRejected(string title)
    {
        NoteStore store = new NoteStore(root);

        Assert.Throws<ArgumentException>(() => store.Create(title));
        Assert.False(Directory.Exists(root));
    }

    // 013 AC-4
    [Theory]
    [InlineData("x.md")]
    [InlineData("X.MD")]
    public void Create_ExistingAfterStripping_Throws(string title)
    {
        NoteStore store = new NoteStore(root);
        store.Create("x");

        NoteExistsException error = Assert.Throws<NoteExistsException>(() => store.Create(title));

        Assert.Equal("x", error.Name, ignoreCase: true);
        Assert.Single(Directory.GetFiles(root));
    }

    // 013 AC-7
    [Fact]
    public void TryOpen_LegacyDoubleExtension_OpensByListedName()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "x.md.md"), "legacy");
        NoteStore store = new NoteStore(root);

        Assert.Equal(["x.md"], store.List().Select(n => n.Name));
        Assert.True(store.TryOpen("x.md", out Note? note));
        Assert.NotNull(note);
        Assert.Equal("x.md", note.Name);
        Assert.Equal("legacy", note.Text);
    }

    // 013 AC-7
    [Fact]
    public void TryOpen_WithMd_StillOpensPlainNote()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "y.md"), "plain");
        NoteStore store = new NoteStore(root);

        Assert.True(store.TryOpen("y.md", out Note? note));
        Assert.NotNull(note);
        Assert.Equal("y", note.Name);
        Assert.Equal("plain", note.Text);
    }

    // 013 AC-7
    [Fact]
    public void TryOpen_BothFilesExist_TypedNameWinsThenStripped()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "x.md.md"), "legacy");
        File.WriteAllText(Path.Combine(root, "x.md"), "plain");
        NoteStore store = new NoteStore(root);

        Assert.True(store.TryOpen("x.md", out Note? legacy));
        Assert.Equal("legacy", legacy!.Text);
        Assert.True(store.TryOpen("x", out Note? plain));
        Assert.Equal("plain", plain!.Text);
    }

    private const string ReservedMessage =
        "A note title can't be a name Windows reserves (CON, PRN, AUX, NUL, COM1-9, LPT1-9).";

    private const string TrailingMessage = "A note title can't end with a dot or a space.";

    // 018 AC-1
    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("Prn")]
    [InlineData("AUX")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("com9")]
    [InlineData("LPT1")]
    [InlineData("lpt9")]
    [InlineData("COM\u00B9")]
    [InlineData("LPT\u00B3")]
    [InlineData("CON.txt")]
    [InlineData("nul.tar.gz")]
    [InlineData("Aux.notes")]
    [InlineData("CON .txt")]
    public void ValidateTitle_WindowsReservedName_IsRejected(string title)
    {
        Assert.Equal(ReservedMessage, NoteStore.ValidateTitle(title));
    }

    // 018 AC-2
    [Theory]
    [InlineData("CONSOLE")]
    [InlineData("Contact")]
    [InlineData("ICON")]
    [InlineData("NULL")]
    [InlineData("COM")]
    [InlineData("COM0")]
    [InlineData("COM10")]
    [InlineData("LPT")]
    [InlineData("LPT0")]
    [InlineData("my CON")]
    [InlineData("CON x")]
    [InlineData("AUX-notes")]
    [InlineData("x.CON")]
    public void ValidateTitle_ResemblesReservedName_IsValid(string title)
    {
        Assert.Null(NoteStore.ValidateTitle(title));
    }

    // 018 AC-3
    [Theory]
    [InlineData("x.")]
    [InlineData("x..")]
    [InlineData("Notes...")]
    [InlineData("x ")]
    public void ValidateTitle_TrailingDotOrSpace_IsRejected(string title)
    {
        Assert.Equal(TrailingMessage, NoteStore.ValidateTitle(title));
    }

    // 018 AC-4
    [Theory]
    [InlineData("v1.2 notes")]
    [InlineData("a. b")]
    [InlineData("Shopping list")]
    public void ValidateTitle_InnerDotsAndSpaces_AreValid(string title)
    {
        Assert.Null(NoteStore.ValidateTitle(title));
    }

    // 018 AC-5
    [Theory]
    [InlineData(".", "A note title can't start with a dot.")]
    [InlineData("..", "A note title can't start with a dot.")]
    [InlineData("a/b.", "A note title can't contain / \\ : or control characters.")]
    public void ValidateTitle_ExistingRules_KeepPriority(string title, string message)
    {
        Assert.Equal(message, NoteStore.ValidateTitle(title));
    }

    // 018 AC-5
    [Fact]
    public void ValidateTitle_TooLongEndingInDot_GetsLengthMessage()
    {
        string title = new string('a', 100) + ".";

        Assert.Equal("A note title can be at most 100 characters.", NoteStore.ValidateTitle(title));
    }

    // 018 AC-6
    [Theory]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("x.")]
    [InlineData("x..md")]
    public void Create_WindowsUnsafeTitle_IsRejected(string title)
    {
        NoteStore store = new NoteStore(root);

        Assert.Throws<ArgumentException>(() => store.Create(title));
        Assert.False(Directory.Exists(root));
    }

    // AC-10
    [Fact]
    public void Save_InvalidTitle_Throws()
    {
        NoteStore store = new NoteStore(root);

        Assert.Throws<ArgumentException>(() => store.Save("CON", [1]));
        Assert.False(Directory.Exists(root));
    }

    // AC-9
    [Fact]
    public void Contains_IsTrueForRootAndInside_FalseOutside_AndFollowsLinks()
    {
        Directory.CreateDirectory(root);
        NoteStore store = new NoteStore(root);
        string link = Path.Combine(parent, "link");
        Directory.CreateSymbolicLink(link, root);

        Assert.True(store.Contains(root));
        Assert.True(store.Contains(Path.Combine(root, "x.md")));
        Assert.True(store.Contains(Path.Combine(root, "sub", "..", "x.md")));
        Assert.True(store.Contains(Path.Combine(link, "x.md")));
        Assert.True(store.Contains(Path.Combine(root.ToUpperInvariant(), "x.md")));
        Assert.False(store.Contains(Path.Combine(parent, "x.md")));
        Assert.False(store.Contains(root + "-other"));
    }

    [Fact]
    public void ReadImport_RejectsMissingAndTooLarge()
    {
        Directory.CreateDirectory(parent);
        string big = Path.Combine(parent, "big.md");
        File.WriteAllBytes(big, new byte[NoteStore.MaxImportBytes + 1]);

        Assert.Throws<FileNotFoundException>(() => NoteStore.ReadImport(Path.Combine(parent, "none.md")));
        Assert.Throws<InvalidDataException>(() => NoteStore.ReadImport(big));
    }

    // 132 AC-7: the old magic bytes (00 43 54 58 45) get no special handling anywhere in the store.
    [Fact]
    public void FileStartingWithTheOldMagicBytes_IsReadLikeAnyOtherFile()
    {
        Directory.CreateDirectory(root);
        byte[] bytes = [0x00, 0x43, 0x54, 0x58, 0x45, 0x01, .. "rest\n"u8];
        File.WriteAllBytes(Path.Combine(root, "Old.md"), bytes);
        string outside = Path.Combine(parent, "old-import.md");
        File.WriteAllBytes(outside, bytes);
        NoteStore store = new NoteStore(root);
        NoteInfo info = Assert.Single(store.List());

        Assert.Equal("\0CTXE\u0001rest\n", store.Read(info));
        Assert.True(store.TryOpen("Old", out Note? note));
        Assert.Equal("\0CTXE\u0001rest\n", note!.Text);
        EditableNote editable = store.ReadForEdit(info);
        Assert.Equal("\0CTXE\u0001rest\n", editable.Text);
        Assert.Equal(NoteSaveResult.Saved, store.SaveIfUnchanged(editable, editable.Text + "more\n", out _));
        Assert.Equal((byte[])[.. bytes, .. "more\n"u8], File.ReadAllBytes(info.Path));
        Assert.Equal(bytes, NoteStore.ReadImport(outside));
    }

    // 132 AC-9: Save writes exactly the given bytes, for a new note and over an existing one.
    [Fact]
    public void Save_CreatesOrReplaces_KeepingTheExistingName_AndWritesTheBytesAsGiven()
    {
        NoteStore store = new NoteStore(root);
        store.Create("Plan");

        (NoteInfo replaced, bool wasReplaced) = store.Save("plan.md", "# New\n"u8.ToArray());
        (NoteInfo created, bool createdReplaced) = store.Save("Fresh", "# Fresh\n"u8.ToArray());

        Assert.True(wasReplaced);
        Assert.Equal("Plan", replaced.Name);
        Assert.False(createdReplaced);
        Assert.Equal(["Fresh", "Plan"], store.List().Select(n => n.Name));
        Assert.Equal("# New\n"u8.ToArray(), File.ReadAllBytes(replaced.Path));
        Assert.Equal("# Fresh\n"u8.ToArray(), File.ReadAllBytes(created.Path));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    // 123 round 2: a FIFO named like a Markdown file is never opened, so the import can't hang.
    [Fact]
    public async Task ReadImport_FifoOrEmptyFile_ReturnsWithoutBlocking()
    {
        Directory.CreateDirectory(parent);
        string empty = Path.Combine(parent, "empty.md");
        File.WriteAllBytes(empty, []);
        Assert.Empty(NoteStore.ReadImport(empty));

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string fifo = Path.Combine(parent, "pipe.md");
        using (System.Diagnostics.Process mkfifo = System.Diagnostics.Process.Start("mkfifo", fifo))
        {
            mkfifo.WaitForExit();
        }

        Task<byte[]> read = Task.Run(() => NoteStore.ReadImport(fifo));
        Assert.Same(read, await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.Empty(await read);
    }

    // 134 AC-1: a rename moves the file, keeps its bytes and last-write time, strips .md and returns the new note.
    [Fact]
    public void Rename_Valid_MovesFileKeepingBytesAndTime()
    {
        NoteStore store = new NoteStore(root);
        store.Create("Plan");
        string old = Path.Combine(root, "Plan.md");
        byte[] bytes = "# Plan\r\nKept\r\n"u8.ToArray();
        File.WriteAllBytes(old, bytes);
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(old, time);

        NoteInfo? renamed = store.Rename("plan", "  Project.md ");

        Assert.NotNull(renamed);
        Assert.Equal("Project", renamed.Name);
        Assert.Equal(Path.Combine(root, "Project.md"), renamed.Path);
        Assert.False(File.Exists(old));
        Assert.Equal(bytes, File.ReadAllBytes(renamed.Path));
        Assert.Equal(time, File.GetLastWriteTimeUtc(renamed.Path));
        Assert.Equal(["Project"], store.List().Select(n => n.Name).ToArray());
    }

    // 134 AC-1: an invalid title throws with the ValidateTitle text and moves nothing.
    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData(".hidden")]
    [InlineData("CON")]
    [InlineData("a:b.md")]
    public void Rename_InvalidTitle_ThrowsValidateText(string title)
    {
        NoteStore store = new NoteStore(root);
        store.Create("Plan");

        ArgumentException error = Assert.Throws<ArgumentException>(() => store.Rename("Plan", title));

        Assert.StartsWith(NoteStore.ValidateTitle(NoteStore.NormalizeTitle(title))!, error.Message, StringComparison.Ordinal);
        Assert.Equal(["Plan"], store.List().Select(n => n.Name).ToArray());
    }

    // 134 AC-1: another note with that title, ignoring case, is never overwritten.
    [Fact]
    public void Rename_OtherNoteExistsIgnoringCase_Throws()
    {
        NoteStore store = new NoteStore(root);
        store.Create("Plan");
        store.Create("Other");

        NoteExistsException error = Assert.Throws<NoteExistsException>(() => store.Rename("Plan", "OTHER"));

        Assert.Equal("Note OTHER already exists.", error.Message);
        Assert.Equal("# Other\n", File.ReadAllText(Path.Combine(root, "Other.md")));
        Assert.Equal(["Other", "Plan"], store.List().Select(n => n.Name).ToArray());
    }

    // 134 AC-1: a case-only rename of the same note is allowed.
    [Fact]
    public void Rename_CaseOnly_Renames()
    {
        NoteStore store = new NoteStore(root);
        store.Create("Plan");

        NoteInfo? renamed = store.Rename("Plan", "plan");

        Assert.Equal("plan", renamed!.Name);
        Assert.Equal(["plan"], store.List().Select(n => n.Name).ToArray());
        Assert.Equal("# Plan\n", File.ReadAllText(renamed.Path));
    }

    // 134 AC-1: a missing note gives null and creates nothing.
    [Fact]
    public void Rename_Missing_ReturnsNull()
    {
        NoteStore store = new NoteStore(root);
        store.Create("Plan");

        Assert.Null(store.Rename("Nope", "Other"));
        Assert.Equal(["Plan"], store.List().Select(n => n.Name).ToArray());
    }

    // 134 AC-1: the same title as before writes nothing and returns the note.
    [Fact]
    public void Rename_SameName_WritesNothing()
    {
        NoteStore store = new NoteStore(root);
        store.Create("Plan");
        string path = Path.Combine(root, "Plan.md");
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, time);

        NoteInfo? same = store.Rename("Plan", "Plan.md");

        Assert.Equal("Plan", same!.Name);
        Assert.Equal(path, same.Path);
        Assert.Equal(time, File.GetLastWriteTimeUtc(path));
        Assert.Equal(path, Assert.Single(Directory.GetFiles(root)));
    }

    // 134 AC-2: a delete removes the matched note only.
    [Fact]
    public void Delete_Hit_DeletesThatNote()
    {
        NoteStore store = new NoteStore(root);
        store.Create("Plan");
        store.Create("Other");

        Assert.True(store.Delete("plan.md"));

        Assert.Equal(["Other"], store.List().Select(n => n.Name).ToArray());
    }

    // 134 AC-2: a name that matches no note deletes nothing, also files outside the root or not in List().
    [Theory]
    [InlineData("Nope")]
    [InlineData("../outside")]
    [InlineData("sub/inner")]
    [InlineData("notes.txt")]
    [InlineData("notes")]
    public void Delete_Miss_DeletesNothing(string name)
    {
        NoteStore store = new NoteStore(root);
        store.Create("Plan");
        File.WriteAllText(Path.Combine(parent, "outside.md"), "x");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "sub", "inner.md"), "x");
        File.WriteAllText(Path.Combine(root, "notes.txt"), "x");

        Assert.False(store.Delete(name));

        Assert.True(File.Exists(Path.Combine(parent, "outside.md")));
        Assert.True(File.Exists(Path.Combine(root, "sub", "inner.md")));
        Assert.True(File.Exists(Path.Combine(root, "notes.txt")));
        Assert.Equal(["Plan"], store.List().Select(n => n.Name).ToArray());
    }
}
