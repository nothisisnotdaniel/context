using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace Context.Tests;

// Architecture test: nothing in the repo depends on Spectre.Console, the removed console's terminal library
// (see CLAUDE.md).
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

    // 094 feedback: Context.Core is managed code only, with no P/Invoke into native libraries.
    [Fact]
    public void CoreSources_HaveNoPInvoke()
    {
        string root = RepoRoot();
        string core = Path.Combine(root, "src/Context.Core");
        string[] found = Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(core, f).StartsWith("bin", StringComparison.Ordinal)
                && !Path.GetRelativePath(core, f).StartsWith("obj", StringComparison.Ordinal))
            .Where(f =>
            {
                string text = File.ReadAllText(f);
                return text.Contains("DllImport", StringComparison.Ordinal)
                    || text.Contains("LibraryImport", StringComparison.Ordinal)
                    || text.Contains("extern ", StringComparison.Ordinal)
                    || text.Contains("\"libc\"", StringComparison.Ordinal);
            })
            .Select(f => Relative(root, f))
            .ToArray();

        Assert.True(File.Exists(Path.Combine(core, "BoundedFile.cs")), "BoundedFile.cs not found");
        Assert.True(found.Length == 0, "Context.Core has P/Invoke: " + string.Join(", ", found));
        Assert.DoesNotContain(
            typeof(NoteStore).Assembly.GetTypes().SelectMany(t => t.GetMethods(
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.DeclaredOnly)),
            m => (m.Attributes & System.Reflection.MethodAttributes.PinvokeImpl) != 0);
    }

    private static readonly string[] AllProjects =
    [
        "src/Context.Core",
        "src/Context",
        "src/Context.Desktop",
        "tests/Context.Tests",
        "tests/Context.Desktop.Tests",
    ];

    // 145 AC-3: the command-line tool and these tests reference no Spectre assembly either.
    [Fact]
    public void AppAndTestAssemblies_ReferenceNoSpectreAssembly()
    {
        Assembly app = typeof(Cli).Assembly;
        Assert.Equal("Context", app.GetName().Name);

        List<string> found = [.. SpectreAssemblyReferences(app), .. SpectreAssemblyReferences(typeof(CoreDependencyTests).Assembly)];

        Assert.True(found.Count == 0, "Spectre assemblies are referenced: " + string.Join(", ", found));
    }

    // 145 AC-3: no project file of the repo has a Spectre package.
    [Fact]
    public void EveryProject_HasNoSpectrePackageReference()
    {
        string root = RepoRoot();

        List<string> found = AllProjects
            .SelectMany(folder => SpectrePackageReferences(root, folder + "/" + Path.GetFileName(folder) + ".csproj"))
            .Distinct()
            .ToList();

        Assert.True(found.Count == 0, "Spectre package references: " + string.Join(", ", found));
    }

    // 145 AC-3: no restored graph of the repo has a Spectre package.
    [Fact]
    public void EveryRestoredGraph_HasNoSpectrePackage()
    {
        string root = RepoRoot();

        List<string> found = AllProjects
            .SelectMany(folder => SpectreAssetPackages(root, folder).Select(id => folder + ": " + id))
            .ToList();

        Assert.True(found.Count == 0, "Restored graphs contain Spectre packages: " + string.Join(", ", found));
    }

    // 145 AC-3: nothing under src/ mentions Spectre, and no test file imports it.
    [Fact]
    public void Sources_DoNotMentionSpectre()
    {
        string root = RepoRoot();
        string import = "using " + "Spectre";
        List<string> tests = Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(Path.GetRelativePath(root, f)))
            .Where(f => File.ReadAllText(f).Contains(import, StringComparison.OrdinalIgnoreCase))
            .Select(f => Relative(root, f))
            .ToList();

        List<string> found = SpectreSourceFiles(root, "src");

        Assert.True(File.Exists(Path.Combine(root, "src/Context/Cli.cs")), "Cli.cs not found");
        Assert.True(found.Count == 0, "Files under src/ mention Spectre: " + string.Join(", ", found));
        Assert.True(tests.Count == 0, "Test files import Spectre: " + string.Join(", ", tests));
    }

    // 145 AC-3 (control): the three scans do find a Spectre package and a Spectre mention when there is one.
    [Fact]
    public void TheScans_FindSpectre_WhenItIsThere()
    {
        string folder = Path.Combine(Path.GetTempPath(), "context-dependency-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "app", "obj"));
        Directory.CreateDirectory(Path.Combine(folder, "app", "sub"));
        try
        {
            File.WriteAllText(
                Path.Combine(folder, "app", "app.csproj"),
                "<Project><ItemGroup><PackageReference Include=\"Spectre.Console\" Version=\"1.0.0\" /></ItemGroup></Project>");
            File.WriteAllText(
                Path.Combine(folder, "app", "obj", "project.assets.json"),
                "{\"targets\":{\"net10.0\":{\"Spectre.Console/1.0.0\":{}}},\"libraries\":{\"Spectre.Console/1.0.0\":{}}}");
            File.WriteAllText(Path.Combine(folder, "app", "sub", "Page.razor"), "<p>@(typeof(SPECTRE.Console.Table))</p>");
            File.WriteAllText(Path.Combine(folder, "app", "Clean.cs"), "nothing here");

            Assert.Equal(["app/app.csproj: Spectre.Console"], SpectrePackageReferences(folder, "app/app.csproj"));
            Assert.Equal(["Spectre.Console"], SpectreAssetPackages(folder, "app"));
            Assert.Equal(["app/app.csproj", "app/sub/Page.razor"], SpectreSourceFiles(folder, "."));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static bool IsBuildOutput(string relativePath)
    {
        string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj") || segments.Contains("node_modules");
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

    // Returns the root-relative paths of *.cs, *.razor, *.csproj, *.props and *.targets files under the folder
    // (bin/, obj/ and node_modules/ skipped at any depth) whose text contains "Spectre".
    private static List<string> SpectreSourceFiles(string root, string folder)
    {
        string projectDirectory = Path.Combine(root, folder);
        string[] extensions = [".cs", ".razor", ".csproj", ".props", ".targets"];
        List<string> found = [];

        foreach (string file in Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(Path.GetRelativePath(projectDirectory, file)))
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

    // 136 AC-10: Core and the console app hold no sealing code and no key store; that lives in the desktop project.
    [Theory]
    [InlineData("AesGcm")]
    [InlineData("ECDiffieHellman")]
    [InlineData("IKeyStore")]
    [InlineData("SealedNote")]
    [InlineData("System.Security.Cryptography.X509Certificates")]
    public void CoreAndConsoleSources_HaveNoSealingCode(string forbidden)
    {
        string root = RepoRoot();
        List<string> found = new[] { "src/Context.Core", "src/Context" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            .Where(f => !Relative(root, f).Contains("/obj/", StringComparison.Ordinal) && !Relative(root, f).Contains("/bin/", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains(forbidden, StringComparison.Ordinal))
            .Select(f => Relative(root, f))
            .ToList();

        Assert.True(found.Count == 0, forbidden + " is in: " + string.Join(", ", found));
    }
}
