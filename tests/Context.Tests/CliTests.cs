using System.Net;
using System.Reflection;
using System.Text;

namespace Context.Tests;

public sealed class CliTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private readonly string parent;
    private readonly string root;
    private readonly string outside;
    private readonly string times;
    private FakeHttpHandler http = FakeHttpHandler.Ok(CalendarFeedTests.Ics);

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
        Cli cli = new Cli(stdout, stderr, root, () => Today, () => Now, http, TimeZoneInfo.Utc, times);
        int code = cli.Run(args);
        return new Result(code, stdout.ToString(), stderr.ToString());
    }

    private void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".md"), text);
    }

    private string Read(string name) => File.ReadAllText(Path.Combine(root, name));

    // Every file under the root with its bytes, to prove nothing was written.
    private Dictionary<string, string> Snapshot()
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
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
            "capture <text...>", "todos", "todo add <yyyy-MM-dd|today|tomorrow> <text...>",
            "todo add every <weekday|day|month> <text...>", "todo done <text...>", "notes",
            "note show <name...>", "tags [tag...]", "find <attribute> <value...>",
            "search <text...>", "current", "review", "new <title...> [--template <name>]", "import <path>",
            "export <note> <path>", "version", "dayend [today|yyyy-MM-dd]",
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

    // 132 AC-4: the removed options are unknown options like any other: exit 2, not echoed, folder untouched.
    [Theory]
    [InlineData("--frobnicate")]
    [InlineData(RemovedFeature.Option + "-stdin", "notes")]
    [InlineData(RemovedFeature.Option, "topSecret55aa", "notes")]
    [InlineData(RemovedFeature.Option + "=topSecret55aa", "notes")]
    [InlineData(RemovedFeature.OptionUpper + "=topSecret55aa", "notes")]
    public void RemovedOptions_AreUsageErrorsLikeAnyUnknownOption(params string[] args)
    {
        Result result = Run(args);

        AssertUsage(result);
        Assert.Equal("Unknown command: that option. Run 'context help'.", result.ErrLines[0]);
        Assert.DoesNotContain("topSecret55aa", result.Out + result.Err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(root));
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
            "Print one JSON object: version, todos, notes, note show, tags, find, search, current, review.",
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
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("todo", "done", "1"), 1, "No todo matches \"1\".");
        Assert.Equal(before, Snapshot());

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
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("todo", "done", "two\u001b[31m", "three"), 1, "No todo matches \"two\uFFFD[31m three\".");
        Assert.Equal(before, Snapshot());
    }

    // 144 AC-8
    [Fact]
    public void TodoDone_SeveralMatches_IsErrorAndWritesNothing()
    {
        Run("todo", "add", "2026-11-01", "Call Anna");
        Run("todo", "add", "2026-11-02", "Call Ben");
        Run("todo", "add", "2026-11-03", "Buy milk");
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("todo", "done", "call"), 1, "2 todos match \"call\". Type more of the todo's text.");
        Assert.Equal(before, Snapshot());
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
            "{\"todos\":[{\"due\":\"2026-10-29\",\"text\":\"stretch\"},{\"due\":\"2026-10-29\",\"text\":\"pay rent\"},"
            + "{\"due\":\"2026-11-02\",\"text\":\"water plants\"}]}\n",
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
        // Every help line except meeting's (167 AC-7 lists "meeting <n>").
        string help = string.Join(
            '\n', Run("help").Out.Split('\n').Where(l => !l.StartsWith("context meeting <n>", StringComparison.Ordinal)));
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

    [Fact]
    public async Task Import_Fifo_ReturnsWithoutBlocking()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string fifo = Path.Combine(outside, "pipe.md");
        using (System.Diagnostics.Process mkfifo = System.Diagnostics.Process.Start("mkfifo", fifo))
        {
            mkfifo.WaitForExit();
        }

        Task<Result> run = Task.Run(() => Run("import", fifo));
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5))));
        // A FIFO reports length 0, so it is never opened: it imports as an empty note and never hangs.
        Assert.Equal(0, (await run).Code);
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

    // 167 AC-1/AC-5: meeting <n> numbers the appointments as current lists them (feed included), creates the note
    // and prints it like note show; a second run prints the same and writes nothing.
    [Fact]
    public void Meeting_CreatesThenPrintsLikeNoteShow()
    {
        WriteMeetingCalendar();

        Result first = Run("meeting", "2");

        const string Expected =
            "# 2026-10-29 Review Q4 plan\n\n#meeting\n\n- Title: Review: Q4/plan\n- Time: 2026-10-29 10:00-11:00\n"
            + "- Location: Room 1\n- Attendees: @[Anna Schmidt], @[Bob]\n";
        Assert.Equal((0, Expected, string.Empty), (first.Code, first.Out, first.Err));
        Assert.Equal(Expected, Read("2026-10-29 Review Q4 plan.md"));
        Assert.Equal(Run("note", "show", "2026-10-29 Review Q4 plan").Out, first.Out);
        Assert.DoesNotContain("example.org", Read("2026-10-29 Review Q4 plan.md"), StringComparison.Ordinal);

        Result standup = Run("MEETING", "01");
        Assert.Equal(0, standup.Code);
        Assert.StartsWith("# 2026-10-29 Standup\n\n#meeting\n\n- Title: Standup\n- Time: 2026-10-29 08:00-08:15\n", standup.Out, StringComparison.Ordinal);

        Dictionary<string, string> before = Snapshot();
        Result second = Run("meeting", "2");

        Assert.Equal((0, Expected, string.Empty), (second.Code, second.Out, second.Err));
        Assert.Equal(before, Snapshot());
    }

    // 167 AC-5: an existing note (another case, other bytes) is printed as it is and never changed.
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
        Directory.CreateDirectory(Path.Combine(root, "2026-10-29 Review Q4 plan.md"));

        AssertError(Run("meeting", "1"), 1, "Can't write 2026-10-29 Review Q4 plan.");
    }

    // 167 AC-5/AC-7: a missing, extra, non-digit, zero or overflowing number, or --json, is a usage error before the
    // folder is read; help lists meeting <n>.
    [Theory]
    [InlineData("meeting")]
    [InlineData("meeting", "")]
    [InlineData("meeting", "1", "2")]
    [InlineData("meeting", "x")]
    [InlineData("meeting", "-1")]
    [InlineData("meeting", "+1")]
    [InlineData("meeting", "1.5")]
    [InlineData("meeting", "0")]
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
        Assert.Contains(Run("help").Out.Split('\n'), l => l.StartsWith("context meeting <n> ", StringComparison.Ordinal));
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
        + "\"due\":[],\"linkedNotes\":[]}\n";

    // 169 review ---------------------------------------------------------------------------------

    private void WriteReviewNote(string name, DateTime modifiedUtc)
    {
        WriteNote(name, "# " + name + "\n");
        File.SetLastWriteTimeUtc(Path.Combine(root, name + ".md"), modifiedUtc);
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
        Assert.Equal("Review, 2026-10-23 to 2026-10-29\nChanged notes\nDone todos\nOverdue\nNext week\n", result.Out);
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
            "Review, 2026-10-23 to 2026-10-29\nChanged notes\n2026-10-28 14:05\tPlan\nDone todos\nOverdue\nNext week\n",
            result.Out);
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
}
