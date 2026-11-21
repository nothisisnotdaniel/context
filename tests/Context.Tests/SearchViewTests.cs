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
    private readonly string times;
    private UnixFileMode? lockedTimesMode;

    // 379: a fixed zone one hour east of UTC, so a note's date in the zone differs from its UTC date near midnight.
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test+01", TimeSpan.FromHours(1), "Test+01", "Test+01");

    public SearchViewTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-searchview-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        times = Path.Combine(parent, "Times");
        Directory.CreateDirectory(parent);
        index = new TagIndex(root);
        store = new NoteStore(root);
    }

    public void Dispose()
    {
        if (lockedTimesMode is { } mode && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(times, mode);
        }

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
        Assert.Empty(index.Search(marker, (note, _) => note));
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

    // AC-5: a version-6 database with rows gets note_text filled for every note on its first Sync, then the current version.
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
        Assert.Equal((long)TagIndex.ParserVersion, (long)version.ExecuteScalar()!);
    }

    // 285 AC-1: each hit has where the match starts in its line (ignoring case), the note's types and its file's
    // last-write time in UTC.
    [Fact]
    public void Build_Hits_HaveMatchStartTypesAndModified()
    {
        string plan = Write("Plan", "# Plan\ntype:: Project\nthe BuDGet line\n");
        Write("people", "# people\n");
        string ann = Write("people/Ann", "# Ann\nann budget talk\n");
        string plain = Write("Plain", "Budget first\n");
        DateTime planTime = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        DateTime annTime = new DateTime(2025, 12, 31, 23, 30, 0, DateTimeKind.Utc);
        DateTime plainTime = new DateTime(2024, 1, 2, 0, 15, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(plan, planTime);
        File.SetLastWriteTimeUtc(ann, annTime);
        File.SetLastWriteTimeUtc(plain, plainTime);

        SearchView view = Build("budget");

        Assert.Equal(SearchViewKind.Found, view.Kind);
        Assert.Equal(["people/Ann", "Plain", "Plan"], view.Hits.Select(h => h.Note).ToArray());
        SearchHit annHit = view.Hits[0];
        Assert.Equal("ann budget talk", annHit.Line);
        Assert.Equal(4, annHit.MatchStart);
        Assert.Equal(["person"], annHit.Types);
        Assert.Equal(annTime, annHit.Modified);
        Assert.Equal(DateTimeKind.Utc, annHit.Modified!.Value.Kind);
        SearchHit plainHit = view.Hits[1];
        Assert.Equal(0, plainHit.MatchStart);
        Assert.Empty(plainHit.Types);
        Assert.Equal(plainTime, plainHit.Modified);
        SearchHit planHit = view.Hits[2];
        Assert.Equal("the BuDGet line", planHit.Line);
        Assert.Equal(4, planHit.MatchStart);
        Assert.Equal("BuDGet", planHit.Line.Substring(planHit.MatchStart, "budget".Length));
        Assert.Equal(["project"], planHit.Types);
        Assert.Equal(planTime, planHit.Modified);
        Assert.Equal(DateTimeKind.Utc, planHit.Modified!.Value.Kind);
    }

    private SqliteConnection Open()
    {
        SqliteConnection connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    // 295 AC-5, AC-6: 250 matching notes give 250 hits, one per note, sorted ignoring case, then ordinal, each with its
    // line; a row the trigram index matches but whose lines don't contain the query (the Kelvin sign folds to k in
    // FTS5, not in an ordinal comparison) is dropped by the selector.
    [Fact]
    public void Build_250Matches_AllHitsInOrder_TrigramOnlyDropped()
    {
        for (int i = 0; i < 250; i++)
        {
            Write((i % 2 == 0 ? "N" : "n") + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), $"# t\nthe shared key {i}\nshared key again");
        }

        Write("Kelvin", "the shared Key");

        SearchView view = Build("shared key");

        Assert.Equal(SearchViewKind.Found, view.Kind);
        Assert.Equal(250, view.Hits.Count);
        Assert.Equal(
            Enumerable.Range(0, 250).Select(i => (i % 2 == 0 ? "N" : "n") + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)),
            view.Hits.Select(h => h.Note));
        Assert.All(view.Hits, h => Assert.Equal(2, h.LineNumber));
        Assert.All(view.Hits, h => Assert.Equal(4, h.MatchStart));
        Assert.All(view.Hits, h => Assert.NotNull(h.Modified));
        Assert.Equal(["Kelvin"], index.Search("shared key", (note, _) => note).Where(n => n == "Kelvin"));
    }

    // 379 ---------------------------------------------------------------------------------------------------------

    private SearchView BuildWith(string text, DateOnly? from = null, DateOnly? to = null, string? folder = "") =>
        SearchView.Build(index, store, folder == string.Empty ? times : folder, text, from, to, Zone);

    private void WriteDay(string day, string json)
    {
        Directory.CreateDirectory(times);
        File.WriteAllText(Path.Combine(times, day + ".json"), json);
    }

    // A day in Times' entries format: "Work" on #1234 08:00-09:30, "Code Review" (no item) 09:30-10:00, "Other" on
    // #99 10:00-10:15, then an open "Late" checkpoint on #1234 (no entry ends it).
    private static string Day(bool open = false) =>
        "{\"entries\":["
        + "{\"kind\":\"arrival\",\"time\":\"08:00\"},"
        + "{\"kind\":\"checkpoint\",\"time\":\"08:00\",\"name\":\"Work\",\"workItem\":1234},"
        + "{\"kind\":\"checkpoint\",\"time\":\"09:30\",\"name\":\"Code Review\"},"
        + "{\"kind\":\"checkpoint\",\"time\":\"10:00\",\"name\":\"Other\",\"workItem\":99},"
        + (open
            ? "{\"kind\":\"checkpoint\",\"time\":\"10:15\",\"name\":\"Late\",\"workItem\":1234}]}"
            : "{\"kind\":\"leaving\",\"time\":\"10:15\"}]}");

    private void Touch(string name, DateTime utc) =>
        File.SetLastWriteTimeUtc(Path.Combine([root, .. name.Split('/')]) + ".md", utc);

    // Every file of the Times folder with its bytes, to prove nothing was written.
    private SortedDictionary<string, string> TimesSnapshot()
    {
        SortedDictionary<string, string> files = new(StringComparer.Ordinal);
        foreach (string path in Directory.GetFiles(times, "*", SearchOption.AllDirectories))
        {
            files[Path.GetRelativePath(times, path)] = Convert.ToBase64String(File.ReadAllBytes(path));
        }

        return files;
    }

    // AC-1: #1234 finds the notes holding work item 1234 (not #12345), each with its first such line and the index of
    // that #; #7 works although it is under MinQueryLength; the item's own page is not a hit.
    [Fact]
    public void WorkItem_FindsNotesWithItem_FirstLineAndMatchStart()
    {
        Write("Alpha", "# Alpha\nsee #12345 first\nthen #12345 and #1234 here\nagain #1234");
        Write("beta", "```\n#1234 in code\n```\n- [ ] #1234 later");
        Write("Gamma", "only #12345 and x#1234");
        Write("Delta", "fix #7 now");
        Write("items", "# items");
        Write("items/1234", "# 1234\n#1234");

        SearchView view = BuildWith(" #1234 ");

        Assert.Equal(SearchViewKind.Found, view.Kind);
        Assert.Equal("#1234", view.Query);
        Assert.Equal(["Alpha", "beta"], view.Hits.Select(h => h.Note));
        SearchHit alpha = view.Hits[0];
        Assert.Equal(3, alpha.LineNumber);
        Assert.Equal("then #12345 and #1234 here", alpha.Line);
        Assert.Equal(alpha.Line.IndexOf("#1234 ", StringComparison.Ordinal), alpha.MatchStart);
        Assert.NotNull(alpha.Modified);
        SearchHit beta = view.Hits[1];
        Assert.Equal(4, beta.LineNumber);
        Assert.Equal(6, beta.MatchStart);

        SearchView seven = BuildWith("#7");
        Assert.Equal(SearchViewKind.Found, seven.Kind);
        SearchHit delta = Assert.Single(seven.Hits);
        Assert.Equal("Delta", delta.Note);
        Assert.Equal(4, delta.MatchStart);
        Assert.Equal(SearchViewKind.Found, SearchView.Build(index, store, "#7").Kind);
    }

    // AC-2: #1234 gives one Times hit per day and group on 1234, newest first, with names, total and open; other
    // items' groups never.
    [Fact]
    public void WorkItem_TimesHits_PerDayAndGroup_NewestFirst()
    {
        WriteDay("2026-10-05", Day());
        WriteDay("2026-10-20", Day(open: true));
        WriteDay("2026-10-12", "{\"entries\":[{\"kind\":\"checkpoint\",\"time\":\"08:00\",\"name\":\"Other\",\"workItem\":99},"
            + "{\"kind\":\"leaving\",\"time\":\"09:00\"}]}");

        SearchView view = BuildWith("#1234");

        Assert.Equal(WorkItemTimesKind.Read, view.TimesKind);
        Assert.Equal(SearchViewKind.Found, view.Kind);
        Assert.Empty(view.Hits);
        Assert.Equal(2, view.Times.Count);
        TimesSearchHit newest = view.Times[0];
        Assert.Equal(new DateOnly(2026, 10, 20), newest.Date);
        Assert.Equal(1234, newest.WorkItem);
        Assert.Equal(["Work", "Late"], newest.Names);
        Assert.Equal(TimeSpan.FromMinutes(90), newest.Total);
        Assert.True(newest.Open);
        TimesSearchHit older = view.Times[1];
        Assert.Equal(new DateOnly(2026, 10, 5), older.Date);
        Assert.Equal(["Work"], older.Names);
        Assert.Equal(TimeSpan.FromMinutes(90), older.Total);
        Assert.False(older.Open);
        Assert.Equal(0, view.TimesSkipped);
    }

    // AC-3: a text query returns the groups where any name contains it, ignoring case, with the note hits as before.
    [Fact]
    public void Text_TimesHits_ByNameIgnoringCase_WithNotes()
    {
        Write("Plan", "# Plan\nreview the budget");
        WriteDay("2026-10-05", Day());

        SearchView view = BuildWith("REVIEW");

        Assert.Equal(SearchViewKind.Found, view.Kind);
        Assert.Equal(["Plan"], view.Hits.Select(h => h.Note));
        TimesSearchHit hit = Assert.Single(view.Times);
        Assert.Null(hit.WorkItem);
        Assert.Equal(["Code Review"], hit.Names);
        Assert.Equal(TimeSpan.FromMinutes(30), hit.Total);
        Assert.Empty(BuildWith("nothing like it").Times);
    }

    // AC-4: with a range, notes outside it (by Modified in the zone) and day files outside it are left out, both bounds
    // inclusive; an out-of-range day file is never opened (an unreadable one does not count as skipped).
    [Fact]
    public void Range_FiltersNotesByZoneDate_AndDayFiles_Inclusive()
    {
        Write("Before", "#1234 before");
        Write("First", "#1234 first");
        Write("Last", "#1234 last");
        Write("After", "#1234 after");
        Touch("Before", new DateTime(2026, 9, 30, 22, 30, 0, DateTimeKind.Utc)); // 23:30 on 09-30 in the zone
        Touch("First", new DateTime(2026, 9, 30, 23, 30, 0, DateTimeKind.Utc)); // 00:30 on 10-01 in the zone
        Touch("Last", new DateTime(2026, 10, 31, 22, 59, 0, DateTimeKind.Utc)); // 23:59 on 10-31 in the zone
        Touch("After", new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc)); // 00:00 on 11-01 in the zone
        WriteDay("2026-09-30", Day());
        WriteDay("2026-10-01", Day());
        WriteDay("2026-10-31", Day());
        WriteDay("2026-11-01", "{ broken");

        SearchView view = BuildWith("#1234", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.Equal(["First", "Last"], view.Hits.Select(h => h.Note));
        Assert.Equal([new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 1)], view.Times.Select(t => t.Date));
        Assert.Equal(0, view.TimesSkipped);

        SearchView fromOnly = BuildWith("#1234", from: new DateOnly(2026, 10, 31));
        Assert.Equal(["After", "Last"], fromOnly.Hits.Select(h => h.Note));
        Assert.Equal(1, fromOnly.TimesSkipped);
        Assert.Equal([new DateOnly(2026, 10, 31)], fromOnly.Times.Select(t => t.Date));

        SearchView toOnly = BuildWith("#1234", to: new DateOnly(2026, 9, 30));
        Assert.Equal(["Before"], toOnly.Hits.Select(h => h.Note));
        Assert.Equal([new DateOnly(2026, 9, 30)], toOnly.Times.Select(t => t.Date));
        Assert.Equal(0, toOnly.TimesSkipped);
    }

    // AC-5: from > to is InvalidRange with nothing read: no context.db is made and the Times folder is not looked at.
    [Fact]
    public void Range_StartAfterEnd_InvalidRange_NothingRead()
    {
        Write("A", "#1234 abc");
        WriteDay("2026-10-05", Day());

        SearchView view = BuildWith("#1234", new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1));

        Assert.Equal(SearchViewKind.InvalidRange, view.Kind);
        Assert.Empty(view.Hits);
        Assert.Empty(view.Times);
        Assert.Equal(WorkItemTimesKind.NotSetUp, view.TimesKind);
        Assert.False(File.Exists(DbPath));
        Assert.Equal(SearchViewKind.InvalidRange, BuildWith("abc", new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1)).Kind);
        Assert.False(File.Exists(DbPath));
        Assert.Equal(SearchViewKind.Found, BuildWith("#1234", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5)).Kind);
    }

    // AC-6: a null, missing or file Times folder is NotSetUp with no hits; unreadable day files are counted and the
    // others still searched; nothing is written into the Times folder.
    [Fact]
    public void Times_NotSetUp_Skipped_NothingWritten()
    {
        Write("A", "#1234 abc");
        Assert.Equal(WorkItemTimesKind.NotSetUp, BuildWith("#1234", folder: null).TimesKind);
        Assert.Equal(WorkItemTimesKind.NotSetUp, BuildWith("#1234").TimesKind);
        string file = Path.Combine(parent, "not-a-folder");
        File.WriteAllText(file, "x");
        SearchView onFile = BuildWith("#1234", folder: file);
        Assert.Equal(WorkItemTimesKind.NotSetUp, onFile.TimesKind);
        Assert.Empty(onFile.Times);
        Assert.Equal(SearchViewKind.Found, onFile.Kind);

        WriteDay("2026-10-05", Day());
        WriteDay("2026-10-06", "{ broken");
        WriteDay("2026-10-07", Day());
        File.WriteAllText(Path.Combine(times, "settings.json"), "{}");
        Directory.CreateDirectory(Path.Combine(times, "sub"));
        File.WriteAllText(Path.Combine(times, "sub", "2026-10-08.json"), Day());
        SortedDictionary<string, string> before = TimesSnapshot();

        SearchView view = BuildWith("#1234");

        Assert.Equal(WorkItemTimesKind.Read, view.TimesKind);
        Assert.Equal(1, view.TimesSkipped);
        Assert.Equal([new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 5)], view.Times.Select(t => t.Date));
        Assert.Equal(before, TimesSnapshot());
    }

    // AC-6: a Times folder that can't be listed is Unlistable, with no Times hits; the notes are still found.
    [Fact]
    public void Times_UnlistableFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Write("A", "#1234 abc");
        WriteDay("2026-10-05", Day());
        lockedTimesMode = File.GetUnixFileMode(times);
        File.SetUnixFileMode(times, UnixFileMode.None);
        try
        {
            Directory.EnumerateFiles(times).ToList();
            return; // Listing still works (for example as root): nothing to check here.
        }
        catch (UnauthorizedAccessException)
        {
        }

        SearchView view = BuildWith("#1234");

        Assert.Equal(WorkItemTimesKind.Unlistable, view.TimesKind);
        Assert.Empty(view.Times);
        Assert.Equal(["A"], view.Hits.Select(h => h.Note));
    }

    // AC-7: Found with note or Times hits, NoMatches with neither; under IndexUnreadable the Times hits still come.
    [Fact]
    public void Kind_FoundNoMatches_IndexUnreadableKeepsTimes()
    {
        Write("A", "nothing here");
        WriteDay("2026-10-05", Day());

        Assert.Equal(SearchViewKind.Found, BuildWith("#1234").Kind);
        Assert.Equal(SearchViewKind.Found, BuildWith("code review").Kind);
        Assert.Equal(SearchViewKind.NoMatches, BuildWith("#4321").Kind);
        Assert.Equal(SearchViewKind.NoMatches, BuildWith("zzz").Kind);

        File.WriteAllText(DbPath, "this is not a database, just some text that fills the header....");
        SearchView broken = BuildWith("#1234");
        Assert.Equal(SearchViewKind.IndexUnreadable, broken.Kind);
        Assert.Empty(broken.Hits);
        Assert.Equal([new DateOnly(2026, 10, 5)], broken.Times.Select(t => t.Date));
        Assert.Single(BuildWith("review").Times);
    }
}
