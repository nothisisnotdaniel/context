using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Context.Tests;

public sealed class CalendarLinkCommandsTests : IDisposable
{
    private const int Width = 160;
    private const int Height = 24;
    private const string Invalid = "The calendar link must be an https:// or webcal:// address.";
    private const string Clear = "\u001b[2J";

    private static readonly Regex Ansi = new(@"\u001b\[[0-9;?]*[A-Za-z]");
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private readonly string parent;
    private readonly string root;

    public CalendarLinkCommandsTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-linkcmds-" + Guid.NewGuid().ToString("N"));
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

    private string LinkPath => Path.Combine(root, CalendarLink.FileName);

    private string CachePath => Path.Combine(root, CalendarFeed.CacheFileName);

    private (int ExitCode, string Output) Run(bool ansi, params string[] commands)
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        CalendarLink link = new CalendarLink(root);
        CalendarFeed feed = new CalendarFeed(root, link, FakeHttpHandler.Throw(), () => Now);
        CalendarLinkCommands.Register(registry, link, feed, IcsParserTests.Zone);
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

    /// <summary>The hint row of the frame drawn after the last command, trimmed and without ANSI codes.</summary>
    private string Hint(params string[] commands)
    {
        (int exitCode, string output) = Run(false, commands);
        Assert.Equal(0, exitCode);
        string[] lines = Ansi.Replace(output.Split(Clear)[^2], string.Empty).Split('\n');
        return lines[^4].Trim();
    }

    /// <summary>Everything drawn after the last command's Enter up to the clear on /quit.</summary>
    private static string AfterLastCommand(string output) => output.Split(Clear)[^2];

    private static void AssertNoSecret(string text)
    {
        Assert.DoesNotContain(FakeHttpHandler.PathToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeHttpHandler.QueryToken, text, StringComparison.Ordinal);
    }

    private void AssertNoSecretInFiles()
    {
        foreach (string path in Directory.GetFiles(parent, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(path) != CalendarLink.FileName)
            {
                AssertNoSecret(File.ReadAllText(path));
            }
        }
    }

    private static string Style(string markup)
    {
        TestConsole console = new TestConsole { EmitAnsiSequences = true };
        console.Markup(markup);
        return console.Output[..console.Output.IndexOf('X')];
    }

    // AC-1, AC-6
    [Fact]
    public void Set_Https_Saves_ShowsMaskedHint()
    {
        (int exitCode, string output) = Run(false, "/calendar-link " + FakeHttpHandler.Link);

        Assert.Equal(0, exitCode);
        Assert.Equal(FakeHttpHandler.Link + "\n", File.ReadAllText(LinkPath));
        string after = AfterLastCommand(output);
        Assert.Contains($"Calendar link saved (https://{FakeHttpHandler.Host}/…).", after);
        AssertNoSecret(after);
        AssertNoSecretInFiles();
    }

    // AC-1
    [Fact]
    public void Set_Webcal_StoredAsHttps()
    {
        string hint = Hint("/calendar-link webcal://" + FakeHttpHandler.Link["https://".Length..]);

        Assert.Equal($"Calendar link saved (https://{FakeHttpHandler.Host}/…).", hint);
        Assert.Equal(FakeHttpHandler.Link + "\n", File.ReadAllText(LinkPath));
    }

    // AC-1: a new link drops the old link's cache.
    [Fact]
    public void Set_ClearsOldCache()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(CachePath, CalendarFeedTests.Ics);

        Hint("/calendar-link https://other.example.com/x.ics");

        Assert.False(File.Exists(CachePath));
    }

    // AC-1, AC-6
    [Theory]
    [InlineData("http://")]
    [InlineData("file:///")]
    [InlineData("/")]
    [InlineData("https://@")]
    [InlineData("https://user:pw@")]
    public void Set_Invalid_RedHint_NotSaved_NotEchoed(string prefix)
    {
        string value = prefix + FakeHttpHandler.Host + "/" + FakeHttpHandler.PathToken + "?t=" + FakeHttpHandler.QueryToken;

        (int exitCode, string output) = Run(true, "/calendar-link " + value);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(LinkPath));
        string after = AfterLastCommand(output);
        Assert.Contains(Style("[red]X[/]") + Invalid, after);
        AssertNoSecret(after);
        AssertNoSecretInFiles();
    }

    // AC-1: an invalid link keeps the old one.
    [Fact]
    public void Set_Invalid_KeepsExistingLink()
    {
        new CalendarLink(root).Set(FakeHttpHandler.Link);

        string hint = Hint("/calendar-link http://example.com/x.ics");

        Assert.Equal(Invalid, hint);
        Assert.Equal(FakeHttpHandler.Link + "\n", File.ReadAllText(LinkPath));
    }

    // AC-1
    [Fact]
    public void Show_NoLink()
    {
        Assert.Equal("No calendar link set.", Hint("/calendar-link"));
    }

    // AC-1, AC-6
    [Fact]
    public void Show_MaskedLink_NotDownloadedYet()
    {
        new CalendarLink(root).Set(FakeHttpHandler.Link);

        (int exitCode, string output) = Run(false, "/calendar-link");

        Assert.Equal(0, exitCode);
        Assert.Contains($"Calendar link: https://{FakeHttpHandler.Host}/… (not downloaded yet)", output);
        AssertNoSecret(output);
    }

    // AC-1: the last download time is the cache's time, in local time.
    [Fact]
    public void Show_MaskedLink_LastDownload()
    {
        new CalendarLink(root).Set(FakeHttpHandler.Link);
        File.WriteAllText(CachePath, CalendarFeedTests.Ics);
        File.SetLastWriteTimeUtc(CachePath, new DateTime(2026, 10, 29, 7, 30, 0, DateTimeKind.Utc));

        string hint = Hint("/calendar-link");

        Assert.Equal($"Calendar link: https://{FakeHttpHandler.Host}/… (last download 2026-10-29 08:30)", hint);
    }

    // AC-1
    [Fact]
    public void Show_DamagedLinkFile_RedHint()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(LinkPath, "http://example.com/" + FakeHttpHandler.PathToken);

        (int exitCode, string output) = Run(true, "/calendar-link");

        Assert.Equal(0, exitCode);
        Assert.Contains(Style("[red]X[/]") + "Can't read calendar-link.txt.", output);
        AssertNoSecret(output);
    }

    // AC-1
    [Theory]
    [InlineData("off")]
    [InlineData("OFF")]
    public void Off_DeletesLinkAndCache(string off)
    {
        new CalendarLink(root).Set(FakeHttpHandler.Link);
        File.WriteAllText(CachePath, CalendarFeedTests.Ics);

        string hint = Hint("/calendar-link " + off);

        Assert.Equal("Calendar link removed.", hint);
        Assert.False(File.Exists(LinkPath));
        Assert.False(File.Exists(CachePath));
        Assert.Equal("No calendar link set.", Hint("/calendar-link"));
    }

    // AC-1
    [Fact]
    public void Off_NoLink_SaysSo()
    {
        Assert.Equal("No calendar link set.", Hint("/calendar-link off"));
    }

    // AC-1
    [Fact]
    public void Help_ListsCalendarLink()
    {
        (_, string output) = Run(false, "/help");

        Assert.Contains("/calendar-link", AfterLastCommand(output));
    }
}
