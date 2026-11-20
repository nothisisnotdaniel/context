namespace Context.Tests;

/// <summary>
/// Keeps the root <c>DEPENDENCIES.md</c> in step with the project files, lock files, <c>package.json</c>,
/// <c>THIRD-PARTY-LICENSES.txt</c>, <c>release.yml</c> and <c>README.md</c> through <see cref="DependencyList.Check"/>.
/// The edit cases work on a copy of those files in their own temp folder.
/// </summary>
public sealed class DependencyListTests : IDisposable
{
    private static readonly string[] Inputs =
    [
        "DEPENDENCIES.md",
        "README.md",
        "Context.slnx",
        "src/Context.Core/Context.Core.csproj",
        "src/Context.Core/packages.lock.json",
        "src/Context/Context.csproj",
        "src/Context/packages.lock.json",
        "src/Context.Desktop/Context.Desktop.csproj",
        "src/Context.Desktop/packages.lock.json",
        "tests/Context.Tests/Context.Tests.csproj",
        "tests/Context.Tests/packages.lock.json",
        "tests/Context.Desktop.Tests/Context.Desktop.Tests.csproj",
        "tests/Context.Desktop.Tests/packages.lock.json",
        DependencyList.PackageJsonPath,
        DependencyList.LicencesPath,
        DependencyList.ReleasePath,
    ];

    private readonly string temp = Path.Combine(Path.GetTempPath(), "context-deps-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    // AC-1
    [Fact]
    public void DependenciesMd_HasTheSectionsTablesAndValidRows() => AssertNoProblems(DependencyList.Format);

    // AC-2
    [Fact]
    public void EveryPackageReference_HasANuGetRow() => AssertNoProblems(DependencyList.Csproj);

    // AC-3
    [Fact]
    public void EveryLockedPackage_HasANuGetRow_WithTheRightUse_AndNoRowIsStale() => AssertNoProblems(DependencyList.Lock);

    // AC-4
    [Fact]
    public void EveryPackageJsonEntry_HasAnNpmRow() => AssertNoProblems(DependencyList.PackageJson);

    // AC-5
    [Fact]
    public void EveryBundledPackage_HasARuntimeNpmRow_WithItsLicence() => AssertNoProblems(DependencyList.Licences);

    // AC-6
    [Fact]
    public void ToolsSection_HasTheRuntime_AndTheReleaseVpkVersion() => AssertNoProblems(DependencyList.Tools);

    // AC-7
    [Fact]
    public void Readme_LinksDependenciesMd() => AssertNoProblems(DependencyList.Readme);

    // AC-8
    [Fact]
    public void Check_OnTheRealRepo_FindsNoProblems() => Assert.Empty(DependencyList.Check(RepoRoot()));

    // AC-8
    [Fact]
    public void Check_OnAnUneditedCopy_FindsNoProblems()
    {
        CopyInputs();

        Assert.Empty(DependencyList.Check(temp));
    }

    // AC-8: each case edits one input of a temp copy; a problem must name the package and the version.
    [Theory]
    [InlineData("csproj-added", "Extra.Package", "1.2.3")]
    [InlineData("lock-added", "Lock.Only", "4.5.6")]
    [InlineData("lock-added-tests", "Test.Only", "7.8.9")]
    [InlineData("package-json-added", "left-pad", "1.3.0")]
    [InlineData("licences-added", "new-lib", "2.0.0")]
    [InlineData("stale-nuget-row", "Stale.Package", "9.9.9")]
    [InlineData("stale-npm-row", "ghost-lib", "0.0.1")]
    [InlineData("nuget-version-mismatch", "Velopack", "1.2.161")]
    [InlineData("npm-version-mismatch", "mermaid", "12.3.1")]
    [InlineData("licence-version-mismatch", "d3", "7.9.3")]
    [InlineData("licence-text-mismatch", "d3", "7.9.2")]
    [InlineData("vpk-version-mismatch", "vpk", "1.2.999")]
    [InlineData("use-test-package-as-runtime", "xunit", "2.9.3")]
    [InlineData("use-src-package-as-test", "Photino.Blazor", "4.0.13")]
    [InlineData("use-bundled-as-build", "lowlight", "3.3.0")]
    [InlineData("use-esbuild-as-runtime", "esbuild", "0.28.2")]
    [InlineData("use-not-a-use", "Jint", "4.17.0")]
    [InlineData("http-link", "Jint", "4.17.0")]
    [InlineData("duplicate-row", "bunit", "2.13.3")]
    public void Check_OnAnEditedCopy_NamesThePackageAndVersion(string edit, string name, string version)
    {
        CopyInputs();
        Apply(edit);

        List<string> problems = DependencyList.Check(temp);

        Assert.True(
            problems.Any(problem => problem.Contains(name + " " + version, StringComparison.Ordinal)),
            "no problem names " + name + " " + version + ":\n" + string.Join("\n", problems));
    }

    // AC-8
    [Theory]
    [InlineData("readme-link-removed", DependencyList.Readme)]
    [InlineData("runtime-row-removed", DependencyList.Tools)]
    [InlineData("section-removed", DependencyList.Format)]
    [InlineData("header-changed", DependencyList.Format)]
    public void Check_OnAnEditedCopy_ReportsTheBrokenRule(string edit, string rule)
    {
        CopyInputs();
        Apply(edit);

        List<string> problems = DependencyList.Check(temp);

        Assert.True(problems.Any(problem => problem.StartsWith(rule, StringComparison.Ordinal)), string.Join("\n", problems));
    }

    private void Apply(string edit)
    {
        switch (edit)
        {
            case "csproj-added":
                Replace("src/Context.Core/Context.Core.csproj", "<PackageReference ",
                    "<PackageReference Include=\"Extra.Package\" Version=\"1.2.3\" />\n    <PackageReference ");
                break;
            case "lock-added":
                Replace("src/Context.Core/packages.lock.json", "\"net10.0\": {",
                    "\"net10.0\": {\n      \"Lock.Only\": { \"type\": \"Transitive\", \"resolved\": \"4.5.6\", \"contentHash\": \"x\" },");
                break;
            case "lock-added-tests":
                Replace("tests/Context.Tests/packages.lock.json", "\"net10.0\": {",
                    "\"net10.0\": {\n      \"Test.Only\": { \"type\": \"Transitive\", \"resolved\": \"7.8.9\", \"contentHash\": \"x\" },");
                break;
            case "package-json-added":
                Replace(DependencyList.PackageJsonPath, "\"devDependencies\": {", "\"devDependencies\": {\n    \"left-pad\": \"1.3.0\",");
                break;
            case "licences-added":
                Replace(DependencyList.LicencesPath, "------------------------------------------------------------\nprosemirror-commands",
                    "------------------------------------------------------------\nnew-lib 2.0.0 (MIT)\n"
                    + "------------------------------------------------------------\n\nMIT text\n\n"
                    + "------------------------------------------------------------\nprosemirror-commands");
                break;
            case "stale-nuget-row":
                Replace("DEPENDENCIES.md", "| xunit.runner.visualstudio |",
                    "| Stale.Package | 9.9.9 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Stale.Package/9.9.9/License) |\n| xunit.runner.visualstudio |");
                break;
            case "stale-npm-row":
                Replace("DEPENDENCIES.md", "| ts-dedent |",
                    "| ghost-lib | 0.0.1 | MIT | runtime | [LICENSE](https://unpkg.com/ghost-lib@0.0.1/LICENSE) |\n| ts-dedent |");
                break;
            case "nuget-version-mismatch":
                Replace("DEPENDENCIES.md", "| Velopack | 1.2.161 |", "| Velopack | 1.2.160 |");
                break;
            case "npm-version-mismatch":
                Replace(DependencyList.PackageJsonPath, "\"mermaid\": \"12.3.0\"", "\"mermaid\": \"12.3.1\"");
                break;
            case "licence-version-mismatch":
                Replace(DependencyList.LicencesPath, "\nd3 7.9.2 (ISC)\n", "\nd3 7.9.3 (ISC)\n");
                break;
            case "licence-text-mismatch":
                Replace("DEPENDENCIES.md", "| d3 | 7.9.2 | ISC |", "| d3 | 7.9.2 | MIT |");
                break;
            case "vpk-version-mismatch":
                Replace(DependencyList.ReleasePath, "--global vpk --version 1.2.161", "--global vpk --version 1.2.999");
                break;
            case "use-test-package-as-runtime":
                Replace("DEPENDENCIES.md", "| xunit | 2.9.3 | Apache-2.0 | test |", "| xunit | 2.9.3 | Apache-2.0 | runtime |");
                break;
            case "use-src-package-as-test":
                Replace("DEPENDENCIES.md", "| Photino.Blazor | 4.0.13 | Apache-2.0 | runtime |", "| Photino.Blazor | 4.0.13 | Apache-2.0 | test |");
                break;
            case "use-bundled-as-build":
                Replace("DEPENDENCIES.md", "| lowlight | 3.3.0 | MIT | runtime |", "| lowlight | 3.3.0 | MIT | build |");
                break;
            case "use-esbuild-as-runtime":
                Replace("DEPENDENCIES.md", "| esbuild | 0.28.2 | MIT | build |", "| esbuild | 0.28.2 | MIT | runtime |");
                break;
            case "use-not-a-use":
                Replace("DEPENDENCIES.md", "| Jint | 4.17.0 | BSD-2-Clause | test |", "| Jint | 4.17.0 | BSD-2-Clause | dev |");
                break;
            case "http-link":
                Replace("DEPENDENCIES.md", "(https://www.nuget.org/packages/Jint/4.17.0/License)", "(http://www.nuget.org/packages/Jint/4.17.0/License)");
                break;
            case "duplicate-row":
                Replace("DEPENDENCIES.md", "| bunit | 2.13.3 | MIT | test | [nuget.org](https://www.nuget.org/packages/bunit/2.13.3/License) |\n",
                    "| bunit | 2.13.3 | MIT | test | [nuget.org](https://www.nuget.org/packages/bunit/2.13.3/License) |\n"
                    + "| bunit | 2.13.3 | MIT | test | [nuget.org](https://www.nuget.org/packages/bunit/2.13.3/License) |\n");
                break;
            case "readme-link-removed":
                Replace("README.md", "[DEPENDENCIES.md](DEPENDENCIES.md)", "DEPENDENCIES.md");
                break;
            case "runtime-row-removed":
                Replace("DEPENDENCIES.md", "| .NET runtime |", "| .NET runtimes |");
                break;
            case "section-removed":
                Replace("DEPENDENCIES.md", "## Vendored assets", "## Vendored files");
                break;
            case "header-changed":
                Replace("DEPENDENCIES.md", "## npm packages", "## npm packages\n\n| Package | Version | Licence |\n|--|--|--|");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(edit), edit, null);
        }
    }

    /// <summary>Replaces the first occurrence of <paramref name="old"/> in a copied file; the text must be there.</summary>
    private void Replace(string relative, string old, string replacement)
    {
        string path = Path.Combine(temp, relative);
        string text = File.ReadAllText(path).ReplaceLineEndings("\n");
        int index = text.IndexOf(old, StringComparison.Ordinal);
        Assert.True(index >= 0, "'" + old + "' not found in " + relative);
        File.WriteAllText(path, text[..index] + replacement + text[(index + old.Length)..]);
    }

    private void CopyInputs()
    {
        string root = RepoRoot();
        foreach (string relative in Inputs)
        {
            string target = Path.Combine(temp, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(root, relative), target);
        }
    }

    private static void AssertNoProblems(string rule)
    {
        string[] problems = [.. DependencyList.Check(RepoRoot()).Where(problem => problem.StartsWith(rule, StringComparison.Ordinal))];
        Assert.True(problems.Length == 0, string.Join("\n", problems));
    }

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
