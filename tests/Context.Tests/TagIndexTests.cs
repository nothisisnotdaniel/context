using Microsoft.Data.Sqlite;

namespace Context.Tests;

public sealed class TagIndexTests : IDisposable
{
    private readonly string parent;
    private readonly string root;
    private readonly NoteStore store;
    private readonly TagIndex index;

    public TagIndexTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-tagindex-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
        store = new NoteStore(root);
        index = new TagIndex(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            foreach (string file in Directory.GetFiles(parent, "*", SearchOption.AllDirectories))
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            Directory.Delete(parent, recursive: true);
        }
    }

    private string DbPath => Path.Combine(root, TagIndex.FileName);

    private void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".md"), text);
    }

    /// <summary>Every (note, tag) row in the database, read directly.</summary>
    private List<(string Note, string Tag)> Rows()
    {
        string connectionString = new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString();
        using SqliteConnection connection = new SqliteConnection(connectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT note, tag FROM note_tags ORDER BY note, tag";
        using SqliteDataReader reader = command.ExecuteReader();
        List<(string, string)> rows = [];
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }

    private SqliteConnection Open()
    {
        string connectionString = new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString();
        SqliteConnection connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private long UserVersion()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return (long)command.ExecuteScalar()!;
    }

    // AC-3
    [Fact]
    public void Sync_CreatesFolderAndDatabase()
    {
        index.Sync(store);

        Assert.True(File.Exists(DbPath));
        Assert.Empty(Rows());
        Assert.Empty(index.Tags());
    }

    // AC-3
    [Fact]
    public void Sync_StoresExactlyTheCurrentPairs()
    {
        WriteNote("A", "# A\n#work #home");
        WriteNote("B", "#Work");
        File.WriteAllText(Path.Combine(root, "C.txt"), "#ignored");

        index.Sync(store);

        Assert.Equal([("A", "home"), ("A", "work"), ("B", "work")], Rows());
        Assert.Equal(
            [new TagCount("home", 1), new TagCount("work", 2)],
            index.Tags());
        Assert.Equal(["A", "B"], index.NotesWithTag("work"));
        Assert.Equal(["A", "B"], index.NotesWithTag("#WORK"));
        Assert.Empty(index.NotesWithTag("missing"));
    }

    // AC-3
    [Fact]
    public void Sync_RemovesDeletedNotesAndRemovedTags()
    {
        WriteNote("A", "#work #home");
        WriteNote("B", "#work");
        index.Sync(store);

        File.Delete(Path.Combine(root, "B.md"));
        string a = Path.Combine(root, "A.md");
        DateTime before = File.GetLastWriteTimeUtc(a);
        File.WriteAllText(a, "#home");
        File.SetLastWriteTimeUtc(a, before.AddSeconds(5));
        index.Sync(store);

        Assert.Equal([("A", "home")], Rows());
        Assert.Null(index.FindNote("B"));
    }

    // AC-3
    [Fact]
    public void Sync_UnchangedTime_DoesNotReparse()
    {
        WriteNote("A", "#old");
        string a = Path.Combine(root, "A.md");
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(a, time);
        index.Sync(store);

        File.WriteAllText(a, "#new");
        File.SetLastWriteTimeUtc(a, time);
        index.Sync(store);
        Assert.Equal([("A", "old")], Rows());

        File.SetLastWriteTimeUtc(a, time.AddSeconds(1));
        index.Sync(store);
        Assert.Equal([("A", "new")], Rows());
    }

    // AC-4
    [Fact]
    public void Sync_UnreadableNote_IsSkipped_OthersSync()
    {
        WriteNote("A", "#work");
        WriteNote("B", "#work");
        index.Sync(store);
        string b = Path.Combine(root, "B.md");
        File.WriteAllText(b, "#work #secret");
        File.SetLastWriteTimeUtc(b, DateTime.UtcNow.AddMinutes(1));
        WriteNote("C", "#work");

        if (OperatingSystem.IsWindows())
        {
            using FileStream locked = new FileStream(b, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            index.Sync(store);
        }
        else
        {
            File.SetUnixFileMode(b, UnixFileMode.None);
            if (CanRead(b))
            {
                // Running as root: permissions don't stop reads, so there is nothing to test.
                return;
            }

            index.Sync(store);
        }

        Assert.Equal([("A", "work"), ("C", "work")], Rows());
        Assert.Equal("B", index.FindNote("B"));
        Assert.Empty(index.Links("B"));

        // Once readable again (same last-write time), the note is parsed on the next sync.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(b, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        index.Sync(store);
        Assert.Equal([("A", "work"), ("B", "secret"), ("B", "work"), ("C", "work")], Rows());
    }

    private static bool CanRead(string path)
    {
        try
        {
            File.ReadAllText(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // AC-5
    [Fact]
    public void Links_SharedTags_SortedByName_NeverSelf()
    {
        WriteNote("A", "#work #home #solo");
        WriteNote("beta", "#home #work");
        WriteNote("Charlie", "#work");
        WriteNote("delta", "#other");
        index.Sync(store);

        IReadOnlyList<NoteLink> links = index.Links("a");

        Assert.Equal(["beta", "Charlie"], links.Select(l => l.Note));
        Assert.Equal(["home", "work"], links[0].SharedTags);
        Assert.Equal(["work"], links[1].SharedTags);
        Assert.DoesNotContain(links, l => l.Note == "A");
        Assert.Empty(index.Links("delta"));
        Assert.Empty(index.Links("missing"));
        Assert.Equal("A", index.FindNote("a"));
    }

    // AC-9, 024 AC-6
    [Fact]
    public void Garbage_Database_Throws_BytesUnchanged()
    {
        WriteNote("A", "#work");
        byte[] garbage = new byte[4096];
        new Random(22).NextBytes(garbage);
        File.WriteAllBytes(DbPath, garbage);

        Assert.Throws<TagIndexException>(() => index.Sync(store));
        Assert.Throws<TagIndexException>(() => index.Tags());
        Assert.Throws<TagIndexException>(() => index.Links("A"));
        Assert.Throws<TagIndexException>(() => index.NotesWithTag("work"));

        Assert.Equal(garbage, File.ReadAllBytes(DbPath));
        Assert.Equal(
            [Path.Combine(root, "A.md"), DbPath],
            Directory.GetFiles(root).Order(StringComparer.Ordinal));
    }

    // AC-9
    [Fact]
    public void TagIndexException_IsIOException()
    {
        Assert.IsAssignableFrom<IOException>(new TagIndexException("x"));
    }

    // Security: names and tags go in as parameters, so quotes and SQL text are stored literally.
    [Fact]
    public void Sync_NameWithQuotes_StoredLiterally()
    {
        WriteNote("x'); DROP TABLE notes; --", "#work");
        WriteNote("y", "#work");
        index.Sync(store);

        Assert.Equal(["x'); DROP TABLE notes; --", "y"], index.NotesWithTag("work"));
        Assert.Equal(["y"], index.Links("x'); DROP TABLE notes; --").Select(l => l.Note));
    }

    // 024 AC-4
    [Theory]
    [InlineData("#[Project Alpha]", "project alpha")]
    [InlineData("[project alpha]", "project alpha")]
    [InlineData("project   alpha", "project alpha")]
    [InlineData(" Project Alpha ", "project alpha")]
    [InlineData("#[ Project \t Alpha ]", "project alpha")]
    [InlineData("#work", "work")]
    [InlineData("#WORK", "work")]
    [InlineData("#[work]", "work")]
    [InlineData("", "")]
    [InlineData("#", "")]
    [InlineData("#[]", "")]
    public void NormalizeTag_AcceptsBothForms(string tag, string normalized)
    {
        Assert.Equal(normalized, TagIndex.NormalizeTag(tag));
    }

    // 024 AC-5
    [Fact]
    public void Links_BracketAndPlainForms_AreOneTag()
    {
        WriteNote("A", "#[project alpha]");
        WriteNote("B", "#[Project  Alpha]");
        WriteNote("C", "#work");
        WriteNote("D", "#[work]");
        index.Sync(store);

        Assert.Equal(["B"], index.Links("A").Select(l => l.Note));
        Assert.Equal(["project alpha"], index.Links("A")[0].SharedTags);
        Assert.Equal(["D"], index.Links("C").Select(l => l.Note));
        Assert.Equal(["work"], index.Links("C")[0].SharedTags);
        Assert.Equal(
            [new TagCount("project alpha", 2), new TagCount("work", 2)],
            index.Tags());
        Assert.Equal(["A", "B"], index.NotesWithTag("#[Project Alpha]"));
    }

    // 024 AC-6
    [Fact]
    public void Sync_SetsUserVersion_OnNewDatabase()
    {
        index.Sync(store);

        Assert.Equal(TagIndex.ParserVersion, UserVersion());
        Assert.Equal(1, TagIndex.ParserVersion);
    }

    // 024 AC-6
    [Fact]
    public void Sync_VersionZeroDatabase_ReparsesOnce()
    {
        WriteNote("A", "#old");
        WriteNote("B", "#keep");
        string a = Path.Combine(root, "A.md");
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(a, time);
        index.Sync(store);

        // Make it look like an index written by the 022 parser: version 0, and A's file has a bracket
        // tag that the old parser did not see.
        using (SqliteConnection connection = Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 0; DELETE FROM note_tags WHERE note = 'A';";
            command.ExecuteNonQuery();
        }

        File.WriteAllText(a, "#[a b]");
        File.SetLastWriteTimeUtc(a, time);
        Assert.Equal(0, UserVersion());

        index.Sync(store);

        Assert.Equal([("A", "a b"), ("B", "keep")], Rows());
        Assert.Equal(1, UserVersion());

        // Upgraded once: unchanged times are not re-parsed again.
        File.WriteAllText(a, "#[c d]");
        File.SetLastWriteTimeUtc(a, time);
        index.Sync(store);

        Assert.Equal([("A", "a b"), ("B", "keep")], Rows());
        Assert.Equal(1, UserVersion());
    }

    // 024 AC-6: a newer version is left alone, not lowered.
    [Fact]
    public void Sync_NewerVersion_IsKept()
    {
        index.Sync(store);
        using (SqliteConnection connection = Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 7";
            command.ExecuteNonQuery();
        }

        index.Sync(store);

        Assert.Equal(7, UserVersion());
    }
}
