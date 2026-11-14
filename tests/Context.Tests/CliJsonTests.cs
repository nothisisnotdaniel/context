using System.Net;
using System.Text;
using System.Text.Json;

namespace Context.Tests;

public sealed class CliJsonTests : IDisposable
{
    private const string JsonOnly =
        "--json works only with version, todos, notes, note show, tags, links, people, person and current. Run 'context help'.";

    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    // A fixed zone one hour east of UTC, so times show a non-zero offset.
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test+01", TimeSpan.FromHours(1), "Test+01", "Test+01");

    private readonly string parent;
    private readonly string root;
    private readonly string outside;
    private FakeHttpHandler http = FakeHttpHandler.Ok(CalendarFeedTests.Ics);

    public CliJsonTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-clijson-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        outside = Path.Combine(parent, "outside");
        Directory.CreateDirectory(outside);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private sealed record Result(int Code, string Out, string Err)
    {
        public string[] ErrLines => Err.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private Result Run(params string[] args)
    {
        StringWriter stdout = new StringWriter();
        StringWriter stderr = new StringWriter();
        Cli cli = new Cli(stdout, stderr, root, () => Today, () => Now, http, Zone);
        int code = cli.Run(args);
        return new Result(code, stdout.ToString(), stderr.ToString());
    }

    private void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".md"), text);
    }

    private void WriteSealed(string name)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".ctxs"), "{\"v\":1,\"text\":\"c2VhbGVk\"}");
    }

    // todos.json written by hand: the JSON text of each todo's text, with its due date.
    private void WriteTodos(params (string Due, string TextJson)[] todos)
    {
        Directory.CreateDirectory(root);
        string items = string.Join(',', todos.Select(t => $"{{\"text\":{t.TextJson},\"due\":\"{t.Due}\"}}"));
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), $"[{items}]");
    }

    private void WriteIcs(string name, string text)
    {
        string folder = Path.Combine(root, CalendarStore.FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), text);
    }

    private void BreakIndex() =>
        File.WriteAllText(Path.Combine(root, TagIndex.FileName), "not a database at all, not at all, really not");

    private Dictionary<string, string> Snapshot()
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f), f => Convert.ToBase64String(File.ReadAllBytes(f)));
    }

    // AC-2: exactly one compact object and one \n, nothing on stderr, no control byte but the last \n.
    private static JsonElement AssertDocument(Result result, int code = Cli.ExitOk)
    {
        Assert.Equal(code, result.Code);
        Assert.EndsWith("\n", result.Out, StringComparison.Ordinal);
        string body = result.Out[..^1];
        Assert.DoesNotContain(body, c => c < 0x20 || c > 0x7E);
        using JsonDocument document = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        return document.RootElement.Clone();
    }

    private static void AssertError(Result result, int code, string line)
    {
        Assert.Equal(code, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.Equal([line], result.ErrLines);
    }

    private void Fixture()
    {
        WriteNote("A", "# A\n#work [[B]] [[Ghost]]\n");
        WriteNote("B", "#work #home\n");
        WriteSealed("Secret");
        WriteTodos(("2026-10-28", "\"late #work\""), ("2026-10-30", "\"later\""));
    }

    // AC-1 ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("--json", "todos")]
    [InlineData("todos", "--JSON")]
    [InlineData("--Json", "todos", "--json")]
    public void Flag_AnyPositionCaseAndRepeat_TurnsOnJson(params string[] args)
    {
        Fixture();

        Result result = Run(args);

        Assert.Equal(string.Empty, result.Err);
        Assert.Equal("{\"todos\":[{\"due\":\"2026-10-28\",\"text\":\"late #work\"},{\"due\":\"2026-10-30\",\"text\":\"later\"}]}\n", result.Out);
    }

    [Fact]
    public void Flag_IsRemovedBeforeArgumentsAreParsed()
    {
        Fixture();

        Assert.Equal("{\"name\":\"A\",\"text\":\"# A\\n#work [[B]] [[Ghost]]\\n\"}\n", Run("note", "--json", "show", "A").Out);
        Assert.Equal("{\"tag\":\"home\",\"notes\":[\"B\"]}\n", Run("tags", "--json", "home").Out);
        AssertDocument(Run("current", "--json"));
    }

    [Fact]
    public void Flag_OnlyAnExactToken()
    {
        Fixture();

        // "--jsonx" is no flag: todos takes no arguments.
        Result result = Run("todos", "--jsonx");

        Assert.Equal(Cli.ExitUsage, result.Code);
        Assert.Equal(["todos takes no arguments. Run 'context help'."], result.ErrLines);
    }

    [Fact]
    public void Flag_Alone_IsNoCommand()
    {
        AssertError(Run("--json"), Cli.ExitUsage, "No command given. Run 'context help'.");
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void WithoutFlag_PlainOutputIsAsBefore()
    {
        Fixture();

        Assert.Equal($"Context {AppVersion.Current}\n", Run("version").Out);
        Assert.Equal("2026-10-28\tlate #work\n2026-10-30\tlater\n", Run("todos").Out);
        Assert.Equal("A\nB\nSecret\tlocked\n", Run("notes").Out);
        Assert.Equal("# A\n#work [[B]] [[Ghost]]\n", Run("note", "show", "A").Out);
        Assert.Equal("#home\t1\n#work\t2\n", Run("tags").Out);
        Assert.Equal("A\nB\n", Run("tags", "work").Out);
        Assert.Equal("B\tlinks to\t#work\nGhost\tlinks to (missing)\t\n", Run("links", "A").Out);
        Result current = Run("current");
        Assert.Equal((0, string.Empty), (current.Code, current.Err));
        Assert.Equal(
            "Today, 2026-10-29\nAppointments\nNo appointments today. Put .ics files in "
                + Path.Combine(Path.GetFullPath(root), CalendarStore.FolderName)
                + " to see them here.\nDue\n2026-10-28\tlate #work\nLinked notes\nA\t#work\nB\t#work\n",
            current.Out);
    }

    // AC-3 ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("version")]
    [InlineData("--version")]
    public void Version_IsOneObject_AndNeverTouchesTheFolder(string command)
    {
        Result result = Run(command, "--json");

        Assert.Equal($"{{\"version\":\"{AppVersion.Current}\"}}\n", result.Out);
        Assert.Equal(string.Empty, result.Err);
        AssertDocument(result);
        Assert.False(Directory.Exists(root));
    }

    // AC-4 ---------------------------------------------------------------------------------------

    [Fact]
    public void Todos_EmptyList()
    {
        Directory.CreateDirectory(root);

        Assert.Equal("{\"todos\":[]}\n", Run("todos", "--json").Out);
    }

    [Fact]
    public void Todos_HaveNoIdOrNumber()
    {
        Fixture();

        JsonElement todo = AssertDocument(Run("todos", "--json")).GetProperty("todos")[0];

        Assert.Equal(["due", "text"], todo.EnumerateObject().Select(p => p.Name));
    }

    // AC-5 ---------------------------------------------------------------------------------------

    [Fact]
    public void Notes_ListsSensitiveAsLocked()
    {
        Fixture();

        Assert.Equal(
            "{\"notes\":[{\"name\":\"A\",\"locked\":false},{\"name\":\"B\",\"locked\":false},{\"name\":\"Secret\",\"locked\":true}]}\n",
            Run("notes", "--json").Out);
    }

    // 152 AC-10: --json keeps {notes:[{name,locked}]} with each subpage's full name, in List() order.
    [Fact]
    public void Notes_Subpages_FullNames()
    {
        Fixture();
        Directory.CreateDirectory(Path.Combine(root, "A"));
        File.WriteAllText(Path.Combine(root, "A", "Child.md"), "c");

        Assert.Equal(
            "{\"notes\":[{\"name\":\"A\",\"locked\":false},{\"name\":\"A/Child\",\"locked\":false},{\"name\":\"B\",\"locked\":false},{\"name\":\"Secret\",\"locked\":true}]}\n",
            Run("notes", "--json").Out);
        Result shown = Run("note", "show", "a/child", "--json");
        Assert.Equal("{\"name\":\"A/Child\",\"text\":\"c\"}\n", shown.Out);
    }

    // AC-6 ---------------------------------------------------------------------------------------

    [Fact]
    public void NoteShow_StoredNameAndCleanedText_NoNewlineAdded()
    {
        WriteNote("Plan", "line1\r\n\tline2");

        Result result = Run("note", "show", "plan.md", "--json");

        Assert.Equal("{\"name\":\"Plan\",\"text\":\"line1\\n    line2\"}\n", result.Out);
        AssertDocument(result);
    }

    [Fact]
    public void NoteShow_MissingOrSensitive_IsThePlainError()
    {
        Fixture();

        AssertError(Run("note", "show", "Nope", "--json"), 1, "No note named Nope.");
        AssertError(Run("note", "show", "secret", "--json"), 1, Cli.SensitiveText("Secret"));
    }

    // AC-7 ---------------------------------------------------------------------------------------

    [Fact]
    public void Tags_AllTagsRaw()
    {
        Fixture();
        WriteNote("C", "#[two words]\n");

        Assert.Equal(
            "{\"tags\":[{\"tag\":\"home\",\"count\":1},{\"tag\":\"two words\",\"count\":1},{\"tag\":\"work\",\"count\":2}]}\n",
            Run("tags", "--json").Out);
    }

    [Theory]
    [InlineData("#work")]
    [InlineData("Work")]
    public void Tags_OneTag_NormalisedWithNotes(string tag)
    {
        Fixture();

        Assert.Equal("{\"tag\":\"work\",\"notes\":[\"A\",\"B\"]}\n", Run("tags", tag, "--json").Out);
    }

    [Fact]
    public void Tags_UnknownTag_EmptyNotes_ExitZero()
    {
        Fixture();

        Result result = Run("tags", "nope", "--json");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.Equal("{\"tag\":\"nope\",\"notes\":[]}\n", result.Out);
    }

    // AC-8 ---------------------------------------------------------------------------------------

    [Fact]
    public void Links_DirectionMissingAndSharedTags()
    {
        Fixture();
        WriteNote("C", "[[a]]\n#home\n");

        Assert.Equal(
            "{\"note\":\"A\",\"links\":["
                + "{\"note\":\"B\",\"direction\":\"to\",\"missing\":false,\"sharedTags\":[\"work\"]},"
                + "{\"note\":\"C\",\"direction\":\"from\",\"missing\":false,\"sharedTags\":[]},"
                + "{\"note\":\"Ghost\",\"direction\":\"to\",\"missing\":true,\"sharedTags\":[]}]}\n",
            Run("links", "a", "--json").Out);
        Assert.Equal(
            "{\"note\":\"B\",\"links\":["
                + "{\"note\":\"A\",\"direction\":\"from\",\"missing\":false,\"sharedTags\":[\"work\"]},"
                + "{\"note\":\"C\",\"direction\":\"none\",\"missing\":false,\"sharedTags\":[\"home\"]}]}\n",
            Run("links", "B", "--json").Out);
    }

    [Fact]
    public void Links_BothWays()
    {
        WriteNote("A", "[[B]]");
        WriteNote("B", "[[A]]");

        Assert.Equal(
            "{\"note\":\"A\",\"links\":[{\"note\":\"B\",\"direction\":\"both\",\"missing\":false,\"sharedTags\":[]}]}\n",
            Run("links", "A", "--json").Out);
    }

    [Fact]
    public void Links_NoLinks_EmptyArray()
    {
        WriteNote("Alone", "nothing");

        Assert.Equal("{\"note\":\"Alone\",\"links\":[]}\n", Run("links", "alone", "--json").Out);
    }

    // AC-9 ---------------------------------------------------------------------------------------

    [Fact]
    public void Current_FullDocument()
    {
        Fixture();
        WriteIcs(
            "work.ics",
            "BEGIN:VCALENDAR\r\n"
                + "BEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20261029\r\nEND:VEVENT\r\n"
                + "BEGIN:VEVENT\r\nSUMMARY:Review\r\nLOCATION:Room 1\r\nDTSTART:20261029T100000Z\r\nDTEND:20261029T110000Z\r\nEND:VEVENT\r\n"
                + "END:VCALENDAR\r\n");
        WriteTodos(("2026-10-28", "\"late #work\""), ("2026-10-29", "\"now\""), ("2026-10-30", "\"later\""));

        Result result = Run("current", "--json");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.Equal(
            "{\"today\":\"2026-10-29\",\"appointments\":["
                + "{\"start\":\"2026-10-29T00:00:00\\u002B01:00\",\"end\":\"2026-10-30T00:00:00\\u002B01:00\",\"allDay\":true,\"title\":\"\",\"location\":null},"
                + "{\"start\":\"2026-10-29T11:00:00\\u002B01:00\",\"end\":\"2026-10-29T12:00:00\\u002B01:00\",\"allDay\":false,\"title\":\"Review\",\"location\":\"Room 1\"}],"
                + "\"calendar\":{\"status\":\"notConfigured\",\"cachedAt\":null,\"unreadableFiles\":[]},"
                + "\"due\":[{\"due\":\"2026-10-28\",\"text\":\"late #work\",\"overdue\":true},{\"due\":\"2026-10-29\",\"text\":\"now\",\"overdue\":false}],"
                + "\"linkedNotes\":[{\"name\":\"A\",\"tags\":[\"work\"]},{\"name\":\"B\",\"tags\":[\"work\"]}]}\n",
            result.Out);
        AssertDocument(result);
    }

    [Fact]
    public void Current_Empty_HasNoHintTexts()
    {
        Directory.CreateDirectory(root);

        Result result = Run("current", "--json");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.Equal(
            "{\"today\":\"2026-10-29\",\"appointments\":[],"
                + "\"calendar\":{\"status\":\"notConfigured\",\"cachedAt\":null,\"unreadableFiles\":[]},\"due\":[],\"linkedNotes\":[]}\n",
            result.Out);
    }

    [Fact]
    public void Current_FreshFeed_HasCachedAtInTheZone()
    {
        new CalendarLink(Directory.CreateDirectory(root).FullName).Set(FakeHttpHandler.Link);

        Result result = Run("current", "--json");

        JsonElement document = AssertDocument(result);
        Assert.Equal(string.Empty, result.Err);
        Assert.Equal(
            "{\"status\":\"fresh\",\"cachedAt\":\"2026-10-29T10:00:00\\u002B01:00\",\"unreadableFiles\":[]}",
            document.GetProperty("calendar").GetRawText());
        Assert.Equal("Standup", document.GetProperty("appointments")[0].GetProperty("title").GetString());
        Assert.Equal("2026-10-29T10:00:00+01:00", document.GetProperty("calendar").GetProperty("cachedAt").GetString());
        Assert.Equal("2026-10-29T09:00:00+01:00", document.GetProperty("appointments")[0].GetProperty("start").GetString());
        Assert.DoesNotContain(FakeHttpHandler.PathToken, result.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeHttpHandler.QueryToken, result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_StaleFeed_StatusAndCacheTime_NoOfflineText()
    {
        new CalendarLink(Directory.CreateDirectory(root).FullName).Set(FakeHttpHandler.Link);
        string cache = Path.Combine(root, CalendarFeed.CacheFileName);
        File.WriteAllText(cache, CalendarFeedTests.Ics);
        File.SetLastWriteTimeUtc(cache, new DateTime(2026, 10, 28, 17, 45, 0, DateTimeKind.Utc));
        http = FakeHttpHandler.Throw();

        Result result = Run("current", "--json");

        JsonElement document = AssertDocument(result);
        Assert.Equal(string.Empty, result.Err);
        Assert.Equal(
            "{\"status\":\"stale\",\"cachedAt\":\"2026-10-28T18:45:00\\u002B01:00\",\"unreadableFiles\":[]}",
            document.GetProperty("calendar").GetRawText());
        Assert.DoesNotContain("offline", result.Out, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("network", result.Out, StringComparison.OrdinalIgnoreCase);
    }

    // AC-10 --------------------------------------------------------------------------------------

    [Fact]
    public void Current_Problems_StillOneDocument_SameStderrAndExit()
    {
        WriteIcs("bad.ics", "not a calendar");
        new CalendarLink(Directory.CreateDirectory(root).FullName).Set(FakeHttpHandler.Link);
        http = FakeHttpHandler.Status(HttpStatusCode.InternalServerError);
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), "{ bad");

        Result result = Run("current", "--json");

        JsonElement document = AssertDocument(result, Cli.ExitError);
        Assert.Equal(
            ["Can't read calendar/bad.ics", "Can't download the Exchange calendar (HTTP 500).", "Can't read todos.json."],
            result.ErrLines);
        Assert.Equal(
            "{\"status\":\"failed\",\"cachedAt\":null,\"unreadableFiles\":[\"bad.ics\"]}",
            document.GetProperty("calendar").GetRawText());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("due").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.GetProperty("linkedNotes").ValueKind);
        Assert.DoesNotContain(FakeHttpHandler.PathToken, result.Out + result.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeHttpHandler.QueryToken, result.Out + result.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("HTTP 500", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_BadContextDb_LinkedNotesNull()
    {
        WriteNote("Plan", "#work");
        WriteTodos(("2026-10-29", "\"x #work\""));
        BreakIndex();

        Result result = Run("current", "--json");

        JsonElement document = AssertDocument(result, Cli.ExitError);
        Assert.Equal(["Can't read context.db."], result.ErrLines);
        Assert.Equal(
            "[{\"due\":\"2026-10-29\",\"text\":\"x #work\",\"overdue\":false}]",
            document.GetProperty("due").GetRawText());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("linkedNotes").ValueKind);
    }

    [Fact]
    public void Current_BadContextDb_NothingDue_IsLikePlain()
    {
        Directory.CreateDirectory(root);
        BreakIndex();

        Result plain = Run("current");
        Result result = Run("current", "--json");

        Assert.Equal((plain.Code, plain.Err), (result.Code, result.Err));
        Assert.EndsWith("\"due\":[],\"linkedNotes\":[]}\n", result.Out, StringComparison.Ordinal);
    }

    // AC-11 --------------------------------------------------------------------------------------

    public static TheoryData<string[], int, string> Errors() => new()
    {
        { ["todos", "x", "--json"], Cli.ExitUsage, "todos takes no arguments. Run 'context help'." },
        { ["notes", "x", "--json"], Cli.ExitUsage, "notes takes no arguments. Run 'context help'." },
        { ["current", "x", "--json"], Cli.ExitUsage, "current takes no arguments. Run 'context help'." },
        { ["links", "--json"], Cli.ExitUsage, "Type: context links <note>. Run 'context help'." },
        { ["note", "--json"], Cli.ExitUsage, "Type: context note show <name>. Run 'context help'." },
        { ["nope", "--json"], Cli.ExitUsage, "Unknown command: nope. Run 'context help'." },
        { ["links", "Nope", "--json"], Cli.ExitError, "No note named Nope." },
        { ["links", "Secret", "--json"], Cli.ExitError, Cli.SensitiveText("Secret") },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void ReadErrors_AreThePlainLine_StdoutEmpty(string[] args, int code, string line)
    {
        Fixture();

        string[] plain = [.. args.Where(a => a != "--json")];
        Result before = Run(plain);
        Result result = Run(args);

        AssertError(result, code, line);
        Assert.Equal((before.Code, before.Err), (result.Code, result.Err));
    }

    [Fact]
    public void UnreadableData_IsThePlainLine_StdoutEmpty()
    {
        Fixture();
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), "{ bad");
        BreakIndex();

        AssertError(Run("todos", "--json"), 1, "Can't read todos.json.");
        AssertError(Run("tags", "--json"), 1, "Can't read context.db.");
        AssertError(Run("tags", "work", "--json"), 1, "Can't read context.db.");
        AssertError(Run("links", "A", "--json"), 1, "Can't read context.db.");
    }

    // AC-12 --------------------------------------------------------------------------------------

    public static TheoryData<string[]> NotJson() => new()
    {
        { ["help", "--json"] },
        { ["--help", "--json"] },
        { ["--json", "-h"] },
        { ["capture", "hello", "--json"] },
        { ["todo", "add", "today", "x", "--json"] },
        { ["todo", "done", "late", "--json"] },
        { ["todo", "--json"] },
        { ["import", "x.md", "--json"] },
        { ["export", "A", "out.md", "--json"] },
    };

    [Theory]
    [MemberData(nameof(NotJson))]
    public void HelpAndWriteCommands_AreAUsageError_NoFolderCreated(string[] args)
    {
        AssertError(Run(args), Cli.ExitUsage, JsonOnly);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [MemberData(nameof(NotJson))]
    public void HelpAndWriteCommands_WriteNothing(string[] args)
    {
        Fixture();
        string source = Path.Combine(outside, "Idea.md");
        File.WriteAllText(source, "# Idea\n");
        string target = Path.Combine(outside, "out.md");
        string[] withPaths = [.. args.Select(a => a == "x.md" ? source : a == "out.md" ? target : a)];
        Dictionary<string, string> before = Snapshot();

        AssertError(Run(withPaths), Cli.ExitUsage, JsonOnly);

        Assert.Equal(before, Snapshot());
        Assert.False(File.Exists(target));
    }

    // AC-13 --------------------------------------------------------------------------------------

    [Fact]
    public void Todos_HostileText_ParsesBackToTheCleanedValue()
    {
        string raw = "a\u0001b \"q\" back\\slash <b>&amp; é中\nnext\u0085end\u007f";
        WriteTodos(("2026-10-29", JsonSerializer.Serialize(raw)));

        JsonElement document = AssertDocument(Run("todos", "--json"));

        string expected = SafeText.Clean(raw).Replace('\n', ' ');
        Assert.Equal(expected, document.GetProperty("todos")[0].GetProperty("text").GetString());
        Assert.Contains('�', expected);
    }

    [Fact]
    public void NoteShow_HostileText_KeepsLineBreaks()
    {
        string raw = "# T\n\u001b[31mred\u001b[0m \"q\" \\ <script> ü\r\nend";
        WriteNote("T", raw);

        Result result = Run("note", "show", "T", "--json");

        JsonElement document = AssertDocument(result);
        Assert.Equal(SafeText.Clean(raw), document.GetProperty("text").GetString());
        Assert.DoesNotContain("<", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void NonAsciiNoteName_ParsesBack()
    {
        string name = "Café & Co's 日本";
        WriteNote(name, $"#work [[{name}]]");
        WriteTodos(("2026-10-29", "\"x #work\""));

        string[] outputs = [Run("notes", "--json").Out, Run("current", "--json").Out, Run("tags", "work", "--json").Out];

        Assert.Equal(name, AssertDocument(new Result(0, outputs[0], string.Empty)).GetProperty("notes")[0].GetProperty("name").GetString());
        Assert.Equal(name, AssertDocument(new Result(0, outputs[1], string.Empty)).GetProperty("linkedNotes")[0].GetProperty("name").GetString());
        Assert.Equal(name, AssertDocument(new Result(0, outputs[2], string.Empty)).GetProperty("notes")[0].GetString());
    }

    // AC-14 --------------------------------------------------------------------------------------

    [Fact]
    public void Help_MentionsJsonAndItsCommands()
    {
        string help = Run("help").Out;

        string line = Assert.Single(help.Split('\n'), l => l.Contains("--json", StringComparison.Ordinal));
        Assert.StartsWith("context ", line, StringComparison.Ordinal);
        foreach (string command in new[] { "version", "todos", "notes", "note show", "tags", "links", "current" })
        {
            Assert.Contains(command, line, StringComparison.Ordinal);
        }
    }

    // AC-15 --------------------------------------------------------------------------------------

    private static string RepoFile(string name)
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Context.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, name);
    }

    [Fact]
    public void Readme_HasPowerShellExamples_ForReadCommandsOnly()
    {
        string readme = File.ReadAllText(RepoFile("README.md"), Encoding.UTF8);

        foreach (string command in new[] { "todos", "notes", "current" })
        {
            Assert.Contains($"context {command} --json | ConvertFrom-Json", readme, StringComparison.Ordinal);
        }

        string[] reads = ["version", "todos", "notes", "note show", "tags", "links", "current"];
        string[] jsonLines = [.. readme.Split('\n').Where(l => l.Contains("--json", StringComparison.Ordinal) && l.Contains("context ", StringComparison.Ordinal))];
        Assert.NotEmpty(jsonLines);
        foreach (string line in jsonLines)
        {
            int at = line.IndexOf("context ", StringComparison.Ordinal);
            string after = line[(at + "context ".Length)..];
            Assert.Contains(reads, r => after.StartsWith(r, StringComparison.Ordinal));
        }
    }

    // 156 AC-8: people pins its field names and order.
    [Fact]
    public void People_FieldsInOrder()
    {
        WriteNote("A", "@[Anna Schmidt] @[bob]");
        WriteNote("B", "@[ANNA schmidt]");

        Result result = Run("people", "--json");

        AssertDocument(result);
        Assert.Equal(
            "{\"people\":[{\"person\":\"ANNA schmidt\",\"count\":2},{\"person\":\"bob\",\"count\":1}]}\n",
            result.Out);
    }

    // 156 AC-8: person pins its field names and order; a missing location is null and times use the usual format.
    [Fact]
    public void Person_FieldsInOrder()
    {
        WriteNote("A", "@[Anna]");
        WriteIcs("a.ics", "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Call @[Anna]\r\nDTSTART:20261027T090000Z\r\n"
            + "DTEND:20261027T100000Z\r\nLOCATION:Room 1\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nSUMMARY:Off\r\n"
            + "DTSTART;VALUE=DATE:20261028\r\nATTENDEE;CN=anna:mailto:anna@example.com\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");

        Result result = Run("person", "anna", "--json");

        AssertDocument(result);
        Assert.Equal(
            "{\"person\":\"Anna\",\"notes\":[\"A\"],\"appointments\":["
                + "{\"start\":\"2026-10-28T00:00:00\\u002B01:00\",\"end\":\"2026-10-29T00:00:00\\u002B01:00\",\"allDay\":true,"
                + "\"title\":\"Off\",\"location\":null},"
                + "{\"start\":\"2026-10-27T10:00:00\\u002B01:00\",\"end\":\"2026-10-27T11:00:00\\u002B01:00\",\"allDay\":false,"
                + "\"title\":\"Call @[Anna]\",\"location\":\"Room 1\"}]}\n",
            result.Out);
        Assert.DoesNotContain("example.com", result.Out, StringComparison.Ordinal);
    }

    // 156 AC-8: no mentions gives empty arrays; a bad index is the usual error line.
    [Fact]
    public void Person_NoMentions_EmptyArrays_BadDbIsError()
    {
        WriteNote("A", "x");

        Assert.Equal("{\"person\":\"Carl\",\"notes\":[],\"appointments\":[]}\n", Run("person", "Carl", "--json").Out);

        BreakIndex();
        AssertError(Run("person", "Carl", "--json"), 1, "Can't read context.db.");
        AssertError(Run("people", "--json"), 1, "Can't read context.db.");
    }
}
