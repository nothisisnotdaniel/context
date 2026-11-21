using Microsoft.Data.Sqlite;

namespace Context.Tests;

public sealed class WorkItemIndexTests : IDisposable
{
    private static readonly DateTime First = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private readonly string parent;
    private readonly string root;
    private readonly NoteStore store;
    private readonly TagIndex index;

    public WorkItemIndexTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-workitemindex-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(root);
        store = new NoteStore(root);
        index = new TagIndex(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private void WriteNote(string name, string text, int minutes = 0)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, First.AddMinutes(minutes));
    }

    private SqliteConnection Open()
    {
        SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root, TagIndex.FileName),
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private void Execute(string sql)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Scalar(string sql)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    // 280 AC-2: the mentioning notes, sorted, never the item's own page and never a sealed note.
    [Fact]
    public void NotesWithItem_ListsMentioningNotes_NotItsPageOrSealed()
    {
        WriteNote("beta", "work on #1234");
        WriteNote("Alpha", "#1234, and #99");
        WriteNote("gamma", "only #12345 and `#1234`");
        WriteNote("items", "# items\n#1234");
        WriteNote("items/1234", "# 1234\nthis is #1234");
        WriteNote("items/99", "# 99\nsee #1234");
        File.WriteAllText(Path.Combine(root, "Secret.ctxs"), "{\"text\":\"#1234\"}");

        index.Sync(store);

        Assert.Equal(["Alpha", "beta", "items", "items/99"], index.NotesWithItem(1234));
        Assert.Equal(["Alpha"], index.NotesWithItem(99));
        Assert.Empty(index.NotesWithItem(5));
        Assert.Equal(19, TagIndex.ParserVersion);
    }

    // 280 AC-2: an edit adds and removes the row; a deleted note goes too.
    [Fact]
    public void NotesWithItem_FollowsEdits()
    {
        WriteNote("Plan", "nothing yet");
        WriteNote("Other", "#1234");
        index.Sync(store);
        Assert.Equal(["Other"], index.NotesWithItem(1234));

        WriteNote("Plan", "now #1234", minutes: 1);
        index.Sync(store);
        Assert.Equal(["Other", "Plan"], index.NotesWithItem(1234));

        WriteNote("Plan", "no longer", minutes: 2);
        File.Delete(Path.Combine(root, "Other.md"));
        index.Sync(store);
        Assert.Empty(index.NotesWithItem(1234));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM note_items"));
    }

    // 280 AC-2: a database at version 12 (no note_items rows) is re-parsed once, then left alone.
    [Fact]
    public void Version12Database_IsReparsedOnce()
    {
        WriteNote("Plan", "#1234");
        index.Sync(store);
        Execute("DELETE FROM note_items; PRAGMA user_version = 12;");
        Assert.Empty(index.NotesWithItem(1234));

        index.Sync(store);

        Assert.Equal(["Plan"], index.NotesWithItem(1234));
        Assert.Equal(19, Scalar("PRAGMA user_version"));

        // Once re-parsed, an unchanged note is not read again: a row removed by hand stays removed.
        Execute("DELETE FROM note_items;");
        index.Sync(store);
        Assert.Empty(index.NotesWithItem(1234));
    }

    // 280 AC-2: a version 12 database without the table gets it.
    [Fact]
    public void Version12Database_WithoutTable_GetsIt()
    {
        WriteNote("Plan", "#1234");
        index.Sync(store);
        Execute("DROP TABLE note_items; PRAGMA user_version = 12;");

        index.Sync(store);

        Assert.Equal(["Plan"], index.NotesWithItem(1234));
    }

    // The people of the given notes with their spelling.
    [Fact]
    public void MentionsIn_GivesTheNotesPeople()
    {
        WriteNote("A", "#1234 @[Anna] @[Bob]");
        WriteNote("B", "#1234 @[anna]");
        WriteNote("C", "@[Carl]");
        index.Sync(store);

        IReadOnlyList<(string Note, string Spelling)> rows = index.MentionsIn(["A", "B"]);

        Assert.Equal(
            ["A|Anna", "A|Bob", "B|anna"],
            rows.Select(r => r.Note + "|" + r.Spelling).Order(StringComparer.Ordinal));
    }
}
