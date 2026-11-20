using System.Text;

namespace Context.Tests;

// 278: the pure steps of the pinned list and pinned.json, each test in its own temp folder.
public sealed class PinnedNotesTests : IDisposable
{
    private readonly string parent;
    private readonly string root;

    public PinnedNotesTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-pinned-" + Guid.NewGuid().ToString("N"));
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

    private string FilePath => Path.Combine(root, PinnedNotesStore.FileName);

    // AC-1: a new name goes at the end; a listed name leaves the list as it is (ordinal, so another case is new).
    [Fact]
    public void Pin_AppendsAtTheEnd_ListedUnchanged()
    {
        Assert.Equal(["A"], PinnedNotes.Pin([], "A"));
        Assert.Equal(["A", "B"], PinnedNotes.Pin(["A"], "B"));
        Assert.Equal(["C", "A", "B"], PinnedNotes.Pin(["C", "A", "B"], "A"));
        Assert.Equal(["Plan", "plan"], PinnedNotes.Pin(["Plan"], "plan"));
    }

    // AC-1: with 20 names a 21st pin leaves the list unchanged.
    [Fact]
    public void Pin_Full_Unchanged()
    {
        IReadOnlyList<string> list = [];
        for (int i = 1; i <= PinnedNotes.MaxCount; i++)
        {
            list = PinnedNotes.Pin(list, $"N{i}");
        }

        Assert.Equal(20, list.Count);
        IReadOnlyList<string> after = PinnedNotes.Pin(list, "N21");
        Assert.Equal(list, after);
        Assert.DoesNotContain("N21", after);
    }

    // AC-1: unpin removes only that name; its subpages and names that start the same way stay.
    [Fact]
    public void Unpin_RemovesTheName()
    {
        Assert.Equal(["Ab", "A/Sub", "B"], PinnedNotes.Unpin(["Ab", "A", "A/Sub", "B"], "A"));
        Assert.Equal(["A"], PinnedNotes.Unpin(["A"], "X"));
    }

    // AC-1: renaming moves the note and its subpages to the new prefix in place; a later duplicate is dropped.
    [Fact]
    public void Renamed_NoteAndSubpages_InPlace()
    {
        Assert.Equal(
            ["X", "D", "Ab", "D/Sub", "D/Sub/Deep"],
            PinnedNotes.Renamed(["X", "A", "Ab", "A/Sub", "A/Sub/Deep"], "A", "D"));
        Assert.Equal(["C/D", "C", "C/D/Sub"], PinnedNotes.Renamed(["D", "C", "D/Sub"], "D", "C/D"));
        Assert.Equal(["B", "X"], PinnedNotes.Renamed(["A", "X", "B"], "A", "B"));
    }

    // AC-1: removing drops the note and its subpages, keeping the order of the rest.
    [Fact]
    public void Removed_NoteAndSubpages()
    {
        Assert.Equal(["Ab", "B"], PinnedNotes.Removed(["A/Sub", "Ab", "A", "B", "A/Sub/Deep"], "A"));
        Assert.Equal(["A"], PinnedNotes.Removed(["A", "A/Sub"], "A/Sub"));
    }

    [Theory]
    [InlineData("Plan", true)]
    [InlineData("Meetings/Notes", true)]
    [InlineData("", false)]
    [InlineData("a\\b", false)]
    [InlineData("Plan/", false)]
    public void IsValidName(string name, bool valid)
    {
        Assert.Equal(valid, PinnedNotes.IsValidName(name));
    }

    // AC-2: written and read back in order, as UTF-8 without BOM with non-ASCII as it is; no temp file is left.
    [Fact]
    public void Store_RoundTrip_ExactBytes()
    {
        PinnedNotesStore store = new PinnedNotesStore(root);

        store.Write(["Plan", "Café", "Meetings/Notes"]);

        Assert.Equal(
            Encoding.UTF8.GetBytes("{\"pinned\":[\"Plan\",\"Café\",\"Meetings/Notes\"]}\n"),
            File.ReadAllBytes(FilePath));
        Assert.Equal(["Plan", "Café", "Meetings/Notes"], store.Read());
        Assert.Equal([PinnedNotesStore.FileName], Directory.GetFiles(root).Select(f => Path.GetFileName(f)!).ToArray());
    }

    // AC-2: the full twenty names are read back; a write replaces a broken file.
    [Fact]
    public void Store_TwentyNames_Read_ReplacesBroken()
    {
        File.WriteAllText(FilePath, "not json");
        string[] names = Enumerable.Range(1, 20).Select(i => $"N{i}").ToArray();

        new PinnedNotesStore(root).Write(names);

        Assert.Equal(names, new PinnedNotesStore(root).Read());
    }

    // AC-2: a missing file reads as empty; nothing is created.
    [Fact]
    public void Store_Missing_Empty()
    {
        Assert.Empty(new PinnedNotesStore(root).Read());
        Assert.False(File.Exists(FilePath));
    }

    // AC-2: broken or wrong-shape files read as empty, never throw, and are left as they are.
    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"pinned\":null}")]
    [InlineData("{\"pinned\":\"Plan\"}")]
    [InlineData("{\"pinned\":[1]}")]
    [InlineData("{\"recent\":[\"Plan\"]}")]
    [InlineData("{\"Pinned\":[\"Plan\"]}")]
    [InlineData("{\"pinned\":[\"Plan\"],\"other\":1}")]
    [InlineData("{\"pinned\":[\"Plan\",\"Plan\"]}")]
    [InlineData("{\"pinned\":[\"\"]}")]
    [InlineData("{\"pinned\":[\"a\\\\b\"]}")]
    [InlineData("{\"pinned\":[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\",\"8\",\"9\",\"10\",\"11\",\"12\",\"13\",\"14\",\"15\",\"16\",\"17\",\"18\",\"19\",\"20\",\"21\"]}")]
    public void Store_Broken_Empty(string json)
    {
        File.WriteAllText(FilePath, json);

        Assert.Empty(new PinnedNotesStore(root).Read());
        Assert.Equal(json, File.ReadAllText(FilePath));
    }

    // AC-2: a file over 64 KiB reads as empty.
    [Fact]
    public void Store_Oversized_Empty()
    {
        string padding = new string(' ', (int)PinnedNotesStore.MaxBytes);
        File.WriteAllText(FilePath, "{\"pinned\":[\"Plan\"]}" + padding);

        Assert.Empty(new PinnedNotesStore(root).Read());
    }

    // AC-2: a missing root is not created; the write throws for the caller to handle and leaves nothing behind.
    [Fact]
    public void Store_MissingRoot_ThrowsAndCreatesNothing()
    {
        string missing = Path.Combine(parent, "Missing");

        Assert.ThrowsAny<IOException>(() => new PinnedNotesStore(missing).Write(["Plan"]));
        Assert.False(Directory.Exists(missing));
    }

    // AC-2: pinned.json as a folder: the write throws and no temp file stays in the root.
    [Fact]
    public void Store_FileIsAFolder_ThrowsAndLeavesNoTempFile()
    {
        Directory.CreateDirectory(FilePath);

        Assert.ThrowsAny<Exception>(() => new PinnedNotesStore(root).Write(["Plan"]));
        Assert.Empty(Directory.GetFiles(root));
        Assert.Empty(new PinnedNotesStore(root).Read());
    }
}
