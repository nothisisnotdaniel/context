using Spectre.Console;

namespace Context;

/// <summary>
/// Turns the commands that match the input line into the rows of the command popup that sits
/// just above the input box. Each row is a markup string exactly <c>width</c> cells wide: the name
/// in <c>steelblue1</c>, padded to the longest shown name, then the description in <c>grey</c>.
/// </summary>
internal static class CommandPopup
{
    /// <summary>The popup never shows more rows than this.</summary>
    public const int MaxRows = 8;

    private const string Gap = "  ";

    /// <summary>
    /// Builds the popup rows for <paramref name="matches"/>. At most <paramref name="maxRows"/>
    /// rows (capped at <see cref="MaxRows"/>) are returned. If more commands match than fit, the
    /// last row is a grey <c>… N more</c>. An empty match list gives one grey
    /// <c>No matching commands</c> row.
    /// </summary>
    public static IReadOnlyList<string> Rows(IReadOnlyList<Command> matches, int width, int maxRows)
    {
        width = Math.Max(1, width);
        maxRows = Math.Clamp(maxRows, 1, MaxRows);

        if (matches.Count == 0)
        {
            return [Grey("No matching commands", width)];
        }

        int shownCount = matches.Count <= maxRows ? matches.Count : maxRows - 1;
        List<Command> shown = matches.Take(shownCount).ToList();
        int longest = shown.Count == 0 ? 0 : shown.Max(c => c.Name.Length);

        List<string> rows = [];
        foreach (Command command in shown)
        {
            rows.Add(Row(command, longest, width));
        }

        if (shownCount < matches.Count)
        {
            rows.Add(Grey($"… {matches.Count - shownCount} more", width));
        }

        return rows;
    }

    private static string Row(Command command, int longest, int width)
    {
        string name = Crop(command.Name.PadRight(longest), width);
        int room = width - name.Length;
        string description = room > Gap.Length ? Crop(Gap + Flatten(command.Description), room) : string.Empty;
        string padding = new string(' ', width - name.Length - description.Length);
        return $"[steelblue1]{Markup.Escape(name)}[/][grey]{Markup.Escape(description)}[/]{padding}";
    }

    private static string Grey(string text, int width)
    {
        string cropped = Crop(text, width);
        return $"[grey]{Markup.Escape(cropped)}[/]{new string(' ', width - cropped.Length)}";
    }

    private static string Crop(string text, int width) => text.Length > width ? text[..width] : text;

    // A description must stay on one row, so line breaks and tabs become spaces.
    private static string Flatten(string text) =>
        new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}

/// <summary>
/// Where the command popup may draw: its rows end on <see cref="BottomRow"/> (1-based) and grow
/// upwards, never above row 2 (row 1 holds the <c>Context</c> title). <see cref="RestoreRow"/> redraws a
/// row the popup covered with what the last frame had there.
/// </summary>
internal sealed class PopupArea
{
    public PopupArea(int bottomRow, int width, Action<int> restoreRow)
    {
        BottomRow = bottomRow;
        Width = width;
        RestoreRow = restoreRow;
    }

    public int BottomRow { get; }

    public int Width { get; }

    public Action<int> RestoreRow { get; }

    /// <summary>How many rows fit between row 2 and <see cref="BottomRow"/>, capped at <see cref="CommandPopup.MaxRows"/>.</summary>
    public int MaxRows => Math.Min(CommandPopup.MaxRows, BottomRow - 1);
}
