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

        AssertOnHintRow(LastFrame(command), "No notes tagged #work.");
        AssertRedHint(command, "No notes tagged #work.");
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

    // 024 AC-7
    [Fact]
    public void Tags_ShowsBracketForm_ForSpacedTags()
    {
        WriteNote("A", "#[Project Alpha] #work");
        WriteNote("B", "#[project  alpha]");

        string[] main = MainView(LastFrame("/tags"));

        int project = Array.FindIndex(main, l => l.Contains("│ #[project alpha] ", StringComparison.Ordinal));
        int work = Array.FindIndex(main, l => l.Contains("│ #work ", StringComparison.Ordinal));
        Assert.True(project >= 0 && work > project);
        Assert.Contains("│ 2 ", main[project]);
        Assert.Contains("│ 1 ", main[work]);

        (_, string output) = Run(true, "/tags");
        Assert.Contains(Style("[steelblue1]X[/]") + "#[project alpha]", output);
    }

    // 024 AC-7
    [Theory]
    [InlineData("/tags project alpha")]
    [InlineData("/tags #[Project Alpha]")]
    [InlineData("/tags [project alpha]")]
    [InlineData("/tags   Project   Alpha  ")]
    public void TagsWithSpacedTag_ListsNotes(string command)
    {
        WriteNote("beta", "#[project alpha]");
        WriteNote("Alpha", "#[Project Alpha]");
        WriteNote("gamma", "#project");

        string[] main = MainView(LastFrame(command));

        Assert.Contains(main, l => l.Contains("Notes tagged #[project alpha]", StringComparison.Ordinal));
        int alpha = Array.FindIndex(main, l => l.Contains("│ Alpha ", StringComparison.Ordinal));
        int beta = Array.FindIndex(main, l => l.Contains("│ beta ", StringComparison.Ordinal));
        Assert.True(alpha >= 0 && beta > alpha);
        Assert.DoesNotContain(main, l => l.Contains("gamma", StringComparison.Ordinal));
    }

    // 024 AC-7
    [Fact]
    public void Links_ShowsSharedSpacedTags_InBracketForm()
    {
        WriteNote("A", "#work #[Project Alpha]");
        WriteNote("B", "#[work] #[project alpha]");

        string[] main = MainView(LastFrame("/links A"));

        int b = Array.FindIndex(main, l => l.Contains("│ B ", StringComparison.Ordinal));
        Assert.True(b >= 0);
        Assert.Contains("#[project alpha], #work", main[b]);
    }

    // 024 AC-8
    [Theory]
    [InlineData("/tags no such tag")]
    [InlineData("/tags #[No  Such Tag]")]
    public void TagsWithSpacedTag_Unknown_ShowsRedHint(string command)
    {
        WriteNote("A", "#home");

        string text = "No notes tagged #[no such tag]. Type /tags to see all tags.";
        AssertOnHintRow(LastFrame(command), text);
        AssertRedHint(command, "No notes tagged #[no such tag].");
    }

    // 024 AC-8: a bracket tag is shown literally, never read as markup.
    [Fact]
    public void SpacedTag_LooksLikeMarkup_ShownLiterally()
    {
        WriteNote("A", "#[red x] #[a b]");
        WriteNote("B", "#[red x] #[a b]");

        string[] tags = MainView(LastFrame("/tags"));
        Assert.Contains(tags, l => l.Contains("│ #[a b] ", StringComparison.Ordinal));
        Assert.Contains(tags, l => l.Contains("│ #[red x] ", StringComparison.Ordinal));

        string[] tagged = MainView(LastFrame("/tags #[red x]"));
        Assert.Contains(tagged, l => l.Contains("Notes tagged #[red x]", StringComparison.Ordinal));

        string[] links = MainView(LastFrame("/links A"));
        Assert.Contains(links, l => l.Contains("#[a b], #[red x]", StringComparison.Ordinal));

        AssertOnHintRow(LastFrame("/tags [bold x]"), "No notes tagged #[bold x].");
    }

    // 048 AC-8: with a note link the table gets a Link column; tag-only rows have an empty Link cell.
    [Fact]
    public void Links_NoteLinks_ShowLinkColumn()
    {
        WriteNote("A", "#work [[Beta]] [[Gamma]] [[delta.md]]");
        WriteNote("Beta", "#work [[A]]");
        WriteNote("c", "#work");
        WriteNote("Delta", "plain");
        WriteNote("Epsilon", "see [[a]]");

        string[] main = MainView(LastFrame("/links A"));

        string header = Assert.Single(main, l => l.Contains("│ Note ", StringComparison.Ordinal));
        int note = header.IndexOf("Note", StringComparison.Ordinal);
        int link = header.IndexOf("Link", StringComparison.Ordinal);
        int tags = header.IndexOf("Shared tags", StringComparison.Ordinal);
        Assert.True(note < link && link < tags);
        Assert.Matches(@"│ Beta +│ both ways +│ #work +│", Assert.Single(main, l => l.Contains("│ Beta ", StringComparison.Ordinal)));
        Assert.Matches(@"│ c +│ +│ #work +│", Assert.Single(main, l => l.Contains("│ c ", StringComparison.Ordinal)));
        Assert.Matches(@"│ Delta +│ links to +│ +│", Assert.Single(main, l => l.Contains("│ Delta ", StringComparison.Ordinal)));
        Assert.Matches(@"│ Epsilon +│ linked from +│ +│", Assert.Single(main, l => l.Contains("│ Epsilon ", StringComparison.Ordinal)));
        Assert.Matches(@"│ Gamma +│ links to \(missing\) +│ +│", Assert.Single(main, l => l.Contains("│ Gamma ", StringComparison.Ordinal)));

        (_, string output) = Run(true, "/links A");
        Assert.Contains(Style("[grey]X[/]") + "Gamma", output);
        Assert.Contains(Style("[steelblue1]X[/]") + "Delta", output);
        Assert.Contains(Style("[grey]X[/]") + "links to (missing)", output);
        Assert.Contains(Style("[grey]X[/]") + "linked from", output);
    }

    // 048 AC-8: tag links only keep the old two columns.
    [Fact]
    public void Links_TagLinksOnly_NoLinkColumn()
    {
        WriteNote("A", "#work");
        WriteNote("B", "#work");

        string[] main = MainView(LastFrame("/links A"));

        Assert.Contains(main, l => l.Contains("│ Note ", StringComparison.Ordinal));
        Assert.DoesNotContain(main, l => l.Contains("Link ", StringComparison.Ordinal));
    }

    // 048 AC-8: a link to a missing note alone still shows the table.
    [Fact]
    public void Links_OnlyMissingLink_ShowsTable()
    {
        WriteNote("A", "[[Nowhere]]");

        string[] main = MainView(LastFrame("/links A"));

        Assert.Contains(main, l => l.Contains("Linked to A", StringComparison.Ordinal));
        Assert.Contains(main, l => l.Contains("links to (missing)", StringComparison.Ordinal));
        Assert.DoesNotContain(main, l => l.Contains("has no linked notes", StringComparison.Ordinal));
    }

    // 048 AC-9
    [Fact]
    public void Links_DescriptionAndUsage_ShowLinksLiterally()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        NoteStore store = new NoteStore(root);
        TagCommands.Register(registry, new TagIndex(root), store);
        Assert.True(registry.TryGet("/links", out Command command));
        Assert.Equal("Linked notes (tags, [[links]]): /links <note>", command.Description);

        CommandResult usage = TagCommands.Render(new LinksView(LinksViewKind.MissingArgument, string.Empty, null, []));
        TestConsole plain = new TestConsole();
        plain.Profile.Width = 200;
        plain.Write(usage.Hint!);
        Assert.Equal(
            "Type /links <note> to see the notes linked to it by tags or [[links]]. Type /notes to see all notes.",
            plain.Output.TrimEnd());

        string[] help = MainView(LastFrame("/help"));
        Assert.Contains(help, l => l.Contains("Linked notes (tags, [[links]]): /links <note>", StringComparison.Ordinal));
        AssertOnHintRow(LastFrame("/links"), "by tags or [[links]].");
    }

    // 048 AC-9: link targets are cleaned and escaped. The parser never yields a '[' in a target, so the
    // markup case is a hand-built view; the control character comes from a real note.
    [Fact]
    public void Links_MarkupAndControlsInTarget_ShownSafely()
    {
        LinksView view = new LinksView(
            LinksViewKind.Links,
            "A",
            "A [b]",
            [new NoteLink("[red]x[/red]", [], NoteLinkDirection.To, missing: true)]);
        TestConsole console = new TestConsole();
        console.Profile.Width = 200;
        console.Write(TagCommands.Render(view).Message!);
        Assert.Contains("[red]x[/red]", console.Output);
        Assert.Contains("Linked to A [b]", console.Output);

        WriteNote("A", "[[bad\u001bname]] [[Nope [x]]");
        (int exitCode, string output) = Run(false, "/links A");

        Assert.Equal(0, exitCode);
        Assert.Contains("bad\uFFFDname", output);
        Assert.DoesNotContain("Nope", output);
        Assert.DoesNotContain('\u001b', Ansi.Replace(output, string.Empty));
    }
}
