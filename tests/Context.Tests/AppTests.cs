using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Context.Tests;

public class AppTests
{
    private const int Width = 100;
    private const int Height = 24;

    private static readonly Regex Ansi = new(@"\u001b\[[0-9;?]*[A-Za-z]");

    private static (int ExitCode, string Output) Run(Action<TestConsole> input, bool ansi = false)
    {
        TestConsole console = new TestConsole().Interactive();
        console.Profile.Width = Width;
        console.Profile.Height = Height;
        console.EmitAnsiSequences = ansi;

        input(console);
        int exitCode = new App(console).Run();
        return (exitCode, console.Output);
    }

    private static (int ExitCode, string Output) RunCommands(params string[] commands) =>
        Run(c =>
        {
            foreach (string command in commands)
            {
                c.Input.PushTextWithEnter(command);
            }
        });

    /// <summary>
    /// Splits the output into drawn frames. Every frame starts with a screen clear (<c>ESC[2J</c>);
    /// ANSI sequences are then stripped and the frame is cut after the input panel's bottom
    /// border, so the echo of typed text that follows is dropped. The final clear on quit leaves
    /// an empty chunk, which is skipped.
    /// </summary>
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

    /// <summary>The ANSI sequence the console writes to start <c>[red]</c> text.</summary>
    private static string RedStart()
    {
        TestConsole console = new TestConsole { EmitAnsiSequences = true };
        console.Markup("[red]R[/]");
        string start = console.Output[..console.Output.IndexOf('R')];
        Assert.StartsWith("\u001b[", start);
        return start;
    }

    /// <summary>Asserts the 24-line layout: the hint row at <c>frame[^4]</c>, the input panel on the last 3 lines.</summary>
    private static void AssertLayout(string[] frame)
    {
        Assert.Equal(Height, frame.Length);
        Assert.StartsWith("╭", frame[^3]);
        Assert.EndsWith("╮", frame[^3].TrimEnd());
        Assert.StartsWith("│ > ", frame[^2]);
        Assert.StartsWith("╰", frame[^1]);
        Assert.Equal(Width, frame[^1].Length);
    }

    /// <summary>Asserts that <paramref name="text"/> sits on the hint row and nowhere above it.</summary>
    private static void AssertOnHintRow(string[] frame, string text)
    {
        AssertLayout(frame);
        Assert.Contains(text, frame[^4]);
        Assert.DoesNotContain(frame[..^4], l => l.Contains(text, StringComparison.Ordinal));
    }

    // AC-2
    [Fact]
    public void Start_ShowsTitleSidebarAndInputPanelAtBottom()
    {
        (_, string output) = RunCommands("/quit");

        Assert.Contains("Context", output);
        Assert.Contains("Todos", output);
        Assert.Contains("No todos yet", output);
        Assert.Contains("Type /help for commands", output);

        List<string[]> frames = Frames(output);
        string[] frame = Assert.Single(frames);
        AssertLayout(frame);
        Assert.True(string.IsNullOrWhiteSpace(frame[^4]));
    }

    // AC-2
    [Fact]
    public void Sidebar_IsOnTheRight_32ColumnsWide()
    {
        (_, string output) = RunCommands("/quit");

        string[] frame = Frames(output)[0];
        string top = frame[0];
        int left = top.IndexOf('╭');
        Assert.Equal(Width - 32, left);
        Assert.Contains("Todos", top);
        Assert.Contains(frame, l => l.Length >= Width && l[(Width - 32)..].Contains("No todos yet", StringComparison.Ordinal));
    }

    // AC-3
    [Theory]
    [InlineData("/quit")]
    [InlineData("/QUIT")]
    [InlineData("  /Quit  ")]
    public void Quit_ReturnsZero(string command)
    {
        (int exitCode, _) = RunCommands(command);

        Assert.Equal(0, exitCode);
    }

    // AC-4
    [Fact]
    public void Help_RedrawsAndListsCommandsInMainView()
    {
        (_, string output) = RunCommands("/help", "/quit");

        List<string[]> frames = Frames(output);
        Assert.Equal(2, frames.Count);
        Assert.DoesNotContain(frames[0], l => l.Contains("List all commands", StringComparison.Ordinal));

        string[] frame = frames[1];
        AssertLayout(frame);
        string[] main = frame[..^4].Select(l => l.Length > Width - 32 ? l[..(Width - 32)] : l).ToArray();
        Assert.Contains(main, l => l.Contains("/help", StringComparison.Ordinal)
            && l.Contains("List all commands", StringComparison.Ordinal));
        Assert.Contains(main, l => l.Contains("/quit", StringComparison.Ordinal)
            && l.Contains("Exit Context", StringComparison.Ordinal));
    }

    // AC-5
    [Fact]
    public void UnknownCommand_ShowsHintAboveInput_LoopContinues()
    {
        (int exitCode, string output) = RunCommands("/nope", "/quit");

        Assert.Equal(0, exitCode);
        List<string[]> frames = Frames(output);
        Assert.Equal(2, frames.Count);
        AssertOnHintRow(frames[1], "/nope");
    }

    // AC-5
    [Fact]
    public void UnknownCommand_HintIsRed()
    {
        (_, string output) = Run(c =>
        {
            c.Input.PushTextWithEnter("/nope");
            c.Input.PushTextWithEnter("/quit");
        }, ansi: true);

        Assert.Contains(RedStart() + "Unknown command ", output);
    }

    // AC-5
    [Fact]
    public void NextCommand_ClearsTheHint()
    {
        (_, string output) = RunCommands("/nope", "/help", "/quit");

        List<string[]> frames = Frames(output);
        Assert.Equal(3, frames.Count);
        AssertOnHintRow(frames[1], "/nope");
        AssertLayout(frames[2]);
        Assert.True(string.IsNullOrWhiteSpace(frames[2][^4]));
        Assert.DoesNotContain(frames[2], l => l.Contains("/nope", StringComparison.Ordinal));
    }

    // AC-6
    [Fact]
    public void TextWithoutSlash_ShowsHint()
    {
        (int exitCode, string output) = RunCommands("hello", "/quit");

        Assert.Equal(0, exitCode);
        List<string[]> frames = Frames(output);
        Assert.Equal(2, frames.Count);
        AssertOnHintRow(frames[1], "Commands start with /");
    }

    // AC-6
    [Fact]
    public void TextWithoutSlash_HintIsRed()
    {
        (_, string output) = Run(c =>
        {
            c.Input.PushTextWithEnter("hello");
            c.Input.PushTextWithEnter("/quit");
        }, ansi: true);

        Assert.Contains(RedStart() + "Commands start with ", output);
    }

    // AC-6
    [Fact]
    public void EmptyLine_ShowsNoHint()
    {
        (int exitCode, string output) = Run(c =>
        {
            c.Input.PushKey(ConsoleKey.Enter);
            c.Input.PushTextWithEnter("/quit");
        });

        Assert.Equal(0, exitCode);
        List<string[]> frames = Frames(output);
        Assert.Equal(2, frames.Count);
        AssertLayout(frames[1]);
        Assert.True(string.IsNullOrWhiteSpace(frames[1][^4]));
    }

    // AC-7
    [Fact]
    public void MarkupInInput_IsEscaped()
    {
        (int exitCode, string output) = RunCommands("/[bad]", "/quit");

        Assert.Equal(0, exitCode);
        List<string[]> frames = Frames(output);
        Assert.Equal(2, frames.Count);
        AssertOnHintRow(frames[1], "/[bad]");
    }

    [Fact]
    public void FailingHandler_ShowsRedHint_NoStackTrace()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        registry.Register("/boom", "Fails", _ => throw new InvalidOperationException("broken [x]"));
        TestConsole console = new TestConsole().Interactive();
        console.Profile.Width = Width;
        console.Profile.Height = Height;
        console.Input.PushTextWithEnter("/boom");
        console.Input.PushTextWithEnter("/quit");

        int exitCode = new App(console, registry).Run();

        Assert.Equal(0, exitCode);
        AssertOnHintRow(Frames(console.Output)[1], "/boom failed: broken [x]");
    }

    // 015 AC-8
    [Fact]
    public void FailingHandler_ControlCharacterInMessage_ShowsPlaceholder()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        registry.Register("/boom", "Fails", _ => throw new IOException("x\u001b[31my"));
        TestConsole console = new TestConsole().Interactive();
        console.Profile.Width = Width;
        console.Profile.Height = Height;
        console.Input.PushTextWithEnter("/boom");
        console.Input.PushTextWithEnter("/help");
        console.Input.PushTextWithEnter("/quit");

        int exitCode = new App(console, registry).Run();

        Assert.Equal(0, exitCode);
        List<string[]> frames = Frames(console.Output);
        Assert.Equal(3, frames.Count);
        AssertOnHintRow(frames[1], "/boom failed: x\uFFFD[31my");
        Assert.DoesNotContain("x\u001b", console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', Ansi.Replace(console.Output, string.Empty));
        Assert.True(string.IsNullOrWhiteSpace(frames[2][^4]));
    }

    // AC-10
    [Fact]
    public void SourceFiles_OutsideProgram_DoNotUseStaticConsoleApis()
    {
        DirectoryInfo? root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Context.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        Regex staticConsole = new(@"(^|[^A-Za-z0-9_.])(Console|AnsiConsole)\.");
        string[] files = Directory.GetFiles(Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => Path.GetFileName(f) != "Program.cs")
            .ToArray();

        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            Assert.DoesNotMatch(staticConsole, File.ReadAllText(file));
        }
    }

    // Command popup (job 010) --------------------------------------------------------------

    private const int HintRow = Height - 3;

    private static string RunAnsi(Action<TestConsole> input, CommandRegistry? registry = null)
    {
        TestConsole console = new TestConsole().Interactive().EmitAnsiSequences();
        console.Profile.Width = Width;
        console.Profile.Height = Height;
        input(console);
        Assert.Equal(0, new App(console, registry).Run());
        return console.Output;
    }

    /// <summary>Each full frame (written at 1;1 right after a clear), split into its screen rows.</summary>
    private static List<string[]> AnsiFrames(string output) =>
        AnsiWrites.Parse(output)
            .Where(w => w.Row == 1 && w.Column == 1 && w.Text.Contains('╰'))
            .Select(w => w.Text.Split('\n'))
            .ToList();

    /// <summary>The writes at column 1 between frame <paramref name="index"/> and the next one: what the editor drew while typing.</summary>
    private static List<AnsiWrite> TypingWrites(string output, int index)
    {
        List<AnsiWrite> writes = AnsiWrites.Parse(output);
        List<int> frameStarts = writes
            .Select((w, i) => (w, i))
            .Where(x => x.w.Row == 1 && x.w.Column == 1 && x.w.Text.Contains('╰'))
            .Select(x => x.i)
            .ToList();
        int end = index + 1 < frameStarts.Count ? frameStarts[index + 1] : writes.Count;
        return writes.Skip(frameStarts[index] + 1).Take(end - frameStarts[index] - 1).Where(w => w.Column == 1).ToList();
    }

    private static CommandRegistry WithDummies()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        registry.Register("/next", "Show the next note", _ => CommandResult.None);
        registry.Register("/prev", "Show the previous note", _ => CommandResult.None);
        registry.Register("/today", "Show today", _ => CommandResult.None);
        return registry;
    }

    private static CommandRegistry ManyCommands()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        for (int i = 1; i <= 12; i++)
        {
            registry.Register($"/c{i:00}", $"Dummy command {i}", _ => CommandResult.None);
        }

        return registry;
    }

    // AC-3
    [Fact]
    public void Slash_OpensPopup_OnRowsEndingAtHintRow()
    {
        string output = RunAnsi(c =>
        {
            c.Input.PushText("/");
            c.Input.PushKey(ConsoleKey.Backspace);
            c.Input.PushTextWithEnter("/quit");
        }, WithDummies());

        Assert.Contains("\u001b[21;1H", output);
        List<AnsiWrite> popup = TypingWrites(output, 0).Take(5).ToList();
        Assert.Equal([17, 18, 19, 20, 21], popup.Select(w => w.Row));
        Assert.Equal(["/help", "/next", "/prev", "/quit", "/today"], popup.Select(w => w.Text.Split(' ')[0]));
        Assert.Contains("List all commands", popup[0].Text);
        Assert.Contains("Show the previous note", popup[2].Text);
        Assert.Contains("Exit Context", popup[3].Text);
        Assert.Equal(Width, popup[0].Text.Length);
    }

    // AC-3
    [Fact]
    public void Slash_WithDefaultRegistry_ListsHelpAndQuit()
    {
        string output = RunAnsi(c =>
        {
            c.Input.PushText("/");
            c.Input.PushKey(ConsoleKey.Backspace);
            c.Input.PushTextWithEnter("/quit");
        });

        List<AnsiWrite> popup = TypingWrites(output, 0).Take(2).ToList();
        Assert.Equal([20, 21], popup.Select(w => w.Row));
        Assert.StartsWith("/help  List all commands", popup[0].Text);
        Assert.StartsWith("/quit  Exit Context", popup[1].Text);
    }

    // AC-4
    [Fact]
    public void Typo_ShowsHelpFirst()
    {
        string output = RunAnsi(c =>
        {
            c.Input.PushTextWithEnter("/hepl");
            c.Input.PushTextWithEnter("/quit");
        }, WithDummies());

        List<AnsiWrite> writes = TypingWrites(output, 0);
        AnsiWrite lastPopupRow = writes.Last(w => w.Row == HintRow && w.Text.StartsWith('/'));
        Assert.StartsWith("/help", lastPopupRow.Text);
        // One popup draw per key ("/", "/h", "/he", "/hep", "/hepl") on the hint row.
        Assert.Equal(5, writes.Count(w => w.Row == HintRow && w.Text.StartsWith('/')));
    }

    // AC-4, AC-5
    [Fact]
    public void ShrunkOrClosedPopup_RestoresTheFramesRows_IncludingSidebarBorder()
    {
        string output = RunAnsi(c =>
        {
            c.Input.PushTextWithEnter("/help");
            c.Input.PushText("/t");
            c.Input.PushKey(ConsoleKey.Backspace);
            c.Input.PushKey(ConsoleKey.Backspace);
            c.Input.PushKey(ConsoleKey.Enter);
            c.Input.PushTextWithEnter("/quit");
        }, WithDummies());

        string[] frame = AnsiFrames(output)[1];
        List<AnsiWrite> writes = TypingWrites(output, 1);

        // "/" fills rows 17-21, "/t" shrinks to row 21 (17-20 restored), Backspace to "/" fills 17-21
        // again, Backspace to "" gives every row back.
        Assert.Contains(writes, w => w.Row == 17 && w.Text.StartsWith("/help", StringComparison.Ordinal));
        List<AnsiWrite> shrink = writes.SkipWhile(w => w.Row != 21).Skip(1).Take(5).ToList();
        Assert.Equal([17, 18, 19, 20, 21], shrink.Select(w => w.Row));
        Assert.Equal(frame[16], shrink[0].Text);
        Assert.StartsWith("/today", shrink[4].Text);

        Dictionary<int, string> screen = [];
        foreach (AnsiWrite write in writes.Where(w => w.Row < Height - 2))
        {
            screen[write.Row] = write.Text;
        }

        for (int row = 17; row <= HintRow; row++)
        {
            Assert.Equal(frame[row - 1], screen[row]);
        }

        Assert.Equal('│', screen[17][Width - 32]);
        Assert.Equal('╰', screen[20][Width - 32]);
        Assert.Equal('╯', screen[20][^1]);
    }

    // AC-5
    [Fact]
    public void ClosingPopup_BringsBackTheRedHint()
    {
        string output = RunAnsi(c =>
        {
            c.Input.PushTextWithEnter("/nope");
            c.Input.PushText("/");
            c.Input.PushKey(ConsoleKey.Backspace);
            c.Input.PushText("/");
            c.Input.PushKey(ConsoleKey.Escape);
            c.Input.PushKey(ConsoleKey.Escape);
            c.Input.PushTextWithEnter("/quit");
        });

        List<string> hintRow = TypingWrites(output, 1).Where(w => w.Row == HintRow).Select(w => w.Text).ToList();

        // popup, restored (Backspace), popup, restored (Esc), popup (/quit typing), ...
        Assert.StartsWith("/quit", hintRow[0]);
        Assert.Contains("Unknown command /nope", hintRow[1]);
        Assert.StartsWith("/quit", hintRow[2]);
        Assert.Contains("Unknown command /nope", hintRow[3]);
        Assert.Contains("Unknown command /nope", hintRow[^1]);
        Assert.Contains(RedStart() + "Unknown command", output[output.LastIndexOf("\u001b[21;1H", StringComparison.Ordinal)..]);
    }

    // AC-5, AC-11
    [Fact]
    public void Enter_StillDispatchesTheTypedLine()
    {
        string output = RunAnsi(c =>
        {
            c.Input.PushTextWithEnter("/help");
            c.Input.PushTextWithEnter("/quit");
        });

        Assert.Contains(AnsiFrames(output)[1], l => l.Contains("Exit Context", StringComparison.Ordinal));
    }

    // AC-6
    [Fact]
    public void Escape_KeepsText_SecondEscapeClears_SlashReopens()
    {
        string output = RunAnsi(c =>
        {
            c.Input.PushText("/he");
            c.Input.PushKey(ConsoleKey.Escape);
            c.Input.PushText("l");
            c.Input.PushKey(ConsoleKey.Escape);
            c.Input.PushText("/");
            c.Input.PushKey(ConsoleKey.Escape);
            c.Input.PushKey(ConsoleKey.Escape);
            c.Input.PushTextWithEnter("/quit");
        });

        List<AnsiWrite> writes = AnsiWrites.Parse(output);
        // The input line showed "/hel" after the first Esc and typing "l": the text was kept.
        Assert.Contains(writes, w => w.Row == Height - 1 && w.Text.StartsWith("/hel", StringComparison.Ordinal));
        // "/", "/h", "/he" opened it; "l" did not (dismissed); the new "/" did; "/quit" typed 5 more.
        List<AnsiWrite> popupDraws = TypingWrites(output, 0).Where(w => w.Row == HintRow && w.Text.StartsWith('/')).ToList();
        Assert.Equal(9, popupDraws.Count);
    }

    // AC-7
    [Fact]
    public void ManyMatches_PopupShowsEightRows_AndMore()
    {
        string output = RunAnsi(c =>
        {
            c.Input.PushText("/");
            c.Input.PushKey(ConsoleKey.Escape);
            c.Input.PushKey(ConsoleKey.Escape);
            c.Input.PushTextWithEnter("/quit");
        }, ManyCommands());

        List<AnsiWrite> popup = TypingWrites(output, 0).Take(8).ToList();
        Assert.Equal(Enumerable.Range(14, 8), popup.Select(w => w.Row));
        // 14 commands: 7 shown, the eighth row counts the other 7.
        Assert.StartsWith("… 7 more", popup[^1].Text);
        Assert.DoesNotContain(TypingWrites(output, 0), w => w.Row == 1 && w.Text.Length > 0);
    }

    // AC-7
    [Fact]
    public void SmallTerminal_PopupNeverCoversTitle()
    {
        TestConsole console = new TestConsole().Interactive().EmitAnsiSequences();
        console.Profile.Width = Width;
        console.Profile.Height = 7;
        console.Input.PushText("/");
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushTextWithEnter("/quit");

        Assert.Equal(0, new App(console, ManyCommands()).Run());

        List<AnsiWrite> popup = TypingWrites(console.Output, 0).Where(w => w.Row < 5).ToList();
        Assert.Equal([2, 3, 4], popup.Take(3).Select(w => w.Row));
        Assert.StartsWith("… 12 more", popup[2].Text);
        Assert.DoesNotContain(TypingWrites(console.Output, 0), w => w.Row == 1 && w.Text.Length > 0);
    }

    // AC-9
    [Fact]
    public void Typing_NeverClearsOrRedrawsTheLayout()
    {
        string output = RunAnsi(c =>
        {
            c.Input.PushText("/tdoay");
            c.Input.PushKey(ConsoleKey.Backspace);
            c.Input.PushKey(ConsoleKey.Escape);
            c.Input.PushKey(ConsoleKey.Escape);
            c.Input.PushTextWithEnter("/quit");
        }, WithDummies());

        // One clear for the first frame, one on /quit; nothing in between.
        Assert.Equal(2, output.Split("\u001b[2J").Length - 1);
        Assert.Single(AnsiFrames(output));
    }
}
