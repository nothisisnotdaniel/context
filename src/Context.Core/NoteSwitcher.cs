namespace Context;

/// <summary>
/// Finds notes by part of their name for the desktop quick switcher (Ctrl/Cmd+P, job 364). Pure: nothing is read
/// from disk.
/// </summary>
internal static class NoteSwitcher
{
    /// <summary>The most rows <see cref="Match"/> returns.</summary>
    public const int MaxRows = 50;

    /// <summary>
    /// The names in <paramref name="names"/> that match <paramref name="query"/>, best first, each once (ordinal), at
    /// most <see cref="MaxRows"/>. A name matches when every non-whitespace character of the trimmed query appears in
    /// it in order, ignoring case per character; a blank query matches every name. The order is the matching entries
    /// of <paramref name="recent"/> (newest first), then those of <paramref name="pinned"/> (pinned order) not listed
    /// yet, then the rest: a name containing the trimmed query as one run (ignoring case) before a scattered match,
    /// then by name ignoring case, then ordinal. Entries of <paramref name="recent"/> and <paramref name="pinned"/>
    /// that are not in <paramref name="names"/> are left out.
    /// </summary>
    public static IReadOnlyList<string> Match(
        IReadOnlyList<string> names, IReadOnlyList<string> recent, IReadOnlyList<string> pinned, string query)
    {
        string trimmed = query.Trim();
        string letters = string.Concat(trimmed.Where(c => !char.IsWhiteSpace(c)));
        HashSet<string> known = new(names, StringComparer.Ordinal);
        HashSet<string> listed = new(StringComparer.Ordinal);
        List<string> rows = [];

        foreach (string name in recent.Concat(pinned))
        {
            if (rows.Count == MaxRows)
            {
                return rows;
            }

            if (known.Contains(name) && IsMatch(name, letters) && listed.Add(name))
            {
                rows.Add(name);
            }
        }

        IEnumerable<string> rest = names
            .Where(name => !listed.Contains(name) && IsMatch(name, letters))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => trimmed.Length == 0 || name.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(name => name, StringComparer.Ordinal);
        rows.AddRange(rest.Take(MaxRows - rows.Count));
        return rows;
    }

    // True when every character of letters appears in name in order, each compared ignoring case (ordinal).
    private static bool IsMatch(string name, string letters)
    {
        int next = 0;
        for (int i = 0; i < name.Length && next < letters.Length; i++)
        {
            if (name.AsSpan(i, 1).Equals(letters.AsSpan(next, 1), StringComparison.OrdinalIgnoreCase))
            {
                next++;
            }
        }

        return next == letters.Length;
    }
}
