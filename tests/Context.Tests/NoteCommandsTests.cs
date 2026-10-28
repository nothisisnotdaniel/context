using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Context.Tests;

public sealed class NoteCommandsTests : IDisposable
{
    private const int Width = 100;
    private const int Height = 24;
    private const int MainWidth = Width - 32;

    private static readonly Regex Ansi = new(@"\u001b\[[0-9;?]*[A-Za-z]");

    private readonly string parent;
    private readonly string root;

    public NoteCommandsTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-notecmds-" + Guid.NewGuid().ToString("N"));
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

    private (int ExitCode, string Output) Run(bool ansi, params string[] commands)
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        NoteCommands.Register(registry, new NoteStore(root));
        TestConsole console = new TestConsole().Interactive();
        console.Profile.Width = Width;
        console.Profile.Height = Height;
        console.EmitAnsiSequences = ansi;
        foreach (string command in commands)
        {
            console.Input.PushTextWithEnter(command);
        }

        console.Input.PushTextWithEnter("/quit");
        int exitCode = new App(console, registry).Run();
        return (exitCode, console.Output);
    }

    /// <summary>Runs the commands and returns the frame drawn after the last one (before <c>/quit</c>).</summary>
    private string[] LastFrame(params string[] commands)
    {
        (int exitCode, string output) = Run(false, commands);
        Assert.Equal(0, exitCode);
        List<string[]> frames = Frames(output);
        Assert.Equal(commands.Length + 1, frames.Count);
        return frames[^1];
    }

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

    /// <summary>The main view's rows (left of the sidebar, above the hint row).</summary>
    private static string[] MainView(string[] frame) =>
        frame[..^4].Select(l => l.Length > MainWidth ? l[..MainWidth] : l).ToArray();

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
    public void New_CreatesFile_ShowsCreated()
    {
        string[] frame = LastFrame("/new Shopping list");

        Assert.Contains(MainView(frame), l => l.Contains("Created Shopping list", StringComparison.Ordinal));
        Assert.True(string.IsNullOrWhiteSpace(frame[^4]));
        Assert.Equal("# Shopping list\n"u8.ToArray(), File.ReadAllBytes(Path.Combine(root, "Shopping list.md")));
    }

    // AC-2
    [Theory]
    [InlineData("/new", "A note needs a title.")]
    [InlineData("/new    ", "A note needs a title.")]
    [InlineData("/new a/b", "can't contain")]
    [InlineData("/new a\\b", "can't contain")]
    [InlineData("/new a:b", "can't contain")]
    [InlineData("/new .hidden", "can't start with a dot")]
    [InlineData("/new ../secret", "can't start with a dot")]
    public void New_InvalidTitle_ShowsRedHint_CreatesNothing(string command, string hint)
    {
        string[] frame = LastFrame(command);

        AssertOnHintRow(frame, hint);
        Assert.False(Directory.Exists(root));
        Assert.Empty(Directory.GetFileSystemEntries(parent));
        AssertRedHint(command, "A note");
    }

    // AC-2
    [Fact]
    public void New_TooLongTitle_ShowsRedHint_CreatesNothing()
    {
        string[] frame = LastFrame("/new " + new string('a', 101));

        AssertOnHintRow(frame, "at most 100 characters");
        Assert.False(Directory.Exists(root));
    }

    // AC-3
    [Fact]
    public void New_Existing_ShowsRedHint_FileUnchanged()
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Shopping list.md");
        byte[] original = "# Mine\n- eggs\n"u8.ToArray();
        File.WriteAllBytes(path, original);

        string[] frame = LastFrame("/new shopping LIST");

        AssertOnHintRow(frame, "Note shopping LIST already exists");
        Assert.Equal(original, File.ReadAllBytes(path));
        AssertRedHint("/new Shopping list", "Note Shopping list already exists");
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    // AC-4
    [Fact]
    public void Notes_ShowsTable_SortedByName_WithModifiedTime()
    {
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "sub", "hidden.md"), "y");
        File.WriteAllText(Path.Combine(root, "other.txt"), "x");
        File.WriteAllText(Path.Combine(root, "beta.md"), "b");
        File.WriteAllText(Path.Combine(root, "Alpha.md"), "a");
        File.SetLastWriteTime(Path.Combine(root, "beta.md"), new DateTime(2026, 3, 4, 5, 6, 0, DateTimeKind.Local));
        File.SetLastWriteTime(Path.Combine(root, "Alpha.md"), new DateTime(2025, 12, 31, 23, 59, 0, DateTimeKind.Local));

        string[] main = MainView(LastFrame("/notes"));

        Assert.Contains(main, l => l.Contains("Note", StringComparison.Ordinal)
            && l.Contains("Modified", StringComparison.Ordinal));
        int alpha = Array.FindIndex(main, l => l.Contains("│ Alpha ", StringComparison.Ordinal));
        int beta = Array.FindIndex(main, l => l.Contains("│ beta ", StringComparison.Ordinal));
        Assert.True(alpha >= 0 && beta > alpha);
        Assert.Contains("2025-12-31 23:59", main[alpha]);
        Assert.Contains("2026-03-04 05:06", main[beta]);
        Assert.StartsWith("╭", main.First(l => l.Contains('╭')).TrimStart());
        Assert.DoesNotContain(main, l => l.Contains("hidden", StringComparison.Ordinal)
            || l.Contains("other", StringComparison.Ordinal));
    }

    // AC-4
    [Fact]
    public void Notes_NameIsSteelBlue_DateIsGrey()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Alpha.md"), "a");
        File.SetLastWriteTime(Path.Combine(root, "Alpha.md"), new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Local));

        (_, string output) = Run(true, "/notes");

        Assert.Contains(Style("[steelblue1]X[/]") + "Alpha", output);
        Assert.Contains(Style("[grey]X[/]") + "2026-01-02 03:04", output);
    }

    private static string Style(string markup)
    {
        TestConsole console = new TestConsole { EmitAnsiSequences = true };
        console.Markup(markup);
        return console.Output[..console.Output.IndexOf('X')];
    }

    // AC-5
    [Fact]
    public void Notes_NoFolder_ShowsEmptyMessage_DoesNotCreateFolder()
    {
        string[] main = MainView(LastFrame("/notes"));

        Assert.Contains(main, l => l.Contains("No notes yet. Type /new <title> to create one.", StringComparison.Ordinal));
        Assert.False(Directory.Exists(root));
        (_, string output) = Run(true, "/notes");
        Assert.Contains(Style("[grey]X[/]") + "No notes yet.", output);
    }

    // AC-5
    [Fact]
    public void Notes_EmptyFolder_ShowsEmptyMessage()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "x.txt"), "x");

        string[] main = MainView(LastFrame("/notes"));

        Assert.Contains(main, l => l.Contains("No notes yet.", StringComparison.Ordinal));
    }

    // AC-6
    [Theory]
    [InlineData("/open Shopping list")]
    [InlineData("/open shopping LIST")]
    [InlineData("/open Shopping list.md")]
    public void Open_ShowsPanel_WithNameHeader_AndLiteralText(string command)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Shopping list.md"), "# Shopping list\n- [x] milk [/]\n- [bold]eggs\n");

        string[] main = MainView(LastFrame(command));

        Assert.Contains(main, l => l.Contains("╭─Shopping list", StringComparison.Ordinal));
        Assert.Contains(main, l => l.Contains("│ # Shopping list", StringComparison.Ordinal));
        Assert.Contains(main, l => l.Contains("- [x] milk [/]", StringComparison.Ordinal));
        Assert.Contains(main, l => l.Contains("- [bold]eggs", StringComparison.Ordinal));
    }

    // AC-7
    [Theory]
    [InlineData("/open missing", "No note named missing. Type /notes to see all notes.")]
    [InlineData("/open ../secret", "No note named ../secret. Type /notes to see all notes.")]
    [InlineData("/open", "Type /notes to see all notes.")]
    public void Open_Unknown_ShowsRedHint(string command, string hint)
    {
        File.WriteAllText(Path.Combine(parent, "secret.md"), "TOP SECRET");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "note.md"), "n");

        (int exitCode, string output) = Run(false, command);

        Assert.Equal(0, exitCode);
        AssertOnHintRow(Frames(output)[1], hint);
        Assert.DoesNotContain("TOP SECRET", output);
        AssertRedHint(command, hint[..4]);
    }

    // AC-7
    [Fact]
    public void Open_AbsolutePath_DoesNotReadOutsideRoot()
    {
        string secret = Path.Combine(parent, "secret.md");
        File.WriteAllText(secret, "TOP SECRET");

        (int exitCode, string output) = Run(false, "/open " + secret);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("TOP SECRET", output);
        AssertOnHintRow(Frames(output)[1], "No note named");
    }

    // AC-8
    [Fact]
    public void MarkupInTitle_IsShownLiterally_Everywhere()
    {
        string[] created = MainView(LastFrame("/new [bold]"));
        Assert.Contains(created, l => l.Contains("Created [bold]", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(root, "[bold].md")));

        string[] listed = MainView(LastFrame("/notes"));
        Assert.Contains(listed, l => l.Contains("│ [bold] ", StringComparison.Ordinal));

        string[] opened = MainView(LastFrame("/open [BOLD]"));
        Assert.Contains(opened, l => l.Contains("╭─[bold]", StringComparison.Ordinal));
        Assert.Contains(opened, l => l.Contains("# [bold]", StringComparison.Ordinal));

        AssertOnHintRow(LastFrame("/new [bold]"), "Note [bold] already exists");
        AssertOnHintRow(LastFrame("/open [x]"), "No note named [x].");
    }

    // AC-9
    [Fact]
    public void Help_ListsNoteCommands()
    {
        string[] main = MainView(LastFrame("/help"));

        Assert.Contains(main, l => l.Contains("/new", StringComparison.Ordinal) && l.Contains("Create a note", StringComparison.Ordinal));
        Assert.Contains(main, l => l.Contains("/notes", StringComparison.Ordinal) && l.Contains("List notes", StringComparison.Ordinal));
        Assert.Contains(main, l => l.Contains("/open", StringComparison.Ordinal) && l.Contains("Open a note", StringComparison.Ordinal));
    }

    // AC-9
    [Fact]
    public void Popup_ListsNoteCommands()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        NoteCommands.Register(registry, new NoteStore(root));
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
        Assert.Contains(rows, w => w.Text.StartsWith("/new  ", StringComparison.Ordinal) && w.Text.Contains("Create a note"));
        Assert.Contains(rows, w => w.Text.StartsWith("/notes", StringComparison.Ordinal) && w.Text.Contains("List notes"));
        Assert.Contains(rows, w => w.Text.StartsWith("/open", StringComparison.Ordinal) && w.Text.Contains("Open a note"));
    }

    // 013 AC-3
    [Fact]
    public void New_TitleEndingInMd_CreatesSingleExtension_OpensWithAndWithoutMd()
    {
        string[] created = MainView(LastFrame("/new x.md"));

        Assert.Contains(created, l => l.Contains("Created x", StringComparison.Ordinal)
            && !l.Contains("Created x.md", StringComparison.Ordinal));
        Assert.Equal("# x\n"u8.ToArray(), File.ReadAllBytes(Path.Combine(root, "x.md")));
        Assert.False(File.Exists(Path.Combine(root, "x.md.md")));

        foreach (string command in new[] { "/open x", "/open x.md" })
        {
            string[] opened = MainView(LastFrame(command));
            Assert.Contains(opened, l => l.Contains("╭─x─", StringComparison.Ordinal));
            Assert.Contains(opened, l => l.Contains("│ # x", StringComparison.Ordinal));
        }
    }

    // 013 AC-4
    [Theory]
    [InlineData("/new x.md")]
    [InlineData("/new X.MD")]
    public void New_ExistingAfterStripping_ShowsRedHint_FileUnchanged(string command)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "x.md");
        byte[] original = "# mine\n"u8.ToArray();
        File.WriteAllBytes(path, original);
        string name = command == "/new x.md" ? "x" : "X";

        AssertOnHintRow(LastFrame(command), $"Note {name} already exists. Type /open {name} to see it.");
        AssertRedHint(command, $"Note {name} already exists");
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(root));
    }

    // 013 AC-5
    [Theory]
    [InlineData("/new .md")]
    [InlineData("/new .md.md")]
    [InlineData("/new  .MD ")]
    public void New_OnlyMd_ShowsNeedsTitle_CreatesNothing(string command)
    {
        AssertOnHintRow(LastFrame(command), "A note needs a title.");
        AssertRedHint(command, "A note needs a title.");
        Assert.Empty(Directory.GetFileSystemEntries(parent));
    }

    // 013 AC-6
    [Fact]
    public void New_100CharTitlePlusMd_IsAccepted()
    {
        string title = new string('a', 100);

        (int exitCode, _) = Run(false, "/new " + title + ".md");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(root, title + ".md")));
        Assert.Single(Directory.GetFiles(root));
    }

    // 013 AC-6
    [Theory]
    [InlineData("/new a/b.md", "can't contain")]
    [InlineData("/new .x.md", "can't start with a dot")]
    [InlineData("/new ..md", "can't start with a dot")]
    public void New_InvalidAfterStripping_ShowsSameHint(string command, string hint)
    {
        AssertOnHintRow(LastFrame(command), hint);
        AssertRedHint(command, "A note");
        Assert.Empty(Directory.GetFileSystemEntries(parent));
    }

    // 013 AC-7
    [Fact]
    public void Open_LegacyDoubleExtension_ListedAndOpenedByListedName()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "x.md.md"), "legacy text");
        File.WriteAllText(Path.Combine(root, "y.md"), "plain text");

        string[] listed = MainView(LastFrame("/notes"));
        Assert.Contains(listed, l => l.Contains("│ x.md ", StringComparison.Ordinal));

        string[] legacy = MainView(LastFrame("/open x.md"));
        Assert.Contains(legacy, l => l.Contains("╭─x.md─", StringComparison.Ordinal));
        Assert.Contains(legacy, l => l.Contains("legacy text", StringComparison.Ordinal));

        string[] plain = MainView(LastFrame("/open y.md"));
        Assert.Contains(plain, l => l.Contains("╭─y─", StringComparison.Ordinal));
        Assert.Contains(plain, l => l.Contains("plain text", StringComparison.Ordinal));
    }
}
