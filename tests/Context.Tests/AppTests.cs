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
}
