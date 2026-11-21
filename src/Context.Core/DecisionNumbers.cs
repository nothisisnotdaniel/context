namespace Context;

/// <summary>
/// What <see cref="DecisionNumbers.Apply"/> did to a note's text: the new text, the numbers given to the bare markers
/// (in marker order) and the numbers whose marker was removed (in marker order). Integers and the note's own text only.
/// </summary>
internal sealed record DecisionEdits
{
    public DecisionEdits(string text, IReadOnlyList<int> bare, IReadOnlyList<int> strip)
    {
        Text = text;
        Bare = bare;
        Strip = strip;
    }

    /// <summary>Returns only the type name: the text is raw note text.</summary>
    public override string ToString() => nameof(DecisionEdits);

    /// <summary>The note's text after the edits (the same instance when nothing changed).</summary>
    public string Text { get; }

    /// <summary>The k-th bare marker that starts a decision got the k-th number.</summary>
    public IReadOnlyList<int> Bare { get; }

    /// <summary>One entry per removed numbered marker: a number owned by another note, or a repeat in this note.</summary>
    public IReadOnlyList<int> Strip { get; }

    /// <summary>Whether any marker changed.</summary>
    public bool Changed => Bare.Count > 0 || Strip.Count > 0;
}

/// <summary>Numbers the bare <c>#!d</c> markers of a note and removes markers it doesn't own (job 343). Pure strings.</summary>
internal static class DecisionNumbers
{
    /// <summary>
    /// Turns every bare marker that starts a decision (<see cref="DecisionParser.Parse"/>) into <c>#!d&lt;n&gt;</c>, in
    /// document order, and removes (<see cref="ParsedDecision.StripLength"/>) every numbered marker whose number
    /// <paramref name="isOwnedElsewhere"/> or that already came earlier in this text; the paragraph and every
    /// reference stay. <paramref name="next"/> is called at most once, with the count of bare markers, and returns the
    /// first of that many consecutive numbers. Every other char (line endings, BOM) stays as it was.
    /// </summary>
    public static DecisionEdits Apply(string text, Func<int, bool> isOwnedElsewhere, Func<int, int> next)
    {
        IReadOnlyList<ParsedDecision> decisions = DecisionParser.Parse(text);
        HashSet<int> seen = [];
        HashSet<ParsedDecision> stripped = [];
        List<int> strip = [];
        int bareCount = 0;
        foreach (ParsedDecision decision in decisions)
        {
            if (decision.Number is not int number)
            {
                bareCount++;
            }
            else if (!seen.Add(number) || isOwnedElsewhere(number))
            {
                stripped.Add(decision);
                strip.Add(number);
            }
        }

        if (bareCount == 0 && strip.Count == 0)
        {
            return new DecisionEdits(text, [], []);
        }

        int first = bareCount > 0 ? next(bareCount) : 0;
        if (bareCount > 0 && (first < 1 || first > DecisionParser.MaxNumber - bareCount + 1))
        {
            throw new ArgumentOutOfRangeException(nameof(next), "The reserved numbers are out of range.");
        }

        List<int> bare = [];
        System.Text.StringBuilder builder = new(text.Length + (bareCount * 10));
        int at = 0;
        foreach (ParsedDecision decision in decisions)
        {
            if (decision.Number is null)
            {
                int number = first + bare.Count;
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
        return new DecisionEdits(builder.ToString(), bare, strip);
    }
}
