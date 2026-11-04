namespace Context;

/// <summary>
/// Ranks commands against what was typed, for the console's command popup and the desktop command palette.
/// </summary>
internal static class CommandMatcher
{
    /// <summary>
    /// The items whose name (<paramref name="nameOf"/>, starting with <c>/</c>) matches the command token of
    /// <paramref name="line"/> (the text up to the first whitespace), best first. An empty token (just <c>/</c>)
    /// returns every item in name order, ignoring case. Otherwise each name scores the smaller Levenshtein distance
    /// to the whole name or to its prefix of the token's length, so a prefix scores 0 and typos still match. An item
    /// is kept if its score is 0 for a 1-letter token, at most 1 for 2–3 letters and at most 2 for longer tokens.
    /// Results are sorted by score, then by name ignoring case.
    /// </summary>
    public static IReadOnlyList<T> Match<T>(IEnumerable<T> items, Func<T, string> nameOf, string line)
    {
        int end = 0;
        while (end < line.Length && !char.IsWhiteSpace(line[end]))
        {
            end++;
        }

        string token = line[..end];
        if (token.StartsWith('/'))
        {
            token = token[1..];
        }

        token = token.ToLowerInvariant();
        if (token.Length == 0)
        {
            return items.OrderBy(nameOf, StringComparer.OrdinalIgnoreCase).ToList();
        }

        int limit = token.Length switch
        {
            1 => 0,
            <= 3 => 1,
            _ => 2,
        };

        List<(T Item, string Name, int Score)> scored = [];
        foreach (T item in items)
        {
            string fullName = nameOf(item);
            string name = (fullName.StartsWith('/') ? fullName[1..] : fullName).ToLowerInvariant();
            string prefix = name[..Math.Min(token.Length, name.Length)];
            int score = Math.Min(Levenshtein(token, name), Levenshtein(token, prefix));
            if (score <= limit)
            {
                scored.Add((item, fullName, score));
            }
        }

        return scored
            .OrderBy(s => s.Score)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.Item)
            .ToList();
    }

    /// <summary>The Levenshtein (edit) distance between two strings: inserts, deletes and substitutions.</summary>
    public static int Levenshtein(string a, string b)
    {
        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
