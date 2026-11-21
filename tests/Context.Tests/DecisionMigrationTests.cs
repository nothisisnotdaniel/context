using System.Text;

namespace Context.Tests;

// 349 AC-10: the one-time move of job 343's #!d markers to !d <prefix>-<n> markers.
public sealed class DecisionMigrationTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Own = new("11111111-2222-3333-4444-555555555555");

    private static readonly Guid Other = new("99999999-8888-7777-6666-555555555555");

    private static readonly string Line = "decision-folder:: " + Own.ToString("D");

    private readonly string root = Path.Combine(Path.GetTempPath(), "context-decision-migration-" + Guid.NewGuid().ToString("N"));
    private readonly NoteStore store;
    private readonly NoteHistory history;
    private readonly TagIndex index;
    private readonly DecisionFolder folder;

    public DecisionMigrationTests()
    {
        Directory.CreateDirectory(root);
        store = new NoteStore(root);
        history = new NoteHistory(store, () => Now);
        index = new TagIndex(root);
        Queue<int> draws = new([10, 11, 1, 2]);
        folder = DecisionFolder.Load(root, _ => draws.Dequeue(), () => Own)!;
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

    private NoteInfo Note(string name) => store.List().Single(n => n.Name == name);

    private DecisionMigrationReport Run(Action<string>? beforeSave = null) => DecisionMigration.Run(index, store, history, folder, beforeSave);

    // AC-10: #!d<n> keeps its number with the prefix, a bare #!d gets a fresh number above every held one, references
    // and every other byte (CRLF, BOM) stay, the folder line is added, the old bytes are a version, the flag is set.
    [Fact]
    public void Run_ConvertsMarkers_KeepsReferences_AndHistory()
    {
        string before = "﻿# Plan\r\n#!d1 We ship.\r\n\r\n- #!d2 second\r\n\r\n#!d bare one\r\n\r\nsee #!d1 and `#!d2`\r\n";
        Write("Plan", before);
        Write("Plain", "no decisions\n");

        DecisionMigrationReport report = Run();

        Assert.True(report.Ran);
        Assert.Equal(1, report.Notes);
        Assert.Empty(report.Failed);
        Assert.Equal(
            $"﻿# Plan\r\n{Line}\r\n!d ab12-1 We ship.\r\n\r\n- !d ab12-2 second\r\n\r\n!d ab12-3 bare one\r\n\r\nsee #!d1 and `#!d2`\r\n",
            Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "Plan.md"))));
        Assert.Equal("no decisions\n", Read("Plain"));
        NoteVersion version = Assert.Single(history.Versions(Note("Plan")));
        Assert.True(history.TryRead(Note("Plan"), version.Id, out byte[]? kept));
        Assert.Equal(Encoding.UTF8.GetBytes(before), kept);
        Assert.Empty(history.Versions(Note("Plain")));
        Assert.True(DecisionFolder.Read(root)!.Migrated);
    }

    // AC-10: bare markers in two notes get distinct numbers that no note holds.
    [Fact]
    public void Run_BareMarkersInTwoNotes_DistinctNumbers()
    {
        Write("A", "#!d5 five\n\n#!d a\n");
        Write("B", "#!d b\n");

        Run();

        HashSet<int> numbers =
        [
            .. DecisionParser.Parse(Read("A")).Select(d => d.Number!.Value),
            .. DecisionParser.Parse(Read("B")).Select(d => d.Number!.Value),
        ];
        Assert.Equal(3, numbers.Count);
        Assert.Contains(5, numbers);
        Assert.All(DecisionParser.Parse(Read("A")).Concat(DecisionParser.Parse(Read("B"))), d => Assert.Equal("ab12", d.Prefix));
    }

    // AC-10: a note of another folder is skipped unchanged and does not stop the flag.
    [Fact]
    public void Run_ForeignNote_Skipped()
    {
        string foreign = $"decision-folder:: {Other:D}\n#!d3 theirs\n";
        Write("Foreign", foreign);

        DecisionMigrationReport report = Run();

        Assert.Equal(0, report.Notes);
        Assert.Equal(foreign, Read("Foreign"));
        Assert.True(DecisionFolder.Read(root)!.Migrated);
    }

    // AC-10: a note changed between read and write keeps the new bytes, counts as failed, and the flag stays unset so
    // the next start runs again.
    [Fact]
    public void Run_ChangedMidPass_NotWritten_NotMigrated()
    {
        Write("Plan", "#!d1 x\n");

        DecisionMigrationReport report = Run(name => Write(name, "#!d1 changed\n"));

        Assert.Equal(["Plan"], report.Failed);
        Assert.Equal("#!d1 changed\n", Read("Plan"));
        Assert.False(DecisionFolder.Read(root)!.Migrated);
    }

    // AC-10: a note that is not strict UTF-8 is left byte for byte and the flag stays unset.
    [Fact]
    public void Run_InvalidUtf8_Left()
    {
        byte[] bad = [.. Encoding.UTF8.GetBytes("#!d1 x "), 0xFF, (byte)'\n'];
        File.WriteAllBytes(Path.Combine(root, "Bad.md"), bad);

        DecisionMigrationReport report = Run();

        Assert.Equal(["Bad"], report.Failed);
        Assert.Equal(bad, File.ReadAllBytes(Path.Combine(root, "Bad.md")));
        Assert.False(DecisionFolder.Read(root)!.Migrated);
    }

    // AC-10: once migrated, a second run reads and writes nothing, even with old markers present.
    [Fact]
    public void Run_Twice_SecondIsNoOp()
    {
        Write("Plan", "#!d1 x\n");
        Run();
        DecisionFolder migrated = DecisionFolder.Read(root)!;
        Write("Late", "#!d7 written by an old version\n");

        DecisionMigrationReport second = DecisionMigration.Run(index, store, history, migrated);

        Assert.False(second.Ran);
        Assert.Equal("#!d7 written by an old version\n", Read("Late"));
    }

    // AC-10: an already migrated note (a rerun after a failure) is not changed again.
    [Fact]
    public void Run_AlreadyConvertedNote_Unchanged()
    {
        string done = $"{Line}\n!d ab12-1 x\n\nsee #!d1\n";
        Write("Plan", done);

        DecisionMigrationReport report = Run();

        Assert.Equal(0, report.Notes);
        Assert.Equal(done, Read("Plan"));
        Assert.Empty(history.Versions(Note("Plan")));
    }

    // AC-10: a sensitive note is never opened or changed.
    [Fact]
    public void Run_SensitiveNote_Untouched()
    {
        byte[] sealedBytes = Encoding.UTF8.GetBytes("#!d1 not really sealed");
        File.WriteAllBytes(Path.Combine(root, "Secret" + NoteStore.SensitiveExtension), sealedBytes);

        Run();

        Assert.Equal(sealedBytes, File.ReadAllBytes(Path.Combine(root, "Secret" + NoteStore.SensitiveExtension)));
    }

    // The report never prints note names.
    [Fact]
    public void Report_ToString_IsTypeName()
    {
        Assert.Equal(nameof(DecisionMigrationReport), new DecisionMigrationReport(true, 1, ["secret"]).ToString());
    }
}
