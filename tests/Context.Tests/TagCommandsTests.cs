using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Context.Tests;

public sealed class TagCommandsTests : IDisposable
{
    private const int Width = 100;
    private const int Height = 24;
    private const int MainWidth = Width - 32;

    private static readonly Regex Ansi = new(@"\u001b\[[0-9;?]*[A-Za-z]");

    private readonly string parent;
    private readonly string root;

    public TagCommandsTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-tagcmds-" + Guid.NewGuid().ToString("N"));
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

    private string DbPath => Path.Combine(root, TagIndex.FileName);

    private void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".md"), text);
    }

    private (int ExitCode, string Output) Run(bool ansi, params string[] commands)
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        NoteStore store = new NoteStore(root);
        NoteCommands.Register(registry, store);
        TagCommands.Register(registry, new TagIndex(root), store);
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

    private static string[] MainView(string[] frame) =>
        frame[..^4].Select(l => l.Length > MainWidth ? l[..MainWidth] : l).ToArray();

    private static string Style(string markup)
    {
        TestConsole console = new TestConsole { EmitAnsiSequences = true };
        console.Markup(markup);
        return console.Output[..console.Output.IndexOf('X')];
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
        Assert.Contains(Style("[red]X[/]") + text, output);
    }

    // AC-6
    [Fact]
    public void Tags_ShowsTable_SortedByTag_WithCounts()
    {
        WriteNote("A", "#work #home");
        WriteNote("B", "#Work");

        string[] main = MainView(LastFrame("/tags"));

        Assert.Contains(main, l => l.Contains("Tag", StringComparison.Ordinal) && l.Contains("Notes", StringComparison.Ordinal));
        int home = Array.FindIndex(main, l => l.Contains("│ #home ", StringComparison.Ordinal));
        int work = Array.FindIndex(main, l => l.Contains("│ #work ", StringComparison.Ordinal));
        Assert.True(home >= 0 && work > home);
        Assert.Contains("│ 1 ", main[home]);
        Assert.Contains("│ 2 ", main[work]);

        (_, string output) = Run(true, "/tags");
        Assert.Contains(Style("[steelblue1]X[/]") + "#home", output);
        Assert.Contains(Style("[grey]X[/]") + "2", output);
    }

    // AC-6
    [Fact]
    public void Tags_None_ShowsGreyMessage()
    {
        WriteNote("A", "# just a heading");

        string[] main = MainView(LastFrame("/tags"));

        Assert.Contains(main, l => l.Contains("No tags yet. Add #tag to a note to link it.", StringComparison.Ordinal));
        (_, string output) = Run(true, "/tags");
        Assert.Contains(Style("[grey]X[/]") + "No tags yet.", output);
    }

    // AC-6 / AC-3: works with no folder at all and creates the database.
    [Fact]
    public void Tags_NoFolder_ShowsGreyMessage_CreatesDatabase()
    {
        string[] main = MainView(LastFrame("/tags"));

        Assert.Contains(main, l => l.Contains("No tags yet.", StringComparison.Ordinal));
        Assert.True(File.Exists(DbPath));
    }

    // AC-7
    [Theory]
    [InlineData("/tags work")]
    [InlineData("/tags #work")]
    [InlineData("/tags WORK")]
    [InlineData("/TAGS #Work")]
    public void TagsWithTag_ListsNotes(string command)
    {
        WriteNote("beta", "#work");
        WriteNote("Alpha", "#Work");
        WriteNote("gamma", "#home");

        string[] main = MainView(LastFrame(command));

        Assert.Contains(main, l => l.Contains("Notes tagged #work", StringComparison.Ordinal));
        int alpha = Array.FindIndex(main, l => l.Contains("│ Alpha ", StringComparison.Ordinal));
        int beta = Array.FindIndex(main, l => l.Contains("│ beta ", StringComparison.Ordinal));
        Assert.True(alpha >= 0 && beta > alpha);
        Assert.DoesNotContain(main, l => l.Contains("gamma", StringComparison.Ordinal));
    }

    // AC-7
    [Theory]
    [InlineData("/tags work")]
    [InlineData("/tags #Work")]
    public void TagsWithTag_Unknown_ShowsRedHint(string command)
    {
        WriteNote("A", "#home");

        AssertOnHintRow(LastFrame(command), "No notes tagged work.");
        AssertRedHint(command, "No notes tagged work.");
    }

    // AC-8
    [Theory]
    [InlineData("/links A")]
    [InlineData("/links a")]
    [InlineData("/links A.md")]
    public void Links_ListsLinkedNotes_WithSharedTags(string command)
    {
        WriteNote("A", "#work #home");
        WriteNote("beta", "#home #work");
        WriteNote("Charlie", "#work");
        WriteNote("delta", "#other");

        string[] main = MainView(LastFrame(command));

        Assert.Contains(main, l => l.Contains("Linked to A", StringComparison.Ordinal));
        int beta = Array.FindIndex(main, l => l.Contains("│ beta ", StringComparison.Ordinal));
        int charlie = Array.FindIndex(main, l => l.Contains("│ Charlie ", StringComparison.Ordinal));
        Assert.True(beta >= 0 && charlie > beta);
        Assert.Contains("#home, #work", main[beta]);
        Assert.Contains("#work", main[charlie]);
        Assert.DoesNotContain(main, l => l.Contains("delta", StringComparison.Ordinal));
        Assert.DoesNotContain(main, l => l.Contains("│ A ", StringComparison.Ordinal));
    }

    // AC-8
    [Fact]
    public void Links_UnknownNote_ShowsRedHint()
    {
        WriteNote("A", "#work");

        AssertOnHintRow(LastFrame("/links missing"), "No note named missing. Type /notes to see all notes.");
        AssertRedHint("/links missing", "No note named missing.");
    }

    // AC-8
    [Fact]
    public void Links_NoLinks_ShowsGreyMessage()
    {
        WriteNote("Lonely", "#solo");
        WriteNote("Other", "#else");

        string[] main = MainView(LastFrame("/links lonely"));

        Assert.Contains(main, l => l.Contains("Lonely has no linked notes.", StringComparison.Ordinal));
        (_, string output) = Run(true, "/links lonely");
        Assert.Contains(Style("[grey]X[/]") + "Lonely has no linked notes.", output);
    }

    // AC-8
    [Fact]
    public void Links_MissingName_ShowsRedUsageHint()
    {
        AssertOnHintRow(LastFrame("/links"), "Type /links <note>");
        AssertRedHint("/links", "Type ");
    }

    // AC-9
    [Theory]
    [InlineData("/tags")]
    [InlineData("/tags work")]
    [InlineData("/links A")]
    public void GarbageDatabase_ShowsRedHint_LoopContinues_BytesUnchanged(string command)
    {
        WriteNote("A", "#work");
        byte[] garbage = new byte[2048];
        new Random(9).NextBytes(garbage);
        File.WriteAllBytes(DbPath, garbage);

        string[] frame = LastFrame(command, "/help", command);

        AssertOnHintRow(frame, "Can't read context.db");
        AssertRedHint(command, "Can't read context.db");
        Assert.Equal(garbage, File.ReadAllBytes(DbPath));
    }

    // AC-10
    [Fact]
    public void MarkupInNoteName_ShownLiterally()
    {
        WriteNote("[red]x", "#work");
        WriteNote("plain", "#work");

        string[] tagged = MainView(LastFrame("/tags work"));
        Assert.Contains(tagged, l => l.Contains("│ [red]x ", StringComparison.Ordinal));

        string[] links = MainView(LastFrame("/links plain"));
        Assert.Contains(links, l => l.Contains("│ [red]x ", StringComparison.Ordinal));

        string[] own = MainView(LastFrame("/links [red]x"));
        Assert.Contains(own, l => l.Contains("Linked to [red]x", StringComparison.Ordinal));

        AssertOnHintRow(LastFrame("/links [bold]y"), "No note named [bold]y.");
    }

    // AC-10
    [Fact]
    public void ControlCharacterInNoteName_ShowsPlaceholder()
    {
        try
        {
            WriteNote("bad\u001bname", "#work");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Windows does not allow control characters in file names.
            return;
        }

        WriteNote("plain", "#work");

        (int exitCode, string output) = Run(false, "/tags work", "/links plain");

        Assert.Equal(0, exitCode);
        Assert.Contains("bad�name", output);
        Assert.DoesNotContain('\u001b', Ansi.Replace(output, string.Empty));
    }

    // UX: both commands appear in /help.
    [Fact]
    public void Help_ListsTagCommands()
    {
        string[] main = MainView(LastFrame("/help"));

        Assert.Contains(main, l => l.Contains("/tags", StringComparison.Ordinal));
        Assert.Contains(main, l => l.Contains("/links", StringComparison.Ordinal));
    }
}
