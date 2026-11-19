namespace Context.Tests;

/// <summary>Every file under a folder with its bytes and last-write time, to prove nothing changed.</summary>
internal static class FileSnapshot
{
    /// <summary>
    /// Root-relative path to "last-write ticks + base64 bytes" for every file under <paramref name="root"/>; files
    /// whose name starts with one of <paramref name="ignorePrefixes"/> are left out, and so are the temp files of an
    /// ignored file (<c>.&lt;name&gt;.&lt;anything&gt;.tmp</c> in that file's own folder), which a write in flight may
    /// create and move away at any moment.
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
            if (ignorePrefixes.Any(p => relative.StartsWith(p, StringComparison.Ordinal) || IsTempFileOf(relative, p)))
            {
                continue;
            }

            files[relative] = File.GetLastWriteTimeUtc(path).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " " + Convert.ToBase64String(File.ReadAllBytes(path));
        }

        return files;
    }

    /// <summary>
    /// True when <paramref name="relative"/> is <c>&lt;folder&gt;/.&lt;name&gt;.&lt;anything&gt;.tmp</c> for the ignored
    /// path <paramref name="prefix"/> = <c>&lt;folder&gt;/&lt;name&gt;</c>, in exactly that folder. Uses only the path.
    /// </summary>
    internal static bool IsTempFileOf(string relative, string prefix)
    {
        int prefixSlash = prefix.LastIndexOf('/');
        string folder = prefixSlash < 0 ? string.Empty : prefix[..(prefixSlash + 1)];
        string name = prefix[(prefixSlash + 1)..];
        if (name.Length == 0 || !relative.StartsWith(folder, StringComparison.Ordinal))
        {
            return false;
        }

        string file = relative[folder.Length..];
        string start = "." + name + ".";
        const string End = ".tmp";
        return !file.Contains('/')
            && file.Length > start.Length + End.Length
            && file.StartsWith(start, StringComparison.Ordinal)
            && file.EndsWith(End, StringComparison.Ordinal);
    }
}
