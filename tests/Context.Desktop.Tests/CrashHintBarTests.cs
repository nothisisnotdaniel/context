using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Context.Tests;

// 237 AC-9, AC-10: the crash hint at the top of Main, with FakeNoteLauncher.
public sealed class CrashHintBarTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private readonly DesktopFixture data = new();

    public CrashHintBarTests()
    {
        Folder = Path.Combine(data.Root, CrashLog.FolderName);
        Hint = new CrashHint(Path.Combine(Folder, "crash-20261029-085900-000.log"), Folder);
    }

    private string Folder { get; }

    private CrashHint Hint { get; }

    public void Dispose() => data.Dispose();

    private void RegisterHint()
    {
        Directory.CreateDirectory(Folder);
        data.CreateState();
        data.Bunit.Services.AddSingleton(Hint);
    }

    // AC-9: with a hint, Main shows the line with the path (also in title) and the two buttons.
    [Fact]
    public async Task Main_WithHint_ShowsTheHint()
    {
        RegisterHint();

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        AngleSharp.Dom.IElement text = cut.Find(".crash-hint .crash-hint-text");
        Assert.Equal("Context closed unexpectedly last time. Crash log: " + Hint.LogPath, text.TextContent);
        Assert.Equal(Hint.LogPath, text.GetAttribute("title"));
        Assert.Equal(
            ["Open folder", "Dismiss"],
            cut.FindAll(".crash-hint button").Select(b => b.TextContent).ToArray());
        Assert.Empty(cut.FindAll(".crash-hint .red"));
    }

    // AC-9: without a hint, no .crash-hint is rendered.
    [Fact]
    public async Task Main_WithoutHint_RendersNoHint()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.NotEmpty(cut.FindAll(".layout"));
        Assert.Empty(cut.FindAll(".crash-hint"));
    }

    // AC-9: Dismiss hides the hint and opens nothing.
    [Fact]
    public async Task Dismiss_HidesTheHint()
    {
        RegisterHint();
        IRenderedComponent<CrashHintBar> cut = await data.RenderAsync<CrashHintBar>();

        await StaleClick.ClickAsync(cut, ".crash-hint-dismiss");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".crash-hint")), Wait);
        Assert.Empty(data.Launcher.Launched);
    }

    // AC-10: Open folder launches only the hint's folder.
    [Fact]
    public async Task OpenFolder_LaunchesTheCrashFolderOnly()
    {
        RegisterHint();
        IRenderedComponent<CrashHintBar> cut = await data.RenderAsync<CrashHintBar>();

        await StaleClick.ClickAsync(cut, ".crash-hint-open");

        Assert.Equal([Folder], data.Launcher.Launched);
        Assert.Empty(cut.FindAll(".crash-hint .red"));
        Assert.Single(cut.FindAll(".crash-hint"));
    }

    // AC-10: a throwing launcher shows the fixed red line.
    [Fact]
    public async Task OpenFolder_LauncherThrows_ShowsFixedRedLine()
    {
        data.Launcher.Throws = new InvalidOperationException("secret launcher detail");
        RegisterHint();
        IRenderedComponent<CrashHintBar> cut = await data.RenderAsync<CrashHintBar>();

        await StaleClick.ClickAsync(cut, ".crash-hint-open");

        cut.WaitForAssertion(() => Assert.Equal("Can't open the folder.", cut.Find(".crash-hint .red").TextContent), Wait);
        Assert.Equal([Folder], data.Launcher.Launched);
        Assert.DoesNotContain("secret launcher detail", cut.Markup, StringComparison.Ordinal);
    }

    // AC-10 (round 2): .logs as a file: nothing is launched and the red line shows.
    [Fact]
    public async Task OpenFolder_FolderIsAFile_LaunchesNothing()
    {
        RegisterHint();
        Directory.Delete(Folder);
        File.WriteAllText(Folder, "not a folder");
        IRenderedComponent<CrashHintBar> cut = await data.RenderAsync<CrashHintBar>();

        await StaleClick.ClickAsync(cut, ".crash-hint-open");

        cut.WaitForAssertion(() => Assert.Equal("Can't open the folder.", cut.Find(".crash-hint .red").TextContent), Wait);
        Assert.Empty(data.Launcher.Launched);
    }

    // AC-10 (round 2): .logs as a symlink to another folder: nothing is launched and the red line shows.
    [Fact]
    public async Task OpenFolder_FolderIsASymlink_LaunchesNothing()
    {
        RegisterHint();
        Directory.Delete(Folder);
        string target = Path.Combine(data.Root, "elsewhere");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(Folder, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // This OS refuses symlinks here; there is nothing to check.
            return;
        }

        IRenderedComponent<CrashHintBar> cut = await data.RenderAsync<CrashHintBar>();

        await StaleClick.ClickAsync(cut, ".crash-hint-open");

        cut.WaitForAssertion(() => Assert.Equal("Can't open the folder.", cut.Find(".crash-hint .red").TextContent), Wait);
        Assert.Empty(data.Launcher.Launched);
    }
}
