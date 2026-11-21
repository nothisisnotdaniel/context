namespace Context;

/// <summary>
/// Finds the installed Times command (job 282): <c>tm</c> (<c>tm.exe</c> on Windows), the .NET tool's command name.
/// Pure: the caller passes the home folder, <c>PATH</c>, the OS and the file check, so tests need no real tool.
/// </summary>
internal static class TimesExecutable
{
    /// <summary>
    /// <c>&lt;home&gt;/.dotnet/tools/tm</c> when it exists, else the first <c>PATH</c> folder (split on <c>;</c> on
    /// Windows, <c>:</c> elsewhere; empty and relative folders skipped) holding it, else null.
    /// </summary>
    public static string? Find(string? home, string? pathVariable, bool isWindows, Func<string, bool> fileExists)
    {
        string name = isWindows ? "tm.exe" : "tm";
        char separator = isWindows ? '\\' : '/';
        if (!string.IsNullOrEmpty(home) && IsAbsolute(home, isWindows))
        {
            string tools = Join(Join(Join(home, ".dotnet", separator), "tools", separator), name, separator);
            if (Exists(fileExists, tools))
            {
                return tools;
            }
        }

        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        foreach (string raw in pathVariable.Split(isWindows ? ';' : ':'))
        {
            string folder = raw.Trim();
            if (isWindows)
            {
                folder = folder.Trim('"');
            }

            if (folder.Length == 0 || !IsAbsolute(folder, isWindows))
            {
                continue;
            }

            string candidate = Join(folder, name, separator);
            if (Exists(fileExists, candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool Exists(Func<string, bool> fileExists, string path)
    {
        try
        {
            return fileExists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsAbsolute(string folder, bool isWindows)
    {
        if (!isWindows)
        {
            return folder.StartsWith('/');
        }

        return folder.StartsWith(@"\\", StringComparison.Ordinal)
            || (folder.Length >= 3 && char.IsAsciiLetter(folder[0]) && folder[1] == ':' && (folder[2] == '\\' || folder[2] == '/'));
    }

    private static string Join(string folder, string name, char separator) =>
        folder.EndsWith(separator) || folder.EndsWith('/') ? folder + name : folder + separator + name;
}
