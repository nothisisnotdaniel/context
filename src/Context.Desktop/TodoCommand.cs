namespace Context;

/// <summary>
/// The <c>todo</c> command's words after the command name (job 282, moved out of <c>Main</c>'s <c>/todo</c>), shared
/// by the palette and the command bar: <c>&lt;yyyy-MM-dd|today|tomorrow&gt; [HH:mm] &lt;text&gt;</c> or
/// <c>every &lt;weekday|day|month&gt; &lt;text&gt;</c>. A time-shaped word after the date is the time, never text
/// (not after <c>every</c>).
/// </summary>
internal sealed class TodoCommand
{
    public const string RepeatUsage = "todo every <weekday|day|month> <text>";

    public const string DateUsage = "todo <yyyy-MM-dd|today|tomorrow> <text>";

    public const string TimeUsage = "todo <yyyy-MM-dd|today|tomorrow> [HH:mm] <text>";

    private TodoCommand(string text, DateOnly due, TodoRepeat? repeat, TimeOnly? time)
    {
        Text = text;
        Due = due;
        Repeat = repeat;
        Time = time;
    }

    /// <summary>The todo's text as typed (the action cleans and checks it).</summary>
    public string Text { get; }

    public DateOnly Due { get; }

    public TodoRepeat? Repeat { get; }

    public TimeOnly? Time { get; }

    /// <summary>
    /// Reads <paramref name="rest"/> (the text after <c>todo</c>, trimmed). False with the usage (one of
    /// <see cref="RepeatUsage"/>, <see cref="DateUsage"/>, <see cref="TimeUsage"/>, without <c>Type</c>, a
    /// <c>/</c> or the full stop) when it isn't a todo.
    /// </summary>
    public static bool TryParse(string rest, DateOnly today, out TodoCommand? parsed, out string usage)
    {
        parsed = null;
        usage = string.Empty;
        int split = rest.IndexOfAny([' ', '\t']);
        TodoRepeat? repeat = null;
        DateOnly due;
        if (string.Equals(split < 0 ? rest : rest[..split], "every", StringComparison.OrdinalIgnoreCase))
        {
            // every <unit> <text>: the unit is the next word, the text the rest.
            string every = split < 0 ? string.Empty : rest[(split + 1)..].TrimStart();
            int unitEnd = every.IndexOfAny([' ', '\t']);
            if (unitEnd < 0 || every[(unitEnd + 1)..].Trim().Length == 0
                || !TodoInput.TryParseRepeat(every[..unitEnd], today, out TodoRepeat unit, out due))
            {
                usage = RepeatUsage;
                return false;
            }

            repeat = unit;
            rest = every;
            split = unitEnd;
        }
        else if (split < 0 || !DesktopText.TryParseDue(rest[..split], today, out due))
        {
            usage = DateUsage;
            return false;
        }

        string text = rest[(split + 1)..].TrimStart();
        TimeOnly? time = null;
        int timeEnd = text.IndexOfAny([' ', '\t']);
        string timeWord = timeEnd < 0 ? text : text[..timeEnd];
        if (repeat is null && TodoInput.LooksLikeTime(timeWord))
        {
            if (timeEnd < 0 || text[(timeEnd + 1)..].Trim().Length == 0
                || !TodoInput.TryParseTime(timeWord, out TimeOnly parsedTime))
            {
                usage = TimeUsage;
                return false;
            }

            time = parsedTime;
            text = text[(timeEnd + 1)..];
        }

        parsed = new TodoCommand(text, due, repeat, time);
        return true;
    }

    public override string ToString() => nameof(TodoCommand);
}
