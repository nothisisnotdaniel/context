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

    /// <summary>The output with the app's own cursor and clear sequences removed, so only rendered text is left.</summary>
    private static string RenderedText(string output) => Ansi.Replace(output, string.Empty);

    // 015 AC-4
    [Fact]
    public void Open_NoteWithControlCharacters_ShowsPlaceholders()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "evil.md"),
            "start\u001b[2Jclear\nti\u001b]0;pwned\u0007tle\nbell\u0007 back\bspace csi\u009b31mred end");

        string[] main = MainView(LastFrame("/open evil"));
        (int exitCode, string output) = Run(false, "/open evil");

        Assert.Equal(0, exitCode);
        Assert.Contains(main, l => l.Contains("start\uFFFD[2Jclear", StringComparison.Ordinal));
        Assert.Contains(main, l => l.Contains("ti\uFFFD]0;pwned\uFFFDtle", StringComparison.Ordinal));
        Assert.Contains(main, l => l.Contains("bell\uFFFD back\uFFFDspace csi\uFFFD31mred end", StringComparison.Ordinal));
        string text = RenderedText(output);
        Assert.DoesNotContain('\u001b', text);
        Assert.DoesNotContain('\u0007', text);
        Assert.DoesNotContain('\b', text);
        Assert.DoesNotContain('\u009b', text);
    }

    // 015 AC-5
    [Fact]
    public void Notes_NameWithControlCharacter_ShowsPlaceholder_TableAligned()
    {
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "bad\u001bname.md"), "x");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Windows does not allow control characters in file names.
            return;
        }

        File.WriteAllText(Path.Combine(root, "Alpha.md"), "a");

        string[] main = MainView(LastFrame("/notes"));
        (_, string output) = Run(false, "/notes");

        int bad = Array.FindIndex(main, l => l.Contains("│ bad\uFFFDname ", StringComparison.Ordinal));
        int alpha = Array.FindIndex(main, l => l.Contains("│ Alpha ", StringComparison.Ordinal));
        Assert.True(bad >= 0 && alpha >= 0);
        Assert.Equal(main[alpha].TrimEnd().Length, main[bad].TrimEnd().Length);
        Assert.Equal(main[alpha].LastIndexOf('│'), main[bad].LastIndexOf('│'));
        Assert.Equal(
            main[alpha].IndexOf('│', main[alpha].IndexOf('│') + 1),
            main[bad].IndexOf('│', main[bad].IndexOf('│') + 1));
        Assert.DoesNotContain('\u001b', RenderedText(output));
        Assert.DoesNotContain("bad\u001b", output, StringComparison.Ordinal);
    }

    // 015 AC-6
    [Fact]
    public void Open_NameWithControlCharacter_HeaderShowsPlaceholder()
    {
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "x\u0007y.md"), "body");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return;
        }

        // LineEditor cannot type control characters, so call the /open handler directly.
        CommandRegistry registry = new CommandRegistry();
        NoteCommands.Register(registry, new NoteStore(root));
        Assert.True(registry.TryGet("/open", out Command? open));
        CommandResult result = open!.Handler("x\u0007y");
        Assert.NotNull(result.Message);
        TestConsole console = new TestConsole();
        console.Profile.Width = 40;
        console.Write(result.Message);

        string[] lines = console.Output.Split('\n');
        Assert.StartsWith("╭─x\uFFFDy─", lines[0]);
        Assert.Contains(lines, l => l.Contains("│ body", StringComparison.Ordinal));
        Assert.DoesNotContain('\u0007', console.Output);
    }

    // 015 AC-7
    [Fact]
    public void Open_CrlfAndTabs_RenderLineByLine()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(
            Path.Combine(root, "multi.md"),
            System.Text.Encoding.UTF8.GetBytes("# Title\r\n\tindented\r\nlast\tline\r\n"));

        string[] main = MainView(LastFrame("/open multi"));
        (_, string output) = Run(false, "/open multi");

        int title = Array.FindIndex(main, l => l.Contains("│ # Title", StringComparison.Ordinal));
        Assert.True(title >= 0);
        Assert.Contains("│     indented", main[title + 1]);
        Assert.Contains("│ last    line", main[title + 2]);
        Assert.DoesNotContain('\r', output);
        Assert.DoesNotContain('\t', output);
    }

    // 015 AC-9
    [Fact]
    public void Open_MarkupInNoteText_StillLiteral()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "m.md"), "see [red]x[/] here\u001b");

        string[] main = MainView(LastFrame("/open m"));

        Assert.Contains(main, l => l.Contains("see [red]x[/] here\uFFFD", StringComparison.Ordinal));
    }

    // 018 AC-7
    [Theory]
    [InlineData("/new CON", "Windows reserves")]
    [InlineData("/new com1", "Windows reserves")]
    [InlineData("/new nul.txt", "Windows reserves")]
    [InlineData("/new CON.md", "Windows reserves")]
    [InlineData("/new x.", "can't end with a dot or a space")]
    [InlineData("/new x..md", "can't end with a dot or a space")]
    public void New_WindowsUnsafeTitle_ShowsRedHint_CreatesNothing(string command, string hint)
    {
        AssertOnHintRow(LastFrame(command), hint);
        AssertRedHint(command, "A note title can't");
        Assert.Empty(Directory.GetFileSystemEntries(parent));
    }

    // 018 AC-8
    [Theory]
    [InlineData("CONSOLE")]
    [InlineData("x.CON")]
    public void New_ResemblesReservedName_CreatesNote(string title)
    {
        string[] frame = LastFrame("/new " + title);

        Assert.Contains(MainView(frame), l => l.Contains("Created " + title, StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(root, title + ".md")));
    }

    // 018 AC-9
    [Fact]
    public void Open_ExistingNoteWithNowRejectedName_StillListedAndOpens()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "x..md"), "old note");

        string[] listed = MainView(LastFrame("/notes"));
        Assert.Contains(listed, l => l.Contains("│ x. ", StringComparison.Ordinal));

        string[] opened = MainView(LastFrame("/open x."));
        Assert.Contains(opened, l => l.Contains("╭─x.─", StringComparison.Ordinal));
        Assert.Contains(opened, l => l.Contains("old note", StringComparison.Ordinal));
    }

    private void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".md"), text);
    }

    // AC-9
    [Fact]
    public void Export_WritesPlainUtf8WithoutBom_NameWithSpaces()
    {
        WriteNote("Big plan", "# Big plan\nÜmlaut text\n");
        string target = Path.Combine(parent, "out dir", "plan copy.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        (int exitCode, string output) = Run(false, $"/export Big plan {target}");

        Assert.Equal(0, exitCode);
        Assert.Equal(new System.Text.UTF8Encoding(false).GetBytes("# Big plan\nÜmlaut text\n"), File.ReadAllBytes(target));
        Assert.Contains("Exported Big plan to", output);
        Assert.DoesNotContain("not encrypted", output);
    }

    // AC-9
    [Fact]
    public void Export_IntoTheContextFolder_IsRefused()
    {
        WriteNote("Plan", "# Plan\n");
        string inside = Path.Combine(root, "copy.md");

        (_, string output) = Run(true, $"/export Plan {inside}");

        Assert.Contains(RedStart() + "Export to a path outside the Context folder.", output);
        Assert.False(File.Exists(inside));
    }

    // AC-9
    [Fact]
    public void Export_ToExistingFile_IsRefusedAndLeavesItAlone()
    {
        WriteNote("Plan", "# Plan\n");
        string target = Path.Combine(parent, "existing.md");
        File.WriteAllText(target, "keep me");

        (_, string output) = Run(true, $"/export Plan {target}");

        Assert.Contains(RedStart() + "That file already exists", output);
        Assert.Equal("keep me", File.ReadAllText(target));
    }

    // AC-9
    [Theory]
    [InlineData("/export")]
    [InlineData("/export Plan")]
    [InlineData("/export Nope /tmp/x.md")]
    public void Export_MissingPartsOrUnknownNote_ShowsRedHint(string command)
    {
        WriteNote("Plan", "# Plan\n");

        (_, string output) = Run(true, command);

        Assert.Contains(RedStart(), output);
        Assert.Contains("/export <note> <path>", output);
    }

    // AC-9: in encrypted mode the copy is plain, and the hint says so.
    [Fact]
    public void Export_Encrypted_WritesPlainAndWarns()
    {
        using Session session = new Session(root);
        string target = Path.Combine(parent, "plan.md");

        session.Run("/encrypt", VaultTests.Passphrase, VaultTests.Passphrase, "/new Plan", $"/export Plan {target}");

        Assert.Equal("# Plan\n"u8.ToArray(), File.ReadAllBytes(target));
        Assert.Contains(RedStart() + "This copy is not encrypted.", session.Console.Output);
        Assert.True(Vault.IsEncrypted(File.ReadAllBytes(Path.Combine(root, "Plan.md"))));
    }

    // AC-10
    [Fact]
    public void Import_CreatesNoteNamedAfterFile_SourceUntouched()
    {
        string source = Path.Combine(parent, "Meeting notes.md");
        File.WriteAllText(source, "# Meeting\n#work\n");
        File.SetLastWriteTimeUtc(source, SampleData.Modified);

        (_, string output) = Run(false, $"/import {source}", "/open Meeting notes");

        Assert.Contains("Imported Meeting notes", output);
        Assert.Contains("delete it if you don't need the plain copy", output);
        Assert.Equal("# Meeting\n#work\n", File.ReadAllText(Path.Combine(root, "Meeting notes.md")));
        Assert.Equal("# Meeting\n#work\n", File.ReadAllText(source));
        Assert.Equal(SampleData.Modified, File.GetLastWriteTimeUtc(source));
    }

    // AC-10
    [Fact]
    public void Import_ExistingNote_IsReplaced()
    {
        WriteNote("Plan", "# Old\n");
        string source = Path.Combine(parent, "plan.md");
        File.WriteAllText(source, "# New\n");

        (_, string output) = Run(false, $"/import {source}");

        Assert.Contains("Replaced Plan", output);
        Assert.Equal("# New\n", File.ReadAllText(Path.Combine(root, "Plan.md")));
        Assert.Single(Directory.GetFiles(root));
    }

    // AC-10
    [Fact]
    public void Import_Encrypted_StoresCiphertext()
    {
        string source = Path.Combine(parent, "Secret.md");
        File.WriteAllText(source, "# Secret\nimportMarker9\n");
        using Session session = new Session(root);

        session.Run("/encrypt", VaultTests.Passphrase, VaultTests.Passphrase, $"/import {source}", "/open Secret");

        byte[] stored = File.ReadAllBytes(Path.Combine(root, "Secret.md"));
        Assert.True(Vault.IsEncrypted(stored));
        Assert.Contains("Imported and encrypted Secret", AnsiWrites.Strip(session.Console.Output));
        Assert.Contains("importMarker9", AnsiWrites.Strip(session.Console.Output));
        Assert.Equal("# Secret\nimportMarker9\n", File.ReadAllText(source));
    }

    // AC-10
    [Fact]
    public void Import_InvalidNameMissingOrEncryptedOrInside_ShowsRedHint_ChangesNothing()
    {
        Directory.CreateDirectory(root);
        string reserved = Path.Combine(parent, "CON.md");
        File.WriteAllText(reserved, "x");
        string encrypted = Path.Combine(parent, "enc.md");
        using (Vault vault = Vault.Create(Path.Combine(parent, "v"), VaultTests.Passphrase, VaultTests.TestIterations))
        {
            File.WriteAllBytes(encrypted, vault.Encrypt(Vault.KindNote, [1, 2, 3]));
        }

        string inside = Path.Combine(root, "x.md");
        File.WriteAllText(inside, "inside");
        string big = Path.Combine(parent, "big.md");
        File.WriteAllBytes(big, new byte[NoteStore.MaxImportBytes + 1]);

        (_, string output) = Run(
            true, $"/import {reserved}", $"/import {Path.Combine(parent, "none.md")}", $"/import {encrypted}",
            $"/import {inside}", $"/import {big}", "/import");

        string red = RedStart();
        Assert.Contains(red + "A note title can't be a name Windows reserves", output);
        Assert.Contains(red + "No such file:", output);
        Assert.Contains(red + "Can't import a file over 5 MB or an encrypted one:", output);
        Assert.Contains(red + "That file is already in the Context folder.", output);
        Assert.Contains(red + "Type ", output);
        Assert.Contains("/import <path>", output);
        Assert.Equal(["x.md"], Directory.GetFiles(root).Select(Path.GetFileName));
        Assert.Equal("inside", File.ReadAllText(inside));
    }

    // AC-7
    [Fact]
    public void Open_UnreadableNote_ShowsCantRead()
    {
        WriteNote("Plan", "x");
        using (Vault vault = Vault.Create(Path.Combine(parent, "v"), VaultTests.Passphrase, VaultTests.TestIterations))
        {
            File.WriteAllBytes(Path.Combine(root, "Plan.md"), vault.Encrypt(Vault.KindNote, [1]));
        }

        AssertRedHint("/open Plan", "Can't read Plan.");
    }
}
