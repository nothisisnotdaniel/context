using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace Context.Tests;

// Architecture test (AC-1): Context.Desktop never depends on Spectre.Console, directly or through a reference.
public sealed class DesktopDependencyTests
{
    private const string DesktopProject = "src/Context.Desktop/Context.Desktop.csproj";

    [Fact]
    public void DesktopAssembly_ReferencesNoSpectreAssembly()
    {
        Assembly desktop = typeof(DesktopState).Assembly;
        Assert.Equal("Context.Desktop", desktop.GetName().Name);

        string[] found = desktop.GetReferencedAssemblies()
            .Select(r => r.Name ?? string.Empty)
            .Where(IsSpectre)
            .ToArray();

        Assert.True(found.Length == 0, "Context.Desktop references Spectre assemblies: " + string.Join(", ", found));
    }

    [Fact]
    public void DesktopProject_HasNoSpectrePackageOrProject()
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Stack<string> pending = new Stack<string>();
        pending.Push(Path.GetFullPath(Path.Combine(root, DesktopProject)));

        while (pending.Count > 0)
        {
            string project = pending.Pop();
            if (!visited.Add(project))
            {
                continue;
            }

            Assert.True(File.Exists(project), "Project file not found: " + project);
            XDocument document = XDocument.Load(project);
            foreach (XElement element in document.Descendants())
            {
                if (element.Name.LocalName == "PackageReference")
                {
                    string id = (element.Attribute("Include") ?? element.Attribute("Update"))?.Value.Trim() ?? string.Empty;
                    if (IsSpectre(id))
                    {
                        found.Add(RepoFiles.Relative(root, project) + ": " + id);
                    }
                }
                else if (element.Name.LocalName == "ProjectReference")
                {
                    string include = element.Attribute("Include")?.Value.Trim() ?? string.Empty;
                    string normalized = include.Replace('\\', Path.DirectorySeparatorChar);
                    string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, normalized));
                    if (Path.GetFileNameWithoutExtension(full) == "Context")
                    {
                        found.Add(RepoFiles.Relative(root, project) + ": the console app (Spectre)");
                    }

                    pending.Push(full);
                }
            }
        }

        Assert.True(visited.Count >= 2, "Context.Desktop should reference Context.Core");
        Assert.True(found.Count == 0, "Spectre reaches Context.Desktop: " + string.Join(", ", found));
    }

    [Fact]
    public void DesktopRestoredGraph_HasNoSpectrePackage()
    {
        string root = RepoFiles.Root();
        string path = Path.Combine(root, "src/Context.Desktop/obj/project.assets.json");
        Assert.True(File.Exists(path), path + " is missing. Run `dotnet restore` first.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement libraries = document.RootElement.GetProperty("libraries");
        string[] found = libraries.EnumerateObject()
            .Select(p => p.Name.Split('/')[0])
            .Where(IsSpectre)
            .Distinct()
            .ToArray();

        Assert.Contains(libraries.EnumerateObject(), p => p.Name.StartsWith("Photino.Blazor/", StringComparison.Ordinal));
        Assert.True(found.Length == 0, "Context.Desktop's restored graph has Spectre: " + string.Join(", ", found));
    }

    [Fact]
    public void DesktopSources_DoNotMentionSpectre()
    {
        string root = RepoFiles.Root();
        string[] found = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor", ".csproj", ".props")
            .Where(f => File.ReadAllText(Path.Combine(root, f)).Contains("Spectre", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(found.Length == 0, "Files in Context.Desktop mention Spectre: " + string.Join(", ", found));
    }

    private static bool IsSpectre(string name) => name.StartsWith("Spectre", StringComparison.OrdinalIgnoreCase);
}
