using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Context.Tests;

public sealed class CurrentCommandsTests : IDisposable
{
    private const int Width = 140;
    private const int Height = 40;
    private const int MainWidth = Width - 32;
    private const string NothingDue = "Nothing due today. Add one with /todo today <text>.";
    private const string NoLinks = "No linked notes. Add the same #tag to a todo and a note to link them.";
    private const string DbUnreadable =
        "Can't read context.db. Move it out of the Context folder and it is rebuilt from your notes.";

    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly Regex Ansi = new(@"\u001b\[[0-9;?]*[A-Za-z]");

    private readonly string parent;
    private readonly string root;
    private DateTime clock = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    public CurrentCommandsTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-current-" + Guid.NewGuid().ToString("N"));
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

    private TodoStore Store() => new(root, () => clock = clock.AddMinutes(1));

    private void AddTodo(string text, DateOnly due) => Store().Add(text, due);

    private void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".md"), text);
    }

    private (int ExitCode, string Output) Run(bool ansi, params string[] commands)
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        NoteStore notes = new NoteStore(root);
        TodoStore todos = Store();
        TodoCommands.Register(registry, todos, () => Today);
        CurrentCommands.Register(registry, todos, new TagIndex(root), notes, () => Today);
        TestConsole console = new TestConsole().Interactive();
        console.Profile.Width = Width;
        console.Profile.Height = Height;
        console.EmitAnsiSequences = ansi;
        foreach (string command in commands)
        {
            console.Input.PushTextWithEnter(command);
        }

        console.Input.PushTextWithEnter("/quit");
        int exitCode = new App(console, registry, new TodoSidebar(todos, () => Today)).Run();
        return (exitCode, console.Output);
    }

    /// <summary>The main view of the frame drawn after the last command, without ANSI codes.</summary>
    private string[] Main(params string[] commands)
    {
        (int exitCode, string output) = Run(false, commands);
        Assert.Equal(0, exitCode);
        List<string[]> frames = Frames(output);
        Assert.Equal(commands.Length + 1, frames.Count);
        return frames[^1][..^4].Select(l => l.Length > MainWidth ? l[..MainWidth] : l).ToArray();
    }

    private string[] Frame(params string[] commands)
    {
        (int exitCode, string output) = Run(false, commands);
        Assert.Equal(0, exitCode);
        return Frames(output)[^1];
    }

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

    private static string Style(string markup)
    {
        TestConsole console = new TestConsole { EmitAnsiSequences = true };
        console.Markup(markup);
        return console.Output[..console.Output.IndexOf('X')];
    }

    private static int RowIndex(string[] main, params string[] cells)
    {
        Regex row = new("│\\s*" + string.Join("\\s*│\\s*", cells.Select(Regex.Escape)) + "\\s*│");
        return Array.FindIndex(main, l => row.IsMatch(l));
    }

    private static bool HasLine(string[] main, string text) =>
        main.Any(l => l.Contains(text, StringComparison.Ordinal));

    // AC-1
    [Fact]
    public void Current_ShowsTitle_AndDueTable_WithSidebarNumbers()
    {
        AddTodo("later", new DateOnly(2026, 11, 5));
        AddTodo("today one", Today);
        AddTodo("old one", new DateOnly(2026, 10, 1));

        string[] main = Main("/current");

        Assert.True(HasLine(main, "Today, 2026-10-29"));
        Assert.True(HasLine(main, "Due"));
        Assert.True(RowIndex(main, "1", "2026-10-01", "old one") >= 0);
        Assert.True(RowIndex(main, "2", "2026-10-29", "today one") >= 0);
        Assert.False(HasLine(main, "later"));
    }

    // AC-1
    [Fact]
    public void Current_Colours_TitleSteelBlue_OverdueRed_TodaySteelBlue()
    {
        AddTodo("old", new DateOnly(2026, 10, 28));
        AddTodo("now", Today);

        (int exitCode, string output) = Run(true, "/current");

        Assert.Equal(0, exitCode);
        Assert.Contains(Style("[steelblue1]X[/]") + "Today, 2026-10-29", output);
        Assert.Contains(Style("[red]X[/]") + "2026-10-28", output);
        Assert.Contains(Style("[steelblue1]X[/]") + "2026-10-29", output);
    }

    // AC-1, AC-9
    [Fact]
    public void Help_ListsCurrent()
    {
        string[] main = Main("/help");

        Assert.Contains(main, l => l.Contains("/current", StringComparison.Ordinal)
            && l.Contains("Today's due todos and linked notes", StringComparison.Ordinal));
    }

    // AC-2
    [Fact]
    public void Current_ShowsOverdueAndToday_NotLater()
    {
        AddTodo("a", new DateOnly(2026, 10, 28));
        AddTodo("b", new DateOnly(2026, 10, 29));
        AddTodo("c", new DateOnly(2026, 10, 30));

        string[] main = Main("/current");

        int a = RowIndex(main, "1", "2026-10-28", "a");
        int b = RowIndex(main, "2", "2026-10-29", "b");
        Assert.True(a >= 0 && b > a);
        Assert.Equal(-1, RowIndex(main, "3", "2026-10-30", "c"));
        Assert.DoesNotContain(main, l => l.Contains("2026-10-30", StringComparison.Ordinal));
        Assert.DoesNotContain(main, l => Regex.IsMatch(l, "│\\s*c\\s*│"));
    }

    // AC-3
    [Fact]
    public void Current_NothingDue_ShowsGreyLine_NoTable()
    {
        AddTodo("later", new DateOnly(2026, 10, 30));

        string[] main = Main("/current");

        Assert.True(HasLine(main, NothingDue));
        Assert.DoesNotContain(main, l => l.Contains('│'));
        Assert.False(HasLine(main, "Linked notes"));
        (_, string output) = Run(true, "/current");
        Assert.Contains(Style("[grey]X[/]") + NothingDue, output);
    }

    // AC-3: no todos file at all
    [Fact]
    public void Current_NoTodos_ShowsNothingDue()
    {
        string[] main = Main("/current");

        Assert.True(HasLine(main, NothingDue));
    }

    // AC-4
    [Fact]
    public void Current_LinkedNotes_SortedByName_WithMatchingTags()
    {
        AddTodo("call Bob #work #[project alpha]", Today);
        WriteNote("Plan", "# Plan\n#work");
        WriteNote("Alpha", "# Alpha\n#[Project Alpha]");
        WriteNote("Home", "# Home\n#home");

        string[] main = Main("/current");

        Assert.True(HasLine(main, "Linked notes"));
        int alpha = RowIndex(main, "Alpha", "#[project alpha]");
        int plan = RowIndex(main, "Plan", "#work");
        Assert.True(alpha >= 0 && plan > alpha);
        Assert.False(HasLine(main, "Home"));

        (_, string output) = Run(true, "/current");
        Assert.Contains(Style("[steelblue1]X[/]") + "Alpha", output);
        Assert.Contains(Style("[grey]X[/]") + "#[project alpha]", output);
    }

    // AC-4: Sync runs first, so a note written after the index exists still links.
    [Fact]
    public void Current_SyncsIndexFirst()
    {
        AddTodo("x #work", Today);
        Main("/current");
        WriteNote("Fresh", "#work");

        string[] main = Main("/current");

        Assert.True(RowIndex(main, "Fresh", "#work") >= 0);
    }

    // AC-5
    [Fact]
    public void Current_OnlyDueTodosLink_AndNotesAreListedOnce()
    {
        AddTodo("one #work #home2", Today);
        AddTodo("two #work #side", new DateOnly(2026, 10, 20));
        AddTodo("later #home", new DateOnly(2026, 11, 1));
        WriteNote("Plan", "#work #side");
        WriteNote("Home", "#home");

        string[] main = Main("/current");

        Assert.True(RowIndex(main, "Plan", "#side, #work") >= 0);
        Assert.Single(main, l => l.Contains("Plan", StringComparison.Ordinal));
        Assert.False(HasLine(main, "Home"));
    }

    // AC-6
    [Fact]
    public void Current_NoMatchingTags_ShowsGreyLine()
    {
        AddTodo("plain todo", Today);
        AddTodo("tagged #nomatch", Today);
        WriteNote("Plan", "#work");

        string[] main = Main("/current");

        Assert.True(HasLine(main, NoLinks));
        Assert.False(HasLine(main, "Linked notes"));
        (_, string output) = Run(true, "/current");
        Assert.Contains(Style("[grey]X[/]") + NoLinks, output);
    }

    // AC-7
    [Fact]
    public void Current_BadTodos_ShowsRedHint_LoopContinues()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, TodoStore.FileName), [0x7b, 0x00, 0xff, 0x13]);
        const string Text = "Can't read todos.json. Fix or move the file and try again.";

        string[] frame = Frame("/current");

        Assert.Contains(Text, frame[^4]);
        Assert.DoesNotContain(frame[..^4], l => l.Contains(Text, StringComparison.Ordinal));
        (int exitCode, string output) = Run(true, "/current", "/help");
        Assert.Equal(0, exitCode);
        Assert.Contains(Style("[red]X[/]") + Text, output);
        Assert.Contains("Today's due todos and linked notes", output);
    }

    // AC-7
    [Fact]
    public void Current_BadDatabase_ShowsDueTable_AndRedLine_BytesUnchanged()
    {
        AddTodo("a #work", Today);
        WriteNote("Plan", "#work");
        string db = Path.Combine(root, TagIndex.FileName);
        byte[] garbage = [.. Enumerable.Range(0, 200).Select(i => (byte)(i * 7))];
        File.WriteAllBytes(db, garbage);

        string[] main = Main("/current");

        Assert.True(RowIndex(main, "1", "2026-10-29", "a #work") >= 0);
        Assert.True(HasLine(main, DbUnreadable));
        Assert.False(HasLine(main, "Linked notes"));
        (_, string output) = Run(true, "/current");
        Assert.Contains(Style("[red]X[/]") + DbUnreadable, output);
        Assert.Equal(garbage, File.ReadAllBytes(db));
    }

    // AC-8
    [Fact]
    public void Current_MarkupAndControlsInText_ShownLiterally()
    {
        AddTodo("[red]x[/] #a\nsecond\u001b", Today);
        WriteNote("[b]", "#a");

        string[] main = Main("/current");

        Assert.True(RowIndex(main, "1", "2026-10-29", "[red]x[/] #a second�") >= 0);
        Assert.True(RowIndex(main, "[b]", "#a") >= 0);
    }

    // AC-9
    [Theory]
    [InlineData("/current foo")]
    [InlineData("/CURRENT")]
    public void Current_IgnoresArgument_AndCase(string command)
    {
        AddTodo("a", Today);

        string[] main = Main(command);

        Assert.True(HasLine(main, "Today, 2026-10-29"));
        Assert.True(RowIndex(main, "1", "2026-10-29", "a") >= 0);
    }
}
