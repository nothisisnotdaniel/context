namespace Context;

/// <summary>
/// Finds work item references <c>#&lt;n&gt;</c> (the Times work item numbers) in Markdown text. A reference is <c>#</c>
/// at the start of a line or after whitespace, then 1 to <see cref="MaxDigits"/> ASCII digits with a first digit
/// 1-9, not directly followed by a letter, a digit, <c>-</c>, <c>_</c> or <c>#</c>. Lines inside fenced code blocks
/// and text inside inline backtick spans are skipped, as in <see cref="TagParser"/>. A reference is never a tag (a tag
/// starts with a letter).
/// </summary>
internal static class WorkItemParser
{
    /// <summary>The most digits a work item number has.</summary>
    public const int MaxDigits = 9;

    /// <summary>The distinct work item numbers in <paramref name="markdown"/>, in order of first appearance.</summary>
    public static IReadOnlyList<int> Parse(string markdown)
    {
        List<int> items = [];
        HashSet<int> seen = [];
        foreach ((int _, string line) in TagParser.NumberedLinesOutsideFences(markdown))
        {
            ScanLine(line, items, seen);
        }

        return items;
    }

    /// <summary>True when <paramref name="text"/> holds the reference <c>#&lt;item&gt;</c>.</summary>
    public static bool Mentions(string text, int item) => Parse(text).Contains(item);

    /// <summary>
    /// Reads a typed work item number: 1 to <see cref="MaxDigits"/> ASCII digits, the first 1-9, nothing else (an
    /// optional leading <c>#</c> is allowed). False for anything else.
    /// </summary>
    public static bool TryParseNumber(string? text, out int item)
    {
        item = 0;
        if (text is null)
        {
            return false;
        }

        ReadOnlySpan<char> span = text.AsSpan();
        if (span.Length > 0 && span[0] == '#')
        {
            span = span[1..];
        }

        if (span.Length == 0 || span.Length > MaxDigits || span[0] is < '1' or > '9')
        {
            return false;
        }

        int value = 0;
        foreach (char c in span)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            value = (value * 10) + (c - '0');
        }

        item = value;
        return true;
    }

    private static void ScanLine(string line, List<int> items, HashSet<int> seen)
    {
        CodeSpans? spans = CodeSpans.For(line);
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '`')
            {
                i = spans!.Skip(i);
                continue;
            }

            if (c != '#' || (i > 0 && !char.IsWhiteSpace(line[i - 1])))
            {
                i++;
                continue;
            }

            int start = i + 1;
            int end = start;
            while (end < line.Length && char.IsAsciiDigit(line[end]))
            {
                end++;
            }

            int digits = end - start;
            bool valid = digits is >= 1 and <= MaxDigits
                && line[start] != '0'
                && (end >= line.Length || !(char.IsLetterOrDigit(line[end]) || line[end] is '-' or '_' or '#'));
            if (valid && TryParseNumber(line[start..end], out int item) && seen.Add(item))
            {
                items.Add(item);
            }

            i = Math.Max(end, i + 1);
        }
    }
}

/// <summary>Where work item pages live: the subpages of the top-level note <c>items</c>.</summary>
internal static class WorkItems
{
    /// <summary>The title of the top-level note whose subpages are the work item pages.</summary>
    public const string FolderName = "items";

    /// <summary>The full name of the page of work item <paramref name="item"/>: <c>items/&lt;n&gt;</c>.</summary>
    public static string PageNote(int item) =>
        FolderName + "/" + item.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The work item a note is the page of: its number when it is a direct subpage of the top-level <c>items</c> note
    /// (ignoring case) and its title is a valid number (<see cref="WorkItemParser.TryParseNumber"/> without <c>#</c>),
    /// else <c>null</c>.
    /// </summary>
    public static int? ItemOf(string noteName)
    {
        int slash = noteName.IndexOf('/');
        if (slash < 0 || noteName.IndexOf('/', slash + 1) >= 0)
        {
            return null;
        }

        if (!string.Equals(noteName[..slash], FolderName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string title = noteName[(slash + 1)..];
        return title.Length > 0 && title[0] != '#' && WorkItemParser.TryParseNumber(title, out int item) ? item : null;
    }

    /// <summary>
    /// Finds or creates the page of work item <paramref name="item"/>: a missing top-level <c>items</c> note is created
    /// as <c># items\n</c> (an existing one is reused, in any case, its spelling kept) and a missing page as
    /// <c># &lt;n&gt;\n</c> through <see cref="NoteStore.CreateChild(string, string)"/>; an existing page is never changed.
    /// A sensitive top-level <c>items</c> is <see cref="WorkItemPageKind.Sensitive"/> with nothing read or written.
    /// Throws <see cref="IOException"/> (or <see cref="UnauthorizedAccessException"/>) when a write fails.
    /// </summary>
    public static WorkItemPage EnsurePage(NoteStore notes, int item)
    {
        if (notes.TryFindSensitive(FolderName, out NoteInfo? locked) && locked is not null)
        {
            return new WorkItemPage(WorkItemPageKind.Sensitive, locked.Name);
        }

        if (!notes.TryFind(FolderName, out NoteInfo? folder) || folder is null)
        {
            try
            {
                folder = notes.Create(FolderName);
            }
            catch (NoteExistsException)
            {
                // Created by someone else between the check and the write.
                if (!notes.TryFind(FolderName, out folder) || folder is null)
                {
                    throw new IOException("The items note can't be found.");
                }
            }
        }

        string title = item.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string name = folder.Name + "/" + title;
        if (notes.TryFind(name, out NoteInfo? page) && page is not null)
        {
            return new WorkItemPage(WorkItemPageKind.Existing, page.Name);
        }

        try
        {
            return new WorkItemPage(WorkItemPageKind.Created, notes.CreateChild(folder.Name, title).Name);
        }
        catch (NoteExistsException)
        {
            return notes.TryFind(name, out page) && page is not null
                ? new WorkItemPage(WorkItemPageKind.Existing, page.Name)
                : throw new IOException("The work item page can't be found.");
        }
    }
}

/// <summary>What <see cref="WorkItems.EnsurePage"/> found.</summary>
internal enum WorkItemPageKind
{
    /// <summary>The page was there already.</summary>
    Existing,

    /// <summary>The page was created now.</summary>
    Created,

    /// <summary>The top-level <c>items</c> note is sensitive: nothing was read or written.</summary>
    Sensitive,
}

/// <summary>The result of <see cref="WorkItems.EnsurePage"/>: its kind and the page's (or the sensitive note's) name.</summary>
internal sealed record WorkItemPage
{
    public WorkItemPage(WorkItemPageKind kind, string name)
    {
        Kind = kind;
        Name = name;
    }

    /// <summary>Found, created or refused.</summary>
    public WorkItemPageKind Kind { get; }

    /// <summary>The page's full name, or the sensitive note's name (raw).</summary>
    public string Name { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(WorkItemPage);
}
