using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Context.Tests;

public sealed class TodoSidebarTests : IDisposable
{
    private const int Width = 100;
    private const int SidebarWidth = 32;
    private const int MainWidth = Width - SidebarWidth;

    private static readonly DateOnly Today = new(2026, 10, 28);
    private static readonly Regex Ansi = new(@"\u001b\[[0-9;?]*[A-Za-z]");

    private readonly string parent;
    private readonly string root;

    public TodoSidebarTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-todosidebar-" + Guid.NewGuid().ToString("N"));
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

    private void WriteJson(string json)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, json, new UTF8Encoding(false));
    }

    private void AddTodos(params (string Text, DateOnly Due)[] todos)
    {
        DateTime clock = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        TodoStore store = new(root, () => clock = clock.AddMinutes(1));
        foreach ((string text, DateOnly due) in todos)
        {
            store.Add(text, due);
        }
    }

    /// <summary>Runs an <see cref="App"/> with the sidebar until <c>/quit</c>; returns the raw output.</summary>
    private string Run(int height, bool ansi)
    {
        TestConsole console = new TestConsole().Interactive();
        console.Profile.Width = Width;
        console.Profile.Height = height;
        console.EmitAnsiSequences = ansi;
        console.Input.PushTextWithEnter("/quit");
        TodoSidebar sidebar = new TodoSidebar(new TodoStore(root), () => Today);

        Assert.Equal(0, new App(console, null, sidebar).Run());
        return console.Output;
    }

    /// <summary>The first frame's sidebar rows (inside the border and padding) with ANSI stripped.</summary>
    private string[] SidebarRows(int height = 24)
    {
        string output = Run(height, ansi: false);
        string chunk = output.Split("\u001b[2J")[1];
        string[] lines = Ansi.Replace(chunk, string.Empty).Split('\n');
        Assert.True(lines.Length >= height);
        Assert.StartsWith("╭─Todos", lines[0][MainWidth..]);
        Assert.StartsWith("╰", lines[height - 5][MainWidth..]);
        return lines[1..(height - 5)]
            .Select(l => l.PadRight(Width)[(MainWidth + 2)..(Width - 2)].TrimEnd())
            .ToArray();
    }

    private static string Style(string markup)
    {
        TestConsole console = new TestConsole { EmitAnsiSequences = true };
        console.Markup(markup);
        return console.Output[..console.Output.IndexOf('X')];
    }

    // AC-3
    [Fact]
    public void Lines_AreNumberDateText_SortedByDue()
    {
        AddTodos(("Later", new DateOnly(2026, 12, 24)), ("Overdue", new DateOnly(2026, 10, 1)), ("Today", Today));

        string[] rows = SidebarRows();

        Assert.Equal(["1 10-01 Overdue", "2 10-28 Today", "3 12-24 Later"], rows[..3]);
        Assert.All(rows[3..], r => Assert.Equal(string.Empty, r));
    }

    // AC-3
    [Fact]
    public void DateColours_RedOverdue_SteelBlueToday_GreyLater()
    {
        AddTodos(("Later", new DateOnly(2026, 10, 29)), ("Overdue", new DateOnly(2026, 10, 27)), ("Today", Today));

        string output = Run(24, ansi: true);

        Assert.Contains(Style("[red]X[/]") + "10-27", output);
        Assert.Contains(Style("[steelblue1]X[/]") + "10-28", output);
        Assert.Contains(Style("[grey]X[/]") + "10-29", output);
    }

    // AC-3
    [Fact]
    public void LongText_IsCutToOneLine_WithEllipsis()
    {
        AddTodos(("word " + new string('a', 60) + " end", new DateOnly(2026, 11, 3)), ("Next", new DateOnly(2026, 11, 4)));

        string[] rows = SidebarRows();

        // 28 inner columns: "1 11-03 " takes 8, so the text gets 19 cells plus the ellipsis.
        Assert.Equal("1 11-03 word " + new string('a', 14) + "…", rows[0]);
        Assert.Equal("2 11-04 Next", rows[1]);
    }

    // AC-3
    [Fact]
    public void Fit_CountsWideCharactersAsTwoCells()
    {
        Assert.Equal("abc", TodoSidebar.Fit("abc", 3));
        Assert.Equal("ab…", TodoSidebar.Fit("abcd", 3));
        Assert.Equal("日本…", TodoSidebar.Fit("日本語です", 6));
        Assert.Equal("日本語", TodoSidebar.Fit("日本語", 6));
    }

    // AC-4
    [Fact]
    public void MoreTodosThanRows_LastRowIsNMore_InGrey()
    {
        // 100x12: the sidebar panel is 8 rows tall, 6 inside the border.
        AddTodos(Enumerable.Range(1, 9).Select(i => ($"Task {i}", new DateOnly(2026, 11, i))).ToArray());

        string[] rows = SidebarRows(12);

        Assert.Equal(6, rows.Length);
        Assert.Equal(["1 11-01 Task 1", "2 11-02 Task 2", "3 11-03 Task 3", "4 11-04 Task 4", "5 11-05 Task 5"], rows[..5]);
        Assert.Equal("… 4 more", rows[5]);
        Assert.Contains(Style("[grey]X[/]") + "… 4 more", Run(12, ansi: true));
    }

    // AC-4
    [Fact]
    public void ExactlyAsManyTodosAsRows_ShowsAll()
    {
        AddTodos(Enumerable.Range(1, 6).Select(i => ($"Task {i}", new DateOnly(2026, 11, i))).ToArray());

        string[] rows = SidebarRows(12);

        Assert.Equal("6 11-06 Task 6", rows[5]);
    }

    // AC-4
    [Fact]
    public void NoTodos_ShowsNoTodosYet_InGrey()
    {
        Assert.Equal("No todos yet", SidebarRows()[0]);
        Assert.Contains(Style("[grey]X[/]") + "No todos yet", Run(24, ansi: true));
        Assert.False(Directory.Exists(root));
    }

    // AC-4: an empty list in the file is the empty state too.
    [Fact]
    public void EmptyList_ShowsNoTodosYet()
    {
        WriteJson("[]");

        Assert.Equal("No todos yet", SidebarRows()[0]);
    }

    // AC-7
    [Fact]
    public void MarkupInFile_ShownLiterally()
    {
        WriteJson("[{\"text\":\"[bold]x[/]\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T00:00:00Z\"}]");

        Assert.Equal("1 11-03 [bold]x[/]", SidebarRows()[0]);
    }

    // AC-7
    [Fact]
    public void ControlCharactersInFile_ShowAsPlaceholders_OnOneLine()
    {
        WriteJson("[{\"text\":\"a\\u001b[2Jb\\r\\nc\\u0007d\\u009be\",\"due\":\"2026-11-03\"}," +
            "{\"text\":\"next\",\"due\":\"2026-11-04\"}]");

        string[] rows = SidebarRows();
        string output = Run(24, ansi: true);

        Assert.Equal("1 11-03 a�[2Jb c�d�e", rows[0]);
        Assert.Equal("2 11-04 next", rows[1]);
        Assert.DoesNotContain('\u0007', output);
        Assert.DoesNotContain('\u009b', output);
        Assert.DoesNotContain("\u001b[2Jb", output, StringComparison.Ordinal);
    }

    // AC-8
    [Theory]
    [InlineData("{ broken")]
    [InlineData("[{\"text\":\"x\"}]")]
    [InlineData("\"just a string\"")]
    public void UnreadableFile_ShowsOneLineRedNotice(string content)
    {
        WriteJson(content);

        string[] rows = SidebarRows();

        Assert.Equal("Can't read todos.json", rows[0]);
        Assert.Equal(string.Empty, rows[1]);
        Assert.Contains(Style("[red]X[/]") + "Can't read todos.json", Run(24, ansi: true));
    }
}
