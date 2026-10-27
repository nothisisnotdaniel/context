using Spectre.Console;

namespace Context;

/// <summary>
/// A small line editor for the input box. It reads keys with <c>ReadKey(intercept: true)</c> and
/// redraws only the input line via <c>Cursor.SetPosition</c>, never the whole screen. Supports
/// printable characters, Backspace, Enter and Esc (clears the line).
/// </summary>
internal sealed class LineEditor
{
    private readonly IAnsiConsole console;

    public LineEditor(IAnsiConsole console)
    {
        this.console = console;
    }

    /// <summary>
    /// Reads one line. <paramref name="column"/> and <paramref name="line"/> are the 1-based screen
    /// position where the text starts, and <paramref name="width"/> is the room available for it.
    /// Returns <c>null</c> if the input has no more keys.
    /// </summary>
    public string? ReadLine(int column, int line, int width)
    {
        string buffer = string.Empty;
        int drawn = 0;

        while (true)
        {
            ConsoleKeyInfo? key = console.Input.ReadKey(intercept: true);
            if (key is null)
            {
                return null;
            }

            ConsoleKeyInfo info = key.Value;
            switch (info.Key)
            {
                case ConsoleKey.Enter:
                    return buffer;

                case ConsoleKey.Backspace:
                    if (buffer.Length > 0)
                    {
                        buffer = buffer[..^1];
                    }

                    break;

                case ConsoleKey.Escape:
                    buffer = string.Empty;
                    break;

                default:
                    if (info.KeyChar != '\0' && !char.IsControl(info.KeyChar))
                    {
                        buffer += info.KeyChar;
                    }

                    break;
            }

            drawn = Redraw(buffer, column, line, width, drawn);
        }
    }

    // Rewrites the visible part of the buffer (its tail, if it is wider than the box) and blanks
    // whatever was drawn before. Returns the number of characters now drawn.
    private int Redraw(string buffer, int column, int line, int width, int previouslyDrawn)
    {
        int room = Math.Max(1, width - 1);
        string visible = buffer.Length > room ? buffer[^room..] : buffer;

        console.Cursor.SetPosition(column, line);
        console.Write(new Text(visible.PadRight(previouslyDrawn)));
        console.Cursor.SetPosition(column + visible.Length, line);
        return visible.Length;
    }
}
