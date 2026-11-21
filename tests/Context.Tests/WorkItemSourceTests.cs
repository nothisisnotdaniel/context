using System.Text.RegularExpressions;

namespace Context.Tests;

// 280, 381: in Core only GitCommitLog.cs and TimesProcessRunner.cs and in the command line only
// ProcessDesktopLauncher.cs name System.Diagnostics or start a process; neither Core file uses a shell.
public sealed class WorkItemSourceTests
{
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Context.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static List<string> FilesNamingProcess(string folder)
    {
        Regex process = new Regex(@"System\.Diagnostics|\bProcess(StartInfo)?\b", RegexOptions.CultureInvariant);
        string root = Path.Combine(RepoRoot(), "src", folder);
        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(root, f).Replace('\\', '/').StartsWith("obj/", StringComparison.Ordinal)
                && !Path.GetRelativePath(root, f).Replace('\\', '/').StartsWith("bin/", StringComparison.Ordinal))
            .Where(f => process.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .Select(f => f!)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void Core_NamesProcessOnlyInGitCommitLogAndTheTimesRunner()
    {
        Assert.Equal(["GitCommitLog.cs", "TimesProcessRunner.cs"], FilesNamingProcess("Context.Core"));
    }

    [Fact]
    public void Cli_NamesProcessOnlyInProcessDesktopLauncher()
    {
        Assert.Equal(["ProcessDesktopLauncher.cs"], FilesNamingProcess("Context"));
    }

    [Fact]
    public void GitCommitLog_UsesArgumentListOnly_NoShell()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Context.Core", "GitCommitLog.cs"));

        Assert.Contains("UseShellExecute = false", text, StringComparison.Ordinal);
        Assert.Contains("ArgumentList.Add(", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Arguments =", text, StringComparison.Ordinal);
        Assert.DoesNotContain("UseShellExecute = true", text, StringComparison.Ordinal);
        Assert.Contains("GIT_TERMINAL_PROMPT", text, StringComparison.Ordinal);
    }

    // 381: the Times runner, now in Core for meeting --book, passes the words only as the ArgumentList (no shell).
    [Fact]
    public void TimesProcessRunner_UsesArgumentListOnly_NoShell()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Context.Core", "TimesProcessRunner.cs"));

        Assert.Contains("UseShellExecute = false", text, StringComparison.Ordinal);
        Assert.Contains("new ProcessStartInfo(path, arguments)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Arguments =", text, StringComparison.Ordinal);
        Assert.DoesNotContain("UseShellExecute = true", text, StringComparison.Ordinal);
    }

    // 381: the command line names the real Times runner only in Program.cs, which wires it into Cli.
    [Fact]
    public void Cli_NamesTheTimesRunnerOnlyInProgram()
    {
        string root = Path.Combine(RepoRoot(), "src", "Context");
        List<string> found = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(root, f).Replace('\\', '/').StartsWith("obj/", StringComparison.Ordinal)
                && !Path.GetRelativePath(root, f).Replace('\\', '/').StartsWith("bin/", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("Times" + "ProcessRunner", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f)!)
            .ToList();

        Assert.Equal(["Program.cs"], found);
    }
}
