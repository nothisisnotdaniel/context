using System.Text;

namespace Context.Tests;

public sealed class DoneLogTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 11, 19);

    private readonly string parent;
    private readonly string root;
    private readonly NoteStore store;

    public DoneLogTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-donelog-" + Guid.NewGuid().ToString("N"));
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

    private string DonePath => Path.Combine(root, "done.md");

    // AC-1, AC-2
    [Fact]
    public void Line_WithAndWithoutDue()
    {
        Assert.Equal("- 2026-11-19 Send report #work (due 2026-11-18)", DoneLog.Line(Day, "Send report #work", new DateOnly(2026, 11, 18)));
        Assert.Equal("- 2026-11-19 call Anna", DoneLog.Line(Day, "call Anna", null));
    }

    // AC-1: a missing done note is created with the heading and the line.
    [Fact]
    public void Append_MissingNote_CreatesIt()
    {
        Assert.Equal(DoneLogResult.Appended, DoneLog.Append(store, Day, "Send report #work", new DateOnly(2026, 11, 18)));

        Assert.Equal("# done\n- 2026-11-19 Send report #work (due 2026-11-18)\n", File.ReadAllText(DonePath));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    // AC-5: control characters become spaces; the text is trimmed.
    [Fact]
    public void Append_CleansControlCharacters()
    {
        DoneLog.Append(store, Day, " a\nb\tc\u001b[31m ", null);

        Assert.Equal("# done\n- 2026-11-19 a b c [31m\n", File.ReadAllText(DonePath));
    }

    // AC-5: a missing final newline is added first; CRLF and the BOM are kept (the store writes the new line in the
    // file's CRLF style), in a note found in any case.
    [Fact]
    public void Append_KeepsBomAndCrlf_AddsMissingNewline()
    {
        string path = Path.Combine(root, "Done.md");
        byte[] before = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("# Done\r\n- 2026-11-01 old")).ToArray();
        File.WriteAllBytes(path, before);

        Assert.Equal(DoneLogResult.Appended, DoneLog.Append(store, Day, "new", null));

        byte[] expected = before.Concat(Encoding.UTF8.GetBytes("\r\n- 2026-11-19 new\r\n")).ToArray();
        Assert.Equal(expected, File.ReadAllBytes(path));
        Assert.Single(store.List());
    }

    // AC-6: the note changes on every attempt; nothing of ours is written and the other writer's bytes stay.
    [Fact]
    public void Append_ChangedEveryTime_ChangedOnDisk()
    {
        File.WriteAllText(DonePath, "# done\n");
        int calls = 0;

        DoneLogResult result = DoneLog.Append(store, Day, "mine", null, () =>
        {
            calls++;
            File.AppendAllText(DonePath, $"- theirs {calls}\n");
        });

        Assert.Equal(DoneLogResult.ChangedOnDisk, result);
        Assert.Equal(Inbox.MaxRetries + 1, calls);
        string expected = "# done\n" + string.Concat(Enumerable.Range(1, calls).Select(i => $"- theirs {i}\n"));
        Assert.Equal(expected, File.ReadAllText(DonePath));
    }

    // AC-6: a note that would grow past MaxEditBytes is left alone.
    [Fact]
    public void Append_TooLarge_WritesNothing()
    {
        byte[] full = Encoding.UTF8.GetBytes("# done\n" + new string('a', (int)NoteStore.MaxEditBytes - 8) + "\n");
        File.WriteAllBytes(DonePath, full);

        Assert.Equal(DoneLogResult.TooLarge, DoneLog.Append(store, Day, "x", null));

        Assert.Equal(full, File.ReadAllBytes(DonePath));
    }

    // AC-6: a done note that can't be read gives Failed, never an exception.
    [Fact]
    public void Append_Unreadable_Failed()
    {
        using (FileStream big = File.Create(DonePath))
        {
            big.SetLength(NoteStore.MaxNoteBytes + 1);
        }

        DoneLogResult result = DoneLog.Append(store, Day, "x", null);

        Assert.NotEqual(DoneLogResult.Appended, result);
        Assert.Equal(NoteStore.MaxNoteBytes + 1, new FileInfo(DonePath).Length);
    }

    // AC-7: only real entry lines outside fences count; the due part is split off.
    [Fact]
    public void Parse_TakesEntryLinesOnly()
    {
        string markdown = "# done\n"
            + "- 2026-11-19 Send report #work (due 2026-11-18)\n"
            + "free text\n"
            + "## heading\n"
            + "```\n- 2026-11-10 fenced\n```\n"
            + "- 2026-02-30 bad date\n"
            + "- 2026-11-12 keeps (due 2026-13-01)\n"
            + "- 2026-11-13  (due 2026-11-01)\n"
            + "- 2026-11-14 call Anna\r\n";

        IReadOnlyList<DoneEntry> entries = DoneLog.Parse(markdown);

        Assert.Equal(
            [
                (new DateOnly(2026, 11, 19), "Send report #work", (DateOnly?)new DateOnly(2026, 11, 18), 2),
                (new DateOnly(2026, 11, 12), "keeps (due 2026-13-01)", null, 9),
                (new DateOnly(2026, 11, 13), "(due 2026-11-01)", null, 10),
                (new DateOnly(2026, 11, 14), "call Anna", null, 11),
            ],
            entries.Select(e => (e.Date, e.Text, e.Due, e.Line)));
    }

    // AC-7: a missing done note reads as no entries.
    [Fact]
    public void Read_Missing_Empty()
    {
        Assert.Empty(DoneLog.Read(store));
    }
}
