using Spectre.Console.Testing;

namespace Context.Tests;

public class LineEditorTests
{
    private static (TestConsole Console, LineEditor Editor) Create()
    {
        TestConsole console = new TestConsole().Interactive();
        return (console, new LineEditor(console));
    }

    private static string? Read(LineEditor editor) => editor.ReadLine(5, 23, 70);

    [Fact]
    public void PrintableCharacters_AndEnter_ReturnTheLine()
    {
        (TestConsole console, LineEditor editor) = Create();
        console.Input.PushTextWithEnter("/help me");

        Assert.Equal("/help me", Read(editor));
    }

    [Fact]
    public void TypedText_IsEchoed()
    {
        (TestConsole console, LineEditor editor) = Create();
        console.Input.PushTextWithEnter("/hel");

        Read(editor);

        Assert.Contains("/hel", console.Output);
    }

    // AC-8
    [Fact]
    public void Backspace_RemovesLastCharacter()
    {
        (TestConsole console, LineEditor editor) = Create();
        console.Input.PushText("abc");
        console.Input.PushKey(ConsoleKey.Backspace);
        console.Input.PushTextWithEnter("d");

        Assert.Equal("abd", Read(editor));
    }

    // AC-8
    [Fact]
    public void Escape_ClearsTheLine()
    {
        (TestConsole console, LineEditor editor) = Create();
        console.Input.PushText("abc");
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushTextWithEnter("x");

        Assert.Equal("x", Read(editor));
    }

    // AC-8
    [Fact]
    public void Backspace_OnEmptyLine_DoesNothing()
    {
        (TestConsole console, LineEditor editor) = Create();
        console.Input.PushKey(ConsoleKey.Backspace);
        console.Input.PushKey(ConsoleKey.Enter);

        Assert.Equal(string.Empty, Read(editor));
    }

    [Fact]
    public void Backspace_OnEmptyLine_ThenText_KeepsText()
    {
        (TestConsole console, LineEditor editor) = Create();
        console.Input.PushKey(ConsoleKey.Backspace);
        console.Input.PushTextWithEnter("/q");

        Assert.Equal("/q", Read(editor));
    }
}
