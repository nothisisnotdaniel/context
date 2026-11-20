namespace Context.Tests;

/// <summary>Finds the repository root and the source files of a project, skipping <c>bin/</c>, <c>obj/</c> and
/// <c>editor/node_modules/</c>.</summary>
internal static class RepoFiles
{
    public const string DesktopFolder = "src/Context.Desktop";

    public static string Root()
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

    public static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>The root-relative paths of the files under <paramref name="folder"/> with one of the extensions.</summary>
    public static List<string> Sources(string root, string folder, params string[] extensions)
    {
        string projectDirectory = Path.Combine(root, folder);
        List<string> found = [];
        foreach (string file in Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories))
        {
            string[] segments = Path.GetRelativePath(projectDirectory, file).Split(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Length > 1 && (segments[0] == "bin" || segments[0] == "obj"))
            {
                continue;
            }

            // The editor's npm packages are third-party code, not the project's sources.
            if (segments.Length > 2 && segments[0] == "editor" && segments[1] == "node_modules")
            {
                continue;
            }

            if (extensions.Any(e => string.Equals(Path.GetExtension(file), e, StringComparison.OrdinalIgnoreCase)))
            {
                found.Add(Relative(root, file));
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }
}
