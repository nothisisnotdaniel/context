namespace Context;

/// <summary>
/// A waiting-for todo's parts (<see cref="WaitingFor.TryParse"/>): the person waited on and the rest of the text.
/// Both are raw outside text.
/// </summary>
/// <param name="Person">The mention's normalized name (<see cref="PersonParser.Normalize"/>).</param>
/// <param name="Rest">The text after the mention, trimmed; may be empty.</param>
internal sealed record WaitingItem(string Person, string Rest)
{
    /// <summary>Only the type name: the contents are raw outside text.</summary>
    public override string ToString() => nameof(WaitingItem);
}

/// <summary>
/// Waiting-for todos: a todo whose text starts with <c>waiting: @[Full Name] &lt;text&gt;</c>. Its due date is the
/// follow-up date. Nothing is stored apart from the text, so <c>todos.json</c> and <c>context.db</c> are unchanged.
/// </summary>
internal static class WaitingFor
{
    /// <summary>The text's prefix, matched ignoring ASCII case.</summary>
    public const string Prefix = "waiting:";

    /// <summary>The follow-up date when none is given: this many days after today.</summary>
    public const int DefaultFollowUpDays = 7;

    /// <summary>
    /// Reads <paramref name="text"/> as a waiting-for todo: optional leading whitespace, <c>waiting:</c> (ASCII,
    /// ignoring case), optional spaces or tabs, then an <c>@[Name]</c> mention by the <see cref="PersonParser"/> rules
    /// (up to the first <c>]</c> on the line, not followed by <c>(</c>, a valid name) that starts right there.
    /// <c>null</c> for anything else. Never throws.
    /// </summary>
    public static WaitingItem? TryParse(string? text)
    {
        if (text is null)
        {
            return null;
        }

        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (text.Length - i < Prefix.Length
            || !text.AsSpan(i, Prefix.Length).Equals(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        i += Prefix.Length;
        while (i < text.Length && text[i] is ' ' or '\t')
        {
            i++;
        }

        if (i + 1 >= text.Length || text[i] != '@' || text[i + 1] != '[')
        {
            return null;
        }

        int open = i + 2;
        int close = text.IndexOfAny([']', '\n', '\r'], open);
        if (close < 0 || text[close] != ']' || (close + 1 < text.Length && text[close + 1] == '('))
        {
            return null;
        }

        string? person = PersonParser.Normalize(text.AsSpan(open, close - open));
        return person is null ? null : new WaitingItem(person, text[(close + 1)..].Trim());
    }

    /// <summary>The todo text <c>waiting: @[person] text</c> for a normalized person and a trimmed text.</summary>
    public static string Compose(string person, string text) => Prefix + " @[" + person + "] " + text;
}
