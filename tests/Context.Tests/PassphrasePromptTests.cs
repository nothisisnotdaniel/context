using Spectre.Console;
using Spectre.Console.Testing;

namespace Context.Tests;

public class PassphrasePromptTests
{
    private static TestConsole Console()
    {
        TestConsole console = new TestConsole().Interactive().EmitAnsiSequences();
        console.Profile.Width = 100;
        console.Profile.Height = 24;
        return console;
    }

    // AC-6, AC-11
    [Fact]
    public void Ask_ShowsLabelAndHint_ReturnsMaskedText()
    {
        TestConsole console = Console();
        console.Input.PushTextWithEnter("ÆØÅæøåÆØÅæøå");

        string? passphrase = new PassphrasePrompt(console).Ask(
            PassphrasePrompt.Label, new Markup("[grey]Esc cancels.[/]"), out bool cancelled);

        Assert.Equal("ÆØÅæøåÆØÅæøå", passphrase);
        Assert.False(cancelled);
        Assert.Contains("Passphrase:", console.Output);
        Assert.Contains("Esc cancels.", console.Output);
        Assert.Contains(new string('•', 12), console.Output);
        Assert.DoesNotContain(console.Output, c => "ÆØÅæøå".Contains(c));
    }

    // AC-6: the label sits in the input panel and the hint on the row above it.
    [Fact]
    public void Ask_DrawsOnTheInputLineAndHintRow()
    {
        TestConsole console = Console();
        console.Input.PushTextWithEnter("x");

        new PassphrasePrompt(console).Ask(PassphrasePrompt.Label, new Markup("hint"), out _);

        List<AnsiWrite> writes = AnsiWrites.Parse(console.Output);
        Assert.Contains(writes, w => w.Row == 21 && w.Column == 1 && w.Text == "hint");
        Assert.Contains(writes, w => w.Row == 23 && w.Column == 3 && w.Text.StartsWith("Passphrase: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Ask_Escape_ReturnsNullCancelled()
    {
        TestConsole console = Console();
        console.Input.PushKey(ConsoleKey.Escape);

        Assert.Null(new PassphrasePrompt(console).Ask(PassphrasePrompt.Label, null, out bool cancelled));
        Assert.True(cancelled);
    }
}
