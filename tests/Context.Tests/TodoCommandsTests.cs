using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Context.Tests;

public sealed class TodoCommandsTests : IDisposable
{
    private const int Width = 100;
    private const int Height = 24;
    private const int SidebarWidth = 32;
    private const int MainWidth = Width - SidebarWidth;
    private const string Form = "/todo <yyyy-MM-dd|today|tomorrow> <text>";

    private static readonly DateOnly Today = new(2026, 10, 28);
    private static readonly Regex Ansi = new(@"\u001b\[[0-9;?]*[A-Za-z]");

    private readonly string parent;
    private readonly string root;
    private DateTime clock = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);

    public TodoCommandsTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-todocmds-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private string FilePath => Path.Combine(root, "todos.json");

    private TodoStore Store() => new(root, () => clock = clock.AddMinutes(1));

    private (int ExitCode, string Output) Run(bool ansi, params string[] commands)
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        TodoStore store = Store();
        TodoCommands.Register(registry, store, () => Today);
        TestConsole console = new TestConsole().Interactive();
        console.Profile.Width = Width;
        console.Profile.Height = Height;
        console.EmitAnsiSequences = ansi;
        foreach (string command in commands)
        {
            console.Input.PushTextWithEnter(command);
        }

        console.Input.PushTextWithEnter("/quit");
        int exitCode = new App(console, registry, new TodoSidebar(store, () => Today)).Run();
        return (exitCode, console.Output);
    }

    /// <summary>Every frame of a run (the first before any command), checking the exit code.</summary>
    private List<string[]> AllFrames(params string[] commands)
    {
        (int exitCode, string output) = Run(false, commands);
        Assert.Equal(0, exitCode);
        List<string[]> frames = Frames(output);
        Assert.Equal(commands.Length + 1, frames.Count);
        return frames;
    }

    private string[] LastFrame(params string[] commands) => AllFrames(commands)[^1];

    /// <summary>Same frame splitting as <c>AppTests.Frames</c>.</summary>
    private static List<string[]> Frames(string output)
    {
        List<string[]> frames = [];
        foreach (string chunk in output.Split("\u001b[2J").Skip(1))
        {
            string[] lines = Ansi.Replace(chunk, string.Empty).Split('\n');
            if (lines.Length < 2)
            {
                continue;
            }

            string last = lines[^1];
            int end = last.IndexOf('╯');
            lines[^1] = end < 0 ? last : last[..(end + 1)];
            frames.Add(lines);
        }

        return frames;
    }

    private static string[] MainView(string[] frame) =>
        frame[..^4].Select(l => l.Length > MainWidth ? l[..MainWidth] : l).ToArray();

    /// <summary>The text inside the sidebar panel, one entry per row, borders and padding removed.</summary>
    private static string[] Sidebar(string[] frame) =>
        frame[1..^5].Select(l => l.PadRight(Width)[(MainWidth + 2)..(Width - 2)].TrimEnd()).ToArray();

    private static string RedStart()
    {
        TestConsole console = new TestConsole { EmitAnsiSequences = true };
        console.Markup("[red]R[/]");
        return console.Output[..console.Output.IndexOf('R')];
    }

    private static void AssertOnHintRow(string[] frame, string text)
    {
        Assert.Equal(Height, frame.Length);
        Assert.Contains(text, frame[^4]);
        Assert.DoesNotContain(frame[..^4], l => l.Contains(text, StringComparison.Ordinal));
    }

    private void AssertRedHint(string command, string text)
    {
        (int exitCode, string output) = Run(true, command);
        Assert.Equal(0, exitCode);
        Assert.Contains(RedStart() + text, output);
    }

    // AC-1
    [Fact]
    public void Todo_AddsTodo_CreatesFolder_ShowsAdded()
    {
        string[] frame = LastFrame("/todo 2026-11-03 Buy milk");

        Assert.Contains(MainView(frame), l => l.Contains("Added Buy milk (due 2026-11-03)", StringComparison.Ordinal));
        Assert.True(string.IsNullOrWhiteSpace(frame[^4]));
        TodoItem todo = Assert.Single(new TodoStore(root).List());
        Assert.Equal("Buy milk", todo.Text);
        Assert.Equal(new DateOnly(2026, 11, 3), todo.Due);
    }

    // AC-1
    [Theory]
    [InlineData("today", 2026, 10, 28)]
    [InlineData("TODAY", 2026, 10, 28)]
    [InlineData("tomorrow", 2026, 10, 29)]
    [InlineData("ToMorrow", 2026, 10, 29)]
    public void Todo_TodayAndTomorrow_UseInjectedToday(string word, int year, int month, int day)
    {
        string[] frame = LastFrame($"/todo {word} Call   Anna");

        string due = new DateOnly(year, month, day).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(MainView(frame), l => l.Contains($"Added Call   Anna (due {due})", StringComparison.Ordinal));
        Assert.Equal(new DateOnly(year, month, day), Assert.Single(new TodoStore(root).List()).Due);
    }

    // AC-2
    [Theory]
    [InlineData("/todo")]
    [InlineData("/todo    ")]
    [InlineData("/todo 2026-11-03")]
    [InlineData("/todo 2026-11-03   ")]
    [InlineData("/todo today")]
    [InlineData("/todo 2026-13-01 Buy milk")]
    [InlineData("/todo 03.11.2026 Buy milk")]
    [InlineData("/todo soon Buy milk")]
    [InlineData("/todo 2026-11-3 Buy milk")]
    [InlineData("/todo Buy milk")]
    public void Todo_BadInput_ShowsRedHint_WritesNothing(string command)
    {
        string[] frame = LastFrame(command);

        AssertOnHintRow(frame, Form);
        Assert.Empty(Directory.GetFileSystemEntries(parent));
        AssertRedHint(command, command.EndsWith(" milk", StringComparison.Ordinal)
            ? "That date isn't valid."
            : "A todo needs a date and text.");
        Assert.Empty(Directory.GetFileSystemEntries(parent));
    }

    // AC-2
    [Fact]
    public void Todo_TextOver200_ShowsRedHint_WritesNothing()
    {
        string command = "/todo today " + new string('a', 201);

        AssertOnHintRow(LastFrame(command), Form);
        AssertRedHint(command, "A todo can be at most 200 characters.");
        Assert.Empty(Directory.GetFileSystemEntries(parent));
    }

    // AC-2
    [Fact]
    public void Todo_Text200_PastDate_IsAccepted()
    {
        string text = new string('a', 200);

        (int exitCode, _) = Run(false, "/todo 2020-01-01 " + text);

        Assert.Equal(0, exitCode);
        TodoItem todo = Assert.Single(new TodoStore(root).List());
        Assert.Equal(text, todo.Text);
        Assert.Equal(new DateOnly(2020, 1, 1), todo.Due);
    }

    // AC-2: the hint fits on the one hint row.
    [Fact]
    public void Todo_Hint_IsOneLine()
    {
        string[] frame = LastFrame("/todo today " + new string('a', 201));

        Assert.Equal(Height, frame.Length);
        Assert.Contains(Form, frame[^4]);
        Assert.StartsWith("╭", frame[^3].TrimStart());
    }

    // AC-5
    [Fact]
    public void Sidebar_UpdatesRightAfterTodoAndDone()
    {
        List<string[]> frames = AllFrames("/todo 2026-11-03 Buy milk", "/todo today Call Anna", "/done 1");

        Assert.Contains("No todos yet", Sidebar(frames[0]));
        Assert.Equal("1 11-03 Buy milk", Sidebar(frames[1])[0]);
        Assert.Equal(["1 10-28 Call Anna", "2 11-03 Buy milk"], Sidebar(frames[2])[..2]);
        Assert.Equal("1 11-03 Buy milk", Sidebar(frames[3])[0]);
        Assert.Equal(string.Empty, Sidebar(frames[3])[1]);
    }

    // AC-5
    [Fact]
    public void Sidebar_FirstFrame_ShowsTodosFromAnEarlierRun()
    {
        Run(false, "/todo 2026-11-03 Buy milk", "/todo 2026-10-30 Pay rent");

        string[] first = AllFrames()[0];

        Assert.Equal(["1 10-30 Pay rent", "2 11-03 Buy milk"], Sidebar(first)[..2]);
    }

    // AC-6
    [Fact]
    public void Done_RemovesTheNumberShown_ShowsDone()
    {
        Run(false, "/todo 2026-11-05 Third", "/todo 2026-11-03 First", "/todo 2026-11-04 Second");

        string[] frame = LastFrame("/done 2");

        Assert.Contains(MainView(frame), l => l.Contains("Done: Second", StringComparison.Ordinal));
        Assert.Equal(["First", "Third"], new TodoStore(root).List().Select(t => t.Text));
        Assert.Equal(["1 11-03 First", "2 11-05 Third"], Sidebar(frame)[..2]);
    }

    // AC-6
    [Theory]
    [InlineData("/done")]
    [InlineData("/done x")]
    [InlineData("/done 0")]
    [InlineData("/done 3")]
    [InlineData("/done -1")]
    [InlineData("/done 1.5")]
    [InlineData("/done 99999999999999")]
    public void Done_BadNumber_ShowsRedHint_ChangesNothing(string command)
    {
        Run(false, "/todo 2026-11-03 First", "/todo 2026-11-04 Second");
        byte[] before = File.ReadAllBytes(FilePath);

        AssertOnHintRow(LastFrame(command), "/done <number>");
        AssertRedHint(command, command is "/done 0" or "/done 3" ? "There is no todo" : "Type ");
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    // AC-6
    [Fact]
    public void Done_NoTodos_ShowsRedHint_CreatesNothing()
    {
        AssertOnHintRow(LastFrame("/done 1"), "There are no todos to mark done.");
        AssertRedHint("/done 1", "There are no todos");
        Assert.Empty(Directory.GetFileSystemEntries(parent));
    }

    // AC-7
    [Fact]
    public void MarkupInText_ShownLiterally_InMessagesAndSidebar()
    {
        List<string[]> frames = AllFrames("/todo today [bold]x[/]", "/done 1");

        Assert.Contains(MainView(frames[1]), l => l.Contains("Added [bold]x[/] (due 2026-10-28)", StringComparison.Ordinal));
        Assert.Equal("1 10-28 [bold]x[/]", Sidebar(frames[1])[0]);
        Assert.Contains(MainView(frames[2]), l => l.Contains("Done: [bold]x[/]", StringComparison.Ordinal));
    }

    // AC-7: control characters in todos.json show as placeholders in the /done message.
    [Fact]
    public void ControlCharacterInFile_ShowsPlaceholder_InDoneMessage()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            FilePath,
            "[{\"text\":\"a\\u001b[2Jb\\nc\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T00:00:00Z\"}]",
            new UTF8Encoding(false));

        (int exitCode, string output) = Run(false, "/done 1");

        Assert.Equal(0, exitCode);
        Assert.Contains(MainView(Frames(output)[1]), l => l.Contains("Done: a�[2Jb c", StringComparison.Ordinal));
        Assert.DoesNotContain('\u001b', Ansi.Replace(output, string.Empty));
    }

    // AC-8
    [Theory]
    [InlineData("/todo today Buy milk")]
    [InlineData("/done 1")]
    public void CorruptFile_ShowsRedHint_FileUnchanged(string command)
    {
        Directory.CreateDirectory(root);
        byte[] original = Encoding.UTF8.GetBytes("{ this is [not] json");
        File.WriteAllBytes(FilePath, original);

        string[] frame = LastFrame(command);

        AssertOnHintRow(frame, "Can't read todos.json, so nothing was changed.");
        Assert.Contains("Can't read todos.json", Sidebar(frame)[0]);
        AssertRedHint(command, "Can't read todos.json");
        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(root));
    }

    // AC-9
    [Fact]
    public void Help_ListsTodoCommands()
    {
        string[] main = MainView(LastFrame("/help"));

        Assert.Contains(main, l => l.Contains("/todo", StringComparison.Ordinal)
            && l.Contains("Add a todo: /todo <date> <text>", StringComparison.Ordinal));
        Assert.Contains(main, l => l.Contains("/done", StringComparison.Ordinal)
            && l.Contains("Mark a todo done: /done <number>", StringComparison.Ordinal));
    }

    // AC-9
    [Fact]
    public void Popup_ListsTodoCommands()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        TodoCommands.Register(registry, Store(), () => Today);
        TestConsole console = new TestConsole().Interactive().EmitAnsiSequences();
        console.Profile.Width = Width;
        console.Profile.Height = Height;
        console.Input.PushText("/");
        console.Input.PushKey(ConsoleKey.Backspace);
        console.Input.PushTextWithEnter("/quit");

        Assert.Equal(0, new App(console, registry).Run());

        List<AnsiWrite> rows = AnsiWrites.Parse(console.Output)
            .Where(w => w.Column == 1 && w.Text.StartsWith('/'))
            .ToList();
        Assert.Contains(rows, w => w.Text.StartsWith("/todo", StringComparison.Ordinal)
            && w.Text.Contains("Add a todo: /todo <date> <text>"));
        Assert.Contains(rows, w => w.Text.StartsWith("/done", StringComparison.Ordinal)
            && w.Text.Contains("Mark a todo done: /done <number>"));
    }

    // App without a sidebar keeps the old placeholder.
    [Fact]
    public void App_WithoutSidebar_ShowsNoTodosYet()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, "[{\"text\":\"x\",\"due\":\"2026-11-03\"}]");
        TestConsole console = new TestConsole().Interactive();
        console.Profile.Width = Width;
        console.Profile.Height = Height;
        console.Input.PushTextWithEnter("/quit");

        Assert.Equal(0, new App(console).Run());

        Assert.Contains("No todos yet", Sidebar(Frames(console.Output)[0]));
    }
}
