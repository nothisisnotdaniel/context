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

/// <summary>
/// One <c>!d</c> decision paragraph of a note (jobs 343, 349), with its day, the people mentioned in it, its label and
/// the other places that hold a copy of it.
/// </summary>
internal sealed record Decision
{
    public Decision(
        string note,
        int line,
        int? number,
        DateOnly date,
        string text,
        IReadOnlyList<string> people,
        string? label = null,
        IReadOnlyList<(string Note, int Line)>? places = null)
    {
        Note = note;
        Line = line;
        Number = number;
        Date = date;
        Text = text;
        People = people;
        Label = number is int n ? label ?? DecisionParser.Label(null, n) : null;
        Places = places ?? [];
    }

    /// <summary>
    /// How the decision is named (job 349): <c>1234</c> for the own folder's, <c>ab12-1234</c> for another prefix (the
    /// alias for a clash); <c>null</c> without a number.
    /// </summary>
    public string? Label { get; }

    /// <summary>The reference that points at it: <c>#!d</c> plus <see cref="Label"/>; <c>null</c> without a number.</summary>
    public string? Reference => Label is null ? null : DecisionParser.Token + Label;

    /// <summary>The other (note, 1-based line) places that hold the same number with the same text (copies).</summary>
    public IReadOnlyList<(string Note, int Line)> Places { get; }

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
            string? own = rows.Count == 0 ? null : index.OwnPrefix;
            List<Decision> decisions = rows
                .Select(r => ToDecision(r, aliases, zone, own))
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
    /// Syncs the index and finds the decision <paramref name="reference"/> points at as its owner holds it
    /// (<see cref="TagIndex.DecisionByKey"/>; no prefix is the own folder's): <c>Found</c> with that one decision,
    /// <c>NoDecisions</c> when no readable owner holds it (or the own prefix can't be read for a plain number),
    /// <c>IndexUnreadable</c>. Never throws for an unreadable index.
    /// </summary>
    public static DecisionsView Find(TagIndex index, NoteStore store, DecisionRef reference, TimeZoneInfo zone)
    {
        try
        {
            index.Sync(store);
            string? own = index.OwnPrefix;
            string? prefix = reference.Prefix ?? own;
            if (prefix is null || index.DecisionByKey(prefix, reference.Number) is not { } row)
            {
                return new DecisionsView(DecisionsViewKind.NoDecisions, string.Empty, []);
            }

            return new DecisionsView(DecisionsViewKind.Found, string.Empty, [ToDecision(row, index.Aliases(), zone, own)]);
        }
        catch (TagIndexException)
        {
            return new DecisionsView(DecisionsViewKind.IndexUnreadable, string.Empty, []);
        }
    }

    private static Decision ToDecision(IndexedDecision row, PersonAliases aliases, TimeZoneInfo zone, string? own) =>
        new(
            row.Note,
            row.Line,
            row.Number,
            DateOf(row.Note, row.Modified, zone),
            row.Text,
            PeopleOf(row.Text, aliases),
            row.Number is int n ? LabelOf(row.Prefix, n, own) : null,
            row.Places);

    /// <summary>A key's label (job 349): the number alone for the own prefix, else <c>&lt;prefix&gt;-&lt;n&gt;</c>.</summary>
    public static string LabelOf(string? prefix, int number, string? own) =>
        DecisionParser.Label(prefix is null || prefix == own ? null : prefix, number);

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
