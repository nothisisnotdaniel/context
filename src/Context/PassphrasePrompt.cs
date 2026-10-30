using Spectre.Console;
using Spectre.Console.Rendering;

namespace Context;

/// <summary>
/// Asks for a passphrase in the input panel: the label (for example <c>Passphrase:</c>) replaces the <c>&gt;</c>
/// prompt and every typed character shows as <c>•</c> (<see cref="LineEditor.ReadSecret"/>). An optional hint
/// goes on the hint row just above the panel. Draws in place over the current frame with
/// <c>Cursor.SetPosition</c>; the next full redraw of <see cref="App"/> replaces it. The passphrase is never
/// written to the console or kept.
/// </summary>
internal sealed class PassphrasePrompt
{
    /// <summary>The label for the start-up prompt and <c>/decrypt</c>.</summary>
    public const string Label = "Passphrase: ";

    private readonly IAnsiConsole console;
    private readonly LineEditor editor;

    public PassphrasePrompt(IAnsiConsole console)
    {
        this.console = console;
        editor = new LineEditor(console, new CommandRegistry());
    }

    /// <summary>
    /// Shows <paramref name="hint"/> on the hint row (when given) and <paramref name="label"/> in the input panel,
    /// then reads a masked line. Returns it, or <c>null</c> with <paramref name="cancelled"/> true after Esc, or
    /// <c>null</c> with it false when the input has no more keys.
    /// </summary>
    public string? Ask(string label, IRenderable? hint, out bool cancelled)
    {
        int width = console.Profile.Width;
        int height = console.Profile.Height;
        if (hint is not null)
        {
            console.Cursor.SetPosition(1, height - App.InputHeight);
            console.Write(new Text(new string(' ', Math.Max(0, width - 1))));
            console.Cursor.SetPosition(1, height - App.InputHeight);
            console.Write(hint);
        }

        // Inside the panel: border + 1 padding on the left, then the label.
        int line = height - 1;
        int start = 3;
        int column = start + label.Length;
        int room = Math.Max(1, width - column - 2);
        console.Cursor.SetPosition(start, line);
        console.Write(new Text(label + new string(' ', room - 1), new Style(Color.SteelBlue1)));
        return editor.ReadSecret(column, line, room, out cancelled);
    }

    /// <summary>Like <see cref="Ask(string, IRenderable?, out bool)"/>, but Esc and the end of input both give <c>null</c>.</summary>
    public string? Ask(string label, IRenderable? hint) => Ask(label, hint, out _);
}
