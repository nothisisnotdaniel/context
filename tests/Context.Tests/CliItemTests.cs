using System.Text.Json;

namespace Context.Tests;

// 280: `context item <n> [--json] [--open]` (AC-7 to AC-9). A fixed zone, an injected today and now, FakeHttpHandler,
// FakeCommitLog, FakeDesktopLauncher and every folder in the test's own temp folder.
public sealed class CliItemTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 10);
    private static readonly DateTime Now = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly string parent;
    private readonly string root;
    private readonly string times;
    private readonly FakeCommitLog log = new FakeCommitLog();
    private readonly FakeDesktopLauncher launcher = new FakeDesktopLauncher();

    public CliItemTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-cliitem-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        times = Path.Combine(parent, "Times");
        Directory.CreateDirectory(root);
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
        public string[] OutLines => Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        public string[] ErrLines => Err.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private Result RunIn(string folder, params string[] args)
    {
        StringWriter stdout = new StringWriter();
        StringWriter stderr = new StringWriter();
        Cli cli = new Cli(
            stdout, stderr, folder, () => Today, () => Now, FakeHttpHandler.Ok(CalendarFeedTests.Ics), TimeZoneInfo.Utc,
            times, launcher, log);
        int code = cli.Run(args);
        return new Result(code, stdout.ToString(), stderr.ToString());
    }

    private Result Run(params string[] args) => RunIn(root, args);

    private void WriteNote(string name, string text)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private void AddTodo(string text, int dueDay, bool done = false)
    {
        TodoItem todo = new TodoStore(root, () => Now).Add(text, new DateOnly(2026, 11, dueDay));
        if (done)
        {
            new TodoStore(root, () => Now.AddHours(1)).Complete(todo.Id);
        }
    }

    private void WriteCalendar(string text)
    {
        string folder = Path.Combine(root, CalendarStore.FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.ics"), text);
    }

    private void WriteBooking(string day, string from, string to, int item)
    {
        Directory.CreateDirectory(times);
        File.WriteAllText(
            Path.Combine(times, day + ".json"),
            "{\"entries\":[{\"kind\":\"arrival\",\"time\":\"" + from + "\"},"
            + "{\"kind\":\"checkpoint\",\"time\":\"" + from + "\",\"name\":\"Work\",\"workItem\":" + item + "},"
            + "{\"kind\":\"leaving\",\"time\":\"" + to + "\"}]}");
    }

    private string Repo(string name)
    {
        string folder = Path.Combine(parent, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private void SetRepositories(params string[] folders) => new SettingsStore(root).SaveRepositories(folders);

    // Sets up one of everything for #1234.
    private void Everything()
    {
        WriteNote("Plan", "#1234 with @[Anna]");
        WriteNote("items", "# items");
        WriteNote("items/1234", "# 1234\n#1234");
        AddTodo("call @[Anna]\nabout #1234", 12);
        AddTodo("done #1234", 5, done: true);
        WriteCalendar("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Plan #1234\r\nLOCATION:Room 1\r\nDTSTART:20261105T090000Z\r\nDTEND:20261105T100000Z\r\nEND:VEVENT\r\n"
            + "BEGIN:VEVENT\r\nSUMMARY:Day #1234\r\nDTSTART;VALUE=DATE:20261104\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
        WriteBooking("2026-11-09", "08:00", "09:30", 1234);
        WriteBooking("2026-11-10", "08:00", "08:15", 1234);
        string repo = Repo("app");
        log.Set(repo, new LoggedCommit("0123456789abcdef", new DateOnly(2026, 11, 9), "Fix #1234"));
        SetRepositories(repo);
    }

    // 280 AC-7: every section, each heading always printed, fields one line.
    [Fact]
    public void Item_PrintsEverySection()
    {
        Everything();

        Result result = Run("item", "1234");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.Equal(
            [
                "Item #1234", "Notes", "Plan",
                "Todos", "2026-11-12\tcall @[Anna] about #1234", "done\t2026-11-10\tdone #1234",
                "People", "Anna\t2",
                "Meetings", "2026-11-05 09:00\tPlan #1234 (Room 1)", "2026-11-04\tDay #1234",
                "Hours", "2026-W46\t1:45", "total\t1:45",
                "Commits", "2026-11-09\tapp\t0123456\tFix #1234",
            ],
            result.OutLines);
    }

    // 280 AC-7: nothing set up; headings still printed, exit 0; the item number may carry a '#'.
    [Fact]
    public void Item_NothingSetUp_PrintsHints()
    {
        Result result = RunIn(root, "ITEM", "#1234");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.Equal(
            ["Item #1234", "Notes", "Todos", "People", "Meetings", "Hours", "- Times isn't set up.", "Commits", "- No repository folders set."],
            result.OutLines);
        Assert.Empty(log.Calls);
    }

    // 280 AC-7: the error lines go to stderr with exit 1, and the rest is still printed.
    [Fact]
    public void Item_Errors_OnStderr_RestPrinted()
    {
        WriteCalendar("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Plan #1234\r\nDTSTART:20261105T090000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
        File.WriteAllText(Path.Combine(root, TagIndex.FileName), "not a database at all, not at all, really not");
        File.WriteAllText(Path.Combine(root, "todos.json"), "{ broken");
        WriteBooking("2026-11-09", "08:00", "09:00", 1234);
        File.WriteAllText(Path.Combine(times, "2026-11-08.json"), "{ broken");
        string good = Repo("good");
        string bad = Repo("bad");
        log.Set(good, new LoggedCommit("abcdef0123", new DateOnly(2026, 11, 9), "Fix\n#1234\u001b[31m"));
        log.Fail(bad);
        SetRepositories(good, bad);

        Result result = Run("item", "1234");

        Assert.Equal(1, result.Code);
        Assert.Equal(
            ["Item #1234", "Notes", "Todos", "People", "Meetings", "2026-11-05 09:00\tPlan #1234", "Hours", "2026-W46\t1:00", "total\t1:00",
                "Commits", "2026-11-09\tgood\tabcdef0\tFix #1234\uFFFD[31m"],
            result.OutLines);
        Assert.Equal(
            ["Can't read context.db.", "Can't read todos.json.", "Skipped 1 unreadable Times day file(s).", "Can't read commits in bad."],
            result.ErrLines);
    }

    // 280 AC-7: a missing git.
    [Fact]
    public void Item_GitMissing_OnStderr()
    {
        SetRepositories(Repo("app"));
        log.GitMissing = true;

        Result result = Run("item", "1234");

        Assert.Equal(1, result.Code);
        Assert.Equal(["Can't run git."], result.ErrLines);
        Assert.Equal("Commits", result.OutLines[^1]);
    }

    // 280 AC-7: a missing or bad number, or a second one, is a usage error before the folder is read.
    [Theory]
    [InlineData]
    [InlineData("0")]
    [InlineData("0123")]
    [InlineData("12a")]
    [InlineData("1234567890")]
    [InlineData("1234", "5")]
    [InlineData("1234", "--open", "--open")]
    [InlineData("1234", "--json", "--open")]
    [InlineData("--open", "1234", "--json")]
    public void Item_BadArguments_UsageError_FolderUntouched(params string[] arguments)
    {
        string missing = Path.Combine(parent, "never");

        Result result = RunIn(missing, ["item", .. arguments]);

        Assert.Equal(2, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.Equal(["Type: context item <n> [--json] [--open] with the work item's number. Run 'context help'."], result.ErrLines);
        Assert.False(Directory.Exists(missing));
        Assert.Equal(0, launcher.Calls);
    }

    // 280 AC-8: the JSON object, names and order.
    [Fact]
    public void Item_Json_EveryField()
    {
        Everything();

        Result result = Run("item", "1234", "--json");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.EndsWith("}\n", result.Out, StringComparison.Ordinal);
        Assert.Equal(
            "{\"item\":1234,\"notes\":[\"Plan\"],"
            + "\"todos\":[{\"due\":\"2026-11-12\",\"time\":null,\"text\":\"call @[Anna] about #1234\",\"done\":null},"
            + "{\"due\":\"2026-11-05\",\"time\":null,\"text\":\"done #1234\",\"done\":\"2026-11-10\"}],"
            + "\"people\":[{\"name\":\"Anna\",\"count\":2}],"
            + "\"meetings\":[{\"start\":\"2026-11-05T09:00:00\\u002B00:00\",\"end\":\"2026-11-05T10:00:00\\u002B00:00\",\"allDay\":false,\"title\":\"Plan #1234\",\"location\":\"Room 1\"},"
            + "{\"start\":\"2026-11-04T00:00:00\\u002B00:00\",\"end\":\"2026-11-05T00:00:00\\u002B00:00\",\"allDay\":true,\"title\":\"Day #1234\",\"location\":null}],"
            + "\"hours\":{\"weeks\":[{\"week\":\"2026-W46\",\"minutes\":105}],\"totalMinutes\":105,\"skippedFiles\":0},"
            + "\"commits\":[{\"repo\":\"app\",\"hash\":\"0123456789abcdef\",\"date\":\"2026-11-09\",\"subject\":\"Fix #1234\"}]}\n",
            result.Out);
    }

    // 280 AC-8: the parts that couldn't be read, or aren't set up, are null.
    [Fact]
    public void Item_Json_Nulls()
    {
        File.WriteAllText(Path.Combine(root, TagIndex.FileName), "not a database at all, not at all, really not");
        File.WriteAllText(Path.Combine(root, "todos.json"), "{ broken");

        Result result = Run("--json", "item", "1234");

        Assert.Equal(1, result.Code);
        using JsonDocument document = JsonDocument.Parse(result.Out);
        JsonElement r = document.RootElement;
        Assert.Equal(
            ["item", "notes", "todos", "people", "meetings", "hours", "commits"],
            r.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Null, r.GetProperty("notes").ValueKind);
        Assert.Equal(JsonValueKind.Null, r.GetProperty("todos").ValueKind);
        Assert.Equal(JsonValueKind.Null, r.GetProperty("people").ValueKind);
        Assert.Equal(JsonValueKind.Array, r.GetProperty("meetings").ValueKind);
        Assert.Equal(JsonValueKind.Null, r.GetProperty("hours").ValueKind);
        Assert.Equal(JsonValueKind.Null, r.GetProperty("commits").ValueKind);
        Assert.Equal(["Can't read context.db.", "Can't read todos.json."], result.ErrLines);
    }

    // 280 AC-9: creates the page, writes the request (0600 in a 0700 folder) and starts the desktop once.
    [Fact]
    public void Open_CreatesPage_WritesRequest_Launches()
    {
        Result result = Run("item", "1234", "--open");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.Equal(["Created items/1234.", "Opened #1234."], result.OutLines);
        Assert.Equal("# 1234\n", File.ReadAllText(Path.Combine(root, "items", "1234.md")));
        string request = DesktopRequest.RequestPath(root);
        Assert.Equal("item 1234\n", File.ReadAllText(request));
        Assert.Equal([DesktopRequest.RequestFileName, DesktopRequest.LockFileName], Directory.GetFiles(DesktopRequest.Folder(root)).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(1, launcher.Calls);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(request));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(DesktopRequest.Folder(root)));
        }

        // A second run finds the page and replaces the request.
        Result again = Run("item", "1234", "--open");

        Assert.Equal(["Opened #1234."], again.OutLines);
        Assert.Equal(2, launcher.Calls);
        Assert.Equal("item 1234\n", File.ReadAllText(request));
    }

    // 280 AC-9: with the lock held (a running desktop) the launcher is not called.
    [Fact]
    public void Open_LockHeld_DoesNotLaunch()
    {
        PrivateFile.CreateFolder(DesktopRequest.Folder(root));
        using (FileStream held = PrivateFile.OpenLock(DesktopRequest.LockPath(root)))
        {
            Result result = Run("item", "77", "--open");

            Assert.Equal((0, string.Empty), (result.Code, result.Err));
            Assert.Equal(["Created items/77.", "Opened #77."], result.OutLines);
            Assert.Equal(0, launcher.Calls);
            Assert.Equal("item 77\n", File.ReadAllText(DesktopRequest.RequestPath(root)));
        }
    }

    // 280 AC-9: a launcher failure is one stderr line, exit 1, and the request stays.
    [Fact]
    public void Open_LauncherFails_RequestStays()
    {
        launcher.Fails = true;

        Result result = Run("item", "1234", "--open");

        Assert.Equal(1, result.Code);
        Assert.Equal(["Created items/1234."], result.OutLines);
        Assert.Equal(["Can't start the desktop app."], result.ErrLines);
        Assert.Equal(1, launcher.Calls);
        Assert.Equal("item 1234\n", File.ReadAllText(DesktopRequest.RequestPath(root)));
    }

    // 280 AC-9: a sensitive items note is the sensitive text, nothing written.
    [Fact]
    public void Open_SensitiveItems_Refused()
    {
        File.WriteAllText(Path.Combine(root, "items.ctxs"), "{}");

        Result result = Run("item", "1234", "--open");

        Assert.Equal(1, result.Code);
        Assert.Equal(string.Empty, result.Out);
        Assert.Equal([Cli.SensitiveText("items")], result.ErrLines);
        Assert.False(Directory.Exists(DesktopRequest.Folder(root)));
        Assert.False(Directory.Exists(Path.Combine(root, "items")));
        Assert.Equal(0, launcher.Calls);
    }

    // 280 AC-9: without a launcher (an unsupported OS) the start fails the same way.
    [Fact]
    public void Open_NoLauncher_CantStart()
    {
        StringWriter stdout = new StringWriter();
        StringWriter stderr = new StringWriter();
        Cli cli = new Cli(stdout, stderr, root, () => Today, () => Now, FakeHttpHandler.Ok(CalendarFeedTests.Ics), TimeZoneInfo.Utc);

        int code = cli.Run(["item", "5", "--open"]);

        Assert.Equal(1, code);
        Assert.Equal("Can't start the desktop app.\n", stderr.ToString());
    }
}
