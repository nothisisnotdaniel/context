using System.Text.RegularExpressions;

namespace Context.Tests;

/// <summary>One piece of text written after a cursor move, with every ANSI sequence stripped.</summary>
internal sealed record AnsiWrite
{
    public AnsiWrite(int row, int column, string text)
    {
        Row = row;
        Column = column;
        Text = text;
    }

    public int Row { get; }

    public int Column { get; }

    public string Text { get; }
}

/// <summary>Splits <c>TestConsole</c> output (with <c>EmitAnsiSequences</c>) on cursor moves (<c>ESC[row;colH</c>).</summary>
internal static class AnsiWrites
{
    private static readonly Regex Position = new(@"\u001b\[(\d+);(\d+)H");
    private static readonly Regex Other = new(@"\u001b\[[0-9;?]*[A-Za-z]");

    public static List<AnsiWrite> Parse(string output)
    {
        List<AnsiWrite> writes = [];
        MatchCollection matches = Position.Matches(output);
        for (int i = 0; i < matches.Count; i++)
        {
            Match match = matches[i];
            int start = match.Index + match.Length;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : output.Length;
            string text = Other.Replace(output[start..end], string.Empty);
            writes.Add(new AnsiWrite(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), text));
        }

        return writes;
    }

    /// <summary>The text of every write that starts at column 1 of <paramref name="row"/>, in order.</summary>
    public static List<string> AtRow(string output, int row) =>
        Parse(output).Where(w => w.Row == row && w.Column == 1).Select(w => w.Text).ToList();

    /// <summary><paramref name="output"/> with every ANSI sequence removed.</summary>
    public static string Strip(string output) => Other.Replace(output, string.Empty);
}
