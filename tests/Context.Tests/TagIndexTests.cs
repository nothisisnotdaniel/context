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
        Assert.Equal(5, TagIndex.ParserVersion);
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
            command.CommandText = "PRAGMA user_version = 7";
            command.ExecuteNonQuery();
        }

        index.Sync(store);

        Assert.Equal(7, UserVersion());
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

        Assert.Equal(5, TagIndex.ParserVersion);
        Assert.Equal(5, UserVersion());
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

        Assert.Equal(5, UserVersion());
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

        Assert.Equal(5, UserVersion());
        Assert.Equal([("A", "role", "chef #work", "Chef #work")], AttributeRows());

        File.WriteAllText(a, "role:: Other");
        File.SetLastWriteTimeUtc(a, time);
        index.Sync(store);

        Assert.Equal([("A", "role", "chef #work", "Chef #work")], AttributeRows());
    }
}
