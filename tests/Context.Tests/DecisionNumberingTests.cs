using System.Text;

namespace Context.Tests;

// 349 AC-2, AC-3: numbering a note after a write that was not a save (capture, import, the day's end) through
// DecisionNumbering.NumberWritten; the folder id is asked for only when the note holds a !d marker.
// 351 AC-1 to AC-3: the note's previous bytes are kept as a history version before the numbered write.
public sealed class DecisionNumberingTests : IDisposable
{
    private static readonly Guid Own = new("11111111-2222-3333-4444-555555555555");

    private readonly string root = Path.Combine(Path.GetTempPath(), "context-decision-numbering-" + Guid.NewGuid().ToString("N"));
    private readonly NoteStore store;
    private readonly TagIndex index;
    private readonly DecisionFolder folder = new("ab12", Own, true);
    private readonly NoteHistory history;
    private DateTime clock = new(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);

    public DecisionNumberingTests()
    {
        Directory.CreateDirectory(root);
        store = new NoteStore(root);
        index = new TagIndex(root);
        history = new NoteHistory(store, () => clock);
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

        bool wrote = DecisionNumbering.NumberWritten(index, store, history, () => { asked++; return folder; }, "Plan", out DecisionEdits? numbered);

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

        bool wrote = DecisionNumbering.NumberWritten(index, store, history, () => { asked++; return folder; }, "Plan", out DecisionEdits? numbered);

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

        Assert.False(DecisionNumbering.NumberWritten(index, store, history, () => null, "Plan", out DecisionEdits? numbered));
        Assert.Null(numbered);
        Assert.Equal("# Plan\n!d ship it\n", Read("Plan"));
    }

    private NoteInfo Note(string name) => Assert.IsType<NoteInfo>(store.TryFind(name, out NoteInfo? found) ? found : null);

    private byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(root, name + ".md"));

    // 351 AC-1: a bare !d is numbered (its leading 3 read as a number, the case the history undoes), and exactly one version holds the file's bytes before the call, BOM and CRLF too.
    [Fact]
    public void NumberWritten_Marker_KeepsPreviousBytesAsVersion()
    {
        byte[] before = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("# Plan\r\n!d 3 people hired\r\n")];
        File.WriteAllBytes(Path.Combine(root, "Plan.md"), before);

        Assert.True(DecisionNumbering.NumberWritten(index, store, history, folder, "Plan"));

        Assert.Equal($"# Plan\r\ndecision-folder:: {Own:D}\r\n!d ab12-1 people hired\r\n", Read("Plan"));
        NoteVersion version = Assert.Single(history.Versions(Note("Plan")));
        Assert.True(history.TryRead(Note("Plan"), version.Id, out byte[]? kept));
        Assert.Equal(before, kept);
    }

    // 351 AC-1: a second numbering keeps one more version, the bytes as they were before that call.
    [Fact]
    public void NumberWritten_Twice_KeepsOneVersionEach()
    {
        Write("Plan", "# Plan\n!d ship it\n");
        Assert.True(DecisionNumbering.NumberWritten(index, store, history, folder, "Plan"));
        File.AppendAllText(Path.Combine(root, "Plan.md"), "\n!d hire\n");
        byte[] before = Bytes("Plan");
        clock = clock.AddMinutes(1);

        Assert.True(DecisionNumbering.NumberWritten(index, store, history, folder, "Plan"));

        IReadOnlyList<NoteVersion> versions = history.Versions(Note("Plan"));
        Assert.Equal(2, versions.Count);
        Assert.True(history.TryRead(Note("Plan"), versions[0].Id, out byte[]? kept));
        Assert.Equal(before, kept);
    }

    // 351 AC-2: a numbering that changes nothing keeps no version and leaves the bytes.
    [Theory]
    [InlineData("# Plan\nno decision here\n", true)]
    [InlineData("# Plan\n!d ship it\n", false)]
    [InlineData("# Plan\ndecision-folder:: 11111111-2222-3333-4444-555555555555\n!d ab12-1 ship it\n", true)]
    [InlineData("# Plan\ndecision-folder:: 99999999-2222-3333-4444-555555555555\n!d ship it\n", true)]
    public void NumberWritten_NothingChanged_KeepsNoVersion(string text, bool withFolder)
    {
        Write("Plan", text);
        byte[] before = Bytes("Plan");

        Assert.False(DecisionNumbering.NumberWritten(index, store, history, () => withFolder ? folder : null, "Plan", out _));

        Assert.Equal(before, Bytes("Plan"));
        Assert.Empty(history.Versions(Note("Plan")));
        Assert.False(Directory.Exists(Path.Combine(root, NoteHistory.FolderName, "Plan")));
    }

    // 351 AC-3: when the history can't be kept (.history/Plan is a file), nothing is written and nothing throws.
    [Fact]
    public void NumberWritten_HistoryFails_LeavesNote()
    {
        Write("Plan", "# Plan\n!d ship it\n");
        byte[] before = Bytes("Plan");
        Directory.CreateDirectory(Path.Combine(root, NoteHistory.FolderName));
        File.WriteAllText(Path.Combine(root, NoteHistory.FolderName, "Plan"), "not a folder");

        Assert.False(DecisionNumbering.NumberWritten(index, store, history, folder, "Plan"));

        Assert.Equal(before, Bytes("Plan"));
        Assert.Equal("not a folder", File.ReadAllText(Path.Combine(root, NoteHistory.FolderName, "Plan")));
    }
}
