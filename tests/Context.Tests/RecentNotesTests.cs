using System.Text;

namespace Context.Tests;

// 240: the pure steps of the recently opened list and recent.json.
public sealed class RecentNotesTests : IDisposable
{
    private readonly string parent;
    private readonly string root;

    public RecentNotesTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-recent-" + Guid.NewGuid().ToString("N"));
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

    private string FilePath => Path.Combine(root, RecentNotesStore.FileName);

    // AC-2: a new name goes first; a listed name moves to the front and appears once.
    [Fact]
    public void Touch_AddsFirst_MovesListedToTheFront()
    {
        Assert.Equal(["A"], RecentNotes.Touch([], "A"));
        Assert.Equal(["B", "A"], RecentNotes.Touch(["A"], "B"));
        Assert.Equal(["A", "C", "B"], RecentNotes.Touch(["C", "A", "B"], "A"));
        Assert.Equal(["A", "C"], RecentNotes.Touch(["A", "C"], "A"));
    }

    // AC-2: names compare ordinally, so a different case is another note.
    [Fact]
    public void Touch_Ordinal()
    {
        Assert.Equal(["plan", "Plan"], RecentNotes.Touch(["Plan"], "plan"));
    }

    // AC-2: an 11th distinct name pushes the oldest one out.
    [Fact]
    public void Touch_Eleventh_DropsTheOldest()
    {
        IReadOnlyList<string> list = [];
        for (int i = 1; i <= 11; i++)
        {
            list = RecentNotes.Touch(list, $"N{i}");
        }

        Assert.Equal(RecentNotes.MaxCount, list.Count);
        Assert.Equal("N11", list[0]);
        Assert.Equal("N2", list[^1]);
        Assert.DoesNotContain("N1", list);
    }

    // AC-4: the note and its subpages change prefix in place; a name that only starts the same way stays.
    [Fact]
    public void Renamed_NoteAndSubpages_InPlace()
    {
        Assert.Equal(
            ["X", "D", "Ab", "D/Sub", "D/Sub/Deep"],
            RecentNotes.Renamed(["X", "A", "Ab", "A/Sub", "A/Sub/Deep"], "A", "D"));
        Assert.Equal(["C/D", "C", "C/D/Sub"], RecentNotes.Renamed(["D", "C", "D/Sub"], "D", "C/D"));
        Assert.Equal(["X"], RecentNotes.Renamed(["X"], "A", "D"));
    }

    // AC-4: a rename onto a listed name keeps the first one.
    [Fact]
    public void Renamed_Duplicate_FirstStays()
    {
        Assert.Equal(["B", "X"], RecentNotes.Renamed(["A", "X", "B"], "A", "B"));
        Assert.Equal(["X", "B"], RecentNotes.Renamed(["X", "B", "A"], "A", "B"));
    }

    // AC-4: removing drops the note and its subpages, nothing else.
    [Fact]
    public void Removed_NoteAndSubpages()
    {
        Assert.Equal(["Ab", "B"], RecentNotes.Removed(["A/Sub", "Ab", "A", "B", "A/Sub/Deep"], "A"));
        Assert.Equal(["A"], RecentNotes.Removed(["A", "A/Sub"], "A/Sub"));
    }

    [Theory]
    [InlineData("Plan", true)]
    [InlineData("Meetings/Meeting 1/Notes", true)]
    [InlineData("Café", true)]
    [InlineData("", false)]
    [InlineData("a\\b", false)]
    [InlineData("a:b", false)]
    [InlineData("/Plan", false)]
    [InlineData("Plan/", false)]
    [InlineData("a//b", false)]
    [InlineData("a\u0007", false)]
    public void IsValidName(string name, bool valid)
    {
        Assert.Equal(valid, RecentNotes.IsValidName(name));
    }

    // AC-5: written and read back in order, as UTF-8 without BOM with non-ASCII as it is, on one line.
    [Fact]
    public void Store_RoundTrip_ExactBytes()
    {
        RecentNotesStore store = new RecentNotesStore(root);

        store.Write(["Café", "Meetings/Notes", "Plan"]);

        Assert.Equal(
            Encoding.UTF8.GetBytes("{\"recent\":[\"Café\",\"Meetings/Notes\",\"Plan\"]}\n"),
            File.ReadAllBytes(FilePath));
        Assert.Equal(["Café", "Meetings/Notes", "Plan"], store.Read());
        Assert.Equal([RecentNotesStore.FileName], Directory.GetFiles(root).Select(f => Path.GetFileName(f)!).ToArray());
    }

    // AC-5: an empty list is written as an empty array; a write replaces whatever the file held.
    [Fact]
    public void Store_Write_ReplacesTheFile()
    {
        File.WriteAllText(FilePath, "not json at all");
        RecentNotesStore store = new RecentNotesStore(root);

        store.Write([]);

        Assert.Equal("{\"recent\":[]}\n", File.ReadAllText(FilePath));
        Assert.Empty(store.Read());
    }

    // AC-6: a missing file reads as empty; nothing is created.
    [Fact]
    public void Store_Missing_Empty()
    {
        Assert.Empty(new RecentNotesStore(root).Read());
        Assert.False(File.Exists(FilePath));
    }

    // AC-6: broken or wrong-shape files read as empty and are left as they are.
    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"recent\":null}")]
    [InlineData("{\"recent\":\"Plan\"}")]
    [InlineData("{\"recent\":[1]}")]
    [InlineData("{\"recent\":[null]}")]
    [InlineData("{\"recent\":[[\"Plan\"]]}")]
    [InlineData("{\"recent\":[\"Plan\",\"Plan\"]}")]
    [InlineData("{\"recent\":[\"\"]}")]
    [InlineData("{\"recent\":[\"a\\\\b\"]}")]
    [InlineData("{\"recent\":[\"a/\"]}")]
    [InlineData("{\"recent\":[\"A\"],\"other\":1}")]
    [InlineData("{\"Recent\":[\"A\"]}")]
    [InlineData("{\"recent\":[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\",\"8\",\"9\",\"10\",\"11\"]}")]
    public void Store_Broken_Empty(string json)
    {
        File.WriteAllText(FilePath, json);

        Assert.Empty(new RecentNotesStore(root).Read());
        Assert.Equal(json, File.ReadAllText(FilePath));
    }

    // AC-6: a file over 64 KiB reads as empty.
    [Fact]
    public void Store_Oversized_Empty()
    {
        string padding = new string(' ', (int)RecentNotesStore.MaxBytes);
        File.WriteAllText(FilePath, "{\"recent\":[\"Plan\"]}" + padding);

        Assert.Empty(new RecentNotesStore(root).Read());
    }

    // AC-5: the full ten names are read back.
    [Fact]
    public void Store_TenNames_Read()
    {
        string[] names = Enumerable.Range(1, 10).Select(i => $"N{i}").ToArray();
        new RecentNotesStore(root).Write(names);

        Assert.Equal(names, new RecentNotesStore(root).Read());
    }

    // AC-7: a missing root is not created; the write throws for the caller to swallow and leaves no temp file.
    [Fact]
    public void Store_MissingRoot_ThrowsAndCreatesNothing()
    {
        string missing = Path.Combine(parent, "Missing");

        Assert.ThrowsAny<IOException>(() => new RecentNotesStore(missing).Write(["Plan"]));
        Assert.False(Directory.Exists(missing));
    }
}
