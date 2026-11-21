using System.Globalization;

namespace Context;

/// <summary>What <see cref="DecisionsView.Build"/> found.</summary>
internal enum DecisionsViewKind
{
    /// <summary><see cref="DecisionsView.Decisions"/> holds the decisions (never empty then).</summary>
    Found,

    /// <summary>No note (with the tag, when one was given) has a decision.</summary>
    NoDecisions,

    /// <summary>The tag is no valid tag: nothing was read.</summary>
    InvalidTag,

    /// <summary><c>context.db</c> can't be read.</summary>
    IndexUnreadable,
}

/// <summary>One <c>#!d</c> decision paragraph of a note (job 343), with its day and the people mentioned in it.</summary>
internal sealed record Decision
{
    public Decision(string note, int line, int? number, DateOnly date, string text, IReadOnlyList<string> people)
    {
        Note = note;
        Line = line;
        Number = number;
        Date = date;
        Text = text;
        People = people;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(Decision);

    /// <summary>The note's full name (raw).</summary>
    public string Note { get; }

    /// <summary>The 1-based number of the marker line in the note.</summary>
    public int Line { get; }

    /// <summary>The decision's number, or <c>null</c> for a bare marker not yet numbered by a desktop save.</summary>
    public int? Number { get; }

    /// <summary>The note's title: the last segment of <see cref="Note"/> (raw).</summary>
    public string Title => Note.LastIndexOf('/') is int slash and >= 0 ? Note[(slash + 1)..] : Note;

    /// <summary>The note title's leading date, else the note's last write time as a day in the zone.</summary>
    public DateOnly Date { get; }

    /// <summary>The whole paragraph without its marker (raw): one line per paragraph line, joined with <c>\n</c>.</summary>
    public string Text { get; }

    /// <summary>The first line of <see cref="Text"/> (raw).</summary>
    public string Headline => Text.IndexOf('\n', StringComparison.Ordinal) is int end and >= 0 ? Text[..end] : Text;

    /// <summary>The people mentioned anywhere in <see cref="Text"/>, resolved through the aliases, distinct, in line order (raw).</summary>
    public IReadOnlyList<string> People { get; }
}

/// <summary>
/// The data of <c>decisions [tag]</c>, with no rendering. Note names, texts and people are raw text: every front end
/// must clean them (<see cref="SafeText.Clean"/>) and escape them for its own output.
/// </summary>
internal sealed record DecisionsView
{
    private const string DateFormat = "yyyy-MM-dd";

    public DecisionsView(DecisionsViewKind kind, string tag, IReadOnlyList<Decision> decisions)
    {
        Kind = kind;
        Tag = tag;
        Decisions = decisions;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(DecisionsView);

    /// <summary>What was found; read it first.</summary>
    public DecisionsViewKind Kind { get; }

    /// <summary>The normalized tag (no <c>#</c>), empty for no filter or an invalid tag.</summary>
    public string Tag { get; }

    /// <summary>
    /// The decisions for <c>Found</c>: by <see cref="Decision.Date"/> descending, then note ignoring case, then
    /// ordinal, then line; else empty.
    /// </summary>
    public IReadOnlyList<Decision> Decisions { get; }

    /// <summary>
    /// Normalizes a typed tag filter (<see cref="TagIndex.NormalizeTag"/>): <c>true</c> with an empty
    /// <paramref name="normalized"/> for a blank one (no filter), <c>true</c> for a valid tag, <c>false</c> when the
    /// result is no tag by <see cref="TagParser.NormalizeBracket"/> (for example <c>#1x</c> or <c>#</c>).
    /// </summary>
    public static bool TryNormalizeTag(string tag, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return true;
        }

        string candidate = TagIndex.NormalizeTag(tag);
        if (!string.Equals(TagParser.NormalizeBracket(candidate), candidate, StringComparison.Ordinal))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    /// <summary>
    /// Checks the tag (<c>InvalidTag</c> without opening the index), then syncs the index and lists the decisions
    /// (<see cref="TagIndex.Decisions"/>), dated, with their people resolved through <see cref="TagIndex.Aliases"/>.
    /// Never throws for an unreadable index.
    /// </summary>
    public static DecisionsView Build(TagIndex index, NoteStore store, string tag, TimeZoneInfo zone)
    {
        if (!TryNormalizeTag(tag, out string wanted))
        {
            return new DecisionsView(DecisionsViewKind.InvalidTag, string.Empty, []);
        }

        try
        {
            index.Sync(store);
            IReadOnlyList<IndexedDecision> rows = index.Decisions(wanted.Length == 0 ? null : wanted);
            PersonAliases aliases = rows.Count == 0 ? PersonAliases.Empty : index.Aliases();
            List<Decision> decisions = rows
                .Select(r => ToDecision(r, aliases, zone))
                .OrderByDescending(d => d.Date)
                .ThenBy(d => d.Note, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Note, StringComparer.Ordinal)
                .ThenBy(d => d.Line)
                .ToList();
            DecisionsViewKind kind = decisions.Count == 0 ? DecisionsViewKind.NoDecisions : DecisionsViewKind.Found;
            return new DecisionsView(kind, wanted, decisions);
        }
        catch (TagIndexException)
        {
            return new DecisionsView(DecisionsViewKind.IndexUnreadable, wanted, []);
        }
    }

    /// <summary>
    /// Syncs the index and finds the decision numbered <paramref name="number"/> as its owner holds it
    /// (<see cref="TagIndex.DecisionByNumber"/>): <c>Found</c> with that one decision, <c>NoDecisions</c> when no
    /// readable owner holds it, <c>IndexUnreadable</c>. Never throws for an unreadable index.
    /// </summary>
    public static DecisionsView Find(TagIndex index, NoteStore store, int number, TimeZoneInfo zone)
    {
        try
        {
            index.Sync(store);
            if (index.DecisionByNumber(number) is not { } row)
            {
                return new DecisionsView(DecisionsViewKind.NoDecisions, string.Empty, []);
            }

            return new DecisionsView(DecisionsViewKind.Found, string.Empty, [ToDecision(row, index.Aliases(), zone)]);
        }
        catch (TagIndexException)
        {
            return new DecisionsView(DecisionsViewKind.IndexUnreadable, string.Empty, []);
        }
    }

    private static Decision ToDecision(IndexedDecision row, PersonAliases aliases, TimeZoneInfo zone) =>
        new(row.Note, row.Line, row.Number, DateOf(row.Note, row.Modified, zone), row.Text, PeopleOf(row.Text, aliases));

    // The title's leading yyyy-MM-dd (the whole title, or followed by whitespace) when it is a real date, else the
    // last write time as a day in the zone.
    private static DateOnly DateOf(string note, DateTime modifiedUtc, TimeZoneInfo zone)
    {
        int slash = note.LastIndexOf('/');
        string title = slash < 0 ? note : note[(slash + 1)..];
        if (title.Length >= DateFormat.Length
            && (title.Length == DateFormat.Length || char.IsWhiteSpace(title[DateFormat.Length]))
            && DateOnly.TryParseExact(
                title.AsSpan(0, DateFormat.Length), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day))
        {
            return day;
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(modifiedUtc, zone));
    }

    // Each mention in the paragraph shown by its person page's title when it resolves to one, else as written; distinct
    // ignoring case, in line order.
    private static List<string> PeopleOf(string text, PersonAliases aliases)
    {
        List<string> people = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string mention in PersonParser.Parse(text))
        {
            string shown = aliases.Resolve(mention).Person ?? mention;
            if (seen.Add(PersonParser.Key(shown)))
            {
                people.Add(shown);
            }
        }

        return people;
    }
}
