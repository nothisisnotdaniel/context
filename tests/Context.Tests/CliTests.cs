using System.Net;
using System.Text;

namespace Context.Tests;

public sealed class CliTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private readonly string parent;
    private readonly string root;
    private readonly string outside;
    private readonly Dictionary<string, string> env = new(StringComparer.Ordinal);
    private FakeHttpHandler http = FakeHttpHandler.Ok(CalendarFeedTests.Ics);

    public CliTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-cli-" + Guid.NewGuid().ToString("N"));
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

    private Result Run(params string[] args) => RunWithStdin(string.Empty, args);

    private Result RunWithStdin(string input, params string[] args)
    {
        StringWriter stdout = new StringWriter();
        StringWriter stderr = new StringWriter();
        Cli cli = new Cli(
            stdout,
            stderr,
            new StringReader(input),
            name => env.TryGetValue(name, out string? value) ? value : null,
            root,
            () => Today,
            () => Now,
            http,
            VaultTests.TestIterations,
            TimeZoneInfo.Utc);
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

    private void EncryptedRoot(Action<Encryption>? write = null)
    {
        Directory.CreateDirectory(root);
        Encryption encryption = SampleData.On(root);
        write?.Invoke(encryption);
        encryption.Use(null);
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
            "capture <text...>", "todos", "todo add <yyyy-MM-dd|today|tomorrow> <text...>", "todo done <n>", "notes",
            "note show <name...>", "tags [tag...]", "links <note...>", "current", "import <path>",
            "export <note> <path>",
        })
        {
            Assert.Contains($"context {usage} ", result.Out, StringComparison.Ordinal);
        }

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Help_WithEncryptionOn_AsksNoPassphrase()
    {
        EncryptedRoot();

        Result result = Run("help");

        Assert.Equal(0, result.Code);
        Assert.Equal(string.Empty, result.Err);
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

    [Fact]
    public void UnknownCommand_WithEncryptionOn_AsksNoPassphrase()
    {
        EncryptedRoot();

        AssertUsage(Run("frobnicate"));
    }

    [Fact]
    public void NoCommand_AfterPassphraseOption_IsUsageError()
    {
        AssertUsage(Run(Cli.PassphraseStdin));
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
        Assert.Equal("1\t2026-10-01\told one\n2\t2026-10-29\tcall #work\n3\t2026-10-30\tbuy milk\n", list.Out);
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
    [InlineData("todo", "done", "0")]
    [InlineData("todo", "done", "-1")]
    [InlineData("todo", "done", "x")]
    [InlineData("todo", "done", "1", "2")]
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

    [Fact]
    public void TodoDone_RemovesTheNthTodo()
    {
        Run("todo", "add", "2026-11-02", "later");
        Run("todo", "add", "2026-11-01", "sooner");

        Result result = Run("todo", "done", "1");

        Assert.Equal((0, "Done: sooner\n", string.Empty), (result.Code, result.Out, result.Err));
        Assert.Equal("1\t2026-11-02\tlater\n", Run("todos").Out);
    }

    [Fact]
    public void TodoDone_NoSuchNumber_IsErrorAndWritesNothing()
    {
        Run("todo", "add", "today", "one");
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("todo", "done", "2"), 1, "There is no todo 2.");
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("todos")]
    [InlineData("todo", "add", "today", "x")]
    [InlineData("todo", "done", "1")]
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
    [InlineData("links")]
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

    [Fact]
    public void NoteShow_Undecryptable_CantRead()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "Locked.md"), [0x00, 0x43, 0x54, 0x58, 0x45, 0x01, 1, 2, 3]);

        Result result = Run("note", "show", "Locked");

        Assert.Equal(1, result.Code);
        Assert.Equal(string.Empty, result.Out);
        // The first line is AC-13's warning, the error is the one line that stops the command.
        Assert.Equal("Can't read Locked.", result.ErrLines[^1]);
        Assert.Equal(2, result.ErrLines.Length);
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

    [Fact]
    public void Links_PrintsNameLinkAndTags()
    {
        WriteNote("A", "#work #home [[B]] [[Ghost]]");
        WriteNote("B", "#work #home [[A]]");
        WriteNote("C", "#work");

        Result result = Run("links", "a");

        Assert.Equal(0, result.Code);
        Assert.Equal("B\tboth ways\t#home #work\nC\t\t#work\nGhost\tlinks to (missing)\t\n", result.Out);
    }

    [Fact]
    public void Links_NoSuchNote_IsError()
    {
        WriteNote("A", "x");

        AssertError(Run("links", "Nope"), 1, "No note named Nope.");
    }

    [Theory]
    [InlineData("tags")]
    [InlineData("links", "A")]
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
            "Today, 2026-10-29\nAppointments\n10:00-11:00\tReview\tRoom 1\nDue\n1\t2026-10-28\tlate #work\n"
                + "2\t2026-10-29\tnow\nLinked notes\nPlan\t#work\n",
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
        Assert.EndsWith("Due\n1\t2026-10-29\tx #work\nLinked notes\n", result.Out, StringComparison.Ordinal);
        Assert.Equal(["Can't read context.db."], result.ErrLines);
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

    [Fact]
    public void Export_WithEncryption_WarnsOnStderrAndExitsZero()
    {
        EncryptedRoot(e => new NoteStore(root, e).Save("Plan", Encoding.UTF8.GetBytes(SampleData.NoteMarker)));
        env[Cli.PassphraseVariable] = VaultTests.Passphrase;
        string target = Path.Combine(outside, "plan.md");

        Result result = Run("export", "Plan", target);

        Assert.Equal((0, "Exported Plan.\n"), (result.Code, result.Out));
        Assert.Equal(["This copy is not encrypted."], result.ErrLines);
        Assert.Equal(SampleData.NoteMarker, File.ReadAllText(target));
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

    // AC-11 --------------------------------------------------------------------------------------

    [Fact]
    public void Encrypted_UnlocksFromEnvironment()
    {
        EncryptedRoot(e => new NoteStore(root, e).Save("Plan", Encoding.UTF8.GetBytes("secret text")));
        env[Cli.PassphraseVariable] = VaultTests.Passphrase;

        Result result = Run("note", "show", "Plan");

        Assert.Equal((0, "secret text\n", string.Empty), (result.Code, result.Out, result.Err));
    }

    [Fact]
    public void Encrypted_UnlocksFromStdinFirstLine_BeforeEnvironment()
    {
        EncryptedRoot(e => new NoteStore(root, e).Save("Plan", Encoding.UTF8.GetBytes("secret text")));
        env[Cli.PassphraseVariable] = "wrong one entirely";

        Result result = RunWithStdin(VaultTests.Passphrase + "\r\nsecond line\n", Cli.PassphraseStdin, "capture", "x");

        Assert.Equal((0, "Captured.\n", string.Empty), (result.Code, result.Out, result.Err));
        Assert.True(Vault.IsEncrypted(File.ReadAllBytes(Path.Combine(root, "inbox.md"))));
    }

    [Fact]
    public void Encrypted_NoPassphrase_IsErrorAndWritesNothing()
    {
        EncryptedRoot();
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("capture", "x"), 1, "Encryption is on: set CONTEXT_PASSPHRASE or use --passphrase-stdin.");
        AssertError(
            RunWithStdin(string.Empty, Cli.PassphraseStdin, "notes"),
            1,
            "Encryption is on: set CONTEXT_PASSPHRASE or use --passphrase-stdin.");
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Encrypted_WrongPassphrase_IsErrorAndNeverShown()
    {
        EncryptedRoot();
        Dictionary<string, string> before = Snapshot();
        const string Wrong = "wrongSecret91c4";
        env[Cli.PassphraseVariable] = Wrong;

        Result fromEnv = Run("todo", "add", "today", "x");
        Result fromStdin = RunWithStdin(Wrong + "\n", Cli.PassphraseStdin, "capture", "x");

        AssertError(fromEnv, 1, "Wrong passphrase.");
        AssertError(fromStdin, 1, "Wrong passphrase.");
        Assert.Equal(before, Snapshot());
        Assert.DoesNotContain(Wrong, fromEnv.Out + fromEnv.Err + fromStdin.Out + fromStdin.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Encrypted_PassphraseNeverInOutput()
    {
        EncryptedRoot();
        env[Cli.PassphraseVariable] = VaultTests.Passphrase;

        Result result = Run("capture", "x");

        Assert.Equal(0, result.Code);
        Assert.DoesNotContain(VaultTests.Passphrase, result.Out + result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Encrypted_BadKeyFile_FixedMessage()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Vault.KeyFilePath(root), "{ broken");
        env[Cli.PassphraseVariable] = VaultTests.Passphrase;
        Dictionary<string, string> before = Snapshot();

        AssertError(Run("notes"), 1, "Can't read encryption.json.");
        Assert.Equal(before, Snapshot());
    }

    // AC-12 --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("--passphrase=topSecret55aa", "notes")]
    [InlineData("--passphrase", "topSecret55aa", "notes")]
    [InlineData("capture", "--passphrase=topSecret55aa")]
    [InlineData("help", "--passphrase", "topSecret55aa")]
    public void PassphraseArgument_IsRefused_AndNotEchoed(params string[] args)
    {
        Result result = RunWithStdin("topSecret55aa\n", args);

        AssertUsage(result);
        Assert.DoesNotContain("topSecret55aa", result.Out + result.Err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void PassphraseStdin_AfterTheCommand_IsUsageError()
    {
        AssertUsage(Run("notes", Cli.PassphraseStdin));
        Assert.False(Directory.Exists(root));
    }

    // AC-13 --------------------------------------------------------------------------------------

    [Fact]
    public void EncryptedFilesWithoutKeyFile_WarnAndCarryOn()
    {
        EncryptedRoot(e => new NoteStore(root, e).Save("Plan", Encoding.UTF8.GetBytes("x")));
        File.Delete(Vault.KeyFilePath(root));

        Result result = Run("notes");

        Assert.Equal((0, "Plan\n"), (result.Code, result.Out));
        Assert.Equal(
            ["1 encrypted file but no encryption.json: new data is NOT encrypted. Restore it from a backup."],
            result.ErrLines);
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
        Run("todo", "done", "1");
        Run("import", source);
        Run("import", source);

        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
    }

    // AC-16 --------------------------------------------------------------------------------------

    [Fact]
    public void CliSource_MentionsNoSpectreAndNoConsole()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Context.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        string source = File.ReadAllText(Path.Combine(dir.FullName, "src", "Context", "Cli.cs"));
        Assert.DoesNotContain("Spectre", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.", source, StringComparison.Ordinal);
    }

    // Round 2 ------------------------------------------------------------------------------------

    [Fact]
    public void Import_Fifo_ReturnsWithoutBlocking()
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
        Assert.True(run.Wait(TimeSpan.FromSeconds(5)), "context import blocked on a FIFO.");
        // A FIFO reports length 0, so it is never opened: it imports as an empty note and never hangs.
        Assert.Equal(0, run.Result.Code);
    }

    [Fact]
    public void PassphraseStdin_TooLong_IsRefusedWithoutUnlocking()
    {
        EncryptedRoot();
        Dictionary<string, string> before = Snapshot();

        Result result = RunWithStdin(new string('p', 100_000), Cli.PassphraseStdin, "notes");

        AssertError(result, 1, $"The passphrase on stdin is longer than {Cli.MaxPassphraseLength} characters.");
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void PassphraseStdin_UpToTheLimit_IsRead()
    {
        EncryptedRoot();
        string longOne = new string('p', Cli.MaxPassphraseLength);

        AssertError(RunWithStdin(longOne + "\r\n", Cli.PassphraseStdin, "notes"), 1, "Wrong passphrase.");
    }

    [Theory]
    [InlineData("--Passphrase=topSecret55aa", "notes")]
    [InlineData("--PASSPHRASE", "topSecret55aa")]
    public void PassphraseArgument_AnyCase_IsRefused(params string[] args)
    {
        Result result = Run(args);

        AssertUsage(result);
        Assert.DoesNotContain("topSecret55aa", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownOption_IsNotEchoed()
    {
        Result result = Run("--pasphrase=topSecret55aa", "notes");

        AssertUsage(result);
        Assert.DoesNotContain("topSecret55aa", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void PassphraseStdin_TrailingCarriageReturnWithoutNewline_IsRemoved()
    {
        EncryptedRoot();

        Result result = RunWithStdin(VaultTests.Passphrase + "\r", Cli.PassphraseStdin, "notes");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
    }
}
