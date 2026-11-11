using System.Text.RegularExpressions;

namespace Context.Tests;

/// <summary>Checks <c>.github/workflows/ci.yml</c> as plain text (no YAML package).</summary>
public sealed class CiWorkflowTests
{
    // AC-1 (131: release.yml joined it; these two files are the only workflows)
    [Fact]
    public void CiYml_IsTheOnlyWorkflow()
    {
        string folder = Path.Combine(RepoRoot(), ".github", "workflows");

        string[] files = [.. Directory.GetFileSystemEntries(folder)
            .Select(path => Path.GetFileName(path) ?? "")
            .Order(StringComparer.Ordinal)];

        Assert.Equal(["ci.yml", "release.yml"], files);
    }

    // AC-2
    [Fact]
    public void Triggers_ArePushToDevelopAndMain_AndEveryPullRequest()
    {
        string text = Workflow();

        Assert.Contains(
            "\non:\n  push:\n    branches:\n      - \"develop\"\n      - \"main\"\n  pull_request:\n\npermissions:",
            text);
        Assert.Single(Regex.Matches(text, @"^on:", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(text, @"branches"));
        Assert.DoesNotContain("pull_request_target", text);
        Assert.DoesNotContain("paths", text);
        Assert.DoesNotContain("tags", text);
    }

    // AC-3
    [Fact]
    public void Steps_AreCheckoutSetupRestoreBuildTest_InThatOrder()
    {
        string text = Workflow();
        string[] steps =
        [
            "uses: actions/checkout@",
            "uses: actions/setup-dotnet@",
            "dotnet-version: \"10.0.x\"",
            "run: dotnet restore\n",
            "run: dotnet build --no-restore -warnaserror\n",
            "run: dotnet test --no-build\n",
        ];

        int[] indexes = [.. steps.Select(step => text.IndexOf(step, StringComparison.Ordinal))];

        Assert.All(indexes, index => Assert.True(index >= 0, "A step is missing from the workflow."));
        Assert.Equal(indexes.Order(), indexes);
        // Nothing else runs: exactly these two actions and these three commands.
        Assert.Equal(2, Regex.Matches(text, @"^\s*(- )?uses:", RegexOptions.Multiline).Count);
        Assert.Equal(3, Regex.Matches(text, @"^\s*(- )?run:", RegexOptions.Multiline).Count);
    }

    // AC-4
    [Fact]
    public void Workflow_HasNoNodeSetup_AndNoContinueOnError()
    {
        string text = Workflow();

        Assert.DoesNotContain("setup-node", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("npm", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("node", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("continue-on-error", text, StringComparison.OrdinalIgnoreCase);
    }

    // AC-5
    [Fact]
    public void Permissions_AreContentsReadOnly_AndCheckoutDropsCredentials()
    {
        string text = Workflow();

        // Top level, one entry, and the next line is not indented (so nothing else is granted).
        Assert.Matches(new Regex(@"^permissions:\n  contents: read\n(?![ \t]+\S)", RegexOptions.Multiline), text);
        Assert.Single(Regex.Matches(text, @"permissions:"));
        // No permission value grants write access (a comment may still use the word).
        Assert.DoesNotMatch(new Regex(@"^[^#\n]*:\s*write\b", RegexOptions.Multiline), text);

        int checkout = text.IndexOf("uses: actions/checkout@", StringComparison.Ordinal);
        int setup = text.IndexOf("uses: actions/setup-dotnet@", StringComparison.Ordinal);
        Assert.True(checkout >= 0 && setup > checkout);
        Assert.Matches(
            new Regex(@"^\s+persist-credentials: false$", RegexOptions.Multiline),
            text[checkout..setup]);
    }

    // AC-6
    [Fact]
    public void Actions_ArePinnedToFullShas_AndRunValuesAreFixedText()
    {
        string text = Workflow();
        string[] uses = [.. text.Split('\n').Where(line => line.TrimStart().TrimStart('-', ' ').StartsWith("uses:", StringComparison.Ordinal))];
        string[] runs = [.. text.Split('\n').Where(line => line.TrimStart().TrimStart('-', ' ').StartsWith("run:", StringComparison.Ordinal))];

        Assert.NotEmpty(uses);
        Assert.All(uses, line => Assert.Matches(@"uses: [\w.-]+/[\w./-]+@[0-9a-f]{40} # v\d[\w.-]*$", line));
        Assert.DoesNotContain("secrets.", text);
        Assert.NotEmpty(runs);
        // Each run value is one line of fixed text: no expression, and no block scalar that could hide one.
        Assert.All(runs, line => Assert.Matches(@"run: dotnet [a-z -]+$", line));
        Assert.All(runs, line => Assert.DoesNotContain("${{", line));
    }

    // AC-7
    [Fact]
    public void Job_RunsOnUbuntu_WithATimeoutWithinTheFactoryWait()
    {
        string text = Workflow();

        Match timeout = Assert.Single(Regex.Matches(text, @"^    timeout-minutes: (\d+)$", RegexOptions.Multiline));
        Assert.InRange(int.Parse(timeout.Groups[1].Value), 1, 8);
        Assert.Single(Regex.Matches(text, @"^    runs-on: ubuntu-latest$", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(text, @"runs-on:"));
    }

    private static string Workflow() =>
        File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml")).ReplaceLineEndings("\n");

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Context.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail("No folder containing Context.slnx found above " + AppContext.BaseDirectory);
        return "";
    }
}
