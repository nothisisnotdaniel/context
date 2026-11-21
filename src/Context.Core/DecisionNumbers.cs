namespace Context;

/// <summary>Who else holds a decision number, for <see cref="DecisionNumbers.Apply"/> (job 343).</summary>
internal enum DecisionHolder
{
    /// <summary>No other note owns the number: the marker stays.</summary>
    None,

    /// <summary>Another note owns it and logs this same text: this marker is a copy and goes.</summary>
    Copy,

    /// <summary>Another note owns it with another text, or its text can't be read: the marker stays (a clash).</summary>
    Other,
}

/// <summary>
/// What <see cref="DecisionNumbers.Apply"/> did to a note's text: the new text, the numbers given to the bare markers
/// (in marker order), the numbers whose marker was removed (in marker order), the numbers kept although another note
/// owns them, and the bare markers left as typed because no numbers were left. Integers and the note's own text only.
/// </summary>
internal sealed record DecisionEdits
{
    public DecisionEdits(
        string text, IReadOnlyList<int> bare, IReadOnlyList<int> strip, IReadOnlyList<int>? kept = null, int unnumbered = 0)
    {
        Text = text;
        Bare = bare;
        Strip = strip;
        Kept = kept ?? [];
        Unnumbered = unnumbered;
    }

    /// <summary>Returns only the type name: the text is raw note text.</summary>
    public override string ToString() => nameof(DecisionEdits);

    /// <summary>The note's text after the edits (the same instance when nothing changed).</summary>
    public string Text { get; }

    /// <summary>The k-th bare marker that starts a decision got the k-th number.</summary>
    public IReadOnlyList<int> Bare { get; }

    /// <summary>One entry per removed numbered marker: a copy of another note's decision, or a repeat in this note.</summary>
    public IReadOnlyList<int> Strip { get; }

    /// <summary>
    /// The numbers another note owns that this note keeps because its text differs from the owner's (or the owner's
    /// can't be read), once each in marker order: only the owner's is in the log.
    /// </summary>
    public IReadOnlyList<int> Kept { get; }

    /// <summary>How many bare markers stay bare because no decision numbers are left.</summary>
    public int Unnumbered { get; }

    /// <summary>Whether any marker changed.</summary>
    public bool Changed => Bare.Count > 0 || Strip.Count > 0;

    /// <summary>Whether the user should be told something: a change, a clash kept, or numbers that ran out.</summary>
    public bool Noteworthy => Changed || Kept.Count > 0 || Unnumbered > 0;
}

/// <summary>Numbers the bare <c>#!d</c> markers of a note and removes markers it doesn't own (job 343). Pure strings.</summary>
internal static class DecisionNumbers
{
    /// <summary>
    /// Turns every bare marker that starts a decision (<see cref="DecisionParser.Parse"/>) into <c>#!d&lt;n&gt;</c>, in
    /// document order, and removes (<see cref="ParsedDecision.StripLength"/>) every numbered marker that already came
    /// earlier in this text, or whose first decision in this text <paramref name="holder"/> (given the number and that
    /// decision's text) calls a <see cref="DecisionHolder.Copy"/>; a <see cref="DecisionHolder.Other"/> keeps its
    /// marker and is listed in <see cref="DecisionEdits.Kept"/>. The paragraph and every reference stay.
    /// <paramref name="next"/> is called at most once, with the count of bare markers, and returns the first of that
    /// many consecutive numbers, or <c>null</c> when none are left: the bare markers then stay as typed. Every other char
    /// (line endings, BOM) stays as it was.
    /// </summary>
    public static DecisionEdits Apply(string text, Func<int, string, DecisionHolder> holder, Func<int, int?> next)
    {
        IReadOnlyList<ParsedDecision> decisions = DecisionParser.Parse(text);
        HashSet<int> seen = [];
        HashSet<ParsedDecision> stripped = [];
        List<int> strip = [];
        List<int> kept = [];
        int bareCount = 0;
        foreach (ParsedDecision decision in decisions)
        {
            if (decision.Number is not int number)
            {
                bareCount++;
            }
            else if (!seen.Add(number))
            {
                // A repeat: the first one keeps the number (or, as a copy, went too).
                stripped.Add(decision);
                strip.Add(number);
            }
            else
            {
                DecisionHolder held = holder(number, decision.Text);
                if (held == DecisionHolder.Copy)
                {
                    stripped.Add(decision);
                    strip.Add(number);
                }
                else if (held == DecisionHolder.Other)
                {
                    kept.Add(number);
                }
            }
        }

        if (bareCount == 0 && strip.Count == 0)
        {
            return new DecisionEdits(text, [], [], kept);
        }

        int? first = bareCount > 0 ? next(bareCount) : null;
        if (first is int start && (start < 1 || start > DecisionParser.MaxNumber - bareCount + 1))
        {
            throw new ArgumentOutOfRangeException(nameof(next), "The reserved numbers are out of range.");
        }

        int unnumbered = bareCount > 0 && first is null ? bareCount : 0;
        if (strip.Count == 0 && first is null)
        {
            return new DecisionEdits(text, [], [], kept, unnumbered);
        }

        List<int> bare = [];
        System.Text.StringBuilder builder = new(text.Length + (bareCount * 10));
        int at = 0;
        foreach (ParsedDecision decision in decisions)
        {
            if (decision.Number is null && first is int from)
            {
                int number = from + bare.Count;
                bare.Add(number);
                int end = decision.MarkerIndex + decision.MarkerLength;
                builder.Append(text, at, end - at);
                builder.Append(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                at = end;
            }
            else if (stripped.Contains(decision))
            {
                builder.Append(text, at, decision.MarkerIndex - at);
                at = decision.MarkerIndex + decision.StripLength;
            }
        }

        builder.Append(text, at, text.Length - at);
        return new DecisionEdits(builder.ToString(), bare, strip, kept, unnumbered);
    }
}
