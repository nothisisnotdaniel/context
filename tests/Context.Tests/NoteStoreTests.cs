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

    // 136 AC-9: a sensitive note is a top-level <name>.ctxs; Core lists it by name and never as a plain note.
    [Fact]
    public void ListSensitive_ListsTopLevelSealedFiles_ByNameOnly()
    {
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "Plan.md"), "plain");
        File.WriteAllText(Path.Combine(root, "beta.ctxs"), "sealed-b");
        File.WriteAllText(Path.Combine(root, "Alpha.ctxs"), "sealed-a");
        File.WriteAllText(Path.Combine(root, "sub", "Inner.ctxs"), "sealed-i");
        File.WriteAllText(Path.Combine(root, ".Alpha.ctxs.1234.tmp"), "temp");
        NoteStore store = new NoteStore(root);

        IReadOnlyList<NoteInfo> found = store.ListSensitive();

        Assert.Equal(".ctxs", NoteStore.SensitiveExtension);
        Assert.Equal(["Alpha", "beta"], found.Select(n => n.Name).ToArray());
        Assert.Equal(Path.Combine(root, "Alpha.ctxs"), found[0].Path);
        Assert.Equal(["Plan"], store.List().Select(n => n.Name).ToArray());
        Assert.Empty(new NoteStore(Path.Combine(parent, "nothing")).ListSensitive());
        Assert.False(Directory.Exists(Path.Combine(parent, "nothing")));
    }

    // 136 AC-9: found by name ignoring case, exact case first; never opened or read as a plain note.
    [Fact]
    public void TryFindSensitive_FindsByName_AndPlainLookupsMiss()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Plan.ctxs"), "sealed");
        File.WriteAllText(Path.Combine(root, "Other.md"), "plain");
        NoteStore store = new NoteStore(root);

        Assert.True(store.TryFindSensitive(" plan ", out NoteInfo? found));
        Assert.Equal("Plan", found!.Name);
        Assert.False(store.TryFindSensitive("Other", out _));
        Assert.False(store.TryFindSensitive("Gone", out _));
        Assert.False(store.TryFind("Plan", out _));
        Assert.False(store.TryOpen("Plan", out _));
    }

    // 136 AC-7: DeleteSensitive removes only the .ctxs; Delete never removes one.
    [Fact]
    public void DeleteSensitive_RemovesOnlyTheSealedFile()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Plan.ctxs"), "sealed");
        File.WriteAllText(Path.Combine(root, "Plan.md"), "plain");
        File.WriteAllText(Path.Combine(root, "Only.ctxs"), "sealed");
        NoteStore store = new NoteStore(root);

        Assert.False(store.Delete("Only"));
        Assert.True(File.Exists(Path.Combine(root, "Only.ctxs")));
        Assert.False(store.DeleteSensitive("Gone"));

        Assert.True(store.DeleteSensitive("plan"));

        Assert.False(File.Exists(Path.Combine(root, "Plan.ctxs")));
        Assert.Equal("plain", File.ReadAllText(Path.Combine(root, "Plan.md")));
        Assert.True(File.Exists(Path.Combine(root, "Only.ctxs")));
    }

    // 136 AC-7: a sensitive note's name is taken: no plain note is created, saved or renamed onto it.
    [Fact]
    public void SensitiveName_IsTakenForPlainNotes()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Plan.ctxs"), "sealed");
        File.WriteAllText(Path.Combine(root, "Other.md"), "plain");
        NoteStore store = new NoteStore(root);

        Assert.Throws<NoteExistsException>(() => store.Create("plan"));
        Assert.Throws<NoteExistsException>(() => store.CreateWithText("Plan", "text"));
        Assert.Throws<NoteExistsException>(() => store.Save("PLAN", "text"u8.ToArray()));
        Assert.Throws<NoteExistsException>(() => store.Rename("Other", "Plan"));

        Assert.Equal(["Other.md", "Plan.ctxs"], Directory.GetFiles(root).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("sealed", File.ReadAllText(Path.Combine(root, "Plan.ctxs")));
    }

    // Writes a note file below the root; segments are joined as folders (Meetings/Meeting 1 -> Meetings/Meeting 1.md).
    private string WriteAt(string name, string text)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    // 152 AC-1: children are listed depth-first under Parent/Title, siblings by title ignoring case.
    [Fact]
    public void List_Subpages_DepthFirst_WithFullNames()
    {
        WriteAt("Meetings", "m");
        WriteAt("Meetings/meeting 2", "2");
        WriteAt("Meetings/Meeting 1", "1");
        WriteAt("Meetings/Meeting 1/Notes", "n");
        WriteAt("alpha", "a");
        WriteAt("Zeta", "z");

        IReadOnlyList<NoteInfo> notes = new NoteStore(root).List();

        Assert.Equal(["alpha", "Meetings", "Meetings/Meeting 1", "Meetings/Meeting 1/Notes", "Meetings/meeting 2", "Zeta"],
            notes.Select(n => n.Name).ToArray());
        NoteInfo deep = notes[3];
        Assert.Equal("Notes", deep.Title);
        Assert.Equal("Meetings/Meeting 1", deep.Parent);
        Assert.Equal(Path.Combine(root, "Meetings", "Meeting 1", "Notes.md"), deep.Path);
        Assert.Null(notes[1].Parent);
        Assert.Equal("Meetings", notes[1].Title);
    }

    // 152 AC-1: the child folder matches the stem exactly first, then ignoring case.
    [Fact]
    public void List_ChildFolder_MatchesIgnoringCase()
    {
        WriteAt("Plan", "p");
        Directory.CreateDirectory(Path.Combine(root, "plan"));
        File.WriteAllText(Path.Combine(root, "plan", "Step.md"), "s");

        IReadOnlyList<NoteInfo> notes = new NoteStore(root).List();

        Assert.Equal(["Plan", "Plan/Step"], notes.Select(n => n.Name).ToArray());
    }

    // 152 AC-1: orphan, hidden, calendar folders, bad or too-long titles and nested .ctxs are never listed.
    [Fact]
    public void List_SkipsOrphanHiddenCalendar_BadTitles_AndNestedSealed()
    {
        WriteAt("Orphan/Child", "o");
        WriteAt(".hidden", "h");
        WriteAt(".hidden/Inner", "i");
        WriteAt("calendar", "c");
        WriteAt("calendar/Event", "e");
        WriteAt("Notes", "n");
        WriteAt("Notes/.dot", "d");
        WriteAt("Notes/.dot/Deep", "d");
        WriteAt("Notes/Good", "g");
        File.WriteAllText(Path.Combine(root, "Notes", "Secret.ctxs"), "sealed");
        File.WriteAllText(Path.Combine(root, "Notes", "trail .md"), "bad");
        File.WriteAllText(Path.Combine(root, "Notes", "CON.md"), "bad");
        string longTitle = new string('a', NoteStore.MaxTitleLength);
        string deep = "Notes";
        for (int i = 0; i < 4; i++)
        {
            deep += "/" + longTitle;
            WriteAt(deep, "x");
        }

        NoteStore store = new NoteStore(root);
        IReadOnlyList<NoteInfo> notes = store.List();

        string[] names = notes.Select(n => n.Name).ToArray();
        Assert.Contains("calendar", names);
        Assert.Contains("Notes/Good", names);
        Assert.DoesNotContain(names, n => n.StartsWith("Orphan", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.Contains(".hidden/", StringComparison.Ordinal) || n.Contains(".dot", StringComparison.Ordinal));
        Assert.DoesNotContain("calendar/Event", names);
        Assert.DoesNotContain(names, n => n.Contains("Secret", StringComparison.Ordinal) || n.Contains("trail", StringComparison.Ordinal) || n.Contains("CON", StringComparison.Ordinal));
        Assert.All(names, n => Assert.True(n.Length <= NoteStore.MaxNameLength));
        Assert.Equal(3, names.Count(n => n.StartsWith("Notes/a", StringComparison.Ordinal)));
        Assert.Empty(store.ListSensitive());
    }

    // 152 AC-1: a symlinked child folder is never followed.
    [Fact]
    public void List_SymlinkedFolder_IsNotFollowed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string outside = Path.Combine(parent, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "Leak.md"), "leak");
        WriteAt("Link", "l");
        Directory.CreateSymbolicLink(Path.Combine(root, "Link"), outside);

        IReadOnlyList<NoteInfo> notes = new NoteStore(root).List();

        Assert.Equal(["Link"], notes.Select(n => n.Name).ToArray());
    }

    // 152 AC-2: a data folder of today lists the same names, paths and order, and listing changes nothing.
    [Fact]
    public void List_TodaysDataFolder_Unchanged()
    {
        WriteAt("Plan", "p");
        WriteAt("beta", "b");
        WriteAt("Alpha", "a");
        Directory.CreateDirectory(Path.Combine(root, "calendar"));
        File.WriteAllText(Path.Combine(root, "calendar", "work.ics"), "BEGIN:VCALENDAR");
        File.WriteAllText(Path.Combine(root, "todos.json"), "[]");
        File.WriteAllText(Path.Combine(root, "context.db"), "db");
        string[] before = Snapshot();

        IReadOnlyList<NoteInfo> notes = new NoteStore(root).List();

        Assert.Equal(["Alpha", "beta", "Plan"], notes.Select(n => n.Name).ToArray());
        Assert.Equal(["Alpha.md", "beta.md", "Plan.md"], notes.Select(n => Path.GetRelativePath(root, n.Path)).ToArray());
        Assert.All(notes, n => Assert.Null(n.Parent));
        Assert.Equal(before, Snapshot());
    }

    private string[] Snapshot() =>
        Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f) + ":" + (File.Exists(f) ? File.ReadAllText(f) : "dir"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    // 152 AC-3: a child is found by full name with the usual rules; paths never match.
    [Fact]
    public void TryFind_Child_ByFullName_NeverByPath()
    {
        string path = WriteAt("Meetings", "m");
        WriteAt("Meetings/Meeting 1", "1");
        WriteAt("Orphan/Child", "o");
        NoteStore store = new NoteStore(root);

        Assert.True(store.TryFind("Meetings/Meeting 1", out NoteInfo? exact));
        Assert.Equal("Meetings/Meeting 1", exact!.Name);
        Assert.True(store.TryFind(" meetings/MEETING 1.md ", out NoteInfo? loose));
        Assert.Equal("Meetings/Meeting 1", loose!.Name);
        Assert.False(store.TryFind("Meetings/../Meetings", out _));
        Assert.False(store.TryFind("Meetings\\Meeting 1", out _));
        Assert.False(store.TryFind(path, out _));
        Assert.False(store.TryFind(Path.Combine(root, "Meetings", "Meeting 1"), out _));
        Assert.False(store.TryFind("Orphan/Child", out _));
        Assert.False(store.TryOpen("Orphan/Child", out _));
    }

    // 152 AC-4: CreateChild writes the heading into the parent's folder, creating it.
    [Fact]
    public void CreateChild_WritesHeading_InParentFolder()
    {
        WriteAt("Meetings", "m");
        NoteStore store = new NoteStore(root);

        NoteInfo made = store.CreateChild("meetings", "  Meeting 1 ");

        Assert.Equal("Meetings/Meeting 1", made.Name);
        Assert.Equal(Path.Combine(root, "Meetings", "Meeting 1.md"), made.Path);
        Assert.Equal("# Meeting 1\n"u8.ToArray(), File.ReadAllBytes(made.Path));
        NoteInfo deeper = store.CreateChild("Meetings/Meeting 1", "Notes");
        Assert.Equal(Path.Combine(root, "Meetings", "Meeting 1", "Notes.md"), deeper.Path);
        Assert.Equal(["Meetings", "Meetings/Meeting 1", "Meetings/Meeting 1/Notes"], store.List().Select(n => n.Name).ToArray());
    }

    // 152 AC-4: bad titles, clashes, missing or calendar parents and long names are refused.
    [Fact]
    public void CreateChild_Refusals()
    {
        WriteAt("Meetings", "m");
        WriteAt("Meetings/Meeting 1", "1");
        WriteAt("Calendar", "c");
        WriteAt("Calendar/Sub", "s");
        NoteStore store = new NoteStore(root);
        string[] before = Snapshot();

        Assert.Throws<ArgumentException>(() => store.CreateChild("Meetings", "a/b"));
        Assert.Throws<ArgumentException>(() => store.CreateChild("Meetings", ""));
        Assert.Throws<NoteExistsException>(() => store.CreateChild("Meetings", "MEETING 1"));
        Assert.Throws<ArgumentException>(() => store.CreateChild("Gone", "Child"));
        Assert.Throws<ArgumentException>(() => store.CreateChild("Calendar", "Child"));
        Assert.Throws<ArgumentException>(() => store.CreateChild("../Meetings", "Child"));

        string longTitle = new string('a', NoteStore.MaxTitleLength);
        string name = "Meetings";
        for (int i = 0; i < 3; i++)
        {
            name = store.CreateChild(name, longTitle).Name;
        }

        before = Snapshot();
        Assert.Throws<ArgumentException>(() => store.CreateChild(name, longTitle));
        Assert.Equal(before, Snapshot());
    }

    // 152 AC-5: renaming a parent moves its children; bytes stay; old names are gone.
    [Fact]
    public void Rename_WithChildren_MovesSubtree()
    {
        WriteAt("Meetings", "m");
        WriteAt("Meetings/Meeting 1", "one");
        WriteAt("Meetings/Meeting 1/Notes", "notes");
        NoteStore store = new NoteStore(root);

        NoteInfo? renamed = store.Rename("Meetings", "Calls");

        Assert.Equal("Calls", renamed!.Name);
        Assert.Equal(["Calls", "Calls/Meeting 1", "Calls/Meeting 1/Notes"], store.List().Select(n => n.Name).ToArray());
        Assert.Equal("one", File.ReadAllText(Path.Combine(root, "Calls", "Meeting 1.md")));
        Assert.Equal("notes", File.ReadAllText(Path.Combine(root, "Calls", "Meeting 1", "Notes.md")));
        Assert.False(Directory.Exists(Path.Combine(root, "Meetings")));
        Assert.False(File.Exists(Path.Combine(root, "Meetings.md")));
    }

    // 152 AC-5: a child keeps its parent; a case-only rename with children keeps them; clashes are against siblings.
    [Fact]
    public void Rename_Child_CaseOnly_AndSiblingClash()
    {
        WriteAt("Meetings", "m");
        WriteAt("Meetings/Meeting 1", "one");
        WriteAt("Meetings/Meeting 1/Notes", "notes");
        WriteAt("Meetings/Other", "o");
        WriteAt("Other", "top");
        File.WriteAllText(Path.Combine(root, "Secret.ctxs"), "sealed");
        NoteStore store = new NoteStore(root);

        Assert.Throws<NoteExistsException>(() => store.Rename("Meetings/Meeting 1", "other"));
        NoteInfo? sensitiveTitle = store.Rename("Meetings/Other", "Secret");
        Assert.Equal("Meetings/Secret", sensitiveTitle!.Name);
        Assert.Throws<NoteExistsException>(() => store.Rename("Meetings", "secret"));
        Assert.Throws<ArgumentException>(() => store.Rename("Meetings/Meeting 1", "a/b"));

        NoteInfo? caseOnly = store.Rename("Meetings/Meeting 1", "MEETING 1");

        Assert.Equal("Meetings/MEETING 1", caseOnly!.Name);
        Assert.Equal(["Meetings", "Meetings/MEETING 1", "Meetings/MEETING 1/Notes", "Meetings/Secret", "Other"],
            store.List().Select(n => n.Name).ToArray());
        Assert.Equal("notes", File.ReadAllText(Path.Combine(root, "Meetings", "MEETING 1", "Notes.md")));

        NoteInfo? top = store.Rename("meetings", "MEETINGS");
        Assert.Equal("MEETINGS", top!.Name);
        Assert.Contains("MEETINGS/MEETING 1/Notes", store.List().Select(n => n.Name));
    }

    // 152 AC-6: Move puts a note and its subtree under another note, and back to the top level.
    [Fact]
    public void Move_SubtreeUnderParent_AndBackToTop()
    {
        WriteAt("Meetings", "m");
        WriteAt("Archive", "a");
        WriteAt("Meetings/Meeting 1", "one");
        NoteStore store = new NoteStore(root);

        NoteInfo? moved = store.Move("Meetings", "archive");

        Assert.Equal("Archive/Meetings", moved!.Name);
        Assert.Equal(["Archive", "Archive/Meetings", "Archive/Meetings/Meeting 1"], store.List().Select(n => n.Name).ToArray());
        Assert.Equal("one", File.ReadAllText(Path.Combine(root, "Archive", "Meetings", "Meeting 1.md")));
        Assert.Null(store.Move("Gone", null));

        NoteInfo? back = store.Move("Archive/Meetings", "  ");

        Assert.Equal("Meetings", back!.Name);
        Assert.Equal(["Archive", "Meetings", "Meetings/Meeting 1"], store.List().Select(n => n.Name).ToArray());
        Assert.Equal(store.List()[1], store.Move("Meetings", null));
    }

    // 152 AC-6: under itself, a descendant, calendar, a missing parent, too long or a clash is refused; nothing changes.
    [Fact]
    public void Move_Refusals_ChangeNothing()
    {
        WriteAt("Meetings", "m");
        WriteAt("Meetings/Meeting 1", "one");
        WriteAt("calendar", "c");
        WriteAt("Archive", "a");
        WriteAt("Archive/Meeting 1", "clash");
        WriteAt("Top", "t");
        WriteAt("Meetings/Top", "child");
        string longTitle = new string('a', NoteStore.MaxTitleLength);
        WriteAt("Long", "l");
        WriteAt("Long/" + longTitle, "x");
        WriteAt("Long/" + longTitle + "/" + longTitle, "x");
        WriteAt("Long/" + longTitle + "/" + longTitle + "/" + longTitle, "x");
        WriteAt(longTitle, "deep");
        WriteAt(longTitle + "/" + longTitle, "deep child");
        NoteStore store = new NoteStore(root);
        string[] before = Snapshot();

        Assert.Throws<ArgumentException>(() => store.Move("Meetings", "Meetings"));
        Assert.Throws<ArgumentException>(() => store.Move("Meetings", "Meetings/Meeting 1"));
        Assert.Throws<ArgumentException>(() => store.Move("Meetings", "Calendar"));
        Assert.Throws<ArgumentException>(() => store.Move("Meetings", "Gone"));
        Assert.Throws<ArgumentException>(() => store.Move(longTitle, "Long/" + longTitle + "/" + longTitle));
        Assert.Throws<NoteExistsException>(() => store.Move("Meetings/Meeting 1", "Archive"));
        Assert.Throws<NoteExistsException>(() => store.Move("Meetings/top", null));

        Assert.Equal(before, Snapshot());
    }

    // 152 AC-6: when the folder move fails after the file move, the file goes back and the exception propagates.
    [Fact]
    public void Move_FolderMoveFails_RestoresFile()
    {
        WriteAt("Meetings", "m");
        WriteAt("Meetings/Meeting 1", "one");
        WriteAt("Archive", "a");
        NoteStore store = new NoteStore(root);
        string[] before = Snapshot();
        store.BeforeFolderMove = () => throw new IOException("disk gone");

        IOException error = Assert.Throws<IOException>(() => store.Move("Meetings", "Archive"));

        Assert.Equal("disk gone", error.Message);
        Assert.Equal(before, Snapshot());
        Assert.Equal(["Archive", "Meetings", "Meetings/Meeting 1"], store.List().Select(n => n.Name).ToArray());
    }

    // 152 AC-5: the same restore holds for a rename.
    [Fact]
    public void Rename_FolderMoveFails_RestoresFile()
    {
        WriteAt("Meetings", "m");
        WriteAt("Meetings/Meeting 1", "one");
        NoteStore store = new NoteStore(root);
        string[] before = Snapshot();
        store.BeforeFolderMove = () => throw new UnauthorizedAccessException("no");

        Assert.Throws<UnauthorizedAccessException>(() => store.Rename("Meetings", "Calls"));

        Assert.Equal(before, Snapshot());
    }

    // 152 AC-7: Descendants counts the notes below; Delete removes the subtree and only empty folders.
    [Fact]
    public void Delete_Subtree_KeepsStrayFiles()
    {
        WriteAt("Meetings", "m");
        WriteAt("Meetings/Meeting 1", "one");
        WriteAt("Meetings/Meeting 1/Notes", "notes");
        WriteAt("Meetings/Meeting 2", "two");
        File.WriteAllText(Path.Combine(root, "Meetings", "keep.txt"), "stray");
        WriteAt("Other", "o");
        NoteStore store = new NoteStore(root);

        Assert.Equal(3, store.Descendants("Meetings"));
        Assert.Equal(1, store.Descendants("meetings/meeting 1"));
        Assert.Equal(0, store.Descendants("Other"));
        Assert.Equal(0, store.Descendants("Gone"));

        Assert.True(store.Delete("Meetings"));

        Assert.Equal(["Other"], store.List().Select(n => n.Name).ToArray());
        Assert.False(File.Exists(Path.Combine(root, "Meetings.md")));
        Assert.False(Directory.Exists(Path.Combine(root, "Meetings", "Meeting 1")));
        Assert.Equal("stray", File.ReadAllText(Path.Combine(root, "Meetings", "keep.txt")));
    }

    // 152 AC-7: deleting a child leaves its parent and siblings.
    [Fact]
    public void Delete_Child_RemovesEmptyFolder()
    {
        WriteAt("Meetings", "m");
        WriteAt("Meetings/Meeting 1", "one");
        NoteStore store = new NoteStore(root);

        Assert.True(store.Delete("Meetings/Meeting 1"));

        Assert.Equal(["Meetings"], store.List().Select(n => n.Name).ToArray());
        Assert.True(File.Exists(Path.Combine(root, "Meetings.md")));
    }
}
