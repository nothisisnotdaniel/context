namespace Context;

/// <summary>How the titles of a <see cref="SimilarPair"/> match.</summary>
internal enum TitleMatchKind
{
    /// <summary>The titles don't match.</summary>
    None,

    /// <summary>The title keys are equal.</summary>
    Same,

    /// <summary>The title keys are a small edit apart (<see cref="SimilarNotes.TitleMatch"/>).</summary>
    Similar,
}

/// <summary>What <see cref="SimilarNotesView.Build"/> found.</summary>
internal enum SimilarNotesKind
{
    /// <summary><see cref="SimilarNotesView.Pairs"/> holds the pairs (never empty then).</summary>
    Found,

    /// <summary>No two notes are similar.</summary>
    NoSimilar,

    /// <summary><c>context.db</c> can't be read.</summary>
    IndexUnreadable,
}

/// <summary>Two plain notes that look like near-duplicates.</summary>
internal sealed record SimilarPair
{
    public SimilarPair(string first, string second, TitleMatchKind titleMatch, double? contentScore)
    {
        First = first;
        Second = second;
        TitleMatch = titleMatch;
        ContentScore = contentScore;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(SimilarPair);

    /// <summary>The full name that sorts first (ignoring case, then ordinal; raw).</summary>
    public string First { get; }

    /// <summary>The other full name (raw).</summary>
    public string Second { get; }

    /// <summary>How the titles match.</summary>
    public TitleMatchKind TitleMatch { get; }

    /// <summary>The Jaccard similarity of the two texts' shingles when the texts match, else <c>null</c>.</summary>
    public double? ContentScore { get; }
}

/// <summary>
/// The pure matching rules of the similar-notes view: title keys, a banded edit distance and text shingles.
/// </summary>
internal static class SimilarNotes
{
    /// <summary>The fewest shingles a text needs to match by content.</summary>
    public const int MinShingles = 10;

    /// <summary>The lowest Jaccard similarity of two texts' shingles that counts as a content match.</summary>
    public const double ContentThreshold = 0.5;

    /// <summary>A shingle found in more notes than this is boilerplate and finds no candidate pair.</summary>
    public const int MaxShingleNotes = 50;

    /// <summary>The shortest key (of the two) that may match by a small edit.</summary>
    public const int MinFuzzyKeyLength = 5;

    /// <summary>A shorter key under this length allows one edit, else two.</summary>
    public const int TwoEditKeyLength = 10;

    private const int ShingleSize = 3;

    /// <summary>
    /// The maximal <see cref="char.IsLetterOrDigit(char)"/> runs of <paramref name="text"/>, lower-cased invariant, in
    /// order (repeats kept).
    /// </summary>
    public static List<string> Tokens(string text)
    {
        List<string> tokens = [];
        int start = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool word = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (word && start < 0)
            {
                start = i;
            }
            else if (!word && start >= 0)
            {
                tokens.Add(text[start..i].ToLowerInvariant());
                start = -1;
            }
        }

        return tokens;
    }

    /// <summary>The tokens of the title's last segment (after the last <c>/</c>), joined by one space.</summary>
    public static string TitleKey(string title)
    {
        int slash = title.LastIndexOf('/');
        return string.Join(' ', Tokens(slash < 0 ? title : title[(slash + 1)..]));
    }

    /// <summary>The distinct runs of 3 consecutive tokens of the whole text, each joined by one space.</summary>
    public static HashSet<string> Shingles(string text)
    {
        List<string> tokens = Tokens(text);
        HashSet<string> shingles = new(StringComparer.Ordinal);
        for (int i = 0; i + ShingleSize <= tokens.Count; i++)
        {
            shingles.Add(string.Join(' ', tokens.GetRange(i, ShingleSize)));
        }

        return shingles;
    }

    /// <summary>
    /// How two title keys match: never when either has no letter or they differ only in digits; <c>Same</c> when
    /// equal; <c>Similar</c> when the shorter has at least <see cref="MinFuzzyKeyLength"/> characters and the edit
    /// distance is at most 1 (shorter under <see cref="TwoEditKeyLength"/>) or 2.
    /// </summary>
    public static TitleMatchKind TitleMatch(string first, string second)
    {
        if (!first.Any(char.IsLetter) || !second.Any(char.IsLetter))
        {
            return TitleMatchKind.None;
        }

        if (string.Equals(first, second, StringComparison.Ordinal))
        {
            return TitleMatchKind.Same;
        }

        if (string.Equals(WithoutDigits(first), WithoutDigits(second), StringComparison.Ordinal))
        {
            return TitleMatchKind.None;
        }

        int shorter = Math.Min(first.Length, second.Length);
        if (shorter < MinFuzzyKeyLength)
        {
            return TitleMatchKind.None;
        }

        int limit = shorter < TwoEditKeyLength ? 1 : 2;
        return Distance(first, second, limit) <= limit ? TitleMatchKind.Similar : TitleMatchKind.None;
    }

    /// <summary>
    /// The Levenshtein distance of two strings when it is at most <paramref name="limit"/>, else <c>limit + 1</c>;
    /// only the band of width <c>2 * limit + 1</c> is computed and it stops as soon as a row exceeds the limit.
    /// </summary>
    public static int Distance(string a, string b, int limit)
    {
        if (Math.Abs(a.Length - b.Length) > limit)
        {
            return limit + 1;
        }

        int over = limit + 1;
        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j <= limit ? j : over;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            int from = Math.Max(1, i - limit);
            int to = Math.Min(b.Length, i + limit);
            current[0] = i <= limit ? i : over;
            if (from > 1)
            {
                current[from - 1] = over;
            }

            int best = current[0];
            for (int j = from; j <= to; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int value = Math.Min(previous[j - 1] + cost, Math.Min(previous[j] + 1, current[j - 1] + 1));
                current[j] = Math.Min(value, over);
                best = Math.Min(best, current[j]);
            }

            if (to < b.Length)
            {
                current[to + 1] = over;
            }

            if (best > limit)
            {
                return over;
            }

            (previous, current) = (current, previous);
        }

        return Math.Min(previous[b.Length], over);
    }

    /// <summary>
    /// The Jaccard similarity of two shingle sets when both have at least <see cref="MinShingles"/> and it is at least
    /// <see cref="ContentThreshold"/>, else <c>null</c>.
    /// </summary>
    public static double? ContentScore(HashSet<string> first, HashSet<string> second)
    {
        if (first.Count < MinShingles || second.Count < MinShingles)
        {
            return null;
        }

        HashSet<string> small = first.Count <= second.Count ? first : second;
        HashSet<string> large = ReferenceEquals(small, first) ? second : first;
        int shared = small.Count(large.Contains);
        int union = first.Count + second.Count - shared;
        double score = (double)shared / union;
        return score >= ContentThreshold ? score : null;
    }

    /// <summary>Orders full names ignoring case, then ordinal.</summary>
    public static int CompareNames(string first, string second)
    {
        int result = StringComparer.OrdinalIgnoreCase.Compare(first, second);
        return result != 0 ? result : StringComparer.Ordinal.Compare(first, second);
    }

    /// <summary>
    /// Every similar pair among the given notes (names with their whole texts): pairs by title and, through an
    /// inverted shingle index that skips shingles in more than <see cref="MaxShingleNotes"/> notes, by content.
    /// Unsorted; each unordered pair once with <see cref="SimilarPair.First"/> before <see cref="SimilarPair.Second"/>.
    /// </summary>
    public static List<SimilarPair> Pairs(IReadOnlyList<(string Name, string Text)> notes)
    {
        int count = notes.Count;
        string[] keys = new string[count];
        HashSet<string>[] shingles = new HashSet<string>[count];
        for (int i = 0; i < count; i++)
        {
            keys[i] = TitleKey(notes[i].Name);
            shingles[i] = Shingles(notes[i].Text);
        }

        Dictionary<long, TitleMatchKind> titles = TitlePairs(keys);
        HashSet<long> candidates = ContentCandidates(shingles);
        foreach (long pair in titles.Keys)
        {
            candidates.Add(pair);
        }

        List<SimilarPair> pairs = [];
        foreach (long pair in candidates)
        {
            int a = (int)(pair >> 32);
            int b = (int)(pair & 0xFFFFFFFF);
            TitleMatchKind title = titles.TryGetValue(pair, out TitleMatchKind found) ? found : TitleMatchKind.None;
            double? score = ContentScore(shingles[a], shingles[b]);
            if (title == TitleMatchKind.None && score is null)
            {
                continue;
            }

            string first = notes[a].Name;
            string second = notes[b].Name;
            if (CompareNames(first, second) > 0)
            {
                (first, second) = (second, first);
            }

            pairs.Add(new SimilarPair(first, second, title, score));
        }

        return pairs;
    }

    // The key with every digit removed and the remaining tokens joined by one space again.
    private static string WithoutDigits(string key) =>
        string.Join(' ', key.Split(' ').Select(t => new string(t.Where(c => !char.IsDigit(c)).ToArray())).Where(t => t.Length > 0));

    private static long PairKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    // Keys bucketed by length; each key is compared only with keys at most two characters longer.
    private static Dictionary<long, TitleMatchKind> TitlePairs(string[] keys)
    {
        Dictionary<long, TitleMatchKind> pairs = [];
        Dictionary<int, List<int>> byLength = [];
        for (int i = 0; i < keys.Length; i++)
        {
            if (!keys[i].Any(char.IsLetter))
            {
                continue;
            }

            if (!byLength.TryGetValue(keys[i].Length, out List<int>? bucket))
            {
                bucket = [];
                byLength[keys[i].Length] = bucket;
            }

            bucket.Add(i);
        }

        foreach ((int length, List<int> bucket) in byLength)
        {
            for (int extra = 0; extra <= 2; extra++)
            {
                if (!byLength.TryGetValue(length + extra, out List<int>? other))
                {
                    continue;
                }

                for (int x = 0; x < bucket.Count; x++)
                {
                    for (int y = extra == 0 ? x + 1 : 0; y < other.Count; y++)
                    {
                        TitleMatchKind match = TitleMatch(keys[bucket[x]], keys[other[y]]);
                        if (match != TitleMatchKind.None)
                        {
                            pairs[PairKey(bucket[x], other[y])] = match;
                        }
                    }
                }
            }
        }

        return pairs;
    }

    // Pairs of notes with enough shingles that share at least one shingle found in at most MaxShingleNotes notes.
    private static HashSet<long> ContentCandidates(HashSet<string>[] shingles)
    {
        Dictionary<string, List<int>> owners = new(StringComparer.Ordinal);
        for (int i = 0; i < shingles.Length; i++)
        {
            if (shingles[i].Count < MinShingles)
            {
                continue;
            }

            foreach (string shingle in shingles[i])
            {
                if (!owners.TryGetValue(shingle, out List<int>? list))
                {
                    list = [];
                    owners[shingle] = list;
                }

                list.Add(i);
            }
        }

        HashSet<long> candidates = [];
        foreach (List<int> list in owners.Values)
        {
            if (list.Count < 2 || list.Count > MaxShingleNotes)
            {
                continue;
            }

            for (int x = 0; x < list.Count; x++)
            {
                for (int y = x + 1; y < list.Count; y++)
                {
                    candidates.Add(PairKey(list[x], list[y]));
                }
            }
        }

        return candidates;
    }
}

/// <summary>
/// The data of the desktop's similar-notes page, with no rendering. Note names are raw text: every front end must
/// clean them (<see cref="SafeText.Clean"/>) and escape them for its own output.
/// </summary>
internal sealed record SimilarNotesView
{
    public SimilarNotesView(SimilarNotesKind kind, IReadOnlyList<SimilarPair> pairs)
    {
        Kind = kind;
        Pairs = pairs;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(SimilarNotesView);

    /// <summary>What was found; read it first.</summary>
    public SimilarNotesKind Kind { get; }

    /// <summary>
    /// The pairs for <c>Found</c>: both reasons first, then <see cref="SimilarPair.ContentScore"/> descending (none
    /// last), then <see cref="SimilarPair.First"/> and <see cref="SimilarPair.Second"/> ignoring case, then ordinal;
    /// else empty.
    /// </summary>
    public IReadOnlyList<SimilarPair> Pairs { get; }

    /// <summary>
    /// Syncs the index and pairs the plain notes listed by <see cref="NoteStore.List"/> that have a text row in
    /// <see cref="TagIndex.NoteTexts"/> (so sensitive and unreadable notes never appear). Never throws for an
    /// unreadable index.
    /// </summary>
    public static SimilarNotesView Build(TagIndex index, NoteStore store)
    {
        try
        {
            index.Sync(store);
            Dictionary<string, string> texts = new(StringComparer.Ordinal);
            foreach ((string note, string body) in index.NoteTexts())
            {
                texts[note] = body;
            }

            List<(string Name, string Text)> notes = [];
            foreach (NoteInfo info in store.List())
            {
                if (texts.TryGetValue(info.Name, out string? text))
                {
                    notes.Add((info.Name, text));
                }
            }

            List<SimilarPair> pairs = SimilarNotes.Pairs(notes)
                .OrderByDescending(p => p.TitleMatch != TitleMatchKind.None && p.ContentScore is not null)
                .ThenByDescending(p => p.ContentScore ?? -1)
                .ThenBy(p => p.First, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.First, StringComparer.Ordinal)
                .ThenBy(p => p.Second, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Second, StringComparer.Ordinal)
                .ToList();
            return new SimilarNotesView(pairs.Count == 0 ? SimilarNotesKind.NoSimilar : SimilarNotesKind.Found, pairs);
        }
        catch (TagIndexException)
        {
            return new SimilarNotesView(SimilarNotesKind.IndexUnreadable, []);
        }
    }
}
