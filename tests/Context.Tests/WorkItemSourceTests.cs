using System.Text.RegularExpressions;

namespace Context.Tests;

// 280: in Core only GitCommitLog.cs and in the command line only ProcessDesktopLauncher.cs name System.Diagnostics or
// start a process; GitCommitLog never uses a shell.
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
    public void Core_NamesProcessOnlyInGitCommitLog()
    {
        Assert.Equal(["GitCommitLog.cs"], FilesNamingProcess("Context.Core"));
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
}
