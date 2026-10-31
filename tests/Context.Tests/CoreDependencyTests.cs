using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace Context.Tests;

// Architecture test: Context.Core must never depend on Spectre.Console (see CLAUDE.md).
// This is the one allowed exception to "one test class per production class".
public sealed class CoreDependencyTests
{
    private const string CoreProject = "src/Context.Core/Context.Core.csproj";
    private const string AppProject = "src/Context/Context.csproj";

    // AC-1
    [Fact]
    public void CoreAssembly_ReferencesNoSpectreAssembly()
    {
        Assembly core = typeof(NoteStore).Assembly;
        Assert.Equal("Context.Core", core.GetName().Name);

        List<string> found = SpectreAssemblyReferences(core);

        Assert.True(found.Count == 0, "Context.Core references Spectre assemblies: " + string.Join(", ", found));
    }

    // AC-2
    [Fact]
    public void CoreProject_HasNoSpectrePackageReference()
    {
        string root = RepoRoot();

        List<string> found = SpectrePackageReferences(root, CoreProject);

        Assert.True(found.Count == 0, "Spectre package references reach Context.Core: " + string.Join(", ", found));
    }

    // AC-3
    [Fact]
    public void CoreRestoredGraph_HasNoSpectrePackage()
    {
        string root = RepoRoot();

        List<string> found = SpectreAssetPackages(root, "src/Context.Core");

        Assert.True(found.Count == 0, "Context.Core's restored graph contains Spectre packages: "
            + string.Join(", ", found));
    }

    // AC-4
    [Fact]
    public void CoreSources_DoNotMentionSpectre()
    {
        string root = RepoRoot();

        List<string> found = SpectreSourceFiles(root, "src/Context.Core");

        Assert.True(found.Count == 0, "Files in Context.Core mention Spectre: " + string.Join(", ", found));
    }

    // AC-6 (control for AC-1)
    [Fact]
    public void AppAssembly_ReferencesSpectreAssembly()
    {
        Assembly app = typeof(App).Assembly;
        Assert.Equal("Context", app.GetName().Name);

        Assert.Contains("Spectre.Console", SpectreAssemblyReferences(app));
    }

    // AC-6 (control for AC-2)
    [Fact]
    public void AppProject_HasSpectrePackageReference()
    {
        string root = RepoRoot();

        Assert.Contains(AppProject + ": Spectre.Console", SpectrePackageReferences(root, AppProject));
    }

    // AC-6 (control for AC-3)
    [Fact]
    public void AppRestoredGraph_HasSpectrePackage()
    {
        string root = RepoRoot();

        Assert.Contains("Spectre.Console", SpectreAssetPackages(root, "src/Context"));
    }

    // AC-6 (control for AC-4)
    [Fact]
    public void AppSources_MentionSpectre()
    {
        string root = RepoRoot();

        Assert.Contains("src/Context/App.cs", SpectreSourceFiles(root, "src/Context"));
    }

    // AC-5: the helpers below return the offending items by name so a failure says what broke the rule.
    private static bool IsSpectre(string name)
    {
        return name.StartsWith("Spectre", StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Context.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail("No folder containing Context.slnx found above " + AppContext.BaseDirectory);
        return string.Empty;
    }

    private static string Relative(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static List<string> SpectreAssemblyReferences(Assembly assembly)
    {
        List<string> found = [];
        foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
        {
            string name = reference.Name ?? string.Empty;
            if (IsSpectre(name))
            {
                found.Add(name);
            }
        }

        return found;
    }

    // Returns "<csproj relative to root>: <package id>" for every Spectre PackageReference in the project
    // and, recursively, in every project it references.
    private static List<string> SpectrePackageReferences(string root, string projectRelativePath)
    {
        List<string> found = [];
        HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Stack<string> pending = new Stack<string>();
        pending.Push(Path.GetFullPath(Path.Combine(root, projectRelativePath)));

        while (pending.Count > 0)
        {
            string project = pending.Pop();
            if (!visited.Add(project))
            {
                continue;
            }

            string relative = Relative(root, project);
            if (!File.Exists(project))
            {
                Assert.Fail("Project file not found: " + relative);
            }

            XDocument document = XDocument.Load(project);
            foreach (XElement element in document.Descendants())
            {
                if (element.Name.LocalName == "PackageReference")
                {
                    foreach (string attribute in new[] { "Include", "Update" })
                    {
                        string? id = element.Attribute(attribute)?.Value;
                        if (id is not null && IsSpectre(id.Trim()))
                        {
                            found.Add(relative + ": " + id.Trim());
                        }
                    }
                }
                else if (element.Name.LocalName == "ProjectReference")
                {
                    string? include = element.Attribute("Include")?.Value;
                    if (!string.IsNullOrWhiteSpace(include))
                    {
                        string directory = Path.GetDirectoryName(project) ?? root;
                        string normalized = include.Trim().Replace('\\', Path.DirectorySeparatorChar);
                        pending.Push(Path.GetFullPath(Path.Combine(directory, normalized)));
                    }
                }
            }
        }

        return found;
    }

    // Returns the distinct Spectre package ids under "libraries" and every "targets" entry of the project's
    // obj/project.assets.json.
    private static List<string> SpectreAssetPackages(string root, string projectFolder)
    {
        string path = Path.Combine(root, projectFolder, "obj", "project.assets.json");
        if (!File.Exists(path))
        {
            Assert.Fail(Relative(root, path) + " is missing. Run `dotnet restore` first.");
        }

        List<string> found = [];
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement assets = document.RootElement;

        if (!assets.TryGetProperty("libraries", out JsonElement libraries)
            || libraries.ValueKind != JsonValueKind.Object)
        {
            Assert.Fail(Relative(root, path) + " has no \"libraries\" object.");
        }

        AddSpectreKeys(libraries, found);

        if (!assets.TryGetProperty("targets", out JsonElement targets) || targets.ValueKind != JsonValueKind.Object)
        {
            Assert.Fail(Relative(root, path) + " has no \"targets\" object.");
        }

        foreach (JsonProperty target in targets.EnumerateObject())
        {
            if (target.Value.ValueKind == JsonValueKind.Object)
            {
                AddSpectreKeys(target.Value, found);
            }
        }

        return found;
    }

    private static void AddSpectreKeys(JsonElement element, List<string> found)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            int slash = property.Name.IndexOf('/');
            string id = slash < 0 ? property.Name : property.Name.Substring(0, slash);
            if (IsSpectre(id) && !found.Contains(id))
            {
                found.Add(id);
            }
        }
    }

    // Returns the root-relative paths of *.cs, *.csproj, *.props and *.targets files under the folder (bin/ and
    // obj/ skipped) whose text contains "Spectre".
    private static List<string> SpectreSourceFiles(string root, string folder)
    {
        string projectDirectory = Path.Combine(root, folder);
        string[] extensions = [".cs", ".csproj", ".props", ".targets"];
        List<string> found = [];

        foreach (string file in Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories))
        {
            string[] segments = Path.GetRelativePath(projectDirectory, file).Split(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Length > 1 && (segments[0] == "bin" || segments[0] == "obj"))
            {
                continue;
            }

            string extension = Path.GetExtension(file);
            bool wanted = false;
            foreach (string candidate in extensions)
            {
                if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    wanted = true;
                }
            }

            if (wanted && File.ReadAllText(file).Contains("Spectre", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(Relative(root, file));
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }
}
