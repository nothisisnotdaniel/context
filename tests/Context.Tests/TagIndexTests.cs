using System.Text;
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

    // 132 AC-6: the index is always the file <root>/context.db, and there is no in-memory mode to switch to.
    [Fact]
    public void Sync_AlwaysCreatesAndUsesContextDb_AndThereIsNoInMemoryMember()
    {
        WriteNote("Plan", "#work");
        Assert.False(File.Exists(DbPath));

        index.Sync(store);

        Assert.True(File.Exists(DbPath));
        Assert.Equal(DbPath, index.FilePath);
        // Another instance sees what the first one stored without syncing: the data is in the file.
        Assert.Equal(["Plan"], new TagIndex(root).NotesWithTag("work"));
        System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;
        Assert.DoesNotContain(typeof(TagIndex).GetMembers(all), m => m.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(TagIndex).GetFields(all), f => f.FieldType == typeof(Microsoft.Data.Sqlite.SqliteConnection));
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(TagIndex)));
    }

    private void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".md"), text);
    }

    // 160 AC-5: NoteTags gives every (note, tag) row in one read, sorted by note then tag.
    [Fact]
    public void NoteTags_EveryRow_OneRead()
    {
        WriteNote("beta", "#work #[Big Plan]");
        WriteNote("Alpha", "#work");
        WriteNote("gamma", "no tags");
        index.Sync(store);
        int before = index.Reads;

        IReadOnlyList<(string Note, string Tag)> rows = index.NoteTags();

        Assert.Equal(before + 1, index.Reads);
        Assert.Equal([("Alpha", "work"), ("beta", "big plan"), ("beta", "work")], rows);
    }

    // 160 AC-5: an empty index has no rows; a bad file throws TagIndexException and stays as it was.
    [Fact]
    public void NoteTags_EmptyAndBadFile()
    {
        index.Sync(store);
        Assert.Empty(index.NoteTags());

        File.Delete(DbPath);
        byte[] garbage = new byte[2048];
        new Random(4).NextBytes(garbage);
        File.WriteAllBytes(DbPath, garbage);

        Assert.Throws<TagIndexException>(() => index.NoteTags());
        Assert.Equal(garbage, File.ReadAllBytes(DbPath));
    }

    /// <summary>Every (note, target) row of <c>note_links</c>, read directly.</summary>
    private List<(string Note, string Target)> LinkRows()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT note, target FROM note_links ORDER BY note, target";
        using SqliteDataReader reader = command.ExecuteReader();
        List<(string, string)> rows = [];
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }

    /// <summary>Rewrites a note and moves its last-write time forward, so the next sync re-parses it.</summary>
    private void EditNote(string name, string text)
    {
        string path = Path.Combine(root, name + ".md");
        DateTime before = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, before.AddSeconds(5));
    }

    private static string Describe(NoteLink link) =>
        $"{link.Note}|{link.Direction}|{link.Missing}|{string.Join(",", link.SharedTags)}";

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
        Assert.Equal(13, TagIndex.ParserVersion);
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
        Assert.Equal(TagIndex.ParserVersion, UserVersion());

        // Upgraded once: unchanged times are not re-parsed again.
        File.WriteAllText(a, "#[c d]");
        File.SetLastWriteTimeUtc(a, time);
        index.Sync(store);

        Assert.Equal([("A", "a b"), ("B", "keep")], Rows());
        Assert.Equal(TagIndex.ParserVersion, UserVersion());
    }

    // 024 AC-6: a newer version is left alone, not lowered.
    [Fact]
    public void Sync_NewerVersion_IsKept()
    {
        index.Sync(store);
        using (SqliteConnection connection = Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 14";
            command.ExecuteNonQuery();
        }

        index.Sync(store);

        Assert.Equal(14, UserVersion());
    }

    // 048 AC-4: the old constructor means a tag link only.
    [Fact]
    public void NoteLink_OldConstructor_IsTagOnly()
    {
        NoteLink link = new NoteLink("A", ["work"]);

        Assert.Equal(NoteLinkDirection.None, link.Direction);
        Assert.False(link.Missing);
    }

    // 048 AC-3
    [Theory]
    [InlineData("Beta")]
    [InlineData("beta")]
    [InlineData("beta.MD")]
    [InlineData("BETA.md")]
    public void Links_TargetResolvesLikeOpen(string target)
    {
        WriteNote("A", $"[[{target}]]");
        WriteNote("Beta", "plain");
        index.Sync(store);

        Assert.Equal(["Beta|To|False|"], index.Links("A").Select(Describe));
        Assert.Equal(["A|From|False|"], index.Links("Beta").Select(Describe));
    }

    // 048 AC-3: exact before ignoring case, and the name as written before stripping .md.
    [Fact]
    public void Links_Resolve_ExactFirst_ThenWithoutMd()
    {
        WriteNote("A", "[[x.md]] [[Y]]");
        WriteNote("x.md", "plain");
        WriteNote("x", "plain");
        WriteNote("y", "plain");
        index.Sync(store);

        Assert.Equal(["x.md|To|False|", "y|To|False|"], index.Links("A").Select(Describe));

        NameLookup names = new NameLookup(["Beta", "beta", "c"]);
        Assert.Equal("beta", names.Resolve("beta"));
        Assert.Equal("Beta", names.Resolve("Beta.md"));
        Assert.Equal("c", names.Resolve("C.MD"));
        Assert.Null(names.Resolve(".md"));
        Assert.Null(names.Resolve("d"));
    }

    // 048 AC-4
    [Fact]
    public void Links_Directions_SharedTags_OnceEach_NeverSelf_Sorted()
    {
        WriteNote("A", "#work [[Beta]] [[A]] [[a.md]] [[charlie]] [[Beta.md]]");
        WriteNote("Beta", "#work #home [[A]]");
        WriteNote("charlie", "#home");
        WriteNote("Delta", "[[a]]");
        WriteNote("echo", "#work");
        index.Sync(store);

        Assert.Equal(
            ["Beta|Both|False|work", "charlie|To|False|", "Delta|From|False|", "echo|None|False|work"],
            index.Links("A").Select(Describe));
        Assert.Equal(
            ["A|Both|False|work", "charlie|None|False|home", "echo|None|False|work"],
            index.Links("Beta").Select(Describe));
        Assert.Equal(["A|To|False|"], index.Links("Delta").Select(Describe));
    }

    // 048 AC-4: a note that only links to itself has no links.
    [Fact]
    public void Links_SelfLinkOnly_IsNoLink()
    {
        WriteNote("A", "[[A]] [[a.md]]");
        index.Sync(store);

        Assert.Empty(index.Links("A"));
    }

    // 048 AC-5
    [Fact]
    public void Links_MissingTarget_ResolvesOnceTheNoteExists_WithoutReparsing()
    {
        WriteNote("A", "[[Gamma]] [[gamma.md]]");
        string a = Path.Combine(root, "A.md");
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(a, time);
        index.Sync(store);

        NoteLink missing = Assert.Single(index.Links("A"));
        Assert.Equal("Gamma", missing.Note);
        Assert.Equal(NoteLinkDirection.To, missing.Direction);
        Assert.True(missing.Missing);
        Assert.Empty(missing.SharedTags);

        // A changes on disk but keeps its time, so it must not be read again: the old link stays.
        File.WriteAllText(a, "[[Other]]");
        File.SetLastWriteTimeUtc(a, time);
        WriteNote("Gamma", "plain");
        index.Sync(store);

        Assert.Equal(["Gamma|To|False|"], index.Links("A").Select(Describe));
        Assert.Equal(["A|From|False|"], index.Links("Gamma").Select(Describe));
        Assert.Equal([("A", "Gamma"), ("A", "gamma.md")], LinkRows());
    }

    // 048 AC-6
    [Fact]
    public void Sync_KeepsNoteLinksUpToDate()
    {
        WriteNote("A", "[[Beta]] [[Gamma]]");
        WriteNote("Beta", "[[A]]");
        index.Sync(store);
        Assert.Equal([("A", "Beta"), ("A", "Gamma"), ("Beta", "A")], LinkRows());

        EditNote("A", "[[Gamma]]");
        index.Sync(store);
        Assert.Equal([("A", "Gamma"), ("Beta", "A")], LinkRows());
        Assert.Equal(["Beta|From|False|", "Gamma|To|True|"], index.Links("A").Select(Describe));

        File.Delete(Path.Combine(root, "A.md"));
        index.Sync(store);
        Assert.Equal([("Beta", "A")], LinkRows());
        Assert.Equal(["A|To|True|"], index.Links("Beta").Select(Describe));
    }

    // 048 AC-6: orphaned link rows (no note row) are removed.
    [Fact]
    public void Sync_RemovesOrphanedLinkRows()
    {
        WriteNote("A", "plain");
        index.Sync(store);
        using (SqliteConnection connection = Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO note_links(note, target) VALUES('Ghost', 'A')";
            command.ExecuteNonQuery();
        }

        index.Sync(store);

        Assert.Empty(LinkRows());
        Assert.Empty(index.Links("A"));
    }

    // 048 AC-6: an unreadable note has no link rows and is retried.
    [Fact]
    public void Sync_UnreadableNote_HasNoLinks_AndIsRetried()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteNote("A", "plain");
        WriteNote("B", "[[A]]");
        index.Sync(store);
        string b = Path.Combine(root, "B.md");
        File.WriteAllText(b, "[[A]] [[C]]");
        File.SetLastWriteTimeUtc(b, DateTime.UtcNow.AddMinutes(1));
        File.SetUnixFileMode(b, UnixFileMode.None);
        if (CanRead(b))
        {
            return;
        }

        index.Sync(store);
        Assert.Empty(LinkRows());
        Assert.Empty(index.Links("A"));

        File.SetUnixFileMode(b, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        index.Sync(store);
        Assert.Equal([("B", "A"), ("B", "C")], LinkRows());
    }

    // 048 AC-6: a version 1 database gets the links of unchanged notes after one sync.
    [Fact]
    public void Sync_VersionOneDatabase_ParsesLinksOnce()
    {
        WriteNote("A", "#work [[Beta]]");
        WriteNote("Beta", "plain");
        string a = Path.Combine(root, "A.md");
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(a, time);
        index.Sync(store);

        // Make it look like an index from before note links: version 1 and no link rows.
        using (SqliteConnection connection = Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 1; DELETE FROM note_links WHERE note = 'A';";
            command.ExecuteNonQuery();
        }

        Assert.Empty(LinkRows());
        Assert.Equal(1, UserVersion());

        index.Sync(store);

        Assert.Equal([("A", "Beta")], LinkRows());
        Assert.Equal([("A", "work")], Rows());
        Assert.Equal(TagIndex.ParserVersion, UserVersion());
        Assert.Equal(["Beta|To|False|"], index.Links("A").Select(Describe));
    }

    // 048 AC-6: a database that isn't valid still throws and is left as it was.
    [Fact]
    public void Garbage_Database_LinksThrow_BytesUnchanged()
    {
        WriteNote("A", "[[B]]");
        byte[] garbage = new byte[4096];
        new Random(48).NextBytes(garbage);
        File.WriteAllBytes(DbPath, garbage);

        Assert.Throws<TagIndexException>(() => index.Sync(store));
        Assert.Throws<TagIndexException>(() => index.Links("A"));
        Assert.Equal(garbage, File.ReadAllBytes(DbPath));
    }

    // Security: link targets go in as parameters and are stored literally.
    [Fact]
    public void Sync_TargetWithQuotes_StoredLiterally()
    {
        WriteNote("A", "[[x'); DROP TABLE notes; --]]");
        index.Sync(store);

        Assert.Equal([("A", "x'); DROP TABLE notes; --")], LinkRows());
        Assert.Equal(["x'); DROP TABLE notes; --|To|True|"], index.Links("A").Select(Describe));
    }

    // Job 123, AC-14 ----------------------------------------------------------------------------

    [Fact]
    public void BusyTimeout_IsFiveSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), TagIndex.BusyTimeout);
    }

    // AC-14: another connection holds a write lock for 1 s; Sync waits for it and succeeds.
    [Fact]
    public async Task Sync_WhileLockedForOneSecond_WaitsAndSucceeds()
    {
        WriteNote("A", "#work");
        index.Sync(store);
        WriteNote("B", "#work");

        using SqliteConnection other = OpenWithoutTimeout();
        Execute(other, "BEGIN EXCLUSIVE");
        Task release = Task.Run(() =>
        {
            Thread.Sleep(1000);
            Execute(other, "COMMIT");
        });

        index.Sync(store);
        await release;

        Assert.Equal(["A", "B"], index.NotesWithTag("work"));
    }

    // AC-14: a lock held past the timeout makes Sync throw, and the database is unchanged.
    [Fact]
    public void Sync_LockedPastTimeout_ThrowsAndLeavesDatabaseUnchanged()
    {
        WriteNote("A", "#work");
        index.Sync(store);
        WriteNote("B", "#work");
        TagIndex shortWait = new TagIndex(root, TimeSpan.FromMilliseconds(200));
        byte[] before = File.ReadAllBytes(DbPath);

        using (SqliteConnection other = OpenWithoutTimeout())
        {
            // The other connection only locks; it changes nothing and rolls back.
            Execute(other, "BEGIN EXCLUSIVE");
            Assert.Throws<TagIndexException>(() => shortWait.Sync(store));
            Execute(other, "ROLLBACK");
        }

        Assert.Equal(before, File.ReadAllBytes(DbPath));
        Assert.Equal(["A"], index.NotesWithTag("work"));
    }

    private SqliteConnection OpenWithoutTimeout()
    {
        SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // 136 AC-3: a sensitive note has no row in any table; a link to it counts as missing.
    [Fact]
    public void Sync_SensitiveNote_HasNoRows()
    {
        WriteNote("Plan", "#secret [[Other]]");
        WriteNote("Other", "#kept [[Plan]]");
        index.Sync(store);
        Assert.Contains(("Plan", "secret"), Rows());

        File.Delete(Path.Combine(root, "Plan.md"));
        File.WriteAllText(Path.Combine(root, "Plan.ctxs"), "#secret [[Other]]");
        index.Sync(store);

        Assert.Equal([("Other", "kept")], Rows());
        Assert.Equal([("Other", "Plan")], LinkRows());
        Assert.Null(index.FindNote("Plan"));
        NoteLink link = Assert.Single(index.Links("Other"));
        Assert.True(link.Missing);
    }

    // Writes a note below the root, creating the folders of its parents.
    private void WriteNested(string name, string text)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private List<string> NoteRows()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM notes ORDER BY name";
        using SqliteDataReader reader = command.ExecuteReader();
        List<string> rows = [];
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    // 152 AC-9: subpages are stored by full name and [[Parent/Title]] resolves ignoring case.
    [Fact]
    public void Sync_Subpages_ByFullName_AndLinksResolve()
    {
        WriteNested("Meetings", "#work");
        WriteNested("Meetings/Meeting 1", "#work #call");
        WriteNested("Plan", "see [[meetings/MEETING 1]]");
        WriteNested("Orphan/Child", "#work");

        index.Sync(store);

        Assert.Equal(["Meetings", "Meetings/Meeting 1", "Plan"], NoteRows());
        Assert.Equal([("Meetings", "work"), ("Meetings/Meeting 1", "call"), ("Meetings/Meeting 1", "work")], Rows());
        Assert.Equal(["Meetings", "Meetings/Meeting 1"], index.NotesWithTag("work"));
        Assert.Equal("Meetings/Meeting 1", index.FindNote("meetings/meeting 1"));
        Assert.Equal(["Meetings/Meeting 1|To|False|"], index.Links("Plan").Select(Describe));
        Assert.Equal("Meetings/Meeting 1", new NameLookup(store.List().Select(n => n.Name)).Resolve("MEETINGS/meeting 1"));
    }

    // 152 AC-9: after a rename or move, the old names are gone from notes, note_tags and note_links.
    [Fact]
    public void Sync_AfterRenameAndMove_OldNamesGone()
    {
        WriteNested("Meetings", "#work");
        WriteNested("Meetings/Meeting 1", "#call [[Plan]]");
        WriteNested("Archive", "plain");
        WriteNested("Plan", "plain");
        index.Sync(store);

        store.Rename("Meetings", "Calls");
        index.Sync(store);

        Assert.Equal(["Archive", "Calls", "Calls/Meeting 1", "Plan"], NoteRows());
        Assert.Equal([("Calls", "work"), ("Calls/Meeting 1", "call")], Rows());
        Assert.Equal([("Calls/Meeting 1", "Plan")], LinkRows());

        store.Move("Calls/Meeting 1", "Archive");
        index.Sync(store);

        Assert.Equal(["Archive", "Archive/Meeting 1", "Calls", "Plan"], NoteRows());
        Assert.Equal([("Archive/Meeting 1", "call"), ("Calls", "work")], Rows());
        Assert.Equal([("Archive/Meeting 1", "Plan")], LinkRows());
    }

    // 152 AC-2 / AC-9: a version 2 database is re-parsed once and keeps every top-level note's tags and links.
    [Fact]
    public void Sync_VersionTwoDatabase_ReparsesOnce_KeepsTopLevel()
    {
        WriteNote("A", "#work [[Beta]]");
        WriteNote("Beta", "#home");
        index.Sync(store);
        using (SqliteConnection connection = Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 2;";
            command.ExecuteNonQuery();
        }

        index.Sync(store);

        Assert.Equal(13, TagIndex.ParserVersion);
        Assert.Equal(TagIndex.ParserVersion, UserVersion());
        Assert.Equal(["A", "Beta"], NoteRows());
        Assert.Equal([("A", "work"), ("Beta", "home")], Rows());
        Assert.Equal([("A", "Beta")], LinkRows());
    }

    /// <summary>Every (note, person, spelling) row of <c>note_people</c>, read directly.</summary>
    private List<(string Note, string Person, string Spelling)> PeopleRows()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT note, person, spelling FROM note_people ORDER BY note, person";
        using SqliteDataReader reader = command.ExecuteReader();
        List<(string, string, string)> rows = [];
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }

    private static string Describe(PersonCount person) => $"{person.Person}|{person.Count}";

    // 156 AC-3: Sync stores each note's mentions by key with the spelling, and drops them with the note.
    [Fact]
    public void Sync_StoresMentions_ByKey_AndDropsThemWithTheNote()
    {
        WriteNote("Plan", "Call @[Anna  Schmidt] and @[ANNA schmidt], then @[Bob].");
        WriteNote("Other", "`@[Anna]` @[a/b] a@[Bob]");
        index.Sync(store);

        Assert.Equal([("Plan", "anna schmidt", "Anna Schmidt"), ("Plan", "bob", "Bob")], PeopleRows());

        EditNote("Plan", "Only @[Bob] now");
        index.Sync(store);
        Assert.Equal([("Plan", "bob", "Bob")], PeopleRows());

        File.Delete(Path.Combine(root, "Plan.md"));
        index.Sync(store);
        Assert.Empty(PeopleRows());
    }

    // 156 AC-3: a person is found ignoring case and whitespace runs, and never in their own page.
    [Fact]
    public void NotesMentioning_IgnoresCaseAndSpaces_NeverTheOwnPage()
    {
        WriteNote("people", "# people");
        WriteNested("people/Anna Schmidt", "I am @[Anna Schmidt]");
        WriteNote("b plan", "@[ANNA  schmidt]");
        WriteNote("A plan", "@[anna schmidt]");
        WriteNote("Other", "@[Bob]");
        index.Sync(store);

        Assert.Equal(["A plan", "b plan"], index.NotesMentioning("anna schmidt"));
        Assert.Equal(["A plan", "b plan"], index.NotesMentioning("  Anna   SCHMIDT "));
        Assert.Empty(index.NotesMentioning("Carl"));
    }

    // 156 AC-4: each person once, sorted ignoring case, with counts; the page title wins, else the ordinal-first spelling.
    [Fact]
    public void People_OncePerPerson_SortedWithCounts_PageTitleOrFirstSpelling()
    {
        WriteNote("people", "# people");
        WriteNested("people/Anna Schmidt", "@[anna schmidt] is me");
        WriteNote("A", "@[anna schmidt] @[bob] @[Carl]");
        WriteNote("B", "@[ANNA SCHMIDT] @[Bob]");
        WriteNote("C", "@[BOB]");
        index.Sync(store);

        Assert.Equal(["Anna Schmidt|2", "BOB|3", "Carl|1"], index.People().Select(Describe));
        Assert.Equal("Anna Schmidt", index.PersonName("ANNA schmidt"));
        Assert.Equal("BOB", index.PersonName("bob"));
        Assert.Null(index.PersonName("nobody"));
        Assert.Equal(nameof(PersonCount), new PersonCount("x", 1).ToString());
    }

    // 156 AC-4: a person mentioned only on their own page is not listed.
    [Fact]
    public void People_OnlyOnTheirOwnPage_IsNotListed()
    {
        WriteNote("People", "# People");
        WriteNested("People/Anna", "@[Anna]");
        index.Sync(store);

        Assert.Empty(index.People());
        Assert.Empty(index.NotesMentioning("anna"));
    }

    // 156 AC-3: a version 3 database is re-parsed once, so mentions written before the upgrade are found.
    [Fact]
    public void Sync_VersionThreeDatabase_ReparsesOnce()
    {
        WriteNote("A", "@[Anna] #work");
        string a = Path.Combine(root, "A.md");
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(a, time);
        index.Sync(store);
        using (SqliteConnection connection = Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DROP TABLE note_people; PRAGMA user_version = 3;";
            command.ExecuteNonQuery();
        }

        Assert.Equal(3, UserVersion());

        index.Sync(store);

        Assert.Equal(TagIndex.ParserVersion, UserVersion());
        Assert.Equal([("A", "anna", "Anna")], PeopleRows());
        Assert.Equal([("A", "work")], Rows());

        // Upgraded once: an unchanged time is not re-parsed again.
        File.WriteAllText(a, "@[Bob]");
        File.SetLastWriteTimeUtc(a, time);
        index.Sync(store);

        Assert.Equal([("A", "anna", "Anna")], PeopleRows());
    }

    /// <summary>Every (note, key, value_key, value) row of <c>note_attributes</c>, read directly.</summary>
    private List<(string Note, string Key, string ValueKey, string Value)> AttributeRows()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT note, key, value_key, value FROM note_attributes ORDER BY note, key, value_key";
        using SqliteDataReader reader = command.ExecuteReader();
        List<(string, string, string, string)> rows = [];
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return rows;
    }

    // 291 AC-1: AttributeValues gives exactly the key's rows as stored, by note (ordinal), then line order; an unused key
    // gives nothing. The key is a parameter, so a quote in it is plain text.
    [Fact]
    public void AttributeValues_KeyRows_ByNoteThenLine()
    {
        WriteNote("bob", "role:: Zed\nrole:: [[Lead]]\naffiliation:: ACME\nrole:: alpha\n");
        WriteNote("Zoe", "role:: Chef\n");
        WriteNote("anna", "Role::   Team  lead \nrole:: team lead\n");
        index.Sync(store);

        Assert.Equal(
            [("Zoe", "Chef"), ("anna", "Team lead"), ("bob", "Zed"), ("bob", "[[Lead]]"), ("bob", "alpha")],
            index.AttributeValues("role"));
        Assert.Equal([("bob", "ACME")], index.AttributeValues("affiliation"));
        Assert.Empty(index.AttributeValues("aliases"));
        Assert.Empty(index.AttributeValues("role' OR 1=1 --"));
    }

    // 157 AC-3: Sync stores each note's attributes with the value key, keeps them up to date and drops them with the note.
    [Fact]
    public void Sync_StoresAttributes_AndDropsThemWithTheNote()
    {
        WriteNote("Anna", "# Anna\nRole:: Bereichsleiter\naffiliation:: [[ACME]]\nrole::\n");
        WriteNote("Bob", "role:: x'); DROP TABLE notes; --");
        index.Sync(store);

        Assert.Equal(
            [
                ("Anna", "affiliation", "acme", "[[ACME]]"),
                ("Anna", "role", "bereichsleiter", "Bereichsleiter"),
                ("Bob", "role", "x'); drop table notes; --", "x'); DROP TABLE notes; --"),
            ],
            AttributeRows());

        EditNote("Anna", "role:: Chef");
        index.Sync(store);
        Assert.Equal(["Anna"], index.NotesWithAttribute("role", "chef"));
        Assert.Empty(index.NotesWithAttribute("role", "Bereichsleiter"));

        File.Delete(Path.Combine(root, "Anna.md"));
        index.Sync(store);
        Assert.Equal([("Bob", "role", "x'); drop table notes; --", "x'); DROP TABLE notes; --")], AttributeRows());
    }

    // 157 AC-2 / AC-3: a value matches by its value key (a [[link]] by its target, ignoring case), each note once, sorted.
    [Fact]
    public void NotesWithAttribute_MatchesLinkAndPlainValues_Sorted()
    {
        WriteNote("b", "affiliation:: [[ACME]]");
        WriteNote("A", "affiliation:: ACME");
        WriteNote("C", "affiliation:: acme\naffiliation:: [[ ACME ]]");
        WriteNote("D", "employer:: ACME");
        index.Sync(store);

        Assert.Equal(["A", "b", "C"], index.NotesWithAttribute("affiliation", "[[acme]]"));
        Assert.Equal(["A", "b", "C"], index.NotesWithAttribute("affiliation", "ACME"));
        Assert.Empty(index.NotesWithAttribute("affiliation", "ACM"));
        Assert.Equal(["A", "b", "C"], index.NotesWithoutAttribute("employer"));
    }

    // 157 AC-3: only re-parsed notes change: an attribute added to a file whose time didn't change is not seen.
    [Fact]
    public void Sync_Attributes_OnlyReparsedNotesChange()
    {
        WriteNote("A", "role:: one");
        string a = Path.Combine(root, "A.md");
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(a, time);
        index.Sync(store);

        File.WriteAllText(a, "role:: two");
        File.SetLastWriteTimeUtc(a, time);
        index.Sync(store);

        Assert.Equal([("A", "role", "one", "one")], AttributeRows());
    }

    // 157 AC-3: a sensitive note has no attribute rows.
    [Fact]
    public void Sync_SensitiveNote_HasNoAttributeRows()
    {
        WriteNote("Plan", "role:: secret");
        index.Sync(store);
        Assert.Single(AttributeRows());

        File.Delete(Path.Combine(root, "Plan.md"));
        File.WriteAllText(Path.Combine(root, "Plan.ctxs"), "role:: secret");
        index.Sync(store);

        Assert.Empty(AttributeRows());
        Assert.Empty(index.NotesWithAttribute("role", "secret"));
    }

    // 157 AC-3: a version 4 database is re-parsed once and set to 5, so attributes written before the upgrade are found.
    [Fact]
    public void Sync_VersionFourDatabase_ReparsesOnce()
    {
        WriteNote("A", "role:: Chef #work");
        string a = Path.Combine(root, "A.md");
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(a, time);
        index.Sync(store);
        using (SqliteConnection connection = Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DROP TABLE note_attributes; PRAGMA user_version = 4;";
            command.ExecuteNonQuery();
        }

        index.Sync(store);

        Assert.Equal(TagIndex.ParserVersion, UserVersion());
        Assert.Equal([("A", "role", "chef #work", "Chef #work")], AttributeRows());

        File.WriteAllText(a, "role:: Other");
        File.SetLastWriteTimeUtc(a, time);
        index.Sync(store);

        Assert.Equal([("A", "role", "chef #work", "Chef #work")], AttributeRows());
    }

    // 163 AC-7: a version 5 database is re-parsed once and set to 6, so a link the old parser found in a target that runs
    // into a code span is gone.
    [Fact]
    public void Sync_VersionFiveDatabase_DropsLinkIntoCodeSpan()
    {
        WriteNote("A", "[[a `b]] c`");
        string a = Path.Combine(root, "A.md");
        File.SetLastWriteTimeUtc(a, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        index.Sync(store);
        Assert.Empty(LinkRows());

        // Make it look like an index from the old parser: version 5 and the link it stored.
        using (SqliteConnection connection = Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO note_links(note, target) VALUES ('A', 'a `b'); PRAGMA user_version = 5;";
            command.ExecuteNonQuery();
        }

        Assert.Equal([("A", "a `b")], LinkRows());

        index.Sync(store);

        Assert.Empty(LinkRows());
        Assert.Equal(TagIndex.ParserVersion, UserVersion());
    }

    private void EditNested(string name, string text)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        DateTime before = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, before.AddSeconds(5));
    }

    // 158 AC-4: a note with the alias and the full name counts once under the title; an ambiguous alias is its own
    // entry; the person's own page never counts, alias mentions included.
    [Fact]
    public void People_Aliases_CountOnceUnderTheTitle_AmbiguousStaysOwnEntry()
    {
        WriteNote("people", "# people");
        WriteNested("people/Anna Schmidt", "# Anna Schmidt\naliases:: Anna, AS\nI am @[AS] and @[Anna Schmidt]");
        WriteNested("people/Bob", "# Bob\naliases:: Bobby");
        WriteNote("A", "@[Anna] @[Anna Schmidt] @[as]");
        WriteNote("B", "@[AS]");
        WriteNote("C", "@[bobby] @[Carl]");
        index.Sync(store);

        Assert.Equal(["Anna Schmidt|2", "Bob|1", "Carl|1"], index.People().Select(Describe));
        Assert.Equal(["A", "B"], index.NotesMentioning("Anna"));
        Assert.Equal(["A", "B"], index.NotesMentioning("anna schmidt"));
        Assert.Equal(["C"], index.NotesMentioning("Bob"));
        Assert.Equal("Anna Schmidt", index.PersonName("as"));

        // A second page with the alias Anna: @[Anna] links to no one and is listed as itself.
        WriteNested("people/Anna Berg", "# Anna Berg\naliases:: anna");
        index.Sync(store);

        Assert.Equal(["Anna|1", "Anna Schmidt|2", "Bob|1", "Carl|1"], index.People().Select(Describe));
        Assert.Equal(["A"], index.NotesMentioning("Anna"));
        Assert.Equal(["A", "B"], index.NotesMentioning("AS"));
        Assert.Equal("Anna", index.PersonName("anna"));
    }

    // 158 AC-4 / AC-2: an alias on a note that is no person page, or on a person page's subpage, has no effect.
    [Fact]
    public void People_AliasesOnOtherNotes_NoEffect()
    {
        WriteNote("people", "# people");
        WriteNested("people/Anna", "# Anna");
        WriteNested("people/Anna/Notes", "aliases:: AS");
        WriteNote("Other", "aliases:: A2\n@[AS] @[A2]");
        index.Sync(store);

        Assert.Equal(["A2|1", "AS|1"], index.People().Select(Describe));
        Assert.Empty(index.NotesMentioning("Anna"));
    }

    // 158 AC-7: adding, changing and removing an aliases:: line re-links on the next Sync, re-parsing only that page.
    [Fact]
    public void Sync_AliasChange_RelinksWithoutRescan()
    {
        WriteNote("people", "# people");
        WriteNested("people/Anna Schmidt", "# Anna Schmidt");
        WriteNote("A", "@[AS]");
        WriteNote("B", "@[Anna]");
        index.Sync(store);
        Assert.Equal(["Anna|1", "AS|1"], index.People().Select(Describe));

        // A note changed behind the index's back with the same time: only a rescan would see it.
        string b = Path.Combine(root, "B.md");
        DateTime time = File.GetLastWriteTimeUtc(b);
        File.WriteAllText(b, "@[Carl]");
        File.SetLastWriteTimeUtc(b, time);

        EditNested("people/Anna Schmidt", "# Anna Schmidt\naliases:: AS");
        index.Sync(store);
        Assert.Equal(["Anna|1", "Anna Schmidt|1"], index.People().Select(Describe));
        Assert.Equal(["A"], index.NotesMentioning("Anna Schmidt"));

        EditNested("people/Anna Schmidt", "# Anna Schmidt\naliases:: Anna");
        index.Sync(store);
        Assert.Equal(["Anna Schmidt|1", "AS|1"], index.People().Select(Describe));
        Assert.Equal(["B"], index.NotesMentioning("Anna Schmidt"));

        EditNested("people/Anna Schmidt", "# Anna Schmidt");
        index.Sync(store);
        Assert.Equal(["Anna|1", "AS|1"], index.People().Select(Describe));
        Assert.Empty(index.NotesMentioning("Anna Schmidt"));
        Assert.Equal(TagIndex.ParserVersion, UserVersion());
        Assert.Equal(13, TagIndex.ParserVersion);
    }

    // 170 ----------------------------------------------------------------------------------------

    private List<(string Note, string Body)> TextRows()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT note, body FROM note_text ORDER BY note";
        using SqliteDataReader reader = command.ExecuteReader();
        List<(string Note, string Body)> rows = [];
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }

    // The stored last-write ticks of a note, or null when there is none (a note that couldn't be read).
    private long? StoredModified(string note)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT modified FROM notes WHERE name = $note";
        command.Parameters.AddWithValue("$note", note);
        object? value = command.ExecuteScalar();
        return value is long ticks ? ticks : null;
    }

    // 170 AC-1: Sync writes one note_text row per note with its whole text; Search finds rows ignoring case.
    [Fact]
    public void Sync_WritesNoteText_SearchMatchesIgnoringCase()
    {
        WriteNote("Plan", "# Plan\nBudget 2027");
        WriteNote("Other", "nothing");
        Directory.CreateDirectory(Path.Combine(root, "Plan"));
        File.WriteAllText(Path.Combine(root, "Plan", "Child.md"), "child budget");

        index.Sync(store);

        Assert.Equal([("Other", "nothing"), ("Plan", "# Plan\nBudget 2027"), ("Plan/Child", "child budget")], TextRows());
        Assert.Equal(
            ["Plan", "Plan/Child"],
            index.Search("BUDGET").Select(r => r.Note).Order(StringComparer.Ordinal));
        Assert.Equal("# Plan\nBudget 2027", Assert.Single(index.Search("et 20")).Body);
    }

    // 268 AC-3: NoteTexts gives every note_text row (plain notes only), sorted by note, and reads through one query.
    [Fact]
    public void NoteTexts_AllPlainRows_SortedByNote()
    {
        WriteNote("b", "second");
        WriteNote("A", "first");
        Directory.CreateDirectory(Path.Combine(root, "A"));
        File.WriteAllText(Path.Combine(root, "A", "Child.md"), "child");
        File.WriteAllText(Path.Combine(root, "Secret.ctxs"), "sealed");
        index.Sync(store);
        int before = index.Reads;

        IReadOnlyList<(string Note, string Body)> rows = index.NoteTexts();

        Assert.Equal([("A", "first"), ("A/Child", "child"), ("b", "second")], rows);
        Assert.Equal(before + 1, index.Reads);
    }

    // 170 AC-2: the query is one quoted phrase passed as a parameter: quotes and FTS operators are text.
    [Theory]
    [InlineData("a\" OR \"b")]
    [InlineData("NEAR(x y)")]
    [InlineData("x*y")]
    [InlineData("^abc")]
    [InlineData("\"\"\"")]
    public void Search_FtsSyntax_IsLiteral(string query)
    {
        WriteNote("Literal", "[" + query + "]");
        WriteNote("Words", "a b x y abc OR NEAR");
        index.Sync(store);

        Assert.Equal(["Literal"], index.Search(query).Select(r => r.Note));
    }

    // 170 AC-4: an edited, renamed or deleted note's row is replaced or removed; an orphan row is removed.
    [Fact]
    public void Sync_NoteText_FollowsEditsAndRemovesOrphans()
    {
        WriteNote("A", "one");
        WriteNote("B", "two");
        index.Sync(store);
        using (SqliteConnection connection = Open())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO note_text(note, body) VALUES('Ghost', 'boo')";
            command.ExecuteNonQuery();
        }

        WriteNote("A", "uno");
        File.SetLastWriteTimeUtc(Path.Combine(root, "A.md"), DateTime.UtcNow.AddMinutes(1));
        File.Move(Path.Combine(root, "B.md"), Path.Combine(root, "C.md"));
        index.Sync(store);

        Assert.Equal([("A", "uno"), ("C", "two")], TextRows());
    }

    // 170, 275: a note that can't be read, or is over MaxNoteBytes, has no note_text row and no tags.
    [Fact]
    public void Sync_UnreadableOrTooBigNote_HasNoTextRow()
    {
        WriteNote("Small", "small");
        string big = Path.Combine(root, "Big.md");
        using (FileStream stream = new FileStream(big, FileMode.Create))
        {
            byte[] start = Encoding.UTF8.GetBytes("#work\n[[Small]]\n");
            stream.Write(start);
            stream.SetLength(NoteStore.MaxNoteBytes + 1);
        }

        index.Sync(store);

        Assert.Equal([("Small", "small")], TextRows());
        Assert.Empty(index.NotesWithTag("work"));
        Assert.Empty(index.Links("Small"));
        Assert.Null(StoredModified("Big"));
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string small = Path.Combine(root, "Small.md");
        File.WriteAllText(small, "changed");
        File.SetLastWriteTimeUtc(small, DateTime.UtcNow.AddMinutes(1));
        File.SetUnixFileMode(small, UnixFileMode.None);
        if (CanRead(small))
        {
            return;
        }

        index.Sync(store);

        Assert.Empty(TextRows());
    }

    // 275: a note just under MaxNoteBytes is fully indexed: its tags and its text row.
    [Fact]
    public void Sync_NoteJustUnderLimit_IsFullyIndexed()
    {
        string start = "#work\n";
        WriteNote("Big", start + new string(' ', (int)NoteStore.MaxNoteBytes - 1 - start.Length));

        index.Sync(store);

        Assert.Equal(["Big"], index.NotesWithTag("work"));
        Assert.Equal(["Big"], TextRows().Select(r => r.Note));
        Assert.NotNull(StoredModified("Big"));
    }

    // 170: FTS5 secure-delete is on for note_text, and a sync with nothing to do leaves the file's bytes alone.
    [Fact]
    public void Sync_SecureDeleteOn_NoOpSyncWritesNothing()
    {
        WriteNote("A", "text");
        index.Sync(store);
        using (SqliteConnection connection = Open())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT v FROM note_text_config WHERE k = 'secure-delete'";
            Assert.Equal(1L, command.ExecuteScalar());
        }

        byte[] before = File.ReadAllBytes(DbPath);
        index.Sync(store);

        Assert.Equal(before, File.ReadAllBytes(DbPath));
    }

    // 174 AC-14: a top-level dot-file and a version under .history are not indexed.
    [Fact]
    public void Sync_LeavesOutDotFilesAndHistory()
    {
        WriteNote("Plan", "#work");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".x.md"), "#work");
        Directory.CreateDirectory(Path.Combine(root, ".history", "Plan"));
        File.WriteAllText(Path.Combine(root, ".history", "Plan", "20261029T090000000Z.md"), "#work #old");

        index.Sync(store);

        Assert.Equal(["Plan"], index.NotesWithTag("work"));
        Assert.Empty(index.NotesWithTag("old"));
    }

    // 180 ----------------------------------------------------------------------------------------

    private List<(string Note, long Line, string Text)> DecisionRows()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT note, line, text FROM note_decisions ORDER BY note, line";
        using SqliteDataReader reader = command.ExecuteReader();
        List<(string Note, long Line, string Text)> rows = [];
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1), reader.GetString(2)));
        }

        return rows;
    }

    // 180 AC-3: one note_decisions row per decision; removing a line or the note removes its rows on the next Sync.
    [Fact]
    public void Sync_Decisions_WrittenAndPruned()
    {
        WriteNote("Plan", "# Plan\nDecision: ship it\n- decision: wait\n");
        WriteNote("Other", "Decision: other");
        string plan = Path.Combine(root, "Plan.md");
        File.SetLastWriteTimeUtc(plan, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        index.Sync(store);

        Assert.Equal([("Other", 1L, "other"), ("Plan", 2L, "ship it"), ("Plan", 3L, "wait")], DecisionRows());

        File.WriteAllText(plan, "# Plan\nDecision: ship it\n");
        File.SetLastWriteTimeUtc(plan, new DateTime(2026, 1, 3, 3, 4, 5, DateTimeKind.Utc));
        File.Delete(Path.Combine(root, "Other.md"));
        index.Sync(store);

        Assert.Equal([("Plan", 2L, "ship it")], DecisionRows());
    }

    // 180 AC-3: a database at user_version 7 is fully re-parsed (decisions appear for unchanged notes) and set to the current version.
    [Fact]
    public void Sync_Version7Database_ReparsedForDecisions()
    {
        WriteNote("Plan", "Decision: ship it");
        File.SetLastWriteTimeUtc(Path.Combine(root, "Plan.md"), new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        index.Sync(store);
        using (SqliteConnection connection = Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 7; DELETE FROM note_decisions;";
            command.ExecuteNonQuery();
        }

        Assert.Empty(DecisionRows());

        index.Sync(store);

        Assert.Equal([("Plan", 1L, "ship it")], DecisionRows());
        Assert.Equal(TagIndex.ParserVersion, UserVersion());
    }

    // 180 AC-3: a sensitive .ctxs note never adds decision rows.
    [Fact]
    public void Sync_SensitiveNote_HasNoDecisionRows()
    {
        WriteNote("Other", "Decision: kept");
        File.WriteAllText(Path.Combine(root, "Plan.ctxs"), "Decision: secret");

        index.Sync(store);

        Assert.Equal([("Other", 1L, "kept")], DecisionRows());
        Assert.DoesNotContain(index.Decisions(null), r => r.Note == "Plan");
    }

    // 180 AC-6: Decisions(tag) gives the stored time and filters by the note's tag, normalized and as a parameter.
    [Fact]
    public void Decisions_TagFilter_AndModified()
    {
        WriteNote("Plan", "#work\nDecision: ship it");
        WriteNote("Home", "#home\nDecision: paint");
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(root, "Plan.md"), time);
        index.Sync(store);

        (string Note, int Line, string Text, DateTime Modified) row = Assert.Single(index.Decisions("#Work"));
        Assert.Equal(("Plan", 2, "ship it", time), row);
        Assert.Equal(DateTimeKind.Utc, row.Modified.Kind);
        Assert.Equal(2, index.Decisions(null).Count);
        Assert.Equal(2, index.Decisions(" ").Count);
        Assert.Empty(index.Decisions("x' OR '1'='1"));
    }

    private static string Describe(NoteTodo todo) =>
        $"{todo.Note}|{todo.Line}|{todo.Due?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "-"}|{todo.Text}|{todo.Raw}";

    // 201 AC-3: Sync stores each plain note's open todos; NoteTodos returns them with line, due, text and raw line.
    [Fact]
    public void Sync_StoresNoteTodos()
    {
        WriteNote("Meeting", "# Notes\r\n- [ ] call Anna due:2026-11-20\r\n- [x] done already\r\n  * [ ] send deck\r\n");
        WriteNote("Other", "nothing here");

        index.Sync(store);

        Assert.Equal(
            ["Meeting|2|2026-11-20|call Anna|- [ ] call Anna due:2026-11-20", "Meeting|4|-|send deck|  * [ ] send deck"],
            index.NoteTodos().Select(Describe));
    }

    // 201 AC-3: ticking a line, deleting the note and renaming it update the rows on the next Sync.
    [Fact]
    public void Sync_NoteTodos_FollowEditDeleteAndRename()
    {
        WriteNote("Meeting", "- [ ] one\n- [ ] two\n");
        WriteNote("Gone", "- [ ] three\n");
        index.Sync(store);
        Assert.Equal(3, index.NoteTodos().Count);

        EditNote("Meeting", "- [x] one\n- [ ] two\n");
        File.Delete(Path.Combine(root, "Gone.md"));
        index.Sync(store);
        Assert.Equal(["Meeting|2|-|two|- [ ] two"], index.NoteTodos().Select(Describe));

        store.Rename("Meeting", "Call");
        index.Sync(store);
        Assert.Equal(["Call|2|-|two|- [ ] two"], index.NoteTodos().Select(Describe));
    }

    // 201 AC-3: a database at user_version 8 is re-parsed once (todos appear for unchanged notes) and ends at 9.
    [Fact]
    public void Sync_Version8Database_ReparsedForNoteTodos()
    {
        WriteNote("Plan", "- [ ] ship it");
        File.SetLastWriteTimeUtc(Path.Combine(root, "Plan.md"), new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        index.Sync(store);
        using (SqliteConnection connection = Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 8; DROP TABLE note_todos;";
            command.ExecuteNonQuery();
        }

        index.Sync(store);

        Assert.Equal(["Plan|1|-|ship it|- [ ] ship it"], index.NoteTodos().Select(Describe));
        Assert.Equal(TagIndex.ParserVersion, UserVersion());
    }

    // 201 AC-3: a sensitive .ctxs note never has note todo rows.
    [Fact]
    public void Sync_SensitiveNote_HasNoNoteTodos()
    {
        WriteNote("Other", "- [ ] kept");
        File.WriteAllText(Path.Combine(root, "Plan.ctxs"), "- [ ] secret");

        index.Sync(store);

        Assert.Equal(["Other|1|-|kept|- [ ] kept"], index.NoteTodos().Select(Describe));
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM note_todos WHERE note = 'Plan'";
        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    // 201 AC-3: a bad context.db throws TagIndexException from NoteTodos.
    [Fact]
    public void NoteTodos_BadFile_Throws()
    {
        Directory.CreateDirectory(root);
        byte[] garbage = new byte[2048];
        new Random(5).NextBytes(garbage);
        File.WriteAllBytes(DbPath, garbage);

        Assert.Throws<TagIndexException>(() => index.NoteTodos());
    }

    private static string Describe(NoteTopic topic) => $"{topic.Note}|{topic.Line}|{topic.Person}|{topic.Text}|{topic.Raw}";

    private static IEnumerable<string> TopicRows(TagIndex index) =>
        index.Topics().Select(Describe).Order(StringComparer.Ordinal);

    // 253 AC-3: Sync stores one row per topic line of each plain note; no topic line is a note todo.
    [Fact]
    public void Sync_StoresTopics()
    {
        WriteNote("Plan", "# Plan\r\nnext: @[Ann Lee] budget\r\n- [ ] NEXT: @[Ann  Lee] hiring\r\n- [x] next: @[Ann Lee] old\r\n");
        WriteNote("Other", "- next:@[Bob] travel\n");

        index.Sync(store);

        Assert.Equal(
            [
                "Other|1|Bob|travel|- next:@[Bob] travel",
                "Plan|2|Ann Lee|budget|next: @[Ann Lee] budget",
                "Plan|3|Ann Lee|hiring|- [ ] NEXT: @[Ann  Lee] hiring",
            ],
            TopicRows(index));
        Assert.Empty(index.NoteTodos());
    }

    // 253 AC-3: editing or deleting the note updates or removes its rows on the next Sync.
    [Fact]
    public void Sync_Topics_FollowEditAndDelete()
    {
        WriteNote("Plan", "next: @[Ann Lee] budget\nnext: @[Ann Lee] hiring\n");
        WriteNote("Gone", "next: @[Bob] travel\n");
        index.Sync(store);
        Assert.Equal(3, index.Topics().Count);

        EditNote("Plan", "- [x] next: @[Ann Lee] budget\nnext: @[Ann Lee] hiring now\n");
        File.Delete(Path.Combine(root, "Gone.md"));
        index.Sync(store);

        Assert.Equal(["Plan|2|Ann Lee|hiring now|next: @[Ann Lee] hiring now"], TopicRows(index));
    }

    // 253 AC-3: a database at user_version 10 is re-parsed fully (topics appear for unchanged notes) and ends at 11.
    [Fact]
    public void Sync_Version10Database_ReparsedForTopics()
    {
        WriteNote("Plan", "- [ ] next: @[Ann Lee] budget");
        File.SetLastWriteTimeUtc(Path.Combine(root, "Plan.md"), new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        index.Sync(store);
        using (SqliteConnection connection = Open())
        {
            // As the version 10 parser left it: the line was a note todo and there was no topics table.
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 10; DROP TABLE note_topics; "
                + "INSERT INTO note_todos(note, line, due, text, raw) VALUES('Plan', 1, NULL, 'next: @[Ann Lee] budget', '- [ ] next: @[Ann Lee] budget');";
            command.ExecuteNonQuery();
        }

        index.Sync(store);

        Assert.Equal(["Plan|1|Ann Lee|budget|- [ ] next: @[Ann Lee] budget"], TopicRows(index));
        Assert.Empty(index.NoteTodos());
        Assert.Equal(TagIndex.ParserVersion, UserVersion());
    }

    // 253 AC-3: a sensitive .ctxs note never has topic rows.
    [Fact]
    public void Sync_SensitiveNote_HasNoTopics()
    {
        WriteNote("Other", "next: @[Ann Lee] kept");
        File.WriteAllText(Path.Combine(root, "Plan.ctxs"), "next: @[Ann Lee] secret");

        index.Sync(store);

        Assert.Equal(["Other|1|Ann Lee|kept|next: @[Ann Lee] kept"], TopicRows(index));
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM note_topics WHERE note = 'Plan'";
        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    private static IEnumerable<string> ReviewRows(TagIndex index) =>
        index.Reviews()
            .Select(r => $"{r.Note}|{r.Line}|{r.Due:yyyy-MM-dd}|{r.Raw}")
            .Order(StringComparer.Ordinal);

    // 256 AC-2: Sync stores the first review line of each plain note.
    [Fact]
    public void Sync_StoresReviews()
    {
        WriteNote("Plan", "# Plan\r\n  - Review: 2026-12-01  \r\nreview:2027-01-01\r\n");
        WriteNote("Other", "```\nreview:2026-01-01\n```\n* REVIEW:\t2026-11-19");
        WriteNote("None", "review:2026-02-30\n");

        index.Sync(store);

        Assert.Equal(
            ["Other|4|2026-11-19|* REVIEW:\t2026-11-19", "Plan|2|2026-12-01|  - Review: 2026-12-01  "],
            ReviewRows(index));
    }

    // 256 AC-2: editing or deleting the line, or the note, updates or prunes the row on the next Sync.
    [Fact]
    public void Sync_Reviews_FollowEditAndDelete()
    {
        WriteNote("Plan", "review:2026-12-01\n");
        WriteNote("Dropped", "x\nreview:2026-12-02\n");
        WriteNote("Gone", "review:2026-12-03\n");
        index.Sync(store);
        Assert.Equal(3, index.Reviews().Count);

        EditNote("Plan", "x\n\nreview: 2027-02-01\n");
        EditNote("Dropped", "x\n");
        File.Delete(Path.Combine(root, "Gone.md"));
        index.Sync(store);

        Assert.Equal(["Plan|3|2027-02-01|review: 2027-02-01"], ReviewRows(index));
    }

    // 256 AC-2: a database at user_version 11 is re-parsed fully (review rows appear for unchanged notes).
    [Fact]
    public void Sync_Version11Database_ReparsedForReviews()
    {
        WriteNote("Plan", "review:2026-12-01");
        File.SetLastWriteTimeUtc(Path.Combine(root, "Plan.md"), new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        index.Sync(store);
        using (SqliteConnection connection = Open())
        {
            // As the version 11 parser left it: no review table.
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 11; DROP TABLE note_reviews;";
            command.ExecuteNonQuery();
        }

        index.Sync(store);

        Assert.Equal(["Plan|1|2026-12-01|review:2026-12-01"], ReviewRows(index));
        Assert.Equal(13, UserVersion());
    }

    // 256 AC-2, AC-9: a sensitive .ctxs note never has a review row.
    [Fact]
    public void Sync_SensitiveNote_HasNoReview()
    {
        WriteNote("Other", "review:2026-12-01");
        File.WriteAllText(Path.Combine(root, "Plan.ctxs"), "review:2026-01-01");

        index.Sync(store);

        Assert.Equal(["Other|1|2026-12-01|review:2026-12-01"], ReviewRows(index));
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM note_reviews WHERE note = 'Plan'";
        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    /// <summary>Every (note, target, heading) row of <c>note_links</c>, read directly.</summary>
    private List<(string Note, string Target, string? Heading)> HeadingRows()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT note, target, heading FROM note_links ORDER BY note, target";
        using SqliteDataReader reader = command.ExecuteReader();
        List<(string, string, string?)> rows = [];
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return rows;
    }

    // 205 AC-2: the target is stored as written, the heading after the first # (NULL without one).
    [Fact]
    public void Sync_StoresLinkHeading()
    {
        WriteNote("Plan", "## Goals");
        WriteNote("A", "[[Plan#Goals]] [[Plan]] [[Plan# ]]");

        index.Sync(store);

        Assert.Equal(
            [("A", "Plan", null), ("A", "Plan#", null), ("A", "Plan#Goals", "Goals")],
            HeadingRows());
    }

    // 205 AC-3: a version-9 database (note_links without heading) opens, gains the column and is re-parsed.
    [Fact]
    public void Sync_VersionNineDatabase_AddsHeading_ReparsesEveryNote()
    {
        WriteNote("Plan", "## Goals");
        WriteNote("A", "[[Plan#Goals]]");
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(root, "Plan.md"), time);
        File.SetLastWriteTimeUtc(Path.Combine(root, "A.md"), time);
        index.Sync(store);

        // Rebuild note_links the way ParserVersion 9 had it, with A's row but no heading.
        using (SqliteConnection connection = Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "DROP TABLE note_links; " +
                "CREATE TABLE note_links(note TEXT NOT NULL, target TEXT NOT NULL, PRIMARY KEY(note, target)); " +
                "INSERT INTO note_links(note, target) VALUES('A', 'Old'); " +
                "PRAGMA user_version = 9;";
            command.ExecuteNonQuery();
        }

        Assert.Equal(9, UserVersion());

        index.Sync(store);

        Assert.Equal([("A", "Plan#Goals", "Goals")], HeadingRows());
        Assert.Equal(TagIndex.ParserVersion, UserVersion());
        Assert.Equal(["Plan|To|False|"], index.Links("A").Select(Describe));
    }

    // 205 AC-3: opening for a read alone adds the column, without a Sync.
    [Fact]
    public void Links_VersionNineDatabase_OpensWithoutError()
    {
        WriteNote("A", "[[B]]");
        index.Sync(store);
        using (SqliteConnection connection = Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "DROP TABLE note_links; " +
                "CREATE TABLE note_links(note TEXT NOT NULL, target TEXT NOT NULL, PRIMARY KEY(note, target)); " +
                "INSERT INTO note_links(note, target) VALUES('A', 'B'); " +
                "PRAGMA user_version = 9;";
            command.ExecuteNonQuery();
        }

        Assert.Equal(["B|To|True|"], index.Links("A").Select(Describe));
        Assert.Equal([("A", "B", null)], HeadingRows());
    }

    // 205 AC-4: a heading link counts as a link to the note, both ways, matched like any target.
    [Fact]
    public void Links_HeadingLink_ResolvesToTheNote()
    {
        WriteNote("Plan", "## Goals");
        WriteNote("A", "[[plan#goals]]");

        index.Sync(store);

        Assert.Equal(["A|From|False|"], index.Links("Plan").Select(Describe));
        Assert.Equal(["Plan|To|False|"], index.Links("A").Select(Describe));
    }

    // 205 AC-4: a note whose name has a # is found by the whole target first, with no heading.
    [Fact]
    public void Links_NameWithHash_WholeTargetFirst()
    {
        WriteNote("C# notes", "plain");
        WriteNote("C", "plain");
        WriteNote("A", "[[C# notes]]");

        index.Sync(store);

        Assert.Equal(["C# notes|To|False|"], index.Links("A").Select(Describe));
        Assert.Empty(index.Links("C"));
    }

    // 205 AC-4
    [Fact]
    public void ResolveLink_WholeTarget_ThenNotePart()
    {
        NameLookup names = new NameLookup(["C# notes", "Plan", "C"]);

        NoteLinkTarget? hash = names.ResolveLink("c# NOTES");
        NoteLinkTarget? plan = names.ResolveLink("plan.md#Goals");
        NoteLinkTarget? c = names.ResolveLink("C# other");

        Assert.Equal("C# notes", hash?.Note);
        Assert.Null(hash?.Heading);
        Assert.Equal("Plan", plan?.Note);
        Assert.Equal("Goals", plan?.Heading);
        Assert.Equal("C", c?.Note);
        Assert.Equal("other", c?.Heading);
        Assert.Null(names.ResolveLink("#Plan"));
        Assert.Null(names.ResolveLink("Nope#Plan"));
    }

    // 205 AC-5: one missing row per note part, shown as stored first; a link to a heading only gives no row.
    [Fact]
    public void Links_MissingHeadingLinks_OneRowByNotePart_NoRowForHeadingOnly()
    {
        WriteNote("A", "[[gamma]] [[Gamma#A]] [[#Intro]]");

        index.Sync(store);

        Assert.Equal(["Gamma|To|True|"], index.Links("A").Select(Describe));
    }
}
