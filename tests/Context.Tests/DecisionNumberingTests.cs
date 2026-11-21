using System.Text;

namespace Context.Tests;

// 349 AC-2, AC-3: numbering a note after a write that was not a save (capture, import, the day's end) through
// DecisionNumbering.NumberWritten; the folder id is asked for only when the note holds a !d marker.
public sealed class DecisionNumberingTests : IDisposable
{
    private static readonly Guid Own = new("11111111-2222-3333-4444-555555555555");

    private readonly string root = Path.Combine(Path.GetTempPath(), "context-decision-numbering-" + Guid.NewGuid().ToString("N"));
    private readonly NoteStore store;
    private readonly TagIndex index;
    private readonly DecisionFolder folder = new("ab12", Own, true);

    public DecisionNumberingTests()
    {
        Directory.CreateDirectory(root);
        store = new NoteStore(root);
        index = new TagIndex(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void Write(string name, string text) => File.WriteAllBytes(Path.Combine(root, name + ".md"), Encoding.UTF8.GetBytes(text));

    private string Read(string name) => File.ReadAllText(Path.Combine(root, name + ".md"));

    // A note without a marker never asks for the folder id, so the write creates no decision-folder.json.
    [Fact]
    public void NumberWritten_NoMarker_NeverAsksForFolder()
    {
        Write("Plan", "# Plan\nno decision here\n");
        int asked = 0;

        bool wrote = DecisionNumbering.NumberWritten(index, store, () => { asked++; return folder; }, "Plan", out DecisionEdits? numbered);

        Assert.False(wrote);
        Assert.Null(numbered);
        Assert.Equal(0, asked);
        Assert.Equal("# Plan\nno decision here\n", Read("Plan"));
    }

    // A note with a marker asks once and is numbered with the folder's prefix in a second write.
    [Fact]
    public void NumberWritten_Marker_NumbersWithFolder()
    {
        Write("Plan", "# Plan\n!d ship it\n");
        int asked = 0;

        bool wrote = DecisionNumbering.NumberWritten(index, store, () => { asked++; return folder; }, "Plan", out DecisionEdits? numbered);

        Assert.True(wrote);
        Assert.Equal(1, asked);
        Assert.Equal(["ab12-1"], numbered!.Set.Select(s => s.New));
        Assert.Equal($"# Plan\ndecision-folder:: {Own:D}\n!d ab12-1 ship it\n", Read("Plan"));
    }

    // No folder id (unreadable decision-folder.json): nothing is numbered, the note stays as written.
    [Fact]
    public void NumberWritten_NoFolder_LeavesNote()
    {
        Write("Plan", "# Plan\n!d ship it\n");

        Assert.False(DecisionNumbering.NumberWritten(index, store, () => null, "Plan", out DecisionEdits? numbered));
        Assert.Null(numbered);
        Assert.Equal("# Plan\n!d ship it\n", Read("Plan"));
    }
}
