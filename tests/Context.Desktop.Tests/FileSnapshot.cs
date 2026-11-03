namespace Context.Tests;

/// <summary>Every file under a folder with its bytes and last-write time, to prove nothing changed.</summary>
internal static class FileSnapshot
{
    /// <summary>
    /// Root-relative path to "last-write ticks + base64 bytes" for every file under <paramref name="root"/>; files
    /// whose name starts with one of <paramref name="ignorePrefixes"/> are left out.
    /// </summary>
    public static SortedDictionary<string, string> Take(string root, params string[] ignorePrefixes)
    {
        SortedDictionary<string, string> files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(root))
        {
            return files;
        }

        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (ignorePrefixes.Any(p => relative.StartsWith(p, StringComparison.Ordinal)))
            {
                continue;
            }

            files[relative] = File.GetLastWriteTimeUtc(path).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " " + Convert.ToBase64String(File.ReadAllBytes(path));
        }

        return files;
    }
}
