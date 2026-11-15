using System.Text;
using Microsoft.Data.Sqlite;

namespace Context.Tests;

// 170: the data of search <text>: the notes whose text contains the query, with the first matching line.
public sealed class SearchViewTests : IDisposable
{
    private readonly string parent;
    private readonly string root;
    private readonly TagIndex index;
    private readonly NoteStore store;

    public SearchViewTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-searchview-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
        index = new TagIndex(root);
        store = new NoteStore(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private string DbPath => Path.Combine(root, TagIndex.FileName);

    private string Write(string name, string text)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private SearchView Build(string text) => SearchView.Build(index, store, text);

    private string[] Notes(string text) => Build(text).Hits.Select(h => h.Note).ToArray();

    // The bytes of context.db as Latin-1 text (one char per byte), read after every connection is closed (Pooling=False).
    private string DbBytes() => Encoding.Latin1.GetString(File.ReadAllBytes(DbPath));

    private static string Marker() => "MRK" + Guid.NewGuid().ToString("N");

    // AC-1: a note with "Budget 2027" is found by "budget" with that line and its 1-based number; others are not.
    [Fact]
    public void Build_FindsLineIgnoringCase_WithLineNumber()
    {
        Write("Plan", "# Plan\r\n\r\nBudget 2027 draft\r\nmore");
        Write("Other", "# Other\nnothing to see");

        SearchView view = Build("budget");

        Assert.Equal(SearchViewKind.Found, view.Kind);
        Assert.Equal("budget", view.Query);
        SearchHit hit = Assert.Single(view.Hits);
        Assert.Equal("Plan", hit.Note);
        Assert.Equal(3, hit.LineNumber);
        Assert.Equal("Budget 2027 draft", hit.Line);
        Assert.Equal(nameof(SearchView), view.ToString());
        Assert.Equal(nameof(SearchHit), hit.ToString());
    }

    // AC-1: no note containing the text is its own kind with no hits; the query is trimmed.
    [Fact]
    public void Build_NoMatches_QueryTrimmed()
    {
        Write("Plan", "Budget");

        SearchView view = Build("  forecast  ");

        Assert.Equal(SearchViewKind.NoMatches, view.Kind);
        Assert.Equal("forecast", view.Query);
        Assert.Empty(view.Hits);
    }

    // AC-2: substring inside a word; a query with spaces matches only that exact run.
    [Fact]
    public void Build_Substring_AndExactRunWithSpaces()
    {
        Write("A", "Budget");
        Write("B", "plan 2027");
        Write("C", "plan  2027");
        Write("D", "2027 plan");

        Assert.Equal(["A"], Notes("udge"));
        Assert.Equal(["B"], Notes("plan 20"));
        Assert.Equal(["C"], Notes("plan  20"));
    }

    // AC-2: FTS syntax in the query is literal text: it never throws and matches only notes holding that text.
    [Theory]
    [InlineData("a\" OR \"b")]
    [InlineData("NEAR(x y)")]
    [InlineData("ab*")]
    [InlineData("-abc")]
    [InlineData("x - y")]
    [InlineData("\"\"\"")]
    [InlineData("abc OR xyz")]
    [InlineData("abc AND NOT")]
    [InlineData("body: abc")]
    [InlineData("'); DROP TABLE notes; --")]
    public void Build_FtsSyntax_IsLiteral(string query)
    {
        Write("Literal", "before " + query + " after");
        Write("A", "a OR b, x y, abc xyz, abcdef, abc, body, notes, b");
        Write("B", "NEAR x y abc and maybe not");

        SearchView view = Build(query);

        Assert.Equal(SearchViewKind.Found, view.Kind);
        SearchHit hit = Assert.Single(view.Hits);
        Assert.Equal("Literal", hit.Note);
        Assert.Equal("before " + query + " after", hit.Line);
    }

    // AC-3: only the first matching line per note, each note once; sorted ignoring case, then ordinal; subpages by full name.
    [Fact]
    public void Build_FirstLineOnly_SortedOnce_SubpagesByFullName()
    {
        Write("by", "x\nsecond Alpha\nthird alpha");
        Write("Bx", "alpha");
        Write("a", "ALPHA");
        Write("projects", "# projects");
        Write("projects/Plan", "# Plan\nthe alpha plan");

        SearchView view = Build("alpha");

        Assert.Equal(["a", "Bx", "by", "projects/Plan"], view.Hits.Select(h => h.Note));
        SearchHit b = view.Hits[2];
        Assert.Equal(2, b.LineNumber);
        Assert.Equal("second Alpha", b.Line);
        Assert.Equal((2, "the alpha plan"), (view.Hits[3].LineNumber, view.Hits[3].Line));
    }

    // AC-3: a row whose text matches only across lines is dropped (FTS is only the pre-filter).
    [Fact]
    public void Build_MatchAcrossLines_IsDropped()
    {
        Write("A", "plan\n2027");

        Assert.Equal(SearchViewKind.NoMatches, Build("plan\n2027").Kind);
        Assert.Empty(Notes("an\n20"));
    }

    // AC-4: editing, deleting and renaming a note update the results on the next Sync.
    [Fact]
    public void Build_EditDeleteRename_UpdateOnNextSync()
    {
        string plan = Write("Plan", "the budget");
        Write("Keep", "budget kept");
        Assert.Equal(["Keep", "Plan"], Notes("budget"));

        File.WriteAllText(plan, "the forecast");
        File.SetLastWriteTimeUtc(plan, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(["Keep"], Notes("budget"));
        Assert.Equal(["Plan"], Notes("forecast"));

        File.Move(plan, Path.Combine(root, "Renamed.md"));
        Assert.Equal(["Renamed"], Notes("forecast"));

        File.Delete(Path.Combine(root, "Renamed.md"));
        Assert.Equal(SearchViewKind.NoMatches, Build("forecast").Kind);
        Assert.Equal(["Keep"], Notes("budget"));
    }

    // AC-4: an unchanged note is not re-read: new bytes with the old last-write time keep the old row.
    [Fact]
    public void Build_SameLastWriteTime_KeepsOldText()
    {
        string path = Write("Plan", "the oldword here");
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, time);
        Assert.Equal(["Plan"], Notes("oldword"));

        File.WriteAllText(path, "the newword here");
        File.SetLastWriteTimeUtc(path, time);

        Assert.Equal(["Plan"], Notes("oldword"));
        Assert.Empty(Notes("newword"));
        // The line comes from the stored text too.
        Assert.Equal("the oldword here", Build("oldword").Hits[0].Line);
    }

    // AC-6: a sensitive note's bytes are never indexed, never a hit, never in context.db.
    [Fact]
    public void Build_SensitiveNote_NeverIndexed()
    {
        string marker = Marker();
        Write("Plain", "plain text");
        File.WriteAllText(Path.Combine(root, "Secret" + NoteStore.SensitiveExtension), "sealed " + marker);

        SearchView view = Build(marker);

        Assert.Equal(SearchViewKind.NoMatches, view.Kind);
        Assert.Equal(SearchViewKind.NoMatches, Build("sealed").Kind);
        Assert.Empty(index.Search(marker));
        Assert.DoesNotContain(marker, DbBytes(), StringComparison.Ordinal);
        Assert.DoesNotContain(marker.ToLowerInvariant(), DbBytes(), StringComparison.Ordinal);
    }

    // AC-6: a plain note holding a marker, then deleted as MarkSensitive does (sealed file written, .md deleted),
    // leaves no copy of the marker in context.db after Sync.
    [Fact]
    public void Build_PlainNoteMarkedSensitive_MarkerGoneFromDb()
    {
        string marker = Marker();
        Write("Other", "other text\n" + string.Join('\n', Enumerable.Range(0, 200).Select(i => $"line {i} of filler text")));
        string path = Write("Diary", "# Diary\n" + string.Join('\n', Enumerable.Range(0, 50).Select(i => $"{i} {marker} entry")));
        Assert.Equal(["Diary"], Notes(marker));
        Assert.Contains(marker, DbBytes(), StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(root, "Diary" + NoteStore.SensitiveExtension), "{\"v\":1}");
        File.Delete(path);

        Assert.Equal(SearchViewKind.NoMatches, Build(marker).Kind);
        Assert.Equal(["Other"], Notes("filler"));
        Assert.DoesNotContain(marker, DbBytes(), StringComparison.Ordinal);
        Assert.DoesNotContain(marker.ToLowerInvariant(), DbBytes(), StringComparison.Ordinal);
    }

    // AC-7: blank or under 3 chars after trim is TooShort, over 200 is TooLong; neither creates or opens context.db.
    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(" ab ", false)]
    [InlineData("\tx\n", false)]
    [InlineData(null, true)]
    public void Build_TooShortOrTooLong_NothingOpened(string? text, bool tooLong)
    {
        Write("A", "ab abc");
        string query = text ?? " " + new string('a', SearchView.MaxQueryLength + 1) + " ";

        SearchView view = Build(query);

        Assert.Equal(tooLong ? SearchViewKind.TooLong : SearchViewKind.TooShort, view.Kind);
        Assert.Empty(view.Hits);
        Assert.Equal(0, index.Reads);
        Assert.False(File.Exists(DbPath));
    }

    // AC-7: exactly 3 and exactly 200 characters after trim are searched.
    [Fact]
    public void Build_Bounds_AreSearched()
    {
        string longest = new string('b', SearchView.MaxQueryLength);
        Write("A", "abc " + longest);

        Assert.Equal(["A"], Notes(" abc "));
        Assert.Equal(["A"], Notes(longest));
        Assert.Equal(3, SearchView.MinQueryLength);
        Assert.Equal(200, SearchView.MaxQueryLength);
    }

    // AC-7: an invalid context.db gives IndexUnreadable, never throws, and the file is unchanged.
    [Fact]
    public void Build_BadIndex_IndexUnreadable_FileUnchanged()
    {
        Write("A", "abc");
        File.WriteAllText(DbPath, "this is not a database, just some text that fills the header....");
        byte[] before = File.ReadAllBytes(DbPath);

        SearchView view = Build("abc");

        Assert.Equal(SearchViewKind.IndexUnreadable, view.Kind);
        Assert.Equal("abc", view.Query);
        Assert.Empty(view.Hits);
        Assert.Equal(before, File.ReadAllBytes(DbPath));
    }

    // AC-5: a version-6 database with rows gets note_text filled for every note on its first Sync, then version 7.
    [Fact]
    public void Build_Version6Database_FillsNoteTextOnce()
    {
        Write("A", "alpha #work");
        Write("B", "beta");
        index.Sync(store);
        using (SqliteConnection connection = Open())
        using (SqliteCommand command = connection.CreateCommand())
        {
            // A database as version 6 left it: rows in notes and note_tags, no note text.
            command.CommandText = "DROP TABLE note_text; PRAGMA user_version = 6;";
            command.ExecuteNonQuery();
        }

        Assert.Equal(["A"], Notes("alpha"));
        Assert.Equal(["B"], Notes("beta"));
        Assert.Equal(["A"], index.NotesWithTag("work"));
        using SqliteConnection check = Open();
        using SqliteCommand version = check.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(7L, (long)version.ExecuteScalar()!);
    }

    private SqliteConnection Open()
    {
        SqliteConnection connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}
