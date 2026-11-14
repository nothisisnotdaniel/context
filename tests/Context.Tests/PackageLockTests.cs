using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Context.Tests;

/// <summary>
/// Checks the locked package restore: <c>Directory.Build.props</c>, the committed <c>packages.lock.json</c> next to
/// every project (read as JSON against the csproj read as XML), the runtime identifiers that <c>release.yml</c>
/// publishes, and that neither workflow switches locked mode off.
/// </summary>
public sealed partial class PackageLockTests
{
    private const string Tfm = "net10.0";

    public static TheoryData<string> Projects() => [.. ProjectFiles()];

    // AC-1
    [Fact]
    public void DirectoryBuildProps_UsesLockFiles_AndLocksOnCi()
    {
        XDocument props = XDocument.Load(Path.Combine(RepoRoot(), "Directory.Build.props"));

        XElement[] withLockFile = [.. props.Descendants("RestorePackagesWithLockFile")];
        XElement withLockFileElement = Assert.Single(withLockFile);
        Assert.Equal("true", withLockFileElement.Value.Trim());
        Assert.Null(withLockFileElement.Attribute("Condition"));
        Assert.Null(withLockFileElement.Parent!.Attribute("Condition"));

        XElement[] lockedMode = [.. props.Descendants("RestoreLockedMode")];
        XElement lockedModeElement = Assert.Single(lockedMode);
        Assert.Equal("true", lockedModeElement.Value.Trim());
        Assert.Equal("'$(CI)' == 'true'", lockedModeElement.Attribute("Condition")?.Value);
        Assert.Null(lockedModeElement.Parent!.Attribute("Condition"));
    }

    // AC-2
    [Fact]
    public void EveryProject_IsFound()
    {
        string[] names = [.. ProjectFiles().Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

        Assert.Equal(
            ["Context.Core.csproj", "Context.Desktop.Tests.csproj", "Context.Desktop.csproj", "Context.Tests.csproj", "Context.csproj"],
            names);
    }

    // AC-2
    [Theory]
    [MemberData(nameof(Projects))]
    public void EveryProject_HasALockFile_WithNet10Dependencies(string project)
    {
        string lockPath = LockPath(project);
        Assert.True(File.Exists(lockPath), "missing " + lockPath);

        using JsonDocument lockFile = JsonDocument.Parse(File.ReadAllText(lockPath));
        int version = lockFile.RootElement.GetProperty("version").GetInt32();
        Assert.True(version is 1 or 2, "lock file version " + version);
        Assert.Equal(JsonValueKind.Object, lockFile.RootElement.GetProperty("dependencies").GetProperty(Tfm).ValueKind);
    }

    // AC-3
    [Theory]
    [MemberData(nameof(Projects))]
    public void EveryPackageReference_IsADirectLockEntry_AndNothingElseIs(string project)
    {
        Dictionary<string, string> references = XDocument.Load(project).Descendants("PackageReference")
            .ToDictionary(element => element.Attribute("Include")!.Value, element => element.Attribute("Version")!.Value);

        using JsonDocument lockFile = JsonDocument.Parse(File.ReadAllText(LockPath(project)));
        JsonElement net10 = lockFile.RootElement.GetProperty("dependencies").GetProperty(Tfm);

        foreach ((string name, string version) in references)
        {
            Assert.True(net10.TryGetProperty(name, out JsonElement entry), name + " missing in " + LockPath(project));
            Assert.Equal("Direct", entry.GetProperty("type").GetString());
            Assert.Equal("[" + version + ", )", entry.GetProperty("requested").GetString());
            Assert.Equal(version, entry.GetProperty("resolved").GetString());
        }

        string[] direct = [.. net10.EnumerateObject()
            .Where(entry => entry.Value.GetProperty("type").GetString() == "Direct")
            .Select(entry => entry.Name)
            .Order(StringComparer.Ordinal)];
        Assert.Equal([.. references.Keys.Order(StringComparer.Ordinal)], direct);
    }

    // AC-4
    [Theory]
    [MemberData(nameof(Projects))]
    public void EveryPackageEntry_HasAContentHash(string project)
    {
        using JsonDocument lockFile = JsonDocument.Parse(File.ReadAllText(LockPath(project)));

        int packages = 0;
        foreach (JsonProperty target in lockFile.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (JsonProperty entry in target.Value.EnumerateObject())
            {
                string? type = entry.Value.GetProperty("type").GetString();
                if (type is "Direct" or "Transitive")
                {
                    packages++;
                    Assert.True(
                        entry.Value.TryGetProperty("contentHash", out JsonElement hash) && !string.IsNullOrEmpty(hash.GetString()),
                        target.Name + "/" + entry.Name + " has no contentHash in " + LockPath(project));
                }
            }
        }

        // Context.csproj has no package of its own but still gets the SDK's and Core's packages.
        Assert.True(packages > 0, "no package entries in " + LockPath(project));
    }

    // AC-5
    [Fact]
    public void ReleaseYml_PublishesTheExpectedRids()
    {
        Assert.Equal(["osx-arm64", "osx-x64", "win-x64"], PublishedRids());
    }

    // AC-5
    [Theory]
    [InlineData("src/Context.Core/Context.Core.csproj")]
    [InlineData("src/Context.Desktop/Context.Desktop.csproj")]
    public void PublishedProjects_DeclareTheReleaseRids_AndLockThem(string relativePath)
    {
        string project = Path.Combine(RepoRoot(), relativePath);
        string[] published = PublishedRids();

        XElement element = Assert.Single(XDocument.Load(project).Descendants("RuntimeIdentifiers"));
        string[] declared = [.. element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Order(StringComparer.Ordinal)];
        Assert.Equal(published, declared);

        using JsonDocument lockFile = JsonDocument.Parse(File.ReadAllText(LockPath(project)));
        JsonElement dependencies = lockFile.RootElement.GetProperty("dependencies");
        foreach (string rid in published)
        {
            Assert.True(dependencies.TryGetProperty(Tfm + "/" + rid, out _), Tfm + "/" + rid + " missing in " + LockPath(project));
        }
    }

    // AC-6
    [Theory]
    [InlineData("ci.yml")]
    [InlineData("release.yml")]
    public void Workflow_DoesNotBypassLockedMode(string file)
    {
        string text = Workflow(file);

        foreach (string forbidden in (string[])["--force-evaluate", "RestoreLockedMode", "RestorePackagesWithLockFile", "--no-dependencies", "NuGetLockFilePath"])
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(CiEnvKey(), text);
    }

    // AC-7
    [Theory]
    [MemberData(nameof(Projects))]
    public void LockFile_IsNotGitIgnored(string project)
    {
        string relative = Path.GetRelativePath(RepoRoot(), LockPath(project));
        ProcessStartInfo startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("check-ignore");
        startInfo.ArgumentList.Add("-q");
        startInfo.ArgumentList.Add(relative);

        using Process process = Process.Start(startInfo)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        // Exit 1 means "not ignored"; 0 would mean ignored, 128 an error.
        Assert.True(process.ExitCode == 1, "git check-ignore " + relative + " exit " + process.ExitCode + ": " + error);
    }

    /// <summary>
    /// The RIDs of every <c>dotnet publish -r</c> in <c>release.yml</c>, sorted; <c>-r "$RID"</c> takes the list of the
    /// nearest <c>for RID in ...; do</c> above it.
    /// </summary>
    private static string[] PublishedRids()
    {
        string[] lines = Workflow("release.yml").Split('\n');
        SortedSet<string> rids = new(StringComparer.Ordinal);
        string[] loop = [];

        foreach (string line in lines)
        {
            Match loopMatch = RidLoop().Match(line);
            if (loopMatch.Success)
            {
                loop = loopMatch.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            }

            if (!line.Contains("dotnet publish", StringComparison.Ordinal))
            {
                continue;
            }

            Match rid = PublishRid().Match(line);
            Assert.True(rid.Success, "dotnet publish without -r: " + line.Trim());
            string value = rid.Groups[1].Value.Trim('"');
            if (value == "$RID")
            {
                Assert.NotEmpty(loop);
                rids.UnionWith(loop);
            }
            else
            {
                rids.Add(value);
            }
        }

        return [.. rids];
    }

    private static IEnumerable<string> ProjectFiles() =>
        ((string[])["src", "tests"])
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(RepoRoot(), folder), "*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "node_modules"))
            .Order(StringComparer.Ordinal);

    private static string LockPath(string project) => Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json");

    private static string Workflow(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", file)).ReplaceLineEndings("\n");

    [GeneratedRegex(@"^\s*for RID in ([^;]+); do")]
    private static partial Regex RidLoop();

    [GeneratedRegex(@"\s-r\s+(""[^""]+""|\S+)")]
    private static partial Regex PublishRid();

    [GeneratedRegex(@"(?m)^\s*CI\s*:")]
    private static partial Regex CiEnvKey();

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
