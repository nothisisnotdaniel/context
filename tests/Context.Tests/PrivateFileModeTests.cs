using System.Text;

namespace Context.Tests;

// Job 274 AC-1 to AC-5: every data file Context writes is 0600 and every data folder it creates 0700 on Unix. Each
// test returns early on Windows, where modes don't apply, and uses its own temp folder.
public sealed class PrivateFileModeTests : IDisposable
{
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode PrivateFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode Readable =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private readonly string parent;
    private readonly string root;

    public PrivateFileModeTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-private-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private static UnixFileMode Mode(string path) =>
        OperatingSystem.IsWindows() ? throw new PlatformNotSupportedException() : File.GetUnixFileMode(path);

    // AC-1: Create, CreateChild and CreateWithText write 0600 notes.
    [Fact]
    public void NewNotes_AreOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        NoteStore store = new NoteStore(root);
        NoteInfo plan = store.Create("Plan");
        NoteInfo child = store.CreateChild("Plan", "Step");
        NoteInfo text = store.CreateWithText("Ideas", "# Ideas\n");

        Assert.Equal(Private, Mode(plan.Path));
        Assert.Equal(Private, Mode(child.Path));
        Assert.Equal(Private, Mode(text.Path));
    }

    // AC-2: Save (import) leaves a new note and a replaced 0644 note as 0600.
    [Fact]
    public void Save_NewAndReplacedNote_AreOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        NoteStore store = new NoteStore(root);
        (NoteInfo created, bool replacedNew) = store.Save("Fresh", Encoding.UTF8.GetBytes("# Fresh\n"));
        Assert.False(replacedNew);
        Assert.Equal(Private, Mode(created.Path));

        string old = Path.Combine(root, "Old.md");
        File.WriteAllText(old, "# Old\n");
        File.SetUnixFileMode(old, Readable);
        (NoteInfo replaced, bool wasReplaced) = store.Save("Old", Encoding.UTF8.GetBytes("# Old again\n"));

        Assert.True(wasReplaced);
        Assert.Equal(Private, Mode(replaced.Path));
        Assert.Equal("# Old again\n", File.ReadAllText(replaced.Path));
    }

    // AC-3: todos.json is 0600 after a write, also when it was 0644.
    [Fact]
    public void TodoStore_Write_IsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        TodoStore todos = new TodoStore(root, () => new DateTime(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc), () => Guid.Empty);
        string path = Path.Combine(root, "todos.json");
        todos.Add("First", new DateOnly(2026, 11, 19));
        Assert.Equal(Private, Mode(path));

        File.SetUnixFileMode(path, Readable);
        todos.Add("Second", new DateOnly(2026, 11, 20));
        Assert.Equal(Private, Mode(path));
    }

    // AC-3: settings.json is 0600 after a write, also when it was 0644.
    [Fact]
    public void SettingsStore_Write_IsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        SettingsStore settings = new SettingsStore(root);
        string path = Path.Combine(root, "settings.json");
        settings.SaveDayEnd(new DayEndSettings("2026-11-19T17:00", null));
        Assert.Equal(Private, Mode(path));

        File.SetUnixFileMode(path, Readable);
        settings.SaveDayEnd(new DayEndSettings("2026-11-19T18:00", null));
        Assert.Equal(Private, Mode(path));
    }

    // AC-3: recent.json is 0600 after a write, also when it was 0644.
    [Fact]
    public void RecentNotes_Write_IsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        RecentNotesStore recent = new RecentNotesStore(root);
        recent.Write(["Plan"]);
        Assert.Equal(Private, Mode(recent.FilePath));

        File.SetUnixFileMode(recent.FilePath, Readable);
        recent.Write(["Plan", "Ideas"]);
        Assert.Equal(Private, Mode(recent.FilePath));
    }

    // AC-4: Sync on a new root creates context.db as 0600, and no other file is left beside it.
    [Fact]
    public void TagIndex_Sync_NewDatabase_IsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string fresh = Path.Combine(parent, "Fresh");
        NoteStore store = new NoteStore(fresh);
        TagIndex index = new TagIndex(fresh);
        index.Sync(store);

        Assert.Equal(Private, Mode(index.FilePath));
        Assert.Equal(PrivateFolder, Mode(fresh));
        Assert.Equal([TagIndex.FileName], Directory.GetFiles(fresh).Select(f => Path.GetFileName(f)!).ToArray());
    }

    // AC-4: a 0644 context.db is 0600 after the next Sync and keeps its rows.
    [Fact]
    public void TagIndex_Sync_ReadableDatabase_IsTightenedAndKeepsRows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        NoteStore store = new NoteStore(root);
        store.CreateWithText("Plan", "# Plan\n#work\n");
        TagIndex index = new TagIndex(root);
        index.Sync(store);
        File.SetUnixFileMode(index.FilePath, Readable);

        TagIndex reopened = new TagIndex(root);
        reopened.Sync(store);

        Assert.Equal(Private, Mode(reopened.FilePath));
        Assert.Equal(["Plan"], reopened.NotesWithTag("work"));
    }

    // AC-5: the data root and its missing parents inside the temp folder are 0700.
    [Fact]
    public void NoteStore_Create_MissingRootAndParents_AreOwnerOnlyFolders()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string outer = Path.Combine(parent, "a");
        string inner = Path.Combine(outer, "b", "Context");
        new NoteStore(inner).Create("Plan");

        Assert.Equal(PrivateFolder, Mode(outer));
        Assert.Equal(PrivateFolder, Mode(Path.Combine(outer, "b")));
        Assert.Equal(PrivateFolder, Mode(inner));
    }

    // AC-5: a subpage child folder is 0700.
    [Fact]
    public void NoteStore_CreateChild_ChildFolder_IsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        NoteStore store = new NoteStore(root);
        store.Create("Plan");
        store.CreateChild("Plan", "Step");

        Assert.Equal(PrivateFolder, Mode(Path.Combine(root, "Plan")));
    }

    // AC-5: the .attachments folder is 0700 and the stored file 0600.
    [Fact]
    public void NoteAttachments_Add_Folder_IsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        NoteStore store = new NoteStore(root);
        store.Create("Plan");
        AttachmentAdd added = new NoteAttachments(store).Add("Plan", "shot.png", Png);

        Assert.Equal(AttachmentAddKind.Added, added.Kind);
        string folder = Path.Combine(root, ".attachments");
        Assert.Equal(PrivateFolder, Mode(folder));
        Assert.Equal(Private, Mode(Path.Combine(folder, added.Name!)));
    }

    // AC-5: the target folder of a Move into a parent that had no children is 0700.
    [Fact]
    public void NoteStore_Move_IntoChildlessParent_FolderIsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        NoteStore store = new NoteStore(root);
        store.Create("Plan");
        store.Create("Step");
        Assert.NotNull(store.Move("Step", "Plan"));

        Assert.Equal(PrivateFolder, Mode(Path.Combine(root, "Plan")));
        Assert.Equal(Private, Mode(Path.Combine(root, "Plan", "Step.md")));
    }
}
