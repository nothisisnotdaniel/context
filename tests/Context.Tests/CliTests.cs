using System.Net;
using System.Reflection;
using System.Text;

namespace Context.Tests;

public sealed class CliTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid DecisionFolderGuid = new("11111111-2222-3333-4444-555555555555");

    private readonly string parent;
    private readonly string root;
    private readonly string outside;
    private readonly string times;
    private FakeHttpHandler http = FakeHttpHandler.Ok(CalendarFeedTests.Ics);

    // 381: the local time the Cli sees, and the Times runner meeting --book uses (null: no runner given).
    private DateTime clock = Now;
    private FakeTimesRunner? runner = new FakeTimesRunner();

    public CliTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-cli-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        outside = Path.Combine(parent, "outside");
        times = Path.Combine(parent, "Times");
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
        // 349: a new decision-folder.json gets the prefix ab12 and DecisionFolderGuid, never a random one.
        Queue<int> prefix = new([10, 11, 1, 2]);
        Cli cli = new Cli(stdout, stderr, root, () => Today, () => clock, http, TimeZoneInfo.Utc, times, timesRunner: runner)
        {
            DecisionRandom = _ => prefix.Count > 0 ? prefix.Dequeue() : 0,
            DecisionGuid = () => DecisionFolderGuid,
        };
        int code = cli.Run(args);
        return new Result(code, stdout.ToString(), stderr.ToString());
    }

    private void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".md"), text);
    }

    private string Read(string name) => File.ReadAllText(Path.Combine(root, name));

    // Every file under the root with its bytes, to prove nothing was written. The record of the one-off todo marker
    // conversion (job 329; its own tests check it) is left out: the first todo command of a data folder writes it.
    private Dictionary<string, string> Snapshot()
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        string record = TodoMarkerConversion.RecordPath(root);
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !string.Equals(f, record, StringComparison.Ordinal))
            .ToDictionary(f => Path.GetRelativePath(root, f), f => Convert.ToBase64String(File.ReadAllBytes(f)));
    }

    private static void AssertError(Result result, int code, string line)
    {
        Assert.Equal(code, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.Equal([line], result.ErrLines);
    }

    private static void AssertUsage(Result result)
    {
        Assert.Equal(Cli.ExitUsage, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.Single(result.ErrLines);
        Assert.EndsWith("Run 'context help'.", result.ErrLines[0], StringComparison.Ordinal);
    }

    // AC-1, AC-2 ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("HELP")]
    public void Help_ListsEveryCommand_WithoutTouchingTheFolder(string arg)
    {
        Result result = Run(arg);

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Err);
        foreach (string usage in new[]
        {
            "capture <text...>", "todos", "todo add <yyyy-MM-dd|today|tomorrow> [HH:mm] <text...>",
            "todo add every <weekday|day|month> <text...>", "todo done <text...>", "notes",
            "note show <name...>", "tags [tag...]", "find <attribute> <value...>",
            "search [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--times] <text...>", "current", "review", "new <title...> [--template <name>]", "import <path>",
            "export <note> <path>", "version", "dayend [today|yyyy-MM-dd]", "decisions [--full] [tag...]", "decisions <reference>",
        })
        {
            Assert.Contains($"context {usage} ", result.Out, StringComparison.Ordinal);
        }

        Assert.False(Directory.Exists(root));
    }

    // 132 AC-4: help says nothing about a secret to give, and every line is a command line.
    [Fact]
    public void Help_MentionsNoSecretAndOnlyListsCommands()
    {
        Result result = Run("help");

        Assert.DoesNotContain("pass", result.Out, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("encrypt", result.Out, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stdin", result.Out, StringComparison.OrdinalIgnoreCase);
        Assert.All(
            result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => Assert.StartsWith("context ", line, StringComparison.Ordinal));
    }

    // 132 AC-4: a leftover key file changes nothing: every command runs as on any folder, with nothing on stderr,
    // and the file stays as it was.
    [Fact]
    public void LeftoverKeyFile_IsIgnored_CommandsRunAndStderrStaysEmpty()
    {
        WriteNote("Plan", "# Plan\n#work\n");
        string leftover = Path.Combine(root, RemovedFeature.KeyFileName);
        File.WriteAllText(leftover, "{ \"version\": 1 }");
        string target = Path.Combine(outside, "plan.md");

        Result notes = Run("notes");
        Result[] others =
        [
            Run("note", "show", "Plan"), Run("todo", "add", "today", "x"), Run("todos"), Run("tags"),
            Run("capture", "hello"), Run("export", "Plan", target), Run("version"), Run("help"),
        ];

        Assert.Equal((0, "Plan\n", string.Empty), (notes.Code, notes.Out, notes.Err));
        Assert.All(others, r => Assert.Equal((0, string.Empty), (r.Code, r.Err)));
        Assert.Equal("{ \"version\": 1 }", File.ReadAllText(leftover));
        Assert.True(File.Exists(Path.Combine(root, TagIndex.FileName)));
        Assert.Equal("# Plan\n#work\n", File.ReadAllText(target));
    }

    // 132 AC-4: the command line has no way to read stdin or the environment: no constructor takes a reader or an
    // environment lookup, and the source names neither (Console. is checked by CliSource_MentionsNoSpectreAndNoConsole).
    [Fact]
    public void Cli_CannotReadStdinOrTheEnvironment()
    {
        ConstructorInfo[] constructors = typeof(Cli).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        FieldInfo[] fields = typeof(Cli).GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.NotEmpty(constructors);
        Assert.DoesNotContain(
            constructors.SelectMany(c => c.GetParameters()),
            p => typeof(TextReader).IsAssignableFrom(p.ParameterType) || p.ParameterType == typeof(Func<string, string?>));
        Assert.DoesNotContain(fields, f => typeof(TextReader).IsAssignableFrom(f.FieldType));
        string source = CliSource();
        Assert.DoesNotContain("TextReader", source, StringComparison.Ordinal);
        Assert.DoesNotContain("stdin.", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Environment.", source, StringComparison.Ordinal);
        Assert.DoesNotContain(RemovedFeature.Variable, source, StringComparison.OrdinalIgnoreCase);
    }

    private const string OptionRemoved = "That option was removed. Run 'context help'.\n";

    // 132 AC-4: an unknown option is a usage error: exit 2, not echoed, folder untouched. 273 AC-4: the removed
    // secret option in command position now has its own line.
    [Theory]
    [InlineData("Unknown command: that option. Run 'context help'.\n", "--frobnicate")]
    [InlineData(OptionRemoved, RemovedFeature.Option + "-stdin", "notes")]
    [InlineData(OptionRemoved, RemovedFeature.Option, "topSecret55aa", "notes")]
    [InlineData(OptionRemoved, RemovedFeature.Option + "=topSecret55aa", "notes")]
    [InlineData(OptionRemoved, RemovedFeature.OptionUpper + "=topSecret55aa", "notes")]
    public void RemovedOptions_AreUsageErrorsLikeAnyUnknownOption(string expected, params string[] args)
    {
        Result result = Run(args);

        AssertUsage(result);
        Assert.Equal(expected, result.Err);
        Assert.DoesNotContain("topSecret55aa", result.Out + result.Err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(root));
    }

    // 273 AC-1, AC-2, AC-5: after a command and among its arguments the removed secret option is refused, nothing
    // is echoed, and the data folder is neither created nor changed.
    [Theory]
    [InlineData("capture", RemovedFeature.Option + "=topSecret55aa")]
    [InlineData("capture", "hello", RemovedFeature.Option, "topSecret55aa")]
    [InlineData("capture", RemovedFeature.Option + "-stdin")]
    [InlineData("todo", "add", "today", RemovedFeature.OptionUpper + "=topSecret55aa")]
    [InlineData("note", "show", RemovedFeature.Option + "=topSecret55aa")]
    [InlineData("search", RemovedFeature.Option + "=x")]
    [InlineData("notes", RemovedFeature.Option + "-stdin")]
    public void RemovedSecretOption_AfterACommand_IsRefused_AndNothingIsWritten(params string[] args)
    {
        Result missing = Run(args);

        Assert.Equal((Cli.ExitUsage, string.Empty, OptionRemoved), (missing.Code, missing.Out, missing.Err));
        Assert.DoesNotContain("topSecret55aa", missing.Out + missing.Err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(root));

        WriteNote("inbox", "# inbox\n- 2026-10-28 08:00 old\n");
        Dictionary<string, string> before = Snapshot();

        Result existing = Run(args);

        Assert.Equal((Cli.ExitUsage, string.Empty, OptionRemoved), (existing.Code, existing.Out, existing.Err));
        Assert.DoesNotContain("topSecret55aa", existing.Out + existing.Err, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot());
    }

    // 273 AC-1: with a folder but no inbox, the inbox is not created.
    [Fact]
    public void RemovedSecretOption_InCapture_DoesNotCreateAMissingInbox()
    {
        WriteNote("Plan", "# Plan\n");
        Dictionary<string, string> before = Snapshot();

        Result result = Run("capture", RemovedFeature.Option + "=topSecret55aa");

        Assert.Equal((Cli.ExitUsage, string.Empty, OptionRemoved), (result.Code, result.Out, result.Err));
        Assert.False(File.Exists(Path.Combine(root, "inbox.md")));
        Assert.Equal(before, Snapshot());
    }

    // 273 AC-3, AC-5: the check comes before help, version, standup and the json rules.
    [Theory]
    [InlineData("help", RemovedFeature.Option, "x")]
    [InlineData("version", RemovedFeature.Option + "=x")]
    [InlineData("standup", RemovedFeature.Option)]
    [InlineData("notes", "--json", RemovedFeature.Option + "=x")]
    [InlineData(RemovedFeature.Option + "=x", "--json")]
    public void RemovedSecretOption_IsCheckedBeforeEveryOtherRule(params string[] args)
    {
        Result result = Run(args);

        Assert.Equal((Cli.ExitUsage, string.Empty, OptionRemoved), (result.Code, result.Out, result.Err));
        Assert.DoesNotContain("topSecret55aa", result.Out + result.Err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(root));
    }

    // 273 AC-6: an argument that contains the name without starting with it is still capture text.
    [Theory]
    [InlineData("my", "x" + RemovedFeature.Option, "note")]
    [InlineData("-pass" + "phrase")]
    public void Capture_TextThatOnlyContainsTheRemovedOption_IsStillCaptured(params string[] words)
    {
        Result result = Run(["capture", .. words]);

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.Contains($"- 2026-10-29 09:00 {string.Join(' ', words)}\n", Read("inbox.md"), StringComparison.Ordinal);
    }

    // 273 AC-7: the conventions file says the removed secret option is refused anywhere.
    [Fact]
    public void ClaudeMd_SaysTheRemovedSecretOptionIsRefusedAnywhere()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Context.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        string text = File.ReadAllText(Path.Combine(dir.FullName, "CLAUDE.md"));
        Assert.Contains("the removed secret option is a usage error in any position", text, StringComparison.Ordinal);
    }

    // 132 AC-7: a note that starts with the old magic bytes is shown like any other note.
    [Fact]
    public void NoteShow_FileStartingWithTheOldMagicBytes_IsShownAsText()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "Old.md"), [.. RemovedFeature.Magic, .. "old-marker"u8]);
        File.WriteAllBytes(Path.Combine(root, TodoStore.FileName), [.. RemovedFeature.Magic, 1, 2]);

        Result shown = Run("note", "show", "Old");
        Result listed = Run("notes");
        Result todos = Run("todos");

        Assert.Equal((0, string.Empty), (shown.Code, shown.Err));
        Assert.Contains("CTXE", shown.Out, StringComparison.Ordinal);
        Assert.Contains("old-marker", shown.Out, StringComparison.Ordinal);
        Assert.DoesNotContain('\0', shown.Out);
        Assert.Equal((0, "Old\n", string.Empty), (listed.Code, listed.Out, listed.Err));
        AssertError(todos, 1, "Can't read todos.json.");
    }

    // 131 AC-10
    [Theory]
    [InlineData("version")]
    [InlineData("--version")]
    [InlineData("VERSION")]
    [InlineData("--Version")]
    public void Version_PrintsOneLine_WithoutTouchingTheFolder(string arg)
    {
        Result result = Run(arg);

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Err);
        Assert.Equal("Context 0.1.0\n", result.Out);
        Assert.Equal("Context " + AppVersion.Current + "\n", result.Out);
        Assert.False(Directory.Exists(root));
    }

    // 131 AC-10
    [Fact]
    public void Help_ListsVersion()
    {
        Assert.Contains("context version ", Run("help").Out, StringComparison.Ordinal);
    }

    // 131 AC-8: the release smoke check's second command succeeds on a data folder that does not exist yet.
    [Fact]
    public void Tags_OnAFreshFolder_ExitsZero()
    {
        Result result = Run("tags");

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Err);
        Assert.Equal(string.Empty, result.Out);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("/notes")]
    public void UnknownCommand_IsUsageError_AndNeverTouchesTheFolder(string command)
    {
        Result result = Run(command, "x");

        AssertUsage(result);
        Assert.False(Directory.Exists(root));
    }

    // 173 AC-1, AC-2: links, people, person and types are gone: each is the usual unknown command, with or without
    // --json anywhere, nothing on stdout, and the data folder is not created.
    [Theory]
    [InlineData("links", "A")]
    [InlineData("people")]
    [InlineData("person", "Anna")]
    [InlineData("types")]
    [InlineData("PEOPLE")]
    [InlineData("Links", "A", "B")]
    [InlineData("--json", "people")]
    [InlineData("people", "--json")]
    [InlineData("--json", "links", "A")]
    [InlineData("links", "A", "--json")]
    [InlineData("--json", "person", "Anna")]
    [InlineData("person", "Anna", "--json")]
    [InlineData("--json", "types")]
    [InlineData("TYPES", "--JSON")]
    public void RemovedMirrorCommands_AreUnknownCommands_FolderNotCreated(params string[] args)
    {
        string shown = args.First(a => !a.Equals("--json", StringComparison.OrdinalIgnoreCase));

        Result result = Run(args);

        Assert.Equal(Cli.ExitUsage, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.Equal([$"Unknown command: {shown}. Run 'context help'."], result.ErrLines);
        Assert.False(Directory.Exists(root));
    }

    // 173 AC-3: help lists none of the removed commands, and its --json row names exactly the JSON commands.
    [Fact]
    public void Help_ListsNoRemovedMirrorCommand_JsonRowIsExact()
    {
        string[] lines = Run("help").Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (string removed in new[] { "links", "people", "person", "types" })
        {
            Assert.DoesNotContain(lines, l => l.StartsWith($"context {removed}", StringComparison.Ordinal));
        }

        string json = Assert.Single(lines, l => l.StartsWith("context <command> --json", StringComparison.Ordinal));
        Assert.EndsWith(
            "Print one JSON object: version, todos, notes, note show, tags, find, search, current, review, item.",
            json,
            StringComparison.Ordinal);
    }

    // 145 AC-2: without arguments there is nothing to start; it is the usage error and the folder is not created.
    [Fact]
    public void NoArguments_IsUsageError_AndNeverTouchesTheFolder()
    {
        Result result = Run();

        Assert.Equal(2, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.Equal("No command given. Run 'context help'.\n", result.Err);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Commands_AreMatchedIgnoringCase()
    {
        WriteNote("Plan", "x");

        Result result = Run("NOTES");

        Assert.Equal(0, result.Code);
        Assert.Equal("Plan\n", result.Out);
    }

    // AC-3 ---------------------------------------------------------------------------------------

    // 174 AC-5: the command line keeps no versions when it changes a note.
    [Fact]
    public void Capture_KeepsNoVersion()
    {
        Run("capture", "first");
        Run("capture", "second");

        Assert.Contains("second", Read("inbox.md"), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, NoteHistory.FolderName)));
    }

    [Fact]
    public void Capture_JoinsArgumentsAndAppendsToInbox()
    {
        Result first = Run("capture", "call", "Anna\nabout", "  x ");
        Result second = Run("Capture", "second");

        Assert.Equal((0, "Captured.\n", string.Empty), (first.Code, first.Out, first.Err));
        Assert.Equal(0, second.Code);
        Assert.Equal(
            "# inbox\n- 2026-10-29 09:00 call Anna about   x\n- 2026-10-29 09:00 second\n", Read("inbox.md"));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [Theory]
    [InlineData]
    [InlineData(" ", "\n", "\t")]
    public void Capture_EmptyText_IsUsageError(params string[] words)
    {
        AssertUsage(Run(["capture", .. words]));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Capture_OverThousandChars_IsUsageError()
    {
        AssertUsage(Run("capture", new string('a', 1001)));
        Assert.False(Directory.Exists(root));

        Assert.Equal(0, Run("capture", new string('a', 1000)).Code);
    }

    // AC-4, AC-5 ---------------------------------------------------------------------------------

    [Fact]
    public void TodoAdd_AddsAndTodosListsInOrder()
    {
        Result added = Run("todo", "add", "tomorrow", "buy", "milk");
        Run("todo", "ADD", "2026-10-01", "old\none");
        Run("todo", "add", "today", "call #work");

        Assert.Equal((0, "Added: 2026-10-30 buy milk\n", string.Empty), (added.Code, added.Out, added.Err));
        Result list = Run("todos");
        Assert.Equal(0, list.Code);
        Assert.Equal("2026-10-01\told one\n2026-10-29\tcall #work\n2026-10-30\tbuy milk\n", list.Out);
    }

    [Fact]
    public void Todos_Empty_PrintsNothing()
    {
        Result result = Run("todos");

        Assert.Equal((0, string.Empty, string.Empty), (result.Code, result.Out, result.Err));
    }

    [Theory]
    [InlineData("todo")]
    [InlineData("todo", "list")]
    [InlineData("todo", "add")]
    [InlineData("todo", "add", "today")]
    [InlineData("todo", "add", "someday", "text")]
    [InlineData("todo", "done")]
    [InlineData("todo", "done", "")]
    [InlineData("todo", "done", " ", "  ")]
    [InlineData("todos", "extra")]
    public void Todo_BadArguments_AreUsageErrors(params string[] args)
    {
        AssertUsage(Run(args));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void TodoAdd_TooLong_IsUsageError()
    {
        AssertUsage(Run("todo", "add", "today", new string('a', 201)));
        Assert.Equal(0, Run("todo", "add", "today", new string('a', 200)).Code);
    }

    // 144 AC-8: the arguments, joined with one space, pick the todo by its text.
    [Theory]
    [InlineData("sooner")]
    [InlineData("SOON")]
    [InlineData(" sooner ")]
    public void TodoDone_RemovesThePickedTodo(string text)
    {
        Run("todo", "add", "2026-11-02", "later");
        Run("todo", "add", "2026-11-01", "sooner");

        Result result = Run("todo", "DONE", text);

        Assert.Equal((0, "Done: sooner\n", string.Empty), (result.Code, result.Out, result.Err));
        Assert.Equal("2026-11-02\tlater\n", Run("todos").Out);
    }

    // 144 AC-8
    [Fact]
    public void TodoDone_JoinsArgumentsWithOneSpace_ExactWinsOverContaining()
    {
        Run("todo", "add", "2026-11-01", "buy milk and bread");
        Run("todo", "add", "2026-11-02", "Buy milk");
        Run("todo", "add", "2026-11-03", "Buy milk");

        Result result = Run("todo", "done", "buy", "MILK");

        Assert.Equal((0, "Done: Buy milk\n", string.Empty), (result.Code, result.Out, result.Err));
        Assert.Equal("2026-11-01\tbuy milk and bread\n2026-11-03\tBuy milk\n", Run("todos").Out);
    }

    // 144 AC-8: a digit is text, not a position.
    [Fact]
    public void TodoDone_Digit_IsText()
    {
        Run("todo", "add", "2026-11-01", "Buy milk");
        Run("todo", "add", "2026-11-02", "Call Anna");
        Dictionary<string, string> before = SnapshotWithoutDb();

        AssertError(Run("todo", "done", "1"), 1, "No todo matches \"1\".");
        Assert.Equal(before, SnapshotWithoutDb());

        Run("todo", "add", "2026-11-03", "Read chapter 1");
        Result result = Run("todo", "done", "1");

        Assert.Equal((0, "Done: Read chapter 1\n", string.Empty), (result.Code, result.Out, result.Err));
        Assert.Equal("2026-11-01\tBuy milk\n2026-11-02\tCall Anna\n", Run("todos").Out);
    }

    // 144 AC-8
    [Fact]
    public void TodoDone_NoMatch_IsErrorAndWritesNothing()
    {
        Run("todo", "add", "today", "one");
        Dictionary<string, string> before = SnapshotWithoutDb();

        AssertError(Run("todo", "done", "two\u001b[31m", "three"), 1, "No todo matches \"two\uFFFD[31m three\".");
        Assert.Equal(before, SnapshotWithoutDb());
    }

    // 144 AC-8
    [Fact]
    public void TodoDone_SeveralMatches_IsErrorAndWritesNothing()
    {
        Run("todo", "add", "2026-11-01", "Call Anna");
        Run("todo", "add", "2026-11-02", "Call Ben");
        Run("todo", "add", "2026-11-03", "Buy milk");
        Dictionary<string, string> before = SnapshotWithoutDb();

        AssertError(Run("todo", "done", "call"), 1, "2 todos match \"call\". Type more of the todo's text.");
        Assert.Equal(before, SnapshotWithoutDb());
    }

    // 144 AC-8
    [Fact]
    public void TodoDone_NoTodos_IsErrorAndCreatesNothing()
    {
        AssertError(Run("todo", "done", "x"), 1, "There are no todos to mark done.");
        Assert.False(File.Exists(Path.Combine(root, TodoStore.FileName)));
    }

    // 168 AC-9: todo add every follows the repeat rules and prints what todo add prints.
    [Fact]
    public void TodoAddEvery_AddsRecurring_FirstDue_TodosUnchanged()
    {
        Result weekly = Run("todo", "add", "every", "Monday", "water", "plants");
        Result daily = Run("todo", "add", "EVERY", "day", "stretch");
        Result monthly = Run("todo", "add", "every", "month", "pay rent");

        Assert.Equal((0, "Added: 2026-11-02 water plants\n", string.Empty), (weekly.Code, weekly.Out, weekly.Err));
        Assert.Equal((0, "Added: 2026-10-29 stretch\n", string.Empty), (daily.Code, daily.Out, daily.Err));
        Assert.Equal((0, "Added: 2026-10-29 pay rent\n", string.Empty), (monthly.Code, monthly.Out, monthly.Err));
        Assert.Equal(
            [TodoRepeat.Daily, TodoRepeat.Monthly(29), TodoRepeat.Weekly(DayOfWeek.Monday)],
            new TodoStore(root).List().Select(t => t.Repeat));
        Assert.Equal("2026-10-29\tstretch\n2026-10-29\tpay rent\n2026-11-02\twater plants\n", Run("todos").Out);
        Assert.Equal(
            "{\"todos\":[{\"due\":\"2026-10-29\",\"time\":null,\"text\":\"stretch\",\"note\":null},{\"due\":\"2026-10-29\",\"time\":null,\"text\":\"pay rent\",\"note\":null},"
            + "{\"due\":\"2026-11-02\",\"time\":null,\"text\":\"water plants\",\"note\":null}]}\n",
            Run("todos", "--json").Out);
    }

    // 168 AC-9: a bad unit is a usage error with exit 2 and writes nothing.
    [Theory]
    [InlineData("week")]
    [InlineData("mon")]
    [InlineData("2")]
    public void TodoAddEvery_BadUnit_IsUsageError(string unit)
    {
        Result result = Run("todo", "add", "every", unit, "x");

        Assert.Equal(2, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.Equal(
            ["That repeat isn't valid. Type: context todo add every <weekday|day|month> <text>. Run 'context help'."],
            result.ErrLines);
        Assert.False(Directory.Exists(root));
    }

    // 168 AC-9: no unit or no text, or too long a text, is a usage error too.
    [Theory]
    [InlineData("todo", "add", "every")]
    [InlineData("todo", "add", "every", "day")]
    [InlineData("todo", "add", "every", "day", " ")]
    public void TodoAddEvery_NoText_IsUsageError(params string[] args)
    {
        AssertUsage(Run(args));
        Assert.False(Directory.Exists(root));
        AssertUsage(Run("todo", "add", "every", "day", new string('a', 201)));
    }

    // 168 AC-9: done on a recurring todo prints Done and the next due date, and the next todo is listed.
    [Fact]
    public void TodoDone_Recurring_PrintsNext()
    {
        Run("todo", "add", "every", "thursday", "water plants");
        Run("todo", "add", "today", "call Anna");

        Result result = Run("todo", "done", "water");

        Assert.Equal((0, "Done: water plants\nNext: 2026-11-05\n", string.Empty), (result.Code, result.Out, result.Err));
        Assert.Equal("2026-10-29\tcall Anna\n2026-11-05\twater plants\n", Run("todos").Out);
        Result plain = Run("todo", "done", "call");
        Assert.Equal((0, "Done: call Anna\n", string.Empty), (plain.Code, plain.Out, plain.Err));
    }

    // 293 AC-3: todo done appends the done-log line for a stored todo; stdout and the exit code are unchanged.
    [Fact]
    public void TodoDone_StoredTodo_AppendsDoneLogLine()
    {
        Run("todo", "add", "2026-10-28", "Send report #work");

        Result result = Run("todo", "done", "send report");

        Assert.Equal((0, "Done: Send report #work\n", string.Empty), (result.Code, result.Out, result.Err));
        Assert.Equal("# done\n- 2026-10-29 Send report #work (due 2026-10-28)\n", Read("done.md"));
    }

    // 293 AC-3: a note todo without due: appends a line with no due part.
    [Fact]
    public void TodoDone_NoteTodo_AppendsDoneLogLineWithoutDue()
    {
        WriteNote("Meeting", "# Meeting\n- { } call Anna\n");
        WriteNote("done", "# done\n- 2026-10-01 earlier");

        Result result = Run("todo", "done", "call anna");

        Assert.Equal((0, "Done: call Anna\n", string.Empty), (result.Code, result.Out, result.Err));
        Assert.Equal("# done\n- 2026-10-01 earlier\n- 2026-10-29 call Anna\n", Read("done.md"));
    }

    // 293 AC-6: a done.md that would grow past MaxEditBytes is left alone; the todo is done, stderr says so, exit 0.
    [Fact]
    public void TodoDone_DoneLogTooLarge_StderrAndExitZero()
    {
        Run("todo", "add", "2026-10-28", "Send report");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "done.md");
        byte[] full = Encoding.UTF8.GetBytes("# done\n" + new string('a', (int)NoteStore.MaxEditBytes - 8) + "\n");
        File.WriteAllBytes(path, full);

        Result result = Run("todo", "done", "send");

        Assert.Equal((0, "Done: Send report\n"), (result.Code, result.Out));
        Assert.Equal(["Couldn't add it to the done log."], result.ErrLines);
        Assert.Equal(full, File.ReadAllBytes(path));
        Assert.Equal(string.Empty, Run("todos").Out);
    }

    // 293 AC-11: a todo that can't be completed appends nothing to done.md.
    [Fact]
    public void TodoDone_NoMatch_AppendsNothing()
    {
        Run("todo", "add", "2026-10-28", "Send report");

        Assert.Equal(1, Run("todo", "done", "nothing like it").Code);
        Assert.False(File.Exists(Path.Combine(root, "done.md")));
    }

    // 178 AC-3, AC-4: a time after the date makes a timed todo; it shows in Added:, todos and --json.
    [Fact]
    public void TodoAdd_WithTime_AddsTimedTodo_ListsTime()
    {
        Result added = Run("todo", "add", "2026-11-16", "14:00", "Call", "Anna");
        Result untimed = Run("todo", "add", "today", "Call", "Bob");
        Result short_ = Run("todo", "add", "2026-11-16", "9:30", "standup");

        Assert.Equal((0, "Added: 2026-11-16 14:00 Call Anna\n", string.Empty), (added.Code, added.Out, added.Err));
        Assert.Equal((0, "Added: 2026-10-29 Call Bob\n", string.Empty), (untimed.Code, untimed.Out, untimed.Err));
        Assert.Equal((0, "Added: 2026-11-16 09:30 standup\n", string.Empty), (short_.Code, short_.Out, short_.Err));
        Assert.Equal(
            [null, new TimeOnly(9, 30), new TimeOnly(14, 0)],
            new TodoStore(root).List().Select(t => t.Time));
        Assert.Equal("2026-10-29\tCall Bob\n2026-11-16 09:30\tstandup\n2026-11-16 14:00\tCall Anna\n", Run("todos").Out);
        Assert.Equal(
            "{\"todos\":[{\"due\":\"2026-10-29\",\"time\":null,\"text\":\"Call Bob\",\"note\":null},"
            + "{\"due\":\"2026-11-16\",\"time\":\"09:30\",\"text\":\"standup\",\"note\":null},"
            + "{\"due\":\"2026-11-16\",\"time\":\"14:00\",\"text\":\"Call Anna\",\"note\":null}]}\n",
            Run("todos", "--json").Out);
    }

    // 178 AC-3: a bad time, a time with no text and a bad date are usage errors naming the [HH:mm] form.
    [Theory]
    [InlineData("That time isn't valid.", "todo", "add", "today", "25:00", "x")]
    [InlineData("That time isn't valid.", "todo", "add", "today", "14:60", "x")]
    [InlineData("A todo needs a date and text.", "todo", "add", "today", "14:00")]
    [InlineData("A todo needs a date and text.", "todo", "add", "today", "14:00", " ")]
    [InlineData("A todo needs a date and text.", "todo", "add", "today")]
    [InlineData("That date isn't valid.", "todo", "add", "someday", "x")]
    public void TodoAdd_BadTimeNoTextBadDate_IsUsageError(string first, params string[] args)
    {
        Result result = Run(args);

        Assert.Equal(2, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.Equal(
            [first + " Type: context todo add <yyyy-MM-dd|today|tomorrow> [HH:mm] <text>. Run 'context help'."],
            result.ErrLines);
        Assert.False(Directory.Exists(root));
    }

    // 178 AC-3: todo add every keeps a time-like word as text (times on every todos are out of scope).
    [Fact]
    public void TodoAddEvery_KeepsTimeAsText()
    {
        Result result = Run("todo", "add", "every", "monday", "14:00", "x");

        Assert.Equal((0, "Added: 2026-11-02 14:00 x\n", string.Empty), (result.Code, result.Out, result.Err));
        TodoItem todo = Assert.Single(new TodoStore(root).List());
        Assert.Equal(("14:00 x", (TimeOnly?)null), (todo.Text, todo.Time));
    }

    // 144 AC-8: no text is a usage error that names the new form and leaves the file alone.
    [Fact]
    public void TodoDone_NoText_IsUsageError_FileUnchanged()
    {
        Run("todo", "add", "today", "one");
        Dictionary<string, string> before = Snapshot();

        Result result = Run("todo", "done");

        AssertUsage(result);
        Assert.Contains("context todo done <text>", result.Err, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot());
    }

    // 144 AC-9: no todo help line or usage message names a number for todos (167: meeting <n> numbers appointments).
    [Fact]
    public void TodoTexts_NameNoNumber()
    {
        // Every help line except meeting's (167 AC-7, 381 list "meeting [<n>|...]"), item's (280 lists "item <n>") and the
        // decision reference's (349 lists "decisions <reference>").
        string help = string.Join(
            '\n',
            Run("help").Out.Split('\n').Where(l => !l.StartsWith("context meeting [<n>|<H:mm>|<title...>] [--book] ", StringComparison.Ordinal)
                && !l.StartsWith("context item <n>", StringComparison.Ordinal)
                && !l.StartsWith("context decisions <reference>", StringComparison.Ordinal)));
        string text = help + Run("todo").Err + Run("todo", "done").Err;

        Assert.Contains("todo done <text...>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<n>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<number>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("number", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("n-th", text, StringComparison.Ordinal);
    }

    // 149: two identical todos and one without created, none with an id.
    private const string LegacyTodos = "[{\"text\":\"twin\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T09:00:00Z\"},"
        + "{\"text\":\"twin\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T09:00:00Z\"},"
        + "{\"text\":\"other\",\"due\":\"2026-11-01\"}]";

    // 149 AC-8
    [Fact]
    public void Todos_LegacyFile_SameOutput_StoresIds()
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, TodoStore.FileName);
        File.WriteAllText(path, LegacyTodos);
        IReadOnlyList<TodoItem> before = new TodoStore(root).List();

        Result result = Run("todos");

        Assert.Equal((0, "2026-11-01\tother\n2026-11-03\ttwin\n2026-11-03\ttwin\n", string.Empty),
            (result.Code, result.Out, result.Err));
        string text = File.ReadAllText(path);
        foreach (TodoItem todo in before)
        {
            Assert.Contains($"\"id\": \"{todo.Id}\"", text, StringComparison.Ordinal);
        }

        Assert.Empty(Directory.GetFiles(root, ".todos.json.*.tmp"));
    }

    // 149 AC-8
    [Theory]
    [InlineData("help")]
    [InlineData("version")]
    public void HelpAndVersion_LegacyTodos_FileUnchanged(string command)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), LegacyTodos);
        Dictionary<string, string> before = Snapshot();

        Assert.Equal(0, Run(command).Code);

        Assert.Equal(before, Snapshot());
    }

    // 151: todos as JSON objects; the id is the raw value placed into the file.
    private static string TodosFile(params string?[] ids)
    {
        string[] texts = ["a", "b", "c"];
        return "[" + string.Join(",", ids.Select((id, i) =>
            (id is null ? "{" : $"{{\"id\":{id},")
            + $"\"text\":\"{texts[i]}\",\"due\":\"2026-11-0{i + 1}\",\"created\":\"2026-10-01T09:00:00Z\"}}")) + "]";
    }

    private static string Quoted(Guid id) => $"\"{id}\"";

    // 151 AC-6: on a file with bad ids, each todo command behaves and exits as on a valid file; stderr adds one
    // fix line with the count, and a second run adds none.
    [Theory]
    [InlineData(1, "todos")]
    [InlineData(2, "todos")]
    [InlineData(2, "todos", "--json")]
    [InlineData(1, "todo", "add", "today", "x")]
    [InlineData(2, "todo", "add", "today", "x")]
    [InlineData(2, "todo", "done", "a")]
    [InlineData(1, "todo", "done", "zzz")]
    [InlineData(1, "current")]
    [InlineData(2, "current")]
    [InlineData(2, "current", "--json")]
    public void TodoCommands_BadIds_SameOutput_OneFixLine(int bad, params string[] args)
    {
        string path = Path.Combine(root, TodoStore.FileName);
        Directory.CreateDirectory(root);
        File.WriteAllText(path, TodosFile(Quoted(Guid.NewGuid()), Quoted(Guid.NewGuid()), Quoted(Guid.NewGuid())));
        Result valid = Run(args);

        File.WriteAllText(path, bad == 1
            ? TodosFile("\"nope\"", Quoted(Guid.NewGuid()), null)
            : TodosFile("42", "{}", null));
        Result fixedRun = Run(args);

        string line = bad == 1 ? "Fixed a bad todo id in todos.json." : $"Fixed {bad} bad todo ids in todos.json.";
        Assert.Equal(valid.Code, fixedRun.Code);
        Assert.Equal(valid.Out, fixedRun.Out);
        Assert.Equal([line, .. valid.ErrLines], fixedRun.ErrLines);

        Result again = Run(args);
        Assert.DoesNotContain(again.ErrLines, l => l.StartsWith("Fixed", StringComparison.Ordinal));
    }

    // 151 AC-7: missing, empty or repeated ids are written without a fix line.
    [Theory]
    [InlineData("todos")]
    [InlineData("todos", "--json")]
    [InlineData("todo", "add", "today", "x")]
    [InlineData("todo", "done", "a")]
    [InlineData("current")]
    public void TodoCommands_MissingOrRepeatedIds_NoFixLine(params string[] args)
    {
        string path = Path.Combine(root, TodoStore.FileName);
        Directory.CreateDirectory(root);
        string repeated = Quoted(Guid.NewGuid());
        string content = TodosFile(repeated, repeated, Quoted(Guid.Empty));
        File.WriteAllText(path, content);

        Result result = Run(args);

        Assert.Equal(0, result.Code);
        Assert.DoesNotContain(result.ErrLines, l => l.StartsWith("Fixed", StringComparison.Ordinal));
        Assert.NotEqual(content, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("todos")]
    [InlineData("todo", "add", "today", "x")]
    [InlineData("todo", "done", "x")]
    public void TodoCommands_BadTodosJson_ErrorAndFileUnchanged(params string[] args)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), "{ not json");
        Dictionary<string, string> before = Snapshot();

        AssertError(Run(args), 1, "Can't read todos.json.");
        Assert.Equal(before, Snapshot());
    }

    // AC-6 ---------------------------------------------------------------------------------------

    [Fact]
    public void Notes_ListsNamesInOrder()
    {
        WriteNote("beta", "b");
        WriteNote("Alpha", "a");

        Result result = Run("notes");

        Assert.Equal((0, "Alpha\nbeta\n", string.Empty), (result.Code, result.Out, result.Err));
    }

    [Fact]
    public void NoteShow_PrintsTheText_MatchedLikeOpen()
    {
        WriteNote("My Plan", "# My Plan\nline");

        Result result = Run("note", "show", "my", "plan.md");

        Assert.Equal((0, "# My Plan\nline\n", string.Empty), (result.Code, result.Out, result.Err));
    }

    [Fact]
    public void NoteShow_NoMatch_IsError()
    {
        WriteNote("Plan", "x");

        AssertError(Run("note", "show", "Nope"), 1, "No note named Nope.");
    }

    [Theory]
    [InlineData("note")]
    [InlineData("note", "show")]
    [InlineData("note", "open", "x")]
    [InlineData("import")]
    [InlineData("import", "a", "b")]
    [InlineData("export", "a")]
    [InlineData("export", "a", "b", "c")]
    [InlineData("current", "now")]
    [InlineData("notes", "x")]
    public void MissingOrExtraArguments_AreUsageErrors(params string[] args)
    {
        AssertUsage(Run(args));
    }

    // AC-7 ---------------------------------------------------------------------------------------

    [Fact]
    public void Tags_ListsTagsWithCounts_AndNotesWithATag()
    {
        WriteNote("A", "#work #[big plan]");
        WriteNote("B", "#work");

        Result all = Run("tags");
        Result work = Run("tags", "#work");
        Result unknown = Run("tags", "nothing");

        Assert.Equal((0, "#[big plan]\t1\n#work\t2\n"), (all.Code, all.Out));
        Assert.Equal((0, "A\nB\n"), (work.Code, work.Out));
        Assert.Equal((0, string.Empty, string.Empty), (unknown.Code, unknown.Out, unknown.Err));
    }

    // 295 AC-3: a note over the cap changes nothing in the tags output (no skipped line).
    [Fact]
    public void Tags_SkippedNote_OutputUnchanged()
    {
        WriteNote("A", "#work");
        using (FileStream stream = new FileStream(Path.Combine(root, "Big.md"), FileMode.Create))
        {
            stream.Write(System.Text.Encoding.UTF8.GetBytes("#big\n"));
            stream.SetLength(NoteStore.MaxNoteBytes + 1);
        }

        Result all = Run("tags");

        Assert.Equal((0, "#work\t1\n", string.Empty), (all.Code, all.Out, all.Err));
    }

    [Theory]
    [InlineData("tags")]
    public void TagCommands_BadContextDb_IsError(params string[] args)
    {
        WriteNote("A", "#work");
        File.WriteAllText(Path.Combine(root, TagIndex.FileName), "not a database at all, not at all, really not");

        AssertError(Run(args), 1, "Can't read context.db.");
    }

    // AC-8 ---------------------------------------------------------------------------------------

    private void WriteIcs(string name, string text)
    {
        string folder = Path.Combine(root, CalendarStore.FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), text);
    }

    private static string Event(string title, string start, string end, string? location = null) =>
        $"BEGIN:VEVENT\r\nSUMMARY:{title}\r\nDTSTART:{start}\r\nDTEND:{end}\r\n"
        + (location is null ? string.Empty : $"LOCATION:{location}\r\n") + "END:VEVENT\r\n";

    [Fact]
    public void Current_PrintsSectionsAsTabSeparatedRows()
    {
        WriteIcs("work.ics", "BEGIN:VCALENDAR\r\n" + Event("Review", "20261029T100000Z", "20261029T110000Z", "Room 1")
            + "END:VCALENDAR\r\n");
        WriteNote("Plan", "#work");
        Run("todo", "add", "2026-10-28", "late #work");
        Run("todo", "add", "today", "now");
        Run("todo", "add", "tomorrow", "later");

        Result result = Run("current");

        Assert.Equal(string.Empty, result.Err);
        Assert.Equal(0, result.Code);
        Assert.Equal(
            "Today, 2026-10-29\nAppointments\n10:00-11:00\tReview\tRoom 1\nDue\n2026-10-28\tlate #work\n"
                + "2026-10-29\tnow\nLinked notes\nPlan\t#work\n",
            result.Out);
    }

    [Fact]
    public void Current_EmptyStates_UseConsoleTexts()
    {
        Directory.CreateDirectory(root);

        Result result = Run("current");

        Assert.Equal(0, result.Code);
        string folder = Path.Combine(Path.GetFullPath(root), CalendarStore.FolderName);
        Assert.Equal(
            $"Today, 2026-10-29\nAppointments\nNo appointments today. Put .ics files in {folder} to see them here.\n"
                + "Due\nNothing due today. Add one with /todo today <text>.\n",
            result.Out);
    }

    [Fact]
    public void Current_HasNoRowCap()
    {
        StringBuilder ics = new StringBuilder("BEGIN:VCALENDAR\r\n");
        for (int i = 0; i < 20; i++)
        {
            ics.Append(Event($"E{i:00}", $"20261029T{i:00}0000Z", $"20261029T{i:00}3000Z"));
        }

        WriteIcs("many.ics", ics.Append("END:VCALENDAR\r\n").ToString());

        Result result = Run("current");

        Assert.Equal(0, result.Code);
        Assert.Equal(20, result.Out.Split('\n').Count(l => l.Contains("\tE", StringComparison.Ordinal)));
        Assert.DoesNotContain("more", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_RedLinesGoToStderr_WithExitOne()
    {
        WriteIcs("bad.ics", "not a calendar");
        new CalendarLink(Directory.CreateDirectory(root).FullName).Set(FakeHttpHandler.Link);
        http = FakeHttpHandler.Status(HttpStatusCode.InternalServerError);
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), "{ bad");

        Result result = Run("current");

        Assert.Equal(1, result.Code);
        Assert.StartsWith("Today, 2026-10-29\nAppointments\n", result.Out, StringComparison.Ordinal);
        Assert.Equal(
            ["Can't read calendar/bad.ics", "Can't download the Exchange calendar (HTTP 500).", "Can't read todos.json."],
            result.ErrLines);
        Assert.DoesNotContain(FakeHttpHandler.PathToken, result.Out + result.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeHttpHandler.QueryToken, result.Out + result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_BadContextDb_LinkedNotesErrorOnStderr()
    {
        WriteNote("Plan", "#work");
        Run("todo", "add", "today", "x #work");
        File.WriteAllText(Path.Combine(root, TagIndex.FileName), "not a database at all, not at all, really not");

        Result result = Run("current");

        Assert.Equal(1, result.Code);
        Assert.EndsWith("Due\n2026-10-29\tx #work\nLinked notes\n", result.Out, StringComparison.Ordinal);
        Assert.Equal(["Can't read context.db."], result.ErrLines);
    }

    // 160 AC-6: related lines directly after each appointment line, tags formatted, then words, cleaned.
    [Fact]
    public void Current_RelatedLines_AfterEachAppointment()
    {
        WriteIcs("work.ics", "BEGIN:VCALENDAR\r\n"
            + Event("ACME kickoff", "20261029T090000Z", "20261029T100000Z")
            + Event("Lunch", "20261029T110000Z", "20261029T120000Z")
            + "END:VCALENDAR\r\n");
        WriteNote("Clients", "#acme #[acme kickoff]");
        WriteNote("ACME\u001b[31m", "text");

        Result result = Run("current");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.Equal(
            "Today, 2026-10-29\nAppointments\n09:00-10:00\tACME kickoff\t\n"
                + "related\tClients\t#acme #[acme kickoff]\n"
                + "related\tACME\uFFFD[31m\tacme\n"
                + "11:00-12:00\tLunch\t\nDue\nNothing due today. Add one with /todo today <text>.\n",
            result.Out);
    }

    // 160 AC-6: a bad context.db with appointments: one stderr line, exit 1, with or without due todos.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Current_BadContextDb_WithAppointments_OneErrorLine(bool due)
    {
        WriteIcs("work.ics", "BEGIN:VCALENDAR\r\n" + Event("Plan", "20261029T090000Z", "20261029T100000Z") + "END:VCALENDAR\r\n");
        WriteNote("Plan", "#plan");
        if (due)
        {
            Run("todo", "add", "today", "x #plan");
        }

        File.WriteAllText(Path.Combine(root, TagIndex.FileName), "not a database at all, not at all, really not");

        Result result = Run("current");

        Assert.Equal(1, result.Code);
        Assert.Equal(["Can't read context.db."], result.ErrLines);
        Assert.StartsWith("Today, 2026-10-29\nAppointments\n09:00-10:00\tPlan\t\nDue\n", result.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("related", result.Out, StringComparison.Ordinal);
    }

    // 160 AC-6: with a bad todos.json too, the index line still comes once, after the todos line.
    [Fact]
    public void Current_BadContextDbAndTodos_WithAppointments_BothLines()
    {
        WriteIcs("work.ics", "BEGIN:VCALENDAR\r\n" + Event("Plan", "20261029T090000Z", "20261029T100000Z") + "END:VCALENDAR\r\n");
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), "{ bad");
        File.WriteAllText(Path.Combine(root, TagIndex.FileName), "not a database at all, not at all, really not");

        Result result = Run("current");

        Assert.Equal(1, result.Code);
        Assert.Equal(["Can't read todos.json.", "Can't read context.db."], result.ErrLines);
    }

    [Fact]
    public void Current_StaleExchangeCopy_IsGreyTextOnStdout()
    {
        new CalendarLink(Directory.CreateDirectory(root).FullName).Set(FakeHttpHandler.Link);
        string cache = Path.Combine(root, CalendarFeed.CacheFileName);
        File.WriteAllText(cache, CalendarFeedTests.Ics);
        File.SetLastWriteTimeUtc(cache, new DateTime(2026, 10, 28, 17, 45, 0, DateTimeKind.Utc));
        http = FakeHttpHandler.Throw();

        Result result = Run("current");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.Contains("Exchange calendar offline, showing the copy from 2026-10-28 17:45.\n", result.Out, StringComparison.Ordinal);
        Assert.Contains("08:00-08:15\tStandup\t\n", result.Out, StringComparison.Ordinal);
    }

    // AC-9 ---------------------------------------------------------------------------------------

    [Fact]
    public void Import_StoresTheFileAsNote()
    {
        string source = Path.Combine(outside, "Idea.md");
        File.WriteAllText(source, "# Idea\n");

        Result result = Run("import", source);

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Err);
        string[] lines = result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Imported Idea.", lines[0]);
        Assert.StartsWith("The source is unchanged; delete it", lines[1], StringComparison.Ordinal);
        Assert.Equal("# Idea\n", Read("Idea.md"));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Import_InsideRoot_IsRefused()
    {
        WriteNote("Plan", "x");

        AssertError(Run("import", Path.Combine(root, "Plan.md")), 1, "That file is already in the Context folder. Nothing changed.");
    }

    [Fact]
    public void Import_OverFiveMegabytes_IsRefused()
    {
        string source = Path.Combine(outside, "Big.md");
        File.WriteAllBytes(source, new byte[(5 * 1024 * 1024) + 1]);

        Result result = Run("import", source);

        Assert.Equal(1, result.Code);
        Assert.StartsWith("Can't import a file over 5 MB", result.ErrLines.Single(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "Big.md")));
    }

    [Fact]
    public void Import_RelativePath_IsResolvedAgainstCurrentDirectory()
    {
        // Relative to the current directory, with no ~ expansion: "~/x.md" names a folder called "~" here.
        string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.Combine(outside, "Rel.md"));
        File.WriteAllText(Path.Combine(outside, "Rel.md"), "rel");

        Assert.Equal(0, Run("import", relative).Code);
        Assert.Equal("rel", Read("Rel.md"));
        Assert.StartsWith("No such file: ", Run("import", "~/context-cli-none.md").ErrLines.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void Export_WritesNewPlainFile()
    {
        WriteNote("Plan", "# Plan\nbody");
        string target = Path.Combine(outside, "plan copy.md");

        Result result = Run("export", "plan", target);

        // 132 AC-8: nothing on stderr, so no warning about the copy.
        Assert.Equal((0, "Exported Plan.\n", string.Empty), (result.Code, result.Out, result.Err));
        Assert.Equal("# Plan\nbody", File.ReadAllText(target));
    }

    [Fact]
    public void Export_ExistingTarget_IsErrorAndUnchanged()
    {
        WriteNote("Plan", "new");
        string target = Path.Combine(outside, "plan.md");
        File.WriteAllText(target, "keep");

        Result result = Run("export", "Plan", target);

        Assert.Equal(1, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.StartsWith("That file already exists", result.ErrLines.Single(), StringComparison.Ordinal);
        Assert.Equal("keep", File.ReadAllText(target));
    }

    [Fact]
    public void Export_InsideRoot_IsRefused()
    {
        WriteNote("Plan", "x");

        AssertError(
            Run("export", "Plan", Path.Combine(root, "copy.md")),
            1,
            "Export to a path outside the Context folder. Nothing was written.");
        Assert.False(File.Exists(Path.Combine(root, "copy.md")));
    }

    [Fact]
    public void Export_NoSuchNote_IsError()
    {
        Directory.CreateDirectory(root);

        AssertError(Run("export", "Nope", Path.Combine(outside, "x.md")), 1, "No note named Nope.");
    }

    // AC-10 --------------------------------------------------------------------------------------

    [Fact]
    public void Output_FromHostileNote_HasNoRawControls()
    {
        WriteNote("Evil\u001b[31m", "line \u001b[31mred\u0007\u009b\r\nnext");
        WriteNote("Plan", "#work");
        Run("todo", "add", "today", "do \u001b]0;x\u0007 #work");

        string all = string.Concat(
            new[] { Run("notes"), Run("note", "show", "Evil\u001b[31m"), Run("todos"), Run("current"), Run("tags") }
                .Select(r => r.Out + r.Err));

        Assert.Contains("line �[31mred��\nnext", all, StringComparison.Ordinal);
        // Tabs are the column separators the CLI writes itself (a tab from disk is cleaned into spaces).
        Assert.DoesNotContain(all, c => char.IsControl(c) && c != '\n' && c != '\t');
    }

    // AC-15 --------------------------------------------------------------------------------------

    [Fact]
    public void Writes_LeaveNoTempFiles()
    {
        string source = Path.Combine(outside, "Idea.md");
        File.WriteAllText(source, "x");

        Run("capture", "a");
        Run("capture", "b");
        Run("todo", "add", "today", "x");
        Assert.Equal(0, Run("todo", "done", "x").Code);
        Run("import", source);
        Run("import", source);

        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
    }

    // AC-16 --------------------------------------------------------------------------------------

    [Fact]
    public void CliSource_MentionsNoSpectreAndNoConsole()
    {
        string source = CliSource();
        Assert.DoesNotContain("Spectre", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.", source, StringComparison.Ordinal);
    }

    private static string CliSource()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Context.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, "src", "Context", "Cli.cs"));
    }

    // Round 2 ------------------------------------------------------------------------------------

    // 385 AC-2 (was round 2): a FIFO reports length 0, so it is never opened and never hangs; it is refused and an
    // existing note keeps its bytes.
    [Fact]
    public async Task Import_Fifo_IsRefusedWithoutBlocking()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteNote("pipe", "keep\n");
        string fifo = Path.Combine(outside, "pipe.md");
        using (System.Diagnostics.Process mkfifo = System.Diagnostics.Process.Start("mkfifo", fifo))
        {
            mkfifo.WaitForExit();
        }

        Task<Result> run = Task.Run(() => Run("import", fifo));
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5))));
        AssertError(await run, 1, "Can't import an empty or special file: " + fifo);
        Assert.Equal("keep\n", Read("pipe.md"));
    }

    // 385 AC-2: an empty file is refused, so it can't wipe the note it names.
    [Fact]
    public void Import_EmptyFile_IsRefusedAndKeepsTheNote()
    {
        WriteNote("Plan", "keep\n");
        string source = Path.Combine(outside, "Plan.md");
        File.WriteAllBytes(source, []);

        AssertError(Run("import", source), 1, "Can't import an empty or special file: " + source);
        Assert.Equal("keep\n", Read("Plan.md"));
        Assert.Equal(["Plan.md"], Directory.GetFiles(root, "*.md").Select(Path.GetFileName));
    }

    // 385 AC-3: a source that doesn't end in .md (ignoring case) is refused before it is read.
    [Theory]
    [InlineData("notes.txt")]
    [InlineData("notes")]
    [InlineData("notes.md.txt")]
    public void Import_NotMarkdown_IsRefused(string name)
    {
        string source = Path.Combine(outside, name);
        File.WriteAllText(source, "# Notes\n");

        AssertError(Run("import", source), 1, "Can't import a file that isn't .md: " + source);
        Assert.Empty(Directory.Exists(root) ? Directory.GetFiles(root, "*.md") : []);
    }

    // 385 AC-3: the .md ending is matched ignoring case.
    [Fact]
    public void Import_UpperCaseMd_IsAccepted()
    {
        string source = Path.Combine(outside, "a.MD");
        File.WriteAllText(source, "# a\n");

        Result result = Run("import", source);

        Assert.Equal(0, result.Code);
        Assert.Equal("# a\n", Read("a.md"));
    }

    [Fact]
    public void UnknownOption_IsNotEchoed()
    {
        Result result = Run("--pasphrase=topSecret55aa", "notes");

        AssertUsage(result);
        Assert.DoesNotContain("topSecret55aa", result.Err, StringComparison.Ordinal);
    }

    // 136 AC-9 --------------------------------------------------------------------------------------

    private void WriteSealed(string name)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".ctxs"), "{\"v\":1,\"text\":\"c2VhbGVk\"}");
    }

    // 136 AC-9: notes lists a sensitive note by name with a locked marker, sorted with the plain ones.
    [Fact]
    public void Notes_ListsSensitiveNotesAsLocked()
    {
        WriteNote("Beta", "b");
        WriteSealed("alpha");
        WriteSealed("Gamma");

        Result result = Run("notes");

        Assert.Equal(0, result.Code);
        Assert.Equal(["alpha\tlocked", "Beta", "Gamma\tlocked"], result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(string.Empty, result.Err);
    }

    // 136 AC-9: no command prints, exports or follows a sensitive note; each ends with exit 1 and one line.
    [Theory]
    [InlineData("note", "show", "Plan")]
    [InlineData("note", "show", "plan")]
    [InlineData("export", "Plan", "out.md")]
    public void SensitiveNote_IsNeverReadByTheConsoleCommands(params string[] args)
    {
        WriteSealed("Plan");
        WriteNote("Other", "[[Plan]] #tag");
        if (args[0] == "export")
        {
            args[2] = Path.Combine(outside, "out.md");
        }

        Dictionary<string, string> before = Snapshot();

        Result result = Run(args);

        AssertError(result, 1, "Plan is sensitive. Open it in the desktop app.");
        Assert.DoesNotContain("c2VhbGVk", result.Out + result.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(outside, "out.md")));
        before.Remove(TagIndex.FileName);
        Dictionary<string, string> after = Snapshot();
        after.Remove(TagIndex.FileName);
        Assert.Equal(before, after);
    }

    // 136 AC-9: import and capture never write a plain note over or beside a sensitive one.
    [Fact]
    public void ImportAndCapture_OntoASensitiveNote_WriteNothing()
    {
        WriteSealed("Plan");
        WriteSealed("Inbox");
        Directory.CreateDirectory(outside);
        string source = Path.Combine(outside, "Plan.md");
        File.WriteAllText(source, "imported");
        Dictionary<string, string> before = Snapshot();

        Result imported = Run("import", source);
        Result captured = Run("capture", "a thought");

        AssertError(imported, 1, "Plan is sensitive. Open it in the desktop app.");
        AssertError(captured, 1, "Inbox is sensitive. Open it in the desktop app.");
        Assert.Equal(before, Snapshot());
    }

    // 152 ------------------------------------------------------------------------------------------

    private void WriteNested(string name, string text)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // 152 AC-10: notes prints full names depth-first, with sensitive notes sorted among the top level.
    [Fact]
    public void Notes_PrintsSubpagesByFullName_InListOrder()
    {
        WriteNested("Meetings", "m");
        WriteNested("Meetings/Meeting 2", "2");
        WriteNested("Meetings/meeting 1", "1");
        WriteNested("Meetings/meeting 1/Notes", "n");
        WriteNested("Zeta", "z");
        WriteSealed("Alpha");
        WriteSealed("Plan");

        Result result = Run("notes");

        Assert.Equal(0, result.Code);
        Assert.Equal(
            ["Alpha\tlocked", "Meetings", "Meetings/meeting 1", "Meetings/meeting 1/Notes", "Meetings/Meeting 2", "Plan\tlocked", "Zeta"],
            result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // 152 AC-10: note show, export and tags take and print a subpage's full name.
    [Fact]
    public void SubpageByFullName_ShowExportTags()
    {
        WriteNested("Meetings", "#work [[Meetings/Meeting 1]]");
        WriteNested("Meetings/Meeting 1", "# Meeting 1\n#work");
        string target = Path.Combine(outside, "m1.md");

        Result show = Run("note", "show", "Meetings/Meeting", "1");
        Result tags = Run("tags", "work");
        Result export = Run("export", "Meetings/Meeting 1", target);
        Result path = Run("note", "show", Path.Combine(root, "Meetings", "Meeting 1"));

        Assert.Equal((0, "# Meeting 1\n#work\n"), (show.Code, show.Out));
        Assert.Equal((0, "Meetings\nMeetings/Meeting 1\n"), (tags.Code, tags.Out));
        Assert.Equal((0, "Exported Meetings/Meeting 1.\n"), (export.Code, export.Out));
        Assert.Equal("# Meeting 1\n#work", File.ReadAllText(target));
        Assert.Equal(1, path.Code);
    }

    // 152 AC-10: import and capture still write only top-level notes.
    [Fact]
    public void ImportAndCapture_WriteTopLevelOnly()
    {
        WriteNested("Meetings", "m");
        WriteNested("Meetings/inbox", "child inbox");
        string source = Path.Combine(outside, "Meeting 1.md");
        File.WriteAllText(source, "# Meeting 1\n");

        Assert.Equal(0, Run("import", source).Code);
        Assert.Equal(0, Run("capture", "hello").Code);

        Assert.Equal("# Meeting 1\n", Read("Meeting 1.md"));
        Assert.StartsWith("# inbox\n", Read("inbox.md"), StringComparison.Ordinal);
        Assert.Equal("child inbox", Read(Path.Combine("Meetings", "inbox.md")));
        Assert.Equal(["inbox.md"], Directory.GetFiles(Path.Combine(root, "Meetings")).Select(f => Path.GetFileName(f)).ToArray());
    }

    // 156 AC-7 -----------------------------------------------------------------------------------

    // Every file but context.db (and its journal) with its bytes.
    private Dictionary<string, string> SnapshotWithoutDb() =>
        Snapshot()
            .Where(f => !f.Key.StartsWith(TagIndex.FileName, StringComparison.Ordinal))
            .ToDictionary(f => f.Key, f => f.Value);

    // 157 AC-7 -----------------------------------------------------------------------------------

    [Fact]
    public void Find_PrintsMatchingNotes_SortedOnce_WritesOnlyContextDb()
    {
        WriteNote("d", "role:: Bereichsleiter\nROLE:: bereichsleiter");
        WriteNote("B", "Role:: BEREICHSLEITER");
        WriteNote("a", "role::   Bereichsleiter  ");
        WriteNote("c", "role:: Chef\n```\nrole:: Bereichsleiter\n```");
        Dictionary<string, string> before = SnapshotWithoutDb();

        Result result = Run("find", "Role", "bereichsleiter");

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Err);
        Assert.Equal("a\nB\nd\n", result.Out);
        Assert.Equal(before, SnapshotWithoutDb());
    }

    [Fact]
    public void Find_ValueArgsJoinedWithOneSpace_LinkValueMatches()
    {
        WriteNote("Anna", "role:: Head of Sales\naffiliation:: [[ACME Corp]]");

        Assert.Equal("Anna\n", Run("find", "role", "Head", "of", "Sales").Out);
        Assert.Equal("Anna\n", Run("find", "role", "Head  of Sales").Out);
        Assert.Equal("Anna\n", Run("find", "affiliation", "acme", "corp").Out);
        Assert.Equal("Anna\n", Run("find", "affiliation", "[[ACME Corp]]").Out);
    }

    // 332 AC-3: find matches any one part of a comma-separated affiliation.
    [Fact]
    public void Find_AffiliationListPart_Matches()
    {
        WriteNote("Anna", "affiliation:: [[ACME]], Beta GmbH");

        Result beta = Run("find", "affiliation", "Beta", "GmbH");
        Assert.Equal((0, "Anna\n"), (beta.Code, beta.Out));
        Assert.Equal("Anna\n", Run("find", "affiliation", "acme").Out);
    }

    [Fact]
    public void Find_NoMatch_NoOutput_ExitZero()
    {
        WriteNote("Anna", "role:: Chef\nrole::");

        Result result = Run("find", "role", "nobody");

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.Equal(string.Empty, result.Err);
    }

    [Fact]
    public void Find_TypePerson_ListsUntypedPersonPages()
    {
        WriteNote("people", "# people");
        Directory.CreateDirectory(Path.Combine(root, "people"));
        File.WriteAllText(Path.Combine(root, "people", "Anna.md"), "# Anna\n");
        File.WriteAllText(Path.Combine(root, "people", "ACME.md"), "type:: organization\n");
        WriteNote("Bob", "type:: Person");

        Assert.Equal("Bob\npeople/Anna\n", Run("find", "type", "person").Out);
        Assert.Equal("people/ACME\n", Run("find", "type", "organization").Out);
    }

    [Fact]
    public void Find_CleansNames()
    {
        WriteNote("A\u0007b", "k:: v");

        Assert.Equal("A\uFFFDb\n", Run("find", "k", "v").Out);
    }

    [Theory]
    [InlineData("find")]
    [InlineData("find", "role")]
    [InlineData("find", "1role", "x")]
    [InlineData("find", "ro le", "x")]
    [InlineData("find", "role::", "x")]
    [InlineData("find", "role", " ")]
    public void Find_BadArguments_UsageError_FolderNotRead(params string[] args)
    {
        Result result = Run(args);

        AssertUsage(result);
        Assert.Equal("Type: context find <attribute> <value>. Run 'context help'.", result.ErrLines[0]);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Find_BadContextDb_IsError()
    {
        WriteNote("A", "role:: x");
        File.WriteAllText(Path.Combine(root, TagIndex.FileName), "not a database at all, not at all, really not");
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("find", "role", "x"), 1, "Can't read context.db.");
        Assert.Equal(before, Snapshot());
    }

    // 170 AC-8 -----------------------------------------------------------------------------------

    [Fact]
    public void Search_PrintsNameLineNumberLine_Sorted_WritesOnlyContextDb()
    {
        WriteNote("beta", "# beta\r\nplan 2027 budget\r\nBudget again");
        WriteNote("Alpha", "the budget");
        WriteNote("gamma", "no match");
        Dictionary<string, string> before = SnapshotWithoutDb();

        Result result = Run("search", "BUDGET");

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Err);
        Assert.Equal("Alpha\t1\tthe budget\nbeta\t2\tplan 2027 budget\n", result.Out);
        Assert.Equal(before, SnapshotWithoutDb());
    }

    [Fact]
    public void Search_ArgsJoinedWithOneSpace_CleansNameAndLine()
    {
        WriteNote("A\u0007b", "x plan 2027\u0007 y\tz");
        WriteNote("C", "plan  2027");

        Assert.Equal("A\uFFFDb\t1\tx plan 2027\uFFFD y    z\n", Run("search", "plan", "2027").Out);
    }

    [Fact]
    public void Search_NoHits_NoOutput_ExitZero()
    {
        WriteNote("A", "something");

        Result result = Run("search", "nothing");

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.Equal(string.Empty, result.Err);
    }

    // 379: a day file in Times' entries format: "Work" on #1234 08:00-09:30, then "Code Review" (no item) 09:30-10:00.
    private void WriteTimesDay(string day)
    {
        Directory.CreateDirectory(times);
        File.WriteAllText(Path.Combine(times, day + ".json"), "{\"entries\":["
            + "{\"kind\":\"checkpoint\",\"time\":\"08:00\",\"name\":\"Work\",\"workItem\":1234},"
            + "{\"kind\":\"checkpoint\",\"time\":\"09:30\",\"name\":\"Code\\u0007 Review\"},"
            + "{\"kind\":\"leaving\",\"time\":\"10:00\"}]}");
    }

    // 379 AC-10: plain lines stay as they are; --times adds one times row per booking, newest first; --from/--to limit
    // both (the notes by their date in the zone, UTC here).
    [Fact]
    public void Search_Times_RowsAfterNotes_AndRange()
    {
        WriteNote("A", "# A\nfix #1234 today");
        WriteNote("B", "#12345 only");
        File.SetLastWriteTimeUtc(Path.Combine(root, "A.md"), new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc));
        WriteTimesDay("2026-10-05");
        WriteTimesDay("2026-10-20");

        Result plain = Run("search", "#1234");
        Assert.Equal(0, plain.Code);
        Assert.Equal("A\t2\tfix #1234 today\n", plain.Out);

        Result withTimes = Run("search", "--times", "#1234");
        Assert.Equal(0, withTimes.Code);
        Assert.Equal(string.Empty, withTimes.Err);
        Assert.Equal(
            "A\t2\tfix #1234 today\ntimes\t2026-10-20\t#1234\tWork\t1:30\ntimes\t2026-10-05\t#1234\tWork\t1:30\n",
            withTimes.Out);

        Assert.Equal(
            "times\t2026-10-20\t\tCode� Review\t0:30\n",
            Run("search", "--FROM", "2026-10-11", "--times", "--to", "2026-10-31", "review").Out);
        Assert.Equal(
            "A\t2\tfix #1234 today\ntimes\t2026-10-05\t#1234\tWork\t1:30\n",
            Run("search", "--to", "2026-10-10", "--times", "#1234").Out);
        Assert.Equal("A\t2\tfix #1234 today\n", Run("search", "--from", "2026-10-10", "--to", "2026-10-10", "#1234").Out);
    }

    // 379 AC-10, AC-6: unreadable day files and an unlistable folder are reported after the rows, exit 1.
    [Fact]
    public void Search_Times_SkippedFile_ReportedAfterRows()
    {
        WriteNote("A", "nothing");
        WriteTimesDay("2026-10-05");
        File.WriteAllText(Path.Combine(times, "2026-10-06.json"), "{ broken");

        Result result = Run("search", "--times", "#1234");

        Assert.Equal(1, result.Code);
        Assert.Equal("times\t2026-10-05\t#1234\tWork\t1:30\n", result.Out);
        Assert.Equal(["Skipped 1 unreadable Times day file(s)."], result.ErrLines);
    }

    // 379 AC-5, AC-10: a bad date, a missing date, a repeated --from/--to or a start after the end is a usage error
    // before the folder is read.
    [Theory]
    [InlineData("Type: context search [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--times] <text>. Run 'context help'.", "--from", "2026-13-01", "abc")]
    [InlineData("Type: context search [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--times] <text>. Run 'context help'.", "--to", "1.10.2026", "abc")]
    [InlineData("Type: context search [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--times] <text>. Run 'context help'.", "--from")]
    [InlineData("Type: context search [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--times] <text>. Run 'context help'.", "--from", "2026-10-01", "--from", "2026-10-02", "abc")]
    [InlineData("Type: context search [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--times] <text>. Run 'context help'.", "--to", "2026-10-01", "--TO", "2026-10-02", "abc")]
    [InlineData("The start date is after the end date. Run 'context help'.", "--from", "2026-10-02", "--to", "2026-10-01", "abc")]
    [InlineData("Type: context search <text>. Run 'context help'.", "--times", "--from", "2026-10-02")]
    public void Search_BadRange_UsageError_FolderNotRead(string error, params string[] words)
    {
        Result result = Run(["search", .. words]);

        AssertUsage(result);
        Assert.Equal(error, result.Err.TrimEnd('\n'));
        Assert.Equal(Cli.ExitUsage, result.Code);
        Assert.Equal(2, Cli.ExitUsage);
        Assert.False(Directory.Exists(root));
        Assert.False(Directory.Exists(times));
    }

    [Theory]
    [InlineData("search")]
    [InlineData("search", " ")]
    [InlineData("SEARCH", "", "\t")]
    public void Search_NoText_UsageError_FolderNotRead(params string[] args)
    {
        Result result = Run(args);

        AssertUsage(result);
        Assert.Equal("Type: context search <text>. Run 'context help'.", result.ErrLines[0]);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("Type at least 3 characters to search.", "ab")]
    [InlineData("Type at least 3 characters to search.", " a ", " ")]
    [InlineData("Search text can be at most 200 characters.", "LONG")]
    public void Search_TooShortOrTooLong_IsError_DbNotCreated(string error, params string[] words)
    {
        WriteNote("A", "abc");
        string[] args = ["search", .. words.Select(w => w == "LONG" ? new string('x', 201) : w)];

        Result result = Run(args);

        AssertError(result, 1, error);
        Assert.False(File.Exists(Path.Combine(root, TagIndex.FileName)));
    }

    [Fact]
    public void Search_BadContextDb_IsError()
    {
        WriteNote("A", "abc");
        File.WriteAllText(Path.Combine(root, TagIndex.FileName), "not a database at all, not at all, really not");
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("search", "abc"), 1, "Can't read context.db.");
        Assert.Equal(before, Snapshot());
    }

    // 180, 343, 349 --------------------------------------------------------------------------------

    private const string DecisionsUsage = "Type: context decisions [--full] [tag] or context decisions <reference>. Run 'context help'.";

    private static readonly string FolderLine = "decision-folder:: " + DecisionFolderGuid.ToString("D");

    private void WriteDecisionFolder(string prefix = "ab12")
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, DecisionFolder.FileName),
            "{\"prefix\":\"" + prefix + "\",\"guid\":\"" + DecisionFolderGuid.ToString("D") + "\",\"migrated\":true}");
    }

    // 349 AC-12 (343 AC-11): one line per decision: date, reference (#!d<n> for the own prefix, #!d<prefix>-<n> for
    // another, empty for a bare marker), note name, people and headline, newest first, each field one clean line.
    [Fact]
    public void Decisions_PrintsOneLinePerDecision()
    {
        WriteDecisionFolder();
        WriteNote("meetings", "# meetings\n");
        Directory.CreateDirectory(Path.Combine(root, "meetings"));
        WriteNote("meetings/2026-11-10 Weekly sync", "# x\n!d ab12-12 ship it with @[Anna]\nand @[Bo]\n");
        WriteNote("2026-11-12", "- !d a\u0007b\tc\n");
        WriteNote("Plans", "!d later\n\n!d zz99-4 theirs\n\nDecision: old");
        File.SetLastWriteTimeUtc(Path.Combine(root, "Plans.md"), new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc));

        Result result = Run("decisions");

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Err);
        Assert.Equal(
            "2026-11-12\t\t2026-11-12\t\ta\uFFFDb c\n"
                + "2026-11-10\t#!d12\tmeetings/2026-11-10 Weekly sync\tAnna, Bo\tship it with @[Anna]\n"
                + "2026-11-01\t\tPlans\t\tlater\n"
                + "2026-11-01\t#!dzz99-4\tPlans\t\ttheirs\n",
            result.Out);
    }

    // 349 AC-2, AC-12: a folder without decision-folder.json gets one on the first decisions run, from the fakes.
    [Fact]
    public void Decisions_CreatesDecisionFolderFile()
    {
        WriteNote("Plan", "!d ab12-3 one\n");

        Assert.Equal(0, Run("decisions").Code);

        DecisionFolder? folder = DecisionFolder.Read(root);
        Assert.Equal(new DecisionFolder("ab12", DecisionFolderGuid, false), folder);
    }

    // 343 AC-11: --full (any case) prints the whole text through OneLine instead of the headline.
    [Theory]
    [InlineData("--full")]
    [InlineData("--FULL")]
    public void Decisions_Full_PrintsWholeText(string flag)
    {
        WriteDecisionFolder();
        WriteNote("Plan", "!d ab12-3 one\ntwo\n");
        File.SetLastWriteTimeUtc(Path.Combine(root, "Plan.md"), new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc));

        Result result = Run("decisions", flag);

        Assert.Equal(0, result.Code);
        Assert.Equal("2026-11-01\t#!d3\tPlan\t\tone two\n", result.Out);
    }

    // 349 AC-12: decisions <ref> in every form and case prints one decision with its note, date, people, a blank line
    // and the text.
    [Theory]
    [InlineData("3")]
    [InlineData("#!d3")]
    [InlineData("ab12-3")]
    [InlineData("AB12-3")]
    [InlineData("#!dab12-3")]
    [InlineData("#!DAB12-3")]
    public void Decisions_Reference_PrintsOne(string reference)
    {
        WriteDecisionFolder();
        WriteNote("Plan", "!d ab12-3 one @[Anna]\ntwo\u0007\n");
        File.SetLastWriteTimeUtc(Path.Combine(root, "Plan.md"), new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc));

        Result result = Run("decisions", reference);

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Err);
        Assert.Equal("#!d3\nNote: Plan\nDate: 2026-11-01\nPeople: Anna\n\none @[Anna]\ntwo\uFFFD\n", result.Out);
    }

    // 349 AC-12: another prefix's decision is shown with its prefix.
    [Fact]
    public void Decisions_OtherPrefix_PrintsOne()
    {
        WriteDecisionFolder();
        WriteNote("Plan", "!d zz99-7 theirs\n");
        File.SetLastWriteTimeUtc(Path.Combine(root, "Plan.md"), new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc));

        Result result = Run("decisions", "ZZ99-7");

        Assert.Equal(0, result.Code);
        Assert.Equal("#!dzz99-7\nNote: Plan\nDate: 2026-11-01\nPeople: \n\ntheirs\n", result.Out);
    }

    // 349 AC-5, AC-12: a decision with copies in other notes lists them in an Also in: line.
    [Fact]
    public void Decisions_Reference_WithCopies_PrintsAlsoIn()
    {
        WriteDecisionFolder();
        WriteNote("Plan", $"{FolderLine}\n!d ab12-3 one\n");
        WriteNote("Copy B", $"{FolderLine}\n!d ab12-3 one\n");
        WriteNote("Copy A", $"{FolderLine}\n!d ab12-3 one\n");
        File.SetLastWriteTimeUtc(Path.Combine(root, "Plan.md"), new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Combine(root, "Copy A.md"), new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Combine(root, "Copy B.md"), new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc));

        Result result = Run("decisions", "3");

        Assert.Equal(0, result.Code);
        Assert.Equal("#!d3\nNote: Plan\nDate: 2026-11-01\nPeople: \nAlso in: Copy A, Copy B\n\none\n", result.Out);
    }

    // 349 AC-12: an unknown reference is an error with exit 1, shown as the user would type it.
    [Theory]
    [InlineData("4", "No decision #!d4.")]
    [InlineData("ab12-4", "No decision #!d4.")]
    [InlineData("ZZ99-4", "No decision #!dzz99-4.")]
    public void Decisions_UnknownReference_IsError(string reference, string error)
    {
        WriteDecisionFolder();
        WriteNote("Plan", "!d ab12-3 one");

        AssertError(Run("decisions", reference), 1, error);
    }

    // 343 AC-11: a reference mixed with a tag or --full is the usage error, before the folder is read.
    [Theory]
    [InlineData("3", "work")]
    [InlineData("work", "#!d3")]
    [InlineData("--full", "3")]
    [InlineData("--full", "ab12-3")]
    public void Decisions_NumberWithOthers_UsageError(params string[] args)
    {
        Result result = Run(["decisions", .. args]);

        AssertUsage(result);
        Assert.Equal(DecisionsUsage, result.ErrLines[0]);
        Assert.False(Directory.Exists(root));
    }

    // 180 AC-7: no decisions prints nothing and exits 0.
    [Fact]
    public void Decisions_None_NoOutput_ExitZero()
    {
        WriteNote("A", "nothing\n\nDecision: old\n\n#!d old marker");

        Result result = Run("decisions");

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.Equal(string.Empty, result.Err);
    }

    // 180 AC-7: a tag filters; its words are joined with one space, so #[a b] works.
    [Theory]
    [InlineData("work")]
    [InlineData("#work")]
    [InlineData("DECISIONS", "[work]")]
    [InlineData("decisions", "#[project", "alpha]")]
    public void Decisions_Tag_Filters(params string[] args)
    {
        WriteNote("Plan", "#work #[project alpha]\n\n!d go");
        WriteNote("Home", "#home\n\n!d paint");
        File.SetLastWriteTimeUtc(Path.Combine(root, "Plan.md"), new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc));
        string[] full = args[0].Equals("decisions", StringComparison.OrdinalIgnoreCase) ? args : ["decisions", .. args];

        Result result = Run(full);

        Assert.Equal(0, result.Code);
        Assert.Equal("2026-11-01\t\tPlan\t\tgo\n", result.Out);
    }

    // 343 AC-11: an invalid tag is the usage error and the folder is not read.
    [Theory]
    [InlineData("#1x")]
    [InlineData("#")]
    public void Decisions_InvalidTag_UsageError_FolderNotRead(string tag)
    {
        Result result = Run("decisions", tag);

        AssertUsage(result);
        Assert.Equal(DecisionsUsage, result.ErrLines[0]);
        Assert.False(Directory.Exists(root));
    }

    // 180 AC-7: --json is a usage error before the folder is read.
    [Fact]
    public void Decisions_Json_UsageError()
    {
        Result result = Run("decisions", "--json");

        AssertUsage(result);
        Assert.False(Directory.Exists(root));
    }

    // 180 AC-7: a bad context.db is an error and the file is left alone.
    [Fact]
    public void Decisions_BadContextDb_IsError()
    {
        WriteDecisionFolder();
        WriteNote("A", "!d x");
        File.WriteAllText(Path.Combine(root, TagIndex.FileName), "not a database at all, not at all, really not");
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("decisions"), 1, "Can't read context.db.");
        Assert.Equal(before, Snapshot());
    }

    // 349 AC-11: capture numbers the inbox's !d markers in a second write and adds the folder line (a captured line
    // starts with its time, so it is no marker itself).
    [Fact]
    public void Capture_NumbersDecision()
    {
        WriteDecisionFolder();
        WriteNote("inbox", "# inbox\n!d we ship\n\n");

        Assert.Equal(0, Run("capture", "more").Code);

        Assert.Equal($"# inbox\n{FolderLine}\n!d ab12-1 we ship\n\n- 2026-10-29 09:00 more\n", Read("inbox.md"));
    }

    // 351 AC-6: the numbering write keeps the inbox as captured (the 3 still there) as one version in .history first.
    [Fact]
    public void Capture_NumbersDecision_KeepsCapturedBytesAsVersion()
    {
        WriteDecisionFolder();
        WriteNote("inbox", "# inbox\n!d 3 people hired\n\n");

        Assert.Equal(0, Run("capture", "more").Code);

        Assert.Equal($"# inbox\n{FolderLine}\n!d ab12-1 people hired\n\n- 2026-10-29 09:00 more\n", Read("inbox.md"));
        string version = Assert.Single(Directory.GetFiles(Path.Combine(root, NoteHistory.FolderName, "inbox")));
        Assert.Equal(
            Encoding.UTF8.GetBytes("# inbox\n!d 3 people hired\n\n- 2026-10-29 09:00 more\n"),
            File.ReadAllBytes(version));
    }

    // 351 AC-6: a capture that numbers nothing keeps no version.
    [Fact]
    public void Capture_NothingNumbered_KeepsNoVersion()
    {
        WriteDecisionFolder();
        WriteNote("inbox", "# inbox\n");

        Assert.Equal(0, Run("capture", "more").Code);

        Assert.False(Directory.Exists(Path.Combine(root, NoteHistory.FolderName)));
    }

    // 349 AC-11: dayend numbers the day's note after its write.
    [Fact]
    public void Dayend_NumbersDecision()
    {
        WriteDecisionFolder();
        WriteNote("2026-10-29", "# 2026-10-29\n!d we stop\n");

        Assert.Equal(0, Run("dayend").Code);

        string note = Read("2026-10-29.md");
        Assert.StartsWith($"# 2026-10-29\n{FolderLine}\n!d ab12-1 we stop\n", note, StringComparison.Ordinal);
    }

    // 349 AC-11: import numbers the imported note's bare marker and keeps the source unchanged.
    [Fact]
    public void Import_NumbersDecision()
    {
        WriteDecisionFolder();
        string source = Path.Combine(outside, "Plan.md");
        File.WriteAllText(source, "# Plan\n!d go\n");

        Assert.Equal(0, Run("import", source).Code);

        Assert.Equal($"# Plan\n{FolderLine}\n!d ab12-1 go\n", Read("Plan.md"));
        Assert.Equal("# Plan\n!d go\n", File.ReadAllText(source));
    }

    // 349 AC-11: new with a template numbers the template's markers; an unreadable decision-folder.json leaves them.
    [Fact]
    public void New_WithTemplate_NumbersDecision_UnlessFolderFileBad()
    {
        WriteDecisionFolder();
        Directory.CreateDirectory(Path.Combine(root, "templates"));
        File.WriteAllText(Path.Combine(root, "templates", "Dec.md"), "# {{title}}\n!d pick one\n");

        Assert.Equal(0, Run("new", "Choice", "--template", "Dec").Code);
        Assert.Equal($"# Choice\n{FolderLine}\n!d ab12-1 pick one\n", Read("Choice.md"));

        File.WriteAllText(Path.Combine(root, DecisionFolder.FileName), "{broken");
        Assert.Equal(0, Run("new", "Other", "--template", "Dec").Code);
        Assert.Equal("# Other\n!d pick one\n", Read("Other.md"));
        Assert.Equal("{broken", Read(DecisionFolder.FileName));
    }

    // 180 AC-7: context with no arguments still creates no folder.
    [Fact]
    public void NoArguments_StillCreatesNoFolder()
    {
        AssertUsage(Run());
        Assert.False(Directory.Exists(root));
    }

    // 164 ------------------------------------------------------------------------------------------

    // 164 AC-6: today creates the daily note and prints it like note show; a second run prints the same, writes nothing.
    [Fact]
    public void Today_CreatesThenPrintsLikeNoteShow()
    {
        Run("todo", "add", "today", "call Anna");
        Directory.CreateDirectory(Path.Combine(root, CalendarStore.FolderName));
        File.WriteAllText(
            Path.Combine(root, CalendarStore.FolderName, "work.ics"),
            "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Standup\r\nDTSTART:20261029T090000Z\r\n"
            + "DTEND:20261029T091500Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");

        Result first = Run("today");

        const string Expected =
            "# 2026-10-29\n\n## Appointments\n- 09:00-09:15 Standup\n\n## Todos\n- call Anna\n";
        Assert.Equal((0, Expected, string.Empty), (first.Code, first.Out, first.Err));
        Assert.Equal(Expected, Read("2026-10-29.md"));
        Assert.Equal(Run("note", "show", "2026-10-29").Out, first.Out);

        Dictionary<string, string> before = Snapshot();
        Run("todo", "add", "today", "later todo");
        Dictionary<string, string> withTodo = Snapshot();
        Result second = Run("TODAY");

        Assert.Equal((0, Expected, string.Empty), (second.Code, second.Out, second.Err));
        Assert.NotEqual(before, withTodo);
        Assert.Equal(withTodo, Snapshot());
    }

    // 164 AC-6: a sensitive daily note answers the sensitive line and writes nothing.
    [Fact]
    public void Today_SensitiveNote_SensitiveTextNothingWritten()
    {
        WriteSealed("2026-10-29");
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("today"), 1, Cli.SensitiveText("2026-10-29"));
        Assert.Equal(before, Snapshot());
    }

    // 164 AC-6: a write failure is one fixed line without exception text.
    [Fact]
    public void Today_WriteFails_FixedLine()
    {
        Directory.CreateDirectory(Path.Combine(root, "2026-10-29.md"));

        AssertError(Run("today"), 1, "Can't write 2026-10-29.");
    }

    // 164 AC-6: --json with today is a usage error that creates nothing; today takes no arguments; help lists it.
    [Fact]
    public void Today_JsonOrArguments_UsageError_HelpListsIt()
    {
        AssertUsage(Run("today", "--json"));
        AssertUsage(Run("--json", "today"));
        AssertUsage(Run("today", "x"));
        Assert.False(Directory.Exists(root));
        Assert.Contains(Run("help").Out.Split('\n'), l => l.StartsWith("context today ", StringComparison.Ordinal));
    }

    // 165 ---------------------------------------------------------------------------------------

    private void WriteTemplate(string file, string text)
    {
        string path = Path.Combine(root, NoteTemplates.FolderName, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // 165 AC-9: a note from a template, title words joined with one space, filled from the fixed clock; exact bytes.
    [Fact]
    public void New_FromTemplate_CreatesFilledNote()
    {
        WriteTemplate("meeting.md", "# {{title}}\r\n{{date}} {{time}}\r\n{{Date}}\r\n");
        string template = File.ReadAllText(Path.Combine(root, NoteTemplates.FolderName, "meeting.md"));

        Result result = Run("new", "Weekly", "sync", "--TEMPLATE", "Meeting.md");

        Assert.Equal((0, "Created Weekly sync\n", string.Empty), (result.Code, result.Out, result.Err));
        Assert.Equal(
            Encoding.UTF8.GetBytes("# Weekly sync\r\n2026-10-29 09:00\r\n{{Date}}\r\n"),
            File.ReadAllBytes(Path.Combine(root, "Weekly sync.md")));
        Assert.Equal(template, File.ReadAllText(Path.Combine(root, NoteTemplates.FolderName, "meeting.md")));
    }

    // 165 AC-9: without --template the plain note is created.
    [Fact]
    public void New_Plain_CreatesTitleNote()
    {
        Result result = Run("new", "Plan", "B");

        Assert.Equal((0, "Created Plan B\n", string.Empty), (result.Code, result.Out, result.Err));
        Assert.Equal("# Plan B\n", Read("Plan B.md"));
    }

    // 165 AC-9 (with AC-6): an invalid or taken title is one stderr line, exit 1, nothing written, template not read.
    [Theory]
    [InlineData("a/b", "A note title can't contain / \\ : or control characters.")]
    [InlineData("plan", "Note plan already exists.")]
    [InlineData("Secret", "Note Secret already exists.")]
    public void New_BadOrTakenTitle_Error(string title, string line)
    {
        WriteNote("Plan", "# Plan\n");
        File.WriteAllBytes(Path.Combine(root, "secret" + NoteStore.SensitiveExtension), [1]);
        WriteTemplate("meeting.md", "m");
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("new", title, "--template", "meeting"), Cli.ExitError, line);
        AssertError(Run("new", title), Cli.ExitError, line);
        Assert.Equal(before, Snapshot());
    }

    // 165 AC-9 (with AC-5): a missing template lists the templates, or says how to add one; nothing written.
    [Fact]
    public void New_MissingTemplate_Hints()
    {
        AssertError(
            Run("new", "Plan", "--template", "standup"),
            Cli.ExitError,
            "No template called standup. Add one as templates/standup.md.");
        Assert.False(Directory.Exists(root));

        WriteTemplate("review.md", "r");
        WriteTemplate("Meeting.md", "m");
        Dictionary<string, string> before = Snapshot();

        AssertError(
            Run("new", "Plan", "--template", "../review"),
            Cli.ExitError,
            "No template called ../review. Templates: Meeting, review.");
        Assert.Equal(before, Snapshot());
    }

    // 165 AC-9 (with AC-5): an unreadable or too-big template, nothing written.
    [Fact]
    public void New_UnreadableOrTooBig_Error()
    {
        string folder = Path.Combine(root, NoteTemplates.FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "big.md"), new byte[NoteStore.MaxEditBytes + 1]);
        WriteTemplate("long.md", string.Concat(Enumerable.Repeat("{{title}}", (int)(NoteStore.MaxEditBytes / 9))));
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("new", "Plan", "--template", "big"), Cli.ExitError, "Can't read template big.");
        AssertError(Run("new", "A long title", "--template", "LONG"), Cli.ExitError, "Template long is too big.");
        Assert.Equal(before, Snapshot());
    }

    // 165 AC-9 (with AC-7): a sensitive templates note means no template is used.
    [Fact]
    public void New_TemplatesSensitive_Error()
    {
        WriteTemplate("meeting.md", "m");
        File.WriteAllBytes(Path.Combine(root, "Templates" + NoteStore.SensitiveExtension), [1]);
        Dictionary<string, string> before = Snapshot();

        AssertError(
            Run("new", "Plan", "--template", "meeting"),
            Cli.ExitError,
            "templates is sensitive, so its templates can't be used.");
        Assert.Equal(before, Snapshot());
    }

    // 165 AC-9: no title, --template without a name, or --json is a usage error before the folder is touched.
    [Theory]
    [InlineData("new")]
    [InlineData("new", "  ")]
    [InlineData("new", "--template", "meeting")]
    [InlineData("new", "Plan", "--template")]
    [InlineData("new", "Plan", "--template", " ")]
    [InlineData("new", "Plan", "--json")]
    [InlineData("--json", "new", "Plan", "--template", "meeting")]
    public void New_UsageErrors_FolderNotCreated(params string[] args)
    {
        AssertUsage(Run(args));
        Assert.False(Directory.Exists(root));
    }

    // 165 AC-9: a name from the folder is cleaned before it is printed.
    [Fact]
    public void New_MissingTemplate_NamesCleaned()
    {
        WriteTemplate("a\u0007b.md", "x");

        Result result = Run("new", "Plan", "--template", "zz\u001b");

        Assert.Equal(Cli.ExitError, result.Code);
        Assert.DoesNotContain('\u001b', result.Err);
    }

    // 167 ------------------------------------------------------------------------------------------

    private const string MeetingIcs =
        "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Review: Q4/plan\r\nDTSTART:20261029T100000Z\r\n"
        + "DTEND:20261029T110000Z\r\nLOCATION:Room 1\r\nATTENDEE;CN=Anna Schmidt:mailto:anna@example.org\r\n"
        + "ATTENDEE;CN=Bob:mailto:bob@example.org\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    // A calendar file (10:00 Review) and a fresh Exchange feed (08:00 Standup): the feed's event is number 1.
    private void WriteMeetingCalendar()
    {
        WriteIcs("work.ics", MeetingIcs);
        new CalendarLink(root).Set(FakeHttpHandler.Link);
    }

    // 167 AC-1/AC-5, 220 AC-9: meeting <n> numbers the appointments as current lists them (feed included), creates
    // Meetings/<title>/<date> and prints it like note show; a second run prints the same and writes nothing.
    [Fact]
    public void Meeting_CreatesThenPrintsLikeNoteShow()
    {
        WriteMeetingCalendar();

        Result first = Run("meeting", "2");

        const string Expected =
            "# 2026-10-29\n\n#meeting\n\n- Title: Review: Q4/plan\n- Time: 2026-10-29 10:00-11:00\n"
            + "- Location: Room 1\n- Attendees: @[Anna Schmidt], @[Bob]\n"
            + "\n## Notes\n\n## Decisions\n\n## Actions\n";
        string file = Path.Combine("Meetings", "Review Q4 plan", "2026-10-29.md");
        Assert.Equal((0, Expected, string.Empty), (first.Code, first.Out, first.Err));
        Assert.Equal(Expected, Read(file));
        Assert.Equal("# Meetings\n", Read("Meetings.md"));
        Assert.Equal("# Review Q4 plan\n", Read(Path.Combine("Meetings", "Review Q4 plan.md")));
        Assert.False(File.Exists(Path.Combine(root, "2026-10-29 Review Q4 plan.md")));
        Assert.Equal(Run("note", "show", "Meetings/Review Q4 plan/2026-10-29").Out, first.Out);
        Assert.DoesNotContain("example.org", Read(file), StringComparison.Ordinal);

        Result standup = Run("MEETING", "01");
        Assert.Equal(0, standup.Code);
        Assert.StartsWith("# 2026-10-29\n\n#meeting\n\n- Title: Standup\n- Time: 2026-10-29 08:00-08:15\n", standup.Out, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "Meetings", "Standup", "2026-10-29.md")));

        Dictionary<string, string> before = Snapshot();
        Result second = Run("meeting", "2");

        Assert.Equal((0, Expected, string.Empty), (second.Code, second.Out, second.Err));
        Assert.Equal(before, Snapshot());
    }

    // 245 AC-11: meeting <n> copies yesterday's open todo of the same meeting under "## From last time".
    [Fact]
    public void Meeting_PreviousOccurrence_CarriesOverOpenTodo()
    {
        WriteIcs("weekly.ics", "BEGIN:VCALENDAR\r\n" + Event("Weekly", "20261029T090000", "20261029T093000") + "END:VCALENDAR\r\n");
        WriteNested("Meetings", "# Meetings\n");
        WriteNested("Meetings/Weekly", "# Weekly\n");
        WriteNested("Meetings/Weekly/2026-10-28", "# 2026-10-28\n\n- [ ] Send budget\n- [x] Done thing\n");

        Result result = Run("meeting", "1");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.EndsWith(
            "\n## From last time\n\nFrom [[Meetings/Weekly/2026-10-28]]:\n\n- [ ] Send budget\n", result.Out, StringComparison.Ordinal);
        Assert.StartsWith("# 2026-10-29\n\n#meeting\n\n- Title: Weekly\n", result.Out, StringComparison.Ordinal);
        Assert.Equal(result.Out, Read(Path.Combine("Meetings", "Weekly", "2026-10-29.md")));
    }

    // 167 AC-5, 220 AC-6: an existing legacy note (another case, other bytes) is printed as it is and never changed.
    [Fact]
    public void Meeting_ExistingNote_PrintedUnchanged()
    {
        WriteIcs("work.ics", MeetingIcs);
        WriteNote("2026-10-29 REVIEW Q4 PLAN", "mine\n");
        Dictionary<string, string> before = Snapshot();

        Result result = Run("meeting", "1");

        Assert.Equal((0, "mine\n", string.Empty), (result.Code, result.Out, result.Err));
        Assert.Equal(before, Snapshot());
    }

    // 167 AC-5: an existing sensitive note answers the sensitive line and writes nothing.
    [Fact]
    public void Meeting_SensitiveNote_SensitiveTextNothingWritten()
    {
        WriteIcs("work.ics", MeetingIcs);
        WriteSealed("2026-10-29 Review Q4 plan");
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("meeting", "1"), 1, Cli.SensitiveText("2026-10-29 Review Q4 plan"));
        Assert.Equal(before, Snapshot());
    }

    // 167 AC-5: no appointments, or a number beyond the count: one stderr line, exit 1, nothing written.
    [Fact]
    public void Meeting_NoSuchAppointment_ErrorNothingWritten()
    {
        Directory.CreateDirectory(root);
        AssertError(Run("meeting", "1"), 1, "There are no appointments today.");
        Assert.Empty(Snapshot());

        WriteIcs("work.ics", MeetingIcs);
        Dictionary<string, string> before = Snapshot();
        AssertError(Run("meeting", "2"), 1, "There is no appointment 2 today.");
        AssertError(Run("meeting", "2147483647"), 1, "There is no appointment 2147483647 today.");
        Assert.Equal(before, Snapshot());
    }

    // 167 AC-5: a failed write is one fixed line without exception text.
    [Fact]
    public void Meeting_WriteFails_FixedLine()
    {
        WriteIcs("work.ics", MeetingIcs);
        WriteNested("Meetings", "# Meetings\n");
        WriteNested("Meetings/Review Q4 plan", "# Review Q4 plan\n");
        Directory.CreateDirectory(Path.Combine(root, "Meetings", "Review Q4 plan", "2026-10-29.md"));

        AssertError(Run("meeting", "1"), 1, "Can't write Meetings/Review Q4 plan/2026-10-29.");
    }

    // 220 AC-8: a sensitive top-level Meetings is in the way: the fixed write line, nothing written.
    [Fact]
    public void Meeting_SensitiveMeetingsFolder_FixedLineNothingWritten()
    {
        WriteIcs("work.ics", MeetingIcs);
        WriteSealed("Meetings");
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("meeting", "1"), 1, "Can't write Meetings/Review Q4 plan/2026-10-29.");
        Assert.Equal(before, Snapshot());
    }

    // 167 AC-5/AC-7, 381 AC-2/AC-10: a zero or overflowing number, --json, --book twice or --book with --json is a
    // usage error before the folder is read (the other rows of 167 now pick by AC-1/AC-4); help lists meeting.
    [Theory]
    [InlineData("meeting", "0")]
    [InlineData("meeting", "00")]
    [InlineData("meeting", "0", "--book")]
    [InlineData("meeting", "--book", "--book")]
    [InlineData("meeting", "--BOOK", "Review", "--book")]
    [InlineData("meeting", "--book", "--json")]
    [InlineData("--json", "meeting", "Review", "--book")]
    [InlineData("meeting", "--json")]
    [InlineData("meeting", "2147483648")]
    [InlineData("meeting", "99999999999999999999999")]
    [InlineData("meeting", "1", "--json")]
    [InlineData("--JSON", "meeting", "1")]
    public void Meeting_BadArguments_UsageError(params string[] args)
    {
        AssertUsage(Run(args));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Meeting_HelpListsIt()
    {
        Assert.Contains(
            Run("help").Out.Split('\n'),
            l => l.StartsWith("context meeting [<n>|<H:mm>|<title...>] [--book] ", StringComparison.Ordinal));
    }

    // 381 ----------------------------------------------------------------------------------------

    // An all-day Holiday, Standup 08:00-08:15, Review 10:00-11:00, Plan 10:30-11:30 and Retro 14:00-15:00 today.
    private void WriteDayCalendar()
    {
        WriteIcs(
            "day.ics",
            "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Holiday\r\nDTSTART;VALUE=DATE:20261029\r\nEND:VEVENT\r\n"
            + Event("Standup", "20261029T080000Z", "20261029T081500Z")
            + Event("Review", "20261029T100000Z", "20261029T110000Z")
            + Event("Plan", "20261029T103000Z", "20261029T113000Z")
            + Event("Retro", "20261029T140000Z", "20261029T150000Z")
            + "END:VCALENDAR\r\n");
    }

    private static string TitleLine(Result result) =>
        result.Out.Split('\n').Single(l => l.StartsWith("- Title: ", StringComparison.Ordinal))["- Title: ".Length..];

    private void WriteQuicklist() =>
        WriteFile(
            Path.Combine(times, TimesProjects.FileName),
            "{\"projects\":[{\"workItem\":77,\"title\":\"x\"},{\"workItem\":1234,\"title\":\"Sprint\"}]}");

    private static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // 381 AC-1: no argument (or a blank one) picks the running timed appointment, else the next one; never all-day.
    [Theory]
    [InlineData(7, 0, "Standup")]
    [InlineData(8, 5, "Standup")]
    [InlineData(8, 15, "Review")]
    [InlineData(9, 0, "Review")]
    [InlineData(10, 45, "Review")]
    [InlineData(11, 15, "Plan")]
    [InlineData(11, 30, "Retro")]
    public void Meeting_NoArgument_PicksCurrentOrNext(int hour, int minute, string title)
    {
        WriteDayCalendar();
        clock = new DateTime(2026, 10, 29, hour, minute, 0, DateTimeKind.Utc);

        foreach (string[] args in new[] { new[] { "meeting" }, new[] { "meeting", "" }, new[] { "meeting", " " } })
        {
            Result result = Run(args);
            Assert.Equal((0, string.Empty), (result.Code, result.Err));
            Assert.Equal(title, TitleLine(result));
        }

        Assert.Empty(runner!.Calls);
    }

    // 381 AC-1: after the last timed appointment (an all-day one is still on), one stderr line, nothing written.
    [Fact]
    public void Meeting_NoArgument_AfterLast_ErrorNothingWritten()
    {
        WriteDayCalendar();
        clock = new DateTime(2026, 10, 29, 15, 0, 0, DateTimeKind.Utc);
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("meeting"), 1, "There is no current or next appointment today.");
        AssertError(Run("meeting", "--book"), 1, "There is no current or next appointment today.");
        Assert.Equal(before, Snapshot());
        Assert.Empty(runner!.Calls);
    }

    // 381 AC-2: a number still picks as current lists it (the all-day one included).
    [Fact]
    public void Meeting_Number_PicksAsBefore()
    {
        WriteDayCalendar();

        Assert.Equal("Holiday", TitleLine(Run("meeting", "1")));
        Assert.Equal("Review", TitleLine(Run("meeting", "3")));
        AssertError(Run("meeting", "6"), 1, "There is no appointment 6 today.");
    }

    // 381 AC-3: H:mm or HH:mm picks the first timed appointment running then; none gives one stderr line.
    [Fact]
    public void Meeting_Time_PicksTheOneRunningThen()
    {
        WriteDayCalendar();

        Assert.Equal("Standup", TitleLine(Run("meeting", "8:00")));
        Assert.Equal("Review", TitleLine(Run("meeting", "10:30")));
        Assert.Equal("Plan", TitleLine(Run("meeting", "11:00")));
        Dictionary<string, string> before = Snapshot();
        AssertError(Run("meeting", "08:15"), 1, "There is no appointment at 8:15 today.");
        AssertError(Run("meeting", "23:59"), 1, "There is no appointment at 23:59 today.");
        Assert.Equal(before, Snapshot());
    }

    // 381 AC-4: other words are a title (joined by one space): exact match ignoring case first, else contains.
    [Fact]
    public void Meeting_Title_ExactThenContains()
    {
        WriteDayCalendar();

        Assert.Equal("Plan", TitleLine(Run("meeting", "PLAN")));
        Assert.Equal("Retro", TitleLine(Run("meeting", "etr")));
        Assert.Equal("Standup", TitleLine(Run("meeting", "stand", "--book")));
    }

    // 381 AC-4: words that were usage errors in 167 are now a title that matches nothing: one line, nothing written.
    [Theory]
    [InlineData("1 2", "1", "2")]
    [InlineData("x", "x")]
    [InlineData("-1", "-1")]
    [InlineData("+1", "+1")]
    [InlineData("1.5", "1.5")]
    [InlineData("25:00", "25:00")]
    [InlineData("Re view", "Re", " view ")]
    public void Meeting_UnknownTitle_ErrorNothingWritten(string shown, params string[] words)
    {
        WriteDayCalendar();
        Dictionary<string, string> before = Snapshot();

        AssertError(Run(["meeting", .. words]), 1, $"There is no appointment called {shown} today.");
        Assert.Equal(before, Snapshot());
    }

    // 381 AC-6: a todo written under Actions is listed by todos like any other note todo.
    [Fact]
    public void Meeting_TodoUnderActions_ListedByTodos()
    {
        WriteDayCalendar();
        MarkConverted();
        Assert.Equal(0, Run("meeting", "Review").Code);
        string file = Path.Combine(root, "Meetings", "Review", "2026-10-29.md");
        File.AppendAllText(file, "- { } Send minutes\n");

        Result result = Run("todos");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.Equal("-\tSend minutes\tMeetings/Review/2026-10-29\n", result.Out);
    }

    // 381 AC-7: a meeting note whose title holds AB#1234 is listed under Notes by item 1234.
    [Fact]
    public void Meeting_AzureReferenceInTitle_ListedByItem()
    {
        WriteIcs("sprint.ics", "BEGIN:VCALENDAR\r\n" + Event("Sprint AB#1234", "20261029T100000Z", "20261029T110000Z") + "END:VCALENDAR\r\n");
        Assert.Equal(0, Run("meeting").Code);

        Result result = Run("item", "1234");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        string[] lines = result.Out.Split('\n');
        int notes = Array.IndexOf(lines, "Notes");
        Assert.True(notes >= 0);
        Assert.Contains("Sprint AB#1234", lines[notes + 1], StringComparison.Ordinal);
    }

    // 381 AC-8: without --book nothing runs Times and stdout is the note, as in 167.
    [Fact]
    public void Meeting_WithoutBook_NeverRunsTimes()
    {
        WriteMeetingCalendar();
        WriteQuicklist();

        Result first = Run("meeting", "2");
        Result second = Run("meeting", "Review");

        Assert.Equal((0, string.Empty), (first.Code, first.Err));
        Assert.Equal(Run("note", "show", "Meetings/Review Q4 plan/2026-10-29").Out, first.Out);
        Assert.Equal(first.Out, second.Out);
        Assert.Empty(runner!.Calls);
    }

    // 381 AC-9: --book in any position and case prints the note, runs checkpoint once, then the Booked line.
    [Theory]
    [InlineData("meeting", "Sprint", "--book")]
    [InlineData("meeting", "--book", "Sprint")]
    [InlineData("meeting", "--BOOK")]
    [InlineData("MEETING", "10:00", "--Book")]
    [InlineData("meeting", "1", "--book")]
    public void Meeting_Book_RunsCheckpointOnQuicklist(params string[] args)
    {
        WriteIcs("sprint.ics", "BEGIN:VCALENDAR\r\n" + Event("Sprint  AB#1234 #5", "20261029T100000Z", "20261029T110000Z") + "END:VCALENDAR\r\n");
        WriteQuicklist();

        Result result = Run(args);

        string note = Run("meeting", "Sprint").Out;
        Assert.Equal((0, note + "Booked 10:00 Sprint in Times.\n", string.Empty), (result.Code, result.Out, result.Err));
        Assert.Equal([["checkpoint", "10:00", "#2", "Sprint"]], runner!.Calls);
    }

    // 381 (security review): a title whose first word starts with # or - never reaches tm as a project or an option;
    // with a quicklist hit there is still exactly one #<position>.
    [Fact]
    public void Meeting_Book_HashTitle_OnePositionOnly()
    {
        WriteIcs("weekly.ics", "BEGIN:VCALENDAR\r\n" + Event("#05 Weekly AB#1234", "20261029T091500Z", "20261029T100000Z") + "END:VCALENDAR\r\n");
        WriteQuicklist();

        Result result = Run("meeting", "--book");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.EndsWith("\nBooked 9:15 05 Weekly in Times.\n", result.Out, StringComparison.Ordinal);
        Assert.Equal([["checkpoint", "9:15", "#2", "05 Weekly"]], runner!.Calls);
    }

    // 381 AC-9: a work item not on the quicklist (or none at all) books without a position; an empty name is Meeting.
    [Theory]
    [InlineData("Planning #99", "Planning")]
    [InlineData("Planning", "Planning")]
    [InlineData("AB#1234", "Meeting")]
    [InlineData("#05 Weekly", "05 Weekly")]
    [InlineData("--version", "version")]
    public void Meeting_Book_NoQuicklistHit_NoPosition(string title, string name)
    {
        WriteIcs("one.ics", "BEGIN:VCALENDAR\r\n" + Event(title, "20261029T091500Z", "20261029T100000Z") + "END:VCALENDAR\r\n");
        WriteQuicklist();

        Result result = Run("meeting", "--book");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.EndsWith($"\nBooked 9:15 {name} in Times.\n", result.Out, StringComparison.Ordinal);
        Assert.Equal(
            title == "AB#1234" ? [["checkpoint", "9:15", "#2", name]] : [["checkpoint", "9:15", name]],
            runner!.Calls);
    }

    // 381 AC-10: a failed run still prints the note, then one stderr line and exit 1.
    [Theory]
    [InlineData("notfound", "Times isn't installed, so nothing was booked.")]
    [InlineData("timedout", "Times didn't answer in time.")]
    [InlineData("refused", "Times refused the booking: no project Sprint")]
    [InlineData("quiet", "Times refused the booking: exit code 3")]
    [InlineData("norunner", "Times isn't installed, so nothing was booked.")]
    public void Meeting_Book_Failure_NotePrintedErrorLine(string kind, string line)
    {
        WriteMeetingCalendar();
        runner = kind == "norunner" ? null : new FakeTimesRunner
        {
            Result = kind switch
            {
                "notfound" => TimesRunResult.NotFound(),
                "timedout" => TimesRunResult.TimedOut(),
                "refused" => new TimesRunResult(TimesRunKind.Done, 2, "ignored\n", "\n  no project\tSprint \nmore"),
                _ => new TimesRunResult(TimesRunKind.Done, 3, string.Empty, string.Empty),
            },
        };

        Result result = Run("meeting", "Review", "--book");

        Assert.Equal(1, result.Code);
        Assert.Equal(Run("note", "show", "Meetings/Review Q4 plan/2026-10-29").Out, result.Out);
        Assert.Equal([line], result.ErrLines);
    }

    // 381 AC-10: an all-day appointment prints its note and the all-day line, and starts no run.
    [Fact]
    public void Meeting_Book_AllDay_NoRun()
    {
        WriteDayCalendar();

        Result result = Run("meeting", "Holiday", "--book");

        Assert.Equal(1, result.Code);
        Assert.StartsWith("# 2026-10-29\n\n#meeting\n\n- Title: Holiday\n", result.Out, StringComparison.Ordinal);
        Assert.Equal(["All-day appointments can't be booked in Times."], result.ErrLines);
        Assert.Empty(runner!.Calls);
    }

    // 381 AC-10: when the note can't be made (sensitive, failed write) --book gives the same error and runs nothing.
    [Fact]
    public void Meeting_Book_NoteNotMade_NoRun()
    {
        WriteIcs("work.ics", MeetingIcs);
        WriteSealed("2026-10-29 Review Q4 plan");
        AssertError(Run("meeting", "1", "--book"), 1, Cli.SensitiveText("2026-10-29 Review Q4 plan"));

        File.Delete(Directory.GetFiles(root, "2026-10-29 Review Q4 plan*").Single());
        WriteNested("Meetings", "# Meetings\n");
        WriteNested("Meetings/Review Q4 plan", "# Review Q4 plan\n");
        Directory.CreateDirectory(Path.Combine(root, "Meetings", "Review Q4 plan", "2026-10-29.md"));
        AssertError(Run("meeting", "--book", "1"), 1, "Can't write Meetings/Review Q4 plan/2026-10-29.");

        Assert.Empty(runner!.Calls);
    }

    // 167 AC-5: current's text and JSON output stay byte for byte as before (no numbers).
    [Fact]
    public void Meeting_CurrentOutputUnchanged()
    {
        WriteMeetingCalendar();

        Result text = Run("current");
        Result json = Run("current", "--json");

        Assert.Equal(
            "Today, 2026-10-29\nAppointments\n08:00-08:15\tStandup\t\n10:00-11:00\tReview: Q4/plan\tRoom 1\n"
            + "Due\nNothing due today. Add one with /todo today <text>.\n",
            text.Out);
        Assert.Equal(CurrentJsonGolden, json.Out);
    }

    private const string CurrentJsonGolden =
        "{\"today\":\"2026-10-29\",\"appointments\":["
        + "{\"start\":\"2026-10-29T08:00:00\\u002B00:00\",\"end\":\"2026-10-29T08:15:00\\u002B00:00\",\"allDay\":false,"
        + "\"title\":\"Standup\",\"location\":null,\"related\":[]},"
        + "{\"start\":\"2026-10-29T10:00:00\\u002B00:00\",\"end\":\"2026-10-29T11:00:00\\u002B00:00\",\"allDay\":false,"
        + "\"title\":\"Review: Q4/plan\",\"location\":\"Room 1\",\"related\":[]}],"
        + "\"calendar\":{\"status\":\"fresh\",\"cachedAt\":\"2026-10-29T09:00:00\\u002B00:00\",\"unreadableFiles\":[]},"
        + "\"due\":[],\"linkedNotes\":[],\"times\":null}\n";

    // 169 review ---------------------------------------------------------------------------------

    private void WriteReviewNote(string name, DateTime modifiedUtc)
    {
        WriteNote(name, "# " + name + "\n");
        File.SetLastWriteTimeUtc(Path.Combine(root, name + ".md"), modifiedUtc);
    }

    // done.md with a last-write time before every review window, so it is no changed note.
    private void WriteDoneLog(string text)
    {
        WriteNote(DoneLog.NoteName, text);
        File.SetLastWriteTimeUtc(Path.Combine(root, "done.md"), new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
    }

    private void WriteTodosJson(string json)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), json);
    }

    // 169 AC-4: every heading with its lines, newest note first, done date, due date, next week's appointments.
    [Fact]
    public void Review_PrintsEverySection()
    {
        WriteReviewNote("Plan", new DateTime(2026, 10, 28, 14, 5, 0, DateTimeKind.Utc));
        WriteReviewNote("Older", new DateTime(2026, 10, 23, 8, 0, 0, DateTimeKind.Utc));
        WriteReviewNote("Too old", new DateTime(2026, 10, 22, 23, 0, 0, DateTimeKind.Utc));
        WriteTodosJson("["
            + "{\"text\":\"Sent report\",\"due\":\"2026-10-20\",\"created\":\"2026-10-01T00:00:00Z\",\"done\":\"2026-10-27T10:00:00Z\"},"
            + "{\"text\":\"Call Bob\",\"due\":\"2026-10-25\",\"created\":\"2026-10-01T00:00:00Z\"},"
            + "{\"text\":\"Due today\",\"due\":\"2026-10-29\",\"created\":\"2026-10-01T00:00:00Z\"}]");
        WriteDoneLog("# done\n- 2026-10-27 Sent report (due 2026-10-20)\n");
        WriteIcs("a.ics", "BEGIN:VCALENDAR\r\n"
            + "BEGIN:VEVENT\r\nSUMMARY:Kickoff\r\nLOCATION:Room 1\r\nDTSTART:20261030T090000Z\r\nDTEND:20261030T100000Z\r\nEND:VEVENT\r\n"
            + "BEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20261102\r\nEND:VEVENT\r\n"
            + "BEGIN:VEVENT\r\nSUMMARY:Today\r\nDTSTART:20261029T090000Z\r\nDTEND:20261029T100000Z\r\nEND:VEVENT\r\n"
            + "END:VCALENDAR\r\n");

        Result result = Run("review");

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Err);
        Assert.Equal(
            "Review, 2026-10-23 to 2026-10-29\n"
            + "Changed notes\n2026-10-28 14:05\tPlan\n2026-10-23 08:00\tOlder\n"
            + "Done todos\n2026-10-27\tSent report\n"
            + "Overdue\n2026-10-25\tCall Bob\n"
            + "Next week\n2026-10-30\t09:00-10:00\tKickoff (Room 1)\n2026-11-02\tall day\t(no title)\n",
            result.Out);
        Assert.False(File.Exists(Path.Combine(root, TagIndex.FileName)));
    }

    // 169 AC-4: an empty folder still prints every heading, with no lines, and exits 0.
    [Fact]
    public void Review_Empty_EveryHeading()
    {
        Result result = Run("review");

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Err);
        Assert.Equal("Review, 2026-10-23 to 2026-10-29\nChanged notes\nDone todos\nNo todos done.\nOverdue\nNext week\n", result.Out);
        Assert.False(File.Exists(Path.Combine(root, TagIndex.FileName)));
    }

    // 169 AC-4: text from disk is cleaned and kept on one line.
    [Fact]
    public void Review_CleansText()
    {
        WriteTodosJson("[{\"text\":\"a\\u0007b\\nc\",\"due\":\"2026-10-01\",\"created\":\"2026-10-01T00:00:00Z\"}]");
        WriteIcs("a.ics", "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:x\\ny\r\nLOCATION:p\u001bq\r\n"
            + "DTSTART:20261031T090000Z\r\nDTEND:20261031T100000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");

        Result result = Run("review");

        Assert.Equal(0, result.Code);
        Assert.Contains("Overdue\n2026-10-01\ta�b c\n", result.Out, StringComparison.Ordinal);
        Assert.Contains("2026-10-31\t09:00-10:00\tx y (p�q)\n", result.Out, StringComparison.Ordinal);
    }

    // 169 AC-5: an unreadable todos.json goes to stderr, the rest is still printed, exit 1.
    [Fact]
    public void Review_UnreadableTodos_StderrAndExitOne()
    {
        WriteReviewNote("Plan", new DateTime(2026, 10, 28, 14, 5, 0, DateTimeKind.Utc));
        WriteTodosJson("{ bad");

        Result result = Run("review");

        Assert.Equal(1, result.Code);
        Assert.Equal(["Can't read todos.json."], result.ErrLines);
        Assert.Equal(
            "Review, 2026-10-23 to 2026-10-29\nChanged notes\n2026-10-28 14:05\tPlan\nDone todos\nNo todos done.\nOverdue\nNext week\n",
            result.Out);
    }

    // 293 AC-12: an unreadable done.md leaves out the Done todos heading, prints the rest, stderr line, exit 1.
    [Fact]
    public void Review_UnreadableDoneLog_NoHeading_ExitOne()
    {
        WriteReviewNote("Plan", new DateTime(2026, 10, 28, 14, 5, 0, DateTimeKind.Utc));
        WriteTodosJson("[{\"text\":\"Call Bob\",\"due\":\"2026-10-25\",\"created\":\"2026-10-01T00:00:00Z\"}]");
        using (FileStream big = File.Create(Path.Combine(root, "done.md")))
        {
            big.SetLength(NoteStore.MaxNoteBytes + 1);
        }

        File.SetLastWriteTimeUtc(Path.Combine(root, "done.md"), new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));

        Result result = Run("review");

        Assert.Equal(1, result.Code);
        // 329 AC-10: the over-cap done log is also a note the todo marker conversion could not read.
        Assert.Equal(["Can't convert the todos in done.", "Can't read the done log."], result.ErrLines);
        Assert.Equal(
            "Review, 2026-10-23 to 2026-10-29\nChanged notes\n2026-10-28 14:05\tPlan\n"
            + "Overdue\n2026-10-25\tCall Bob\nNext week\n",
            result.Out);
    }

    // 293 AC-12: entries print as date and text (no due part), newest first; ones outside the week are left out.
    [Fact]
    public void Review_DoneLogEntries_DateAndText()
    {
        WriteDoneLog("# done\n- 2026-10-22 old\n- 2026-10-24 a\u0007b (due 2026-10-20)\n- 2026-10-29 last\n");

        Result result = Run("review");

        Assert.Equal(0, result.Code);
        Assert.Contains("Done todos\n2026-10-29\tlast\n2026-10-24\ta\uFFFDb\nOverdue\n", result.Out, StringComparison.Ordinal);
    }

    // 169 AC-5: unreadable calendar files and a failed feed give current's stderr lines and exit 1; no link shown.
    [Fact]
    public void Review_CalendarErrors_LikeCurrent()
    {
        WriteIcs("bad.ics", "not a calendar");
        new CalendarLink(Directory.CreateDirectory(root).FullName).Set(FakeHttpHandler.Link);
        http = FakeHttpHandler.Status(HttpStatusCode.InternalServerError);

        Result result = Run("review");

        Assert.Equal(1, result.Code);
        Assert.Equal(["Can't read calendar/bad.ics", "Can't download the Exchange calendar (HTTP 500)."], result.ErrLines);
        Assert.Equal(1, http.Requests);
        Assert.DoesNotContain(FakeHttpHandler.PathToken, result.Out + result.Err, StringComparison.Ordinal);
    }

    // 169 AC-5: bad todo ids are fixed first with current's line.
    [Fact]
    public void Review_MigratesBadIds()
    {
        WriteTodosJson("[{\"id\":\"nope\",\"text\":\"Old\",\"due\":\"2026-10-01\",\"created\":\"2026-10-01T00:00:00Z\"}]");

        Result result = Run("review");

        Assert.Equal(0, result.Code);
        Assert.Equal(["Fixed a bad todo id in todos.json."], result.ErrLines);
        Assert.Contains("Overdue\n2026-10-01\tOld\n", result.Out, StringComparison.Ordinal);
        Assert.Empty(Run("review").ErrLines);
    }

    // 169 AC-5: any extra argument is a usage error before the folder is read.
    [Theory]
    [InlineData("review", "x")]
    [InlineData("REVIEW", "--json", "now")]
    public void Review_ExtraArgument_UsageError(params string[] args)
    {
        Result result = Run(args);

        AssertUsage(result);
        Assert.False(Directory.Exists(root));
    }

    // 171 AC-9: dayend closes today: Worked from Times, then the due todos moved; settings.json is never touched.
    [Theory]
    [InlineData]
    [InlineData("today")]
    [InlineData("TODAY")]
    public void DayEnd_ClosesToday(params string[] args)
    {
        Directory.CreateDirectory(times);
        File.WriteAllText(Path.Combine(times, "2026-10-29.json"), TimesDayTests.Example);
        Run("todo", "add", "today", "call Anna");
        Run("todo", "add", "2026-10-30", "later");
        File.WriteAllText(Path.Combine(root, SettingsStore.FileName), "broken");
        SortedDictionary<string, string> timesBefore = FileSnapshot.Take(times);

        Result result = Run(["dayend", .. args]);

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Err);
        Assert.Equal("Updated 2026-10-29.\nMoved 1 todo(s) to 2026-10-30.\n", result.Out);
        Assert.Contains(
            "## Worked\n- Arrived 08:00, left 17:00, worked 8:30\n- #12 A: 4:00\n- B: 4:30\n\n## Moved to tomorrow\n- call Anna\n",
            Read("2026-10-29.md"),
            StringComparison.Ordinal);
        Assert.EndsWith("## Moved from 2026-10-29\n- call Anna\n", Read("2026-10-30.md"), StringComparison.Ordinal);
        Assert.Equal("broken", Read(SettingsStore.FileName));
        Assert.Equal(timesBefore, FileSnapshot.Take(times));

        Result again = Run(["dayend", .. args]);
        Assert.Equal("Updated 2026-10-29.\nMoved 0 todo(s) to 2026-10-30.\n", again.Out);
    }

    // 171 AC-9: a given day; no Times file says so in the note.
    [Fact]
    public void DayEnd_GivenDay()
    {
        Run("todo", "add", "2026-11-10", "old");

        Result result = Run("dayend", "2026-11-14");

        Assert.Equal(0, result.Code);
        Assert.Equal("Updated 2026-11-14.\nMoved 1 todo(s) to 2026-11-15.\n", result.Out);
        Assert.Contains("## Worked\n- No Times bookings for this day.\n", Read("2026-11-14.md"), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, SettingsStore.FileName)));
    }

    // 171 AC-9: any other argument, or more than one, is a usage error and the folder is not read or created.
    [Theory]
    [InlineData("dayend", "tomorrow")]
    [InlineData("dayend", "2026-11-31")]
    [InlineData("dayend", "14.11.2026")]
    [InlineData("dayend", "9999-12-31")]
    [InlineData("dayend", "today", "today")]
    [InlineData("dayend", "2026-11-14", "x")]
    [InlineData("dayend", "--json")]
    [InlineData("DAYEND", "today", "--json")]
    public void DayEnd_BadArguments_AreUsageErrors(params string[] args)
    {
        AssertUsage(Run(args));
        Assert.False(Directory.Exists(root));
    }

    // 171 AC-7: a sensitive note, an unreadable todos.json and a failed write.
    [Fact]
    public void DayEnd_Errors()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "2026-10-29.ctxs"), "{\"v\":1,\"text\":\"c2VhbGVk\"}");
        AssertError(Run("dayend"), Cli.ExitError, Cli.SensitiveText("2026-10-29"));
        File.Delete(Path.Combine(root, "2026-10-29.ctxs"));

        File.WriteAllText(Path.Combine(root, "2026-10-30.ctxs"), "{\"v\":1,\"text\":\"c2VhbGVk\"}");
        AssertError(Run("dayend"), Cli.ExitError, Cli.SensitiveText("2026-10-30"));
        Assert.Contains("## Worked", Read("2026-10-29.md"), StringComparison.Ordinal);
        File.Delete(Path.Combine(root, "2026-10-30.ctxs"));

        File.WriteAllText(Path.Combine(root, TodoStore.FileName), "not json");
        AssertError(Run("dayend"), Cli.ExitError, "Can't read todos.json.");
        Assert.Equal("not json", Read(TodoStore.FileName));

        WriteNote("2026-10-28", "# 2026-10-28\n" + new string('x', (int)NoteStore.MaxEditBytes - 20));
        AssertError(Run("dayend", "2026-10-28"), Cli.ExitError, "Can't write 2026-10-28.");
    }

    // 201 ----------------------------------------------------------------------------------------

    private void BreakDb() =>
        File.WriteAllText(Path.Combine(root, TagIndex.FileName), "not a database at all, not at all, really not");

    // A fixture with todos.json todos and todos inside notes, sharing a date.
    private void NoteTodoFixture()
    {
        Run("todo", "add", "2026-11-01", "stored one");
        Run("todo", "add", "2026-11-20", "14:00", "stored two");
        WriteNote("Meeting", "# Meeting\n- { } call Anna due:2026-11-20\n- { } no date\n- {x} ticked due:2026-11-01\n");
        WriteNote("alpha", "```\n- { } fenced\n```\n- { } first due:2026-11-01\n");
    }

    // AC-10, AC-4: the merged list; note todos carry their note, "-" when undated.
    [Fact]
    public void Todos_MergedList_WithNoteTodos()
    {
        NoteTodoFixture();

        Result result = Run("todos");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.Equal(
            "2026-11-01\tstored one\n2026-11-01\tfirst\talpha\n2026-11-20 14:00\tstored two\n"
                + "2026-11-20\tcall Anna\tMeeting\n-\tno date\tMeeting\n",
            result.Out);
    }

    // 329: marks the data folder as converted, so a "- [ ]" line a test writes stays a checkbox.
    private void MarkConverted()
    {
        string record = TodoMarkerConversion.RecordPath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        File.WriteAllText(record, "todo-braces\n");
    }

    // 329 AC-2: todos, --json and todo done see only the "{ }" todo of a note that also has a "[ ]" checkbox.
    [Fact]
    public void Todos_CheckboxAndTodo_OnlyTheBraceTodo()
    {
        WriteNote("Meeting", "- [ ] a\n- { } b\n");
        MarkConverted();

        Result list = Run("todos");

        Assert.Equal((0, "-\tb\tMeeting\n", string.Empty), (list.Code, list.Out, list.Err));
        Assert.Equal(
            "{\"todos\":[{\"due\":null,\"time\":null,\"text\":\"b\",\"note\":\"Meeting\"}]}\n",
            Run("todos", "--json").Out);
        AssertError(Run("todo", "done", "a"), 1, "No todo matches \"a\".");

        Result done = Run("todo", "done", "b");

        Assert.Equal(0, done.Code);
        Assert.Equal("- [ ] a\n- {x} b\n", Read("Meeting.md"));
    }

    // 329 AC-9, AC-10: the first todo command converts the old todo lines and reports it on stderr only (stdout and
    // exit code as usual); the record stops every later run, so a later "- [ ]" stays a checkbox.
    [Fact]
    public void Todos_OldTodoLines_ConvertedOnce_ReportedOnStderr()
    {
        WriteNote("Meeting", "- [ ] call Anna\n- [x] old\n");
        WriteNote("Plan", "- [ ] two\n");

        Result first = Run("todos");

        Assert.Equal((0, "-\tcall Anna\tMeeting\n-\ttwo\tPlan\n"), (first.Code, first.Out));
        Assert.Equal(["Changed 3 todo line(s) in 2 note(s) to - { }."], first.ErrLines);
        Assert.Equal("- { } call Anna\n- {x} old\n", Read("Meeting.md"));
        Assert.True(File.Exists(TodoMarkerConversion.RecordPath(root)));

        WriteNote("Plan", "- [ ] later\n");
        Result second = Run("todos");

        Assert.Equal((0, "-\tcall Anna\tMeeting\n", string.Empty), (second.Code, second.Out, second.Err));
        Assert.Equal("- [ ] later\n", Read("Plan.md"));
    }

    // AC-10: --json adds note to every todo; a note todo has time null and due null when undated.
    [Fact]
    public void Todos_Json_HasNoteField()
    {
        NoteTodoFixture();

        Result result = Run("todos", "--json");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.Equal(
            "{\"todos\":[{\"due\":\"2026-11-01\",\"time\":null,\"text\":\"stored one\",\"note\":null},"
                + "{\"due\":\"2026-11-01\",\"time\":null,\"text\":\"first\",\"note\":\"alpha\"},"
                + "{\"due\":\"2026-11-20\",\"time\":\"14:00\",\"text\":\"stored two\",\"note\":null},"
                + "{\"due\":\"2026-11-20\",\"time\":null,\"text\":\"call Anna\",\"note\":\"Meeting\"},"
                + "{\"due\":null,\"time\":null,\"text\":\"no date\",\"note\":\"Meeting\"}]}\n",
            result.Out);
    }

    // AC-10: note text and names go through OneLine.
    [Fact]
    public void Todos_NoteTodoText_IsOneSafeLine()
    {
        Directory.CreateDirectory(root);
        WriteNote("Plan", "- { } ring\u0007bell\n");

        Assert.Equal("-\tring\uFFFDbell\tPlan\n", Run("todos").Out);
    }

    // AC-10: a bad context.db still prints the todos.json todos (text or JSON), then the error, exit 1.
    [Fact]
    public void Todos_BadDb_PrintsStoredTodos_ThenError()
    {
        NoteTodoFixture();
        BreakDb();

        Result text = Run("todos");
        Result json = Run("todos", "--json");

        Assert.Equal((1, "2026-11-01\tstored one\n2026-11-20 14:00\tstored two\n"), (text.Code, text.Out));
        Assert.Equal(["Can't read context.db."], text.ErrLines);
        Assert.Equal(1, json.Code);
        Assert.Equal(
            "{\"todos\":[{\"due\":\"2026-11-01\",\"time\":null,\"text\":\"stored one\",\"note\":null},"
                + "{\"due\":\"2026-11-20\",\"time\":\"14:00\",\"text\":\"stored two\",\"note\":null}]}\n",
            json.Out);
        Assert.Equal(["Can't read context.db."], json.ErrLines);
    }

    // AC-10: a bad todos.json stays the one error, nothing on stdout, even with note todos.
    [Fact]
    public void Todos_BadTodosJson_IsTheErrorOnly()
    {
        NoteTodoFixture();
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), "{ bad");

        AssertError(Run("todos"), 1, "Can't read todos.json.");
        AssertError(Run("todos", "--json"), 1, "Can't read todos.json.");
    }

    // AC-11, AC-6: todo done picks a note todo and ticks only its box, keeping CRLF and the BOM.
    [Fact]
    public void TodoDone_NoteTodo_TicksItsLine()
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Meeting.md");
        byte[] bom = [0xEF, 0xBB, 0xBF];
        File.WriteAllBytes(path, [.. bom, .. Encoding.UTF8.GetBytes("# Meeting\r\n- { } call Anna due:2026-11-20\r\n- { } other\r\n")]);
        Run("todo", "add", "2026-11-01", "Buy milk");

        Result result = Run("todo", "done", "CALL anna");

        Assert.Equal((0, "Done: call Anna\n", string.Empty), (result.Code, result.Out, result.Err));
        Assert.Equal(
            [.. bom, .. Encoding.UTF8.GetBytes("# Meeting\r\n- {x} call Anna due:2026-11-20\r\n- { } other\r\n")],
            File.ReadAllBytes(path));
        Assert.Equal("2026-11-01\tBuy milk\n-\tother\tMeeting\n", Run("todos").Out);
    }

    // AC-11, AC-7: a line that changed since it was indexed is refused and the file bytes stay.
    [Fact]
    public void TodoDone_NoteTodo_ChangedLine_WritesNothing()
    {
        WriteNote("Meeting", "- { } call Anna\n");
        string path = Path.Combine(root, "Meeting.md");
        Assert.Equal("-\tcall Anna\tMeeting\n", Run("todos").Out);
        // Same length and the same last-write time, so the index keeps the old line.
        DateTime modified = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "- { } call Bnna\n");
        File.SetLastWriteTimeUtc(path, modified);
        Dictionary<string, string> before = Snapshot();

        AssertError(
            Run("todo", "done", "call anna"), 1, "The todo's line in Meeting changed. Open the note and tick it there.");
        Assert.Equal(before, Snapshot());
    }

    // AC-11: no todos in either list; only ticked lines in notes count as none.
    [Fact]
    public void TodoDone_NoOpenTodoAnywhere_IsNoTodos()
    {
        WriteNote("Meeting", "- {x} call Anna\n");

        AssertError(Run("todo", "done", "call"), 1, "There are no todos to mark done.");
        Assert.Equal("- {x} call Anna\n", Read("Meeting.md"));
    }

    // AC-11: a bad context.db is the error and nothing is written.
    [Fact]
    public void TodoDone_BadDb_IsErrorAndWritesNothing()
    {
        NoteTodoFixture();
        BreakDb();
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("todo", "done", "stored one"), 1, "Can't read context.db.");
        AssertError(Run("todo", "done", "call Anna"), 1, "Can't read context.db.");
        Assert.Equal(before, Snapshot());
    }

    // AC-11: a query matching a stored todo and a note todo is several matches, nothing written.
    [Fact]
    public void TodoDone_MatchesInBothLists_IsSeveral()
    {
        Run("todo", "add", "2026-11-01", "call Ben");
        WriteNote("Meeting", "- { } call Anna\n");
        Dictionary<string, string> before = SnapshotWithoutDb();

        AssertError(Run("todo", "done", "call"), 1, "2 todos match \"call\". Type more of the todo's text.");
        Assert.Equal(before, SnapshotWithoutDb());
    }

    // 265 ----------------------------------------------------------------------------------------

    // 265 AC-7: standup prints the text and exits 0; it writes nothing (AC-11) and help lists it.
    [Fact]
    public void Standup_PrintsText()
    {
        Directory.CreateDirectory(times);
        File.WriteAllText(Path.Combine(times, "2026-10-28.json"), TimesDayTests.Example);
        Run("todo", "add", "today", "call Anna");
        Run("todo", "add", "2026-11-02", "waiting: @[Ben] the slides");
        Dictionary<string, string> before = Snapshot();
        SortedDictionary<string, string> timesBefore = FileSnapshot.Take(times);

        Result result = Run("standup");

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Err);
        Assert.Equal(
            "Yesterday (2026-10-28)\n- #12 A: 4:00\n- B: 4:30\n\nToday\n- call Anna\n\nBlocked\n"
            + "- Ben: the slides (follow up 2026-11-02)\n",
            result.Out);
        Assert.Equal(before, Snapshot());
        Assert.Equal(timesBefore, FileSnapshot.Take(times));
        Assert.Contains(Run("help").Out.Split('\n'), l => l.StartsWith("context standup ", StringComparison.Ordinal));
    }

    // 265 AC-7: bad todo ids are fixed first with the usual line on stderr.
    [Fact]
    public void Standup_MigratesBadIds()
    {
        WriteTodosJson("[{\"id\":\"nope\",\"text\":\"Old\",\"due\":\"2026-10-01\",\"created\":\"2026-10-01T00:00:00Z\"}]");

        Result result = Run("standup");

        Assert.Equal(0, result.Code);
        Assert.Equal(["Fixed a bad todo id in todos.json."], result.ErrLines);
        Assert.Contains("\nToday\n- Old (due 2026-10-01)\n", result.Out, StringComparison.Ordinal);
        Assert.Empty(Run("standup").ErrLines);
    }

    // 265 AC-6, AC-7: an unreadable todos.json: the text still prints with its lines, stderr says so, exit 1.
    [Fact]
    public void Standup_TodosUnreadable_Exit1()
    {
        WriteTodosJson("not json");

        Result result = Run("standup");

        Assert.Equal(Cli.ExitError, result.Code);
        Assert.Equal(["Can't read todos.json."], result.ErrLines);
        Assert.Equal(
            "Yesterday (2026-10-28)\n- No Times bookings in the last 7 days.\n- Can't read todos.json.\n\nToday\n"
            + "- Can't read todos.json.\n\nBlocked\n- Can't read todos.json.\n",
            result.Out);
        Assert.Equal("not json", Read(TodoStore.FileName));
    }

    // 265 AC-7: any argument, --json too, is the usage error before the folder is read.
    [Theory]
    [InlineData("standup", "x")]
    [InlineData("standup", "--json")]
    [InlineData("STANDUP", "today")]
    [InlineData("--json", "standup")]
    public void Standup_Arguments_UsageError(params string[] args)
    {
        Result result = Run(args);

        AssertUsage(result);
        Assert.Equal(["Type: context standup. Run 'context help'."], result.ErrLines);
        Assert.False(Directory.Exists(root));
    }

    // 275 AC-7: `note show` and `export` of a note over NoteStore.MaxNoteBytes say so, fail and write no export file.
    [Fact]
    public void ShowAndExport_NoteOverLimit_SayOver5Mb()
    {
        WriteNote("Big", "#work\n");
        using (FileStream stream = new FileStream(Path.Combine(root, "Big.md"), FileMode.Open))
        {
            stream.SetLength(NoteStore.MaxNoteBytes + 1);
        }

        string target = Path.Combine(outside, "big.md");

        Result show = Run("note", "show", "Big");
        Result export = Run("export", "Big", target);

        Assert.NotEqual(0, show.Code);
        Assert.Equal(string.Empty, show.Out);
        Assert.Equal(["Can't read Big: it is over 5 MB."], show.ErrLines);
        Assert.NotEqual(0, export.Code);
        Assert.Equal(string.Empty, export.Out);
        Assert.Equal(["Can't read Big: it is over 5 MB."], export.ErrLines);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }
}
