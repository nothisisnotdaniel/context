using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Context.Tests;

/// <summary>
/// Test-only checker for the root <c>DEPENDENCIES.md</c>: reads it against every <c>.csproj</c> and
/// <c>packages.lock.json</c> under <c>src/</c> and <c>tests/</c>, the editor's <c>package.json</c> and
/// <c>THIRD-PARTY-LICENSES.txt</c>, <c>release.yml</c> and <c>README.md</c>. <see cref="Check"/> returns one string per
/// problem, each starting with its rule tag (for example <c>[lock]</c>), and an empty list when the file is in step.
/// </summary>
internal static partial class DependencyList
{
    public const string Format = "[format]";
    public const string Csproj = "[csproj]";
    public const string Lock = "[lock]";
    public const string PackageJson = "[package.json]";
    public const string Licences = "[licences]";
    public const string Tools = "[tools]";
    public const string Readme = "[readme]";

    public const string NuGetSection = "NuGet packages";
    public const string NpmSection = "npm packages";
    public const string ToolsSection = "Tools and platform";
    public const string VendoredSection = "Vendored assets";

    public const string Header = "| Package | Version | Licence | Use | Licence text |";

    private const string Dashes = "------------------------------------------------------------";

    private static readonly string[] Uses = ["runtime", "build", "test"];

    private static readonly string[] TableSections = [NuGetSection, NpmSection, ToolsSection];

    public const string LicencesPath = "src/Context.Desktop/editor/THIRD-PARTY-LICENSES.txt";
    public const string PackageJsonPath = "src/Context.Desktop/editor/package.json";
    public const string ReleasePath = ".github/workflows/release.yml";

    public sealed record Row(string Name, string Version, string Licence, string Use, string Link);

    public static List<string> Check(string root)
    {
        List<string> problems = [];
        string dependencies = Path.Combine(root, "DEPENDENCIES.md");
        if (!File.Exists(dependencies))
        {
            problems.Add(Format + " DEPENDENCIES.md is missing");
            return problems;
        }

        Dictionary<string, List<Row>> sections = ReadSections(Read(dependencies), problems);
        List<Row> nuget = sections.GetValueOrDefault(NuGetSection) ?? [];
        List<Row> npm = sections.GetValueOrDefault(NpmSection) ?? [];
        List<Row> tools = sections.GetValueOrDefault(ToolsSection) ?? [];

        CheckCsprojs(root, nuget, problems);
        CheckLockFiles(root, nuget, problems);
        HashSet<(string Name, string Version)> packageJson = CheckPackageJson(root, npm, problems);
        CheckLicences(root, npm, packageJson, problems);
        CheckTools(root, tools, problems);
        CheckReadme(root, problems);
        return problems;
    }

    private static Dictionary<string, List<Row>> ReadSections(string text, List<string> problems)
    {
        Dictionary<string, List<string>> lines = new(StringComparer.Ordinal);
        List<string>? current = null;
        foreach (string line in text.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                string name = line[3..].Trim();
                if (lines.ContainsKey(name))
                {
                    problems.Add(Format + " section '## " + name + "' appears twice");
                    current = null;
                    continue;
                }

                current = [];
                lines[name] = current;
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                current = null;
            }
            else
            {
                current?.Add(line);
            }
        }

        foreach (string name in (string[])[.. TableSections, VendoredSection])
        {
            if (!lines.ContainsKey(name))
            {
                problems.Add(Format + " section '## " + name + "' is missing");
            }
        }

        Dictionary<string, List<Row>> sections = new(StringComparer.Ordinal);
        foreach (string name in TableSections)
        {
            if (lines.TryGetValue(name, out List<string>? body))
            {
                sections[name] = ReadTable(name, body, problems);
            }
        }

        if (lines.TryGetValue(VendoredSection, out List<string>? vendored) && vendored.Any(line => line.StartsWith('|')))
        {
            problems.Add(Format + " section '## " + VendoredSection + "' holds a table");
        }

        return sections;
    }

    private static List<Row> ReadTable(string section, List<string> body, List<string> problems)
    {
        List<Row> rows = [];
        string[] tableLines = [.. body.Where(line => line.StartsWith('|'))];
        int headers = tableLines.Count(line => line == Header);
        if (headers != 1 || tableLines.Length < 2 || tableLines[0] != Header || !SeparatorRow().IsMatch(tableLines[1]))
        {
            problems.Add(Format + " '## " + section + "' needs exactly one table with the header " + Header);
            return rows;
        }

        // The table must be one block: no other text between its lines.
        int first = body.IndexOf(Header);
        if (first + tableLines.Length > body.Count || !body.Skip(first).Take(tableLines.Length).SequenceEqual(tableLines))
        {
            problems.Add(Format + " '## " + section + "' needs exactly one table with the header " + Header);
        }

        HashSet<(string, string)> seen = [];
        foreach (string line in tableLines.Skip(2))
        {
            string[] cells = line.Split('|');
            if (cells.Length != 7 || cells[0].Length != 0 || cells[6].Trim().Length != 0)
            {
                problems.Add(Format + " '## " + section + "' row has not five cells: " + line);
                continue;
            }

            Row row = new(cells[1].Trim(), cells[2].Trim(), cells[3].Trim(), cells[4].Trim(), cells[5].Trim());
            string label = "'## " + section + "' row " + row.Name + " " + row.Version;
            if (row.Name.Length == 0 || row.Version.Length == 0 || row.Licence.Length == 0)
            {
                problems.Add(Format + " " + label + " has an empty name, version or licence");
            }

            if (!Uses.Contains(row.Use, StringComparer.Ordinal))
            {
                problems.Add(Format + " " + label + " has Use '" + row.Use + "', not runtime, build or test");
            }

            Match link = LinkCell().Match(row.Link);
            if (!link.Success || !link.Groups[1].Value.StartsWith("https://", StringComparison.Ordinal))
            {
                problems.Add(Format + " " + label + " has a licence link that is not [text](https://...): " + row.Link);
            }

            if (!seen.Add((Key(section, row.Name), row.Version)))
            {
                problems.Add(Format + " " + label + " appears twice");
            }

            rows.Add(row);
        }

        return rows;
    }

    private static void CheckCsprojs(string root, List<Row> nuget, List<string> problems)
    {
        foreach (string project in Files(root, "*.csproj"))
        {
            string relative = Relative(root, project);
            foreach (XElement reference in XDocument.Parse(Read(project)).Descendants("PackageReference"))
            {
                string name = reference.Attribute("Include")?.Value ?? "";
                string version = reference.Attribute("Version")?.Value ?? "";
                if (Find(nuget, NuGetSection, name, version) is null)
                {
                    problems.Add(Csproj + " " + name + " " + version + " (" + relative + ") has no NuGet row");
                }
            }
        }
    }

    private static void CheckLockFiles(string root, List<Row> nuget, List<string> problems)
    {
        // name + version -> found in a src/ lock file (true) or only in tests/ lock files (false).
        Dictionary<(string Name, string Version), bool> locked = new(new NuGetKeyComparer());
        foreach (string lockFile in Files(root, "packages.lock.json"))
        {
            bool inSrc = Relative(root, lockFile).StartsWith("src/", StringComparison.Ordinal);
            using JsonDocument document = JsonDocument.Parse(Read(lockFile));
            foreach (JsonProperty target in document.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                foreach (JsonProperty entry in target.Value.EnumerateObject())
                {
                    if (entry.Value.TryGetProperty("type", out JsonElement type) && type.GetString() == "Project")
                    {
                        continue;
                    }

                    string version = entry.Value.TryGetProperty("resolved", out JsonElement resolved) ? resolved.GetString() ?? "" : "";
                    (string, string) key = (entry.Name, version);
                    locked[key] = inSrc || locked.GetValueOrDefault(key);
                }
            }
        }

        foreach (((string name, string version), bool inSrc) in locked.OrderBy(pair => pair.Key.Name, StringComparer.Ordinal))
        {
            Row? row = Find(nuget, NuGetSection, name, version);
            if (row is null)
            {
                problems.Add(Lock + " " + name + " " + version + " is in a packages.lock.json but has no NuGet row");
            }
            else if (inSrc && row.Use is not ("runtime" or "build"))
            {
                problems.Add(Lock + " " + name + " " + version + " is in a src/ lock file, so its Use is runtime or build, not " + row.Use);
            }
            else if (!inSrc && row.Use != "test")
            {
                problems.Add(Lock + " " + name + " " + version + " is only in tests/ lock files, so its Use is test, not " + row.Use);
            }
        }

        foreach (Row row in nuget.Where(row => !locked.ContainsKey((row.Name, row.Version))))
        {
            problems.Add(Lock + " NuGet row " + row.Name + " " + row.Version + " is in no packages.lock.json (stale)");
        }
    }

    private static HashSet<(string Name, string Version)> CheckPackageJson(string root, List<Row> npm, List<string> problems)
    {
        HashSet<(string, string)> entries = [];
        string path = Path.Combine(root, PackageJsonPath);
        if (!File.Exists(path))
        {
            problems.Add(PackageJson + " " + PackageJsonPath + " is missing");
            return entries;
        }

        HashSet<(string, string)> bundled = [.. Headings(root, []).Select(heading => (heading.Name, heading.Version))];
        using JsonDocument document = JsonDocument.Parse(Read(path));
        foreach (string group in (string[])["dependencies", "devDependencies"])
        {
            if (!document.RootElement.TryGetProperty(group, out JsonElement packages))
            {
                continue;
            }

            foreach (JsonProperty package in packages.EnumerateObject())
            {
                string version = package.Value.GetString() ?? "";
                entries.Add((package.Name, version));
                Row? row = Find(npm, NpmSection, package.Name, version);
                if (row is null)
                {
                    problems.Add(PackageJson + " " + package.Name + " " + version + " (" + group + ") has no npm row with that version");
                }
                else if (!bundled.Contains((package.Name, version)) && row.Use != "build")
                {
                    problems.Add(PackageJson + " " + package.Name + " " + version + " is not in THIRD-PARTY-LICENSES.txt, so its Use is build, not " + row.Use);
                }
            }
        }

        return entries;
    }

    private static void CheckLicences(string root, List<Row> npm, HashSet<(string Name, string Version)> packageJson, List<string> problems)
    {
        List<(string Name, string Version, string Licence)> headings = Headings(root, problems);
        HashSet<(string, string)> bundled = [];
        foreach ((string name, string version, string licence) in headings)
        {
            bundled.Add((name, version));
            Row? row = Find(npm, NpmSection, name, version);
            if (row is null)
            {
                problems.Add(Licences + " " + name + " " + version + " is in THIRD-PARTY-LICENSES.txt but has no npm row with that version");
                continue;
            }

            if (row.Use != "runtime")
            {
                problems.Add(Licences + " " + name + " " + version + " is bundled, so its Use is runtime, not " + row.Use);
            }

            if (row.Licence != licence)
            {
                problems.Add(Licences + " " + name + " " + version + " has licence '" + row.Licence + "', THIRD-PARTY-LICENSES.txt says '" + licence + "'");
            }
        }

        foreach (Row row in npm)
        {
            bool isBundled = bundled.Contains((row.Name, row.Version));
            if (row.Use == "runtime" && !isBundled)
            {
                problems.Add(Licences + " npm row " + row.Name + " " + row.Version + " is runtime but has no heading in THIRD-PARTY-LICENSES.txt");
            }
            else if (!isBundled && !packageJson.Contains((row.Name, row.Version)))
            {
                problems.Add(Licences + " npm row " + row.Name + " " + row.Version + " is neither in package.json nor in THIRD-PARTY-LICENSES.txt (stale)");
            }
        }
    }

    /// <summary>
    /// The package headings of <c>THIRD-PARTY-LICENSES.txt</c>: each line between two 60-dash lines, in the form
    /// <c>name version (licence)</c> with an optional <c> (bundled copy under x)</c>. Works for both parts of the file.
    /// </summary>
    private static List<(string Name, string Version, string Licence)> Headings(string root, List<string> problems)
    {
        List<(string, string, string)> headings = [];
        string path = Path.Combine(root, LicencesPath);
        if (!File.Exists(path))
        {
            problems.Add(Licences + " " + LicencesPath + " is missing");
            return headings;
        }

        string[] lines = Read(path).Split('\n');
        for (int i = 1; i < lines.Length - 1; i++)
        {
            if (lines[i - 1].TrimEnd() != Dashes || lines[i + 1].TrimEnd() != Dashes)
            {
                continue;
            }

            Match heading = HeadingLine().Match(lines[i].TrimEnd());
            if (heading.Success)
            {
                headings.Add((heading.Groups[1].Value, heading.Groups[2].Value, heading.Groups[3].Value));
            }
            else
            {
                problems.Add(Licences + " heading '" + lines[i].TrimEnd() + "' is not '<name> <version> (<licence>)'");
            }
        }

        return headings;
    }

    private static void CheckTools(string root, List<Row> tools, List<string> problems)
    {
        Row? runtime = tools.FirstOrDefault(row => row.Name == ".NET runtime");
        if (runtime is null)
        {
            problems.Add(Tools + " '## " + ToolsSection + "' has no .NET runtime row (the release publishes --self-contained)");
        }
        else if (runtime.Use != "runtime" || runtime.Licence != "MIT")
        {
            problems.Add(Tools + " .NET runtime " + runtime.Version + " must be runtime and MIT, not " + runtime.Use + " and " + runtime.Licence);
        }

        string release = Path.Combine(root, ReleasePath);
        Match install = File.Exists(release) ? VpkInstall().Match(Read(release)) : Match.Empty;
        if (!install.Success)
        {
            problems.Add(Tools + " " + ReleasePath + " has no 'dotnet tool install --global vpk --version <v>'");
            return;
        }

        string version = install.Groups[1].Value;
        Row? vpk = tools.FirstOrDefault(row => row.Name == "vpk");
        if (vpk is null || vpk.Version != version)
        {
            problems.Add(Tools + " vpk " + version + " (release.yml) has no row with that version in '## " + ToolsSection + "'");
        }
        else if (vpk.Use != "build")
        {
            problems.Add(Tools + " vpk " + version + " must be build, not " + vpk.Use);
        }
    }

    private static void CheckReadme(string root, List<string> problems)
    {
        string readme = Path.Combine(root, "README.md");
        if (!File.Exists(readme) || !ReadmeLink().IsMatch(Read(readme)))
        {
            problems.Add(Readme + " README.md has no Markdown link to DEPENDENCIES.md");
        }
    }

    private static Row? Find(List<Row> rows, string section, string name, string version) =>
        rows.FirstOrDefault(row => Key(section, row.Name) == Key(section, name) && row.Version == version);

    // NuGet ids ignore case; npm names don't.
    private static string Key(string section, string name) => section == NuGetSection ? name.ToLowerInvariant() : name;

    private static IEnumerable<string> Files(string root, string pattern) =>
        ((string[])["src", "tests"])
            .Select(folder => Path.Combine(root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories))
            .Where(path => !Relative(root, path).Split('/').Any(part => part is "bin" or "obj" or "node_modules"))
            .Order(StringComparer.Ordinal);

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string Read(string path) => File.ReadAllText(path).ReplaceLineEndings("\n");

    private sealed class NuGetKeyComparer : IEqualityComparer<(string Name, string Version)>
    {
        public bool Equals((string Name, string Version) x, (string Name, string Version) y) =>
            string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Version, y.Version, StringComparison.Ordinal);

        public int GetHashCode((string Name, string Version) key) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Name), StringComparer.Ordinal.GetHashCode(key.Version));
    }

    [GeneratedRegex(@"^\|(\s*:?-+:?\s*\|){5}$")]
    private static partial Regex SeparatorRow();

    [GeneratedRegex(@"^\[[^\]]+\]\(([^()\s]+)\)$")]
    private static partial Regex LinkCell();

    [GeneratedRegex(@"^(\S+) (\S+) \(([^()]+)\)(?: \(bundled copy under [^()]+\))?$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"dotnet tool install --global vpk --version (\S+)")]
    private static partial Regex VpkInstall();

    [GeneratedRegex(@"\]\((?:\./)?DEPENDENCIES\.md(?:#[^)]*)?\)")]
    private static partial Regex ReadmeLink();
}
