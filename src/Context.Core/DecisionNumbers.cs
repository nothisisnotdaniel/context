namespace Context;

/// <summary>
/// One token a save wrote (job 349): the <see cref="Index"/>-th decision of the text (<see cref="DecisionParser.Parse"/>,
/// in document order) had the token <see cref="Old"/> (lower case; <c>null</c> when bare) and now has
/// <see cref="New"/> (<c>ab12-1234</c>).
/// </summary>
internal sealed record DecisionSet(int Index, string? Old, string New)
{
    /// <summary>Returns only the type name.</summary>
    public override string ToString() => nameof(DecisionSet);
}

/// <summary>
/// What <see cref="DecisionNumbers.Apply"/> did to a note's text (job 349): the new text, the tokens it set (in marker
/// order), the folder GUID whose <c>decision-folder::</c> line it added, the markers left as typed because no numbers
/// were left, and whether the note belongs to another folder (then nothing changed).
/// </summary>
internal sealed record DecisionEdits
{
    public DecisionEdits(
        string text, IReadOnlyList<DecisionSet> set, Guid? folder = null, int unnumbered = 0, bool foreign = false)
    {
        Text = text;
        Set = set;
        Folder = folder;
        Unnumbered = unnumbered;
        Foreign = foreign;
    }

    /// <summary>Returns only the type name: the text is raw note text.</summary>
    public override string ToString() => nameof(DecisionEdits);

    /// <summary>The note's text after the edits (the same instance when nothing changed).</summary>
    public string Text { get; }

    /// <summary>The tokens set, in marker order.</summary>
    public IReadOnlyList<DecisionSet> Set { get; }

    /// <summary>The GUID of the <c>decision-folder::</c> line added, or <c>null</c> when none was.</summary>
    public Guid? Folder { get; }

    /// <summary>How many markers kept their typed (or missing) token because no decision numbers are left.</summary>
    public int Unnumbered { get; }

    /// <summary>Whether the note's first <c>decision-folder::</c> line names another folder: it is never numbered.</summary>
    public bool Foreign { get; }

    /// <summary>Whether the text changed.</summary>
    public bool Changed => Set.Count > 0 || Folder is not null;

    /// <summary>Whether the user should be told something: a change, numbers that ran out, or another folder's note.</summary>
    public bool Noteworthy => Changed || Unnumbered > 0 || Foreign;
}

/// <summary>
/// Numbers the <c>!d</c> markers of a note with the folder's prefix (job 349): pure strings, the index is reached only
/// through the callbacks.
/// </summary>
internal static class DecisionNumbers
{
    /// <summary>
    /// Gives every decision of <paramref name="text"/> (<see cref="DecisionParser.Parse"/>) that has no token of the own
    /// prefix it may keep a fresh <c>&lt;prefix&gt;-&lt;n&gt;</c>, in document order: a bare marker, a plain number, a
    /// token of another prefix, and an own-prefix token that <paramref name="keeps"/> (given the number and the
    /// decision's text) refuses. An own-prefix token that repeats in the text is kept when its text equals the first kept
    /// one's, else replaced. A bare marker gets <c> &lt;token&gt;</c> after <c>!d</c>; any other token is replaced in
    /// place; every other char stays. When own-prefix tokens remain and the note has no <c>decision-folder::</c> line,
    /// <see cref="DecisionFolder.WithFolderLine"/> adds it. A note whose first such line names another folder is left as
    /// it is (<see cref="DecisionEdits.Foreign"/>). <paramref name="next"/> is called at most once, with the count of
    /// tokens to set, and returns the first of that many consecutive numbers, or <c>null</c> when none are left: those
    /// markers then stay as typed (<see cref="DecisionEdits.Unnumbered"/>).
    /// </summary>
    public static DecisionEdits Apply(
        string text, DecisionFolder folder, Func<int, string, bool> keeps, Func<int, int?> next)
    {
        IReadOnlyList<ParsedDecision> decisions = DecisionParser.Parse(text);
        if (decisions.Count == 0)
        {
            return new DecisionEdits(text, []);
        }

        Guid? noteGuid = DecisionFolder.NoteGuid(text);
        if (noteGuid is Guid guid && guid != folder.Guid)
        {
            return new DecisionEdits(text, [], foreign: true);
        }

        Dictionary<int, string> keptTexts = [];
        List<int> replace = [];
        for (int i = 0; i < decisions.Count; i++)
        {
            ParsedDecision decision = decisions[i];
            if (decision.Prefix == folder.Prefix && decision.Number is int number)
            {
                bool keep = keptTexts.TryGetValue(number, out string? kept)
                    ? string.Equals(kept, decision.Text, StringComparison.Ordinal)
                    : keeps(number, decision.Text);
                if (keep)
                {
                    keptTexts.TryAdd(number, decision.Text);
                    continue;
                }
            }

            replace.Add(i);
        }

        int? first = replace.Count > 0 ? next(replace.Count) : null;
        if (first is int start && (start < 1 || start > DecisionParser.MaxNumber - replace.Count + 1))
        {
            throw new ArgumentOutOfRangeException(nameof(next), "The reserved numbers are out of range.");
        }

        int unnumbered = replace.Count > 0 && first is null ? replace.Count : 0;
        List<DecisionSet> set = [];
        string edited = text;
        if (first is int from)
        {
            System.Text.StringBuilder builder = new(text.Length + (replace.Count * 16));
            int at = 0;
            foreach (int i in replace)
            {
                ParsedDecision decision = decisions[i];
                string token = DecisionParser.Label(folder.Prefix, from + set.Count);
                set.Add(new DecisionSet(i, decision.Token, token));
                builder.Append(text, at, decision.TokenIndex - at);
                builder.Append(decision.Bare ? " " + token : token);
                at = decision.TokenIndex + decision.TokenLength;
            }

            builder.Append(text, at, text.Length - at);
            edited = builder.ToString();
        }

        Guid? added = null;
        if (noteGuid is null && (keptTexts.Count > 0 || set.Count > 0))
        {
            edited = DecisionFolder.WithFolderLine(edited, folder.Guid);
            added = folder.Guid;
        }

        return new DecisionEdits(set.Count == 0 && added is null ? text : edited, set, added, unnumbered);
    }
}
