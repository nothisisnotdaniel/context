using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Context.Tests;

// 237 AC-11: a child that throws while rendering gives one component log and the boundary's fixed line.
public sealed class CrashBoundaryTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private readonly string root = Path.Combine(Path.GetTempPath(), "context-boundary-" + Guid.NewGuid().ToString("N"));
    private readonly BunitContext bunit = new();

    public void Dispose()
    {
        bunit.Dispose();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ThrowingChild_WritesOneComponentLog_AndShowsTheLine()
    {
        CrashLog log = new CrashLog(
            root, () => new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc), "1.0.0", "TestOS", "/home/someone");
        bunit.Services.AddSingleton(log);

        IRenderedComponent<CrashBoundary> cut = bunit.Render<CrashBoundary>(p => p.AddChildContent<Throwing>());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("p.crash-boundary")), Wait);
        string[] logs = Directory.GetFiles(log.Folder, "crash-*.log");
        string path = Assert.Single(logs);
        Assert.Contains("Source: component", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("child render failed", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(
            "Something went wrong. Crash log: " + path + ". Close and reopen Context.",
            cut.Find("p.crash-boundary").TextContent);
        Assert.DoesNotContain("child render failed", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void QuietChild_RendersItsContent_AndWritesNothing()
    {
        CrashLog log = new CrashLog(root, () => DateTime.UnixEpoch, "1.0.0", "TestOS", null);
        bunit.Services.AddSingleton(log);

        IRenderedComponent<CrashBoundary> cut = bunit.Render<CrashBoundary>(p => p.AddChildContent("<p class=\"quiet\">fine</p>"));

        Assert.Single(cut.FindAll("p.quiet"));
        Assert.False(Directory.Exists(log.Folder));
    }

    private sealed class Throwing : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) =>
            throw new InvalidOperationException("child render failed");
    }
}
