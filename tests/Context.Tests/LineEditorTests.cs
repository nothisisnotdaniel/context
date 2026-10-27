using Spectre.Console.Testing;

namespace Context.Tests;

public class LineEditorTests
{
    private static (TestConsole Console, LineEditor Editor) Create()
    {
        TestConsole console = new TestConsole().Interactive();
        return (console, new LineEditor(console, CommandRegistry.CreateDefault()));
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

    // Command popup (job 010) --------------------------------------------------------------

    private const int HintRow = 21;

    private sealed class PopupRun
    {
        public PopupRun(int bottomRow = HintRow)
        {
            Console = new TestConsole().Interactive().EmitAnsiSequences();
            CommandRegistry registry = CommandRegistry.CreateDefault();
            registry.Register("/prev", "Go to the previous day", _ => CommandResult.None);
            registry.Register("/next", "Go to the next day", _ => CommandResult.None);
            registry.Register("/today", "Go back to today", _ => CommandResult.None);
            Editor = new LineEditor(Console, registry);
            Area = new PopupArea(bottomRow, 80, row =>
            {
                Restored.Add(row);
                Console.Cursor.SetPosition(1, row);
                Console.Write(new Spectre.Console.Text($"restored {row}"));
            });
        }

        public TestConsole Console { get; }

        public LineEditor Editor { get; }

        public PopupArea Area { get; }

        public List<int> Restored { get; } = [];

        public string? Read() => Editor.ReadLine(5, 23, 70, Area);

        /// <summary>The text last written at each row, like a screen.</summary>
        public Dictionary<int, string> Screen()
        {
            Dictionary<int, string> screen = [];
            foreach (AnsiWrite write in AnsiWrites.Parse(Console.Output).Where(w => w.Column == 1))
            {
                screen[write.Row] = write.Text;
            }

            return screen;
        }

        public int PopupDraws() => AnsiWrites.AtRow(Console.Output, HintRow).Count(t => !t.StartsWith("restored", StringComparison.Ordinal));
    }

    // AC-3
    [Fact]
    public void Slash_OpensPopup_EndingOnHintRow_ListingEveryCommand()
    {
        PopupRun run = new PopupRun();
        run.Console.Input.PushText("/");
        run.Console.Input.PushKey(ConsoleKey.Enter);

        run.Read();

        string output = run.Console.Output;
        Assert.Contains("\u001b[21;1H", output);
        List<AnsiWrite> rows = AnsiWrites.Parse(output).Where(w => w.Column == 1 && !w.Text.StartsWith("restored", StringComparison.Ordinal)).ToList();
        Assert.Equal([17, 18, 19, 20, 21], rows.Select(r => r.Row));
        Assert.StartsWith("/help   List all commands", rows[0].Text);
        Assert.StartsWith("/next   Go to the next day", rows[1].Text);
        Assert.StartsWith("/prev   Go to the previous day", rows[2].Text);
        Assert.StartsWith("/quit   Exit Context", rows[3].Text);
        Assert.StartsWith("/today  Go back to today", rows[4].Text);
    }

    [Fact]
    public void NoPopupArea_DrawsNoPopup()
    {
        TestConsole console = new TestConsole().Interactive().EmitAnsiSequences();
        LineEditor editor = new LineEditor(console, CommandRegistry.CreateDefault());
        console.Input.PushTextWithEnter("/he");

        Assert.Equal("/he", editor.ReadLine(5, 23, 70));
        Assert.DoesNotContain("List all commands", console.Output);
    }

    [Fact]
    public void TextWithoutSlash_OpensNoPopup()
    {
        PopupRun run = new PopupRun();
        run.Console.Input.PushTextWithEnter("help");

        run.Read();

        Assert.Equal(0, run.PopupDraws());
        Assert.DoesNotContain("List all commands", run.Console.Output);
    }

    // AC-4
    [Fact]
    public void EachKey_RedrawsPopup_WithCurrentMatch()
    {
        PopupRun run = new PopupRun();
        run.Console.Input.PushText("/tdoay");

        run.Console.Input.PushKey(ConsoleKey.Enter);
        run.Read();

        Assert.Equal(6, run.PopupDraws());
        Assert.StartsWith("/today", AnsiWrites.AtRow(run.Console.Output, HintRow).Last(t => !t.StartsWith("restored", StringComparison.Ordinal)));
    }

    // AC-4
    [Fact]
    public void ShrinkingPopup_RestoresRowsItNoLongerUses()
    {
        PopupRun run = new PopupRun();
        run.Console.Input.PushText("/t");
        run.Console.Input.PushKey(ConsoleKey.Escape);
        run.Console.Input.PushKey(ConsoleKey.Enter);

        run.Read();

        // "/" fills rows 17-21, "/t" only row 21: rows 17-20 come back first, row 21 on Esc.
        Assert.Equal([17, 18, 19, 20, 21], run.Restored);
        Dictionary<int, string> screen = run.Screen();
        Assert.Equal("restored 17", screen[17]);
        Assert.Equal("restored 21", screen[21]);
    }

    // AC-4
    [Fact]
    public void Backspace_RedrawsPopup()
    {
        PopupRun run = new PopupRun();
        run.Console.Input.PushText("/t");
        run.Console.Input.PushKey(ConsoleKey.Backspace);
        run.Console.Input.PushKey(ConsoleKey.Escape);
        run.Console.Input.PushKey(ConsoleKey.Enter);

        run.Read();

        // After Backspace the popup lists all five commands again, starting on row 17.
        Assert.Equal(3, run.PopupDraws());
        Assert.Equal(2, AnsiWrites.AtRow(run.Console.Output, 17).Count(t => t.StartsWith("/help", StringComparison.Ordinal)));
    }

    // AC-4
    [Fact]
    public void CursorReturnsToEndOfInputLine_AfterEveryPopupRedraw()
    {
        PopupRun run = new PopupRun();
        run.Console.Input.PushText("/he");
        run.Console.Input.PushKey(ConsoleKey.Enter);

        run.Read();

        List<AnsiWrite> writes = AnsiWrites.Parse(run.Console.Output);
        Assert.Equal(new AnsiWrite(23, 8, string.Empty), writes[^1]);
        // Every popup draw on row 21 is followed, eventually, by a cursor move back to row 23.
        for (int i = 0; i < writes.Count; i++)
        {
            if (writes[i].Row == HintRow)
            {
                Assert.Contains(writes.Skip(i + 1), w => w.Row == 23);
            }
        }
    }

    // AC-5
    [Fact]
    public void BackspaceOnSlash_ClosesPopup_AndRestoresEveryRow()
    {
        PopupRun run = new PopupRun();
        run.Console.Input.PushText("/");
        run.Console.Input.PushKey(ConsoleKey.Backspace);
        run.Console.Input.PushKey(ConsoleKey.Enter);

        Assert.Equal("", run.Read());
        Assert.Equal([17, 18, 19, 20, 21], run.Restored);
        Dictionary<int, string> screen = run.Screen();
        for (int row = 17; row <= 21; row++)
        {
            Assert.Equal($"restored {row}", screen[row]);
        }
    }

    // AC-5
    [Fact]
    public void Enter_ClosesPopup_AndReturnsTypedLine()
    {
        PopupRun run = new PopupRun();
        run.Console.Input.PushTextWithEnter("/tdoay x");

        Assert.Equal("/tdoay x", run.Read());
        // "/" covered rows 17-21; "/t" shrank it to row 21, and Enter gives row 21 back.
        Assert.Equal([17, 18, 19, 20, 21], run.Restored);
        Dictionary<int, string> screen = run.Screen();
        for (int row = 17; row <= 21; row++)
        {
            Assert.Equal($"restored {row}", screen[row]);
        }
    }

    // AC-5
    [Fact]
    public void Enter_WithoutPopup_RestoresNothing()
    {
        PopupRun run = new PopupRun();
        run.Console.Input.PushTextWithEnter("abc");

        Assert.Equal("abc", run.Read());
        Assert.Empty(run.Restored);
    }

    // AC-6
    [Fact]
    public void Escape_WithPopupOpen_ClosesIt_AndKeepsText()
    {
        PopupRun run = new PopupRun();
        run.Console.Input.PushText("/he");
        run.Console.Input.PushKey(ConsoleKey.Escape);
        run.Console.Input.PushKey(ConsoleKey.Enter);

        Assert.Equal("/he", run.Read());
        // "/he" matches /help and /next (rows 20-21); Esc gives both rows back.
        Assert.Equal([20, 21], run.Restored.TakeLast(2));
        Dictionary<int, string> screen = run.Screen();
        for (int row = 17; row <= 21; row++)
        {
            Assert.Equal($"restored {row}", screen[row]);
        }
    }

    // AC-6
    [Fact]
    public void SecondEscape_ClearsTheLine()
    {
        PopupRun run = new PopupRun();
        run.Console.Input.PushText("/he");
        run.Console.Input.PushKey(ConsoleKey.Escape);
        run.Console.Input.PushKey(ConsoleKey.Escape);
        run.Console.Input.PushKey(ConsoleKey.Enter);

        Assert.Equal("", run.Read());
    }

    // AC-6
    [Fact]
    public void AfterEscape_PopupStaysClosed_UntilLineStopsStartingWithSlash()
    {
        PopupRun run = new PopupRun();
        run.Console.Input.PushText("/he");
        run.Console.Input.PushKey(ConsoleKey.Escape);
        run.Console.Input.PushText("l");
        run.Console.Input.PushKey(ConsoleKey.Backspace);
        run.Console.Input.PushKey(ConsoleKey.Backspace);
        run.Console.Input.PushKey(ConsoleKey.Backspace);
        run.Console.Input.PushKey(ConsoleKey.Backspace);
        run.Console.Input.PushText("/");
        run.Console.Input.PushKey(ConsoleKey.Enter);

        Assert.Equal("/", run.Read());
        // "/", "/h", "/he" drew the popup; "l" and the Backspaces did not; the new "/" did.
        Assert.Equal(4, run.PopupDraws());
        Assert.Equal(2, AnsiWrites.AtRow(run.Console.Output, 17).Count(t => t.StartsWith("/help", StringComparison.Ordinal)));
    }

    // AC-6
    [Fact]
    public void AfterSecondEscape_TypingSlash_OpensPopupAgain()
    {
        PopupRun run = new PopupRun();
        run.Console.Input.PushText("/he");
        run.Console.Input.PushKey(ConsoleKey.Escape);
        run.Console.Input.PushKey(ConsoleKey.Escape);
        run.Console.Input.PushText("/");
        run.Console.Input.PushKey(ConsoleKey.Enter);

        Assert.Equal("/", run.Read());
        Assert.Equal(4, run.PopupDraws());
    }

    // AC-7
    [Fact]
    public void SmallTerminal_PopupNeverCoversRowOne()
    {
        PopupRun run = new PopupRun(bottomRow: 3);
        run.Console.Input.PushText("/");
        run.Console.Input.PushKey(ConsoleKey.Enter);

        run.Read();

        List<AnsiWrite> rows = AnsiWrites.Parse(run.Console.Output).Where(w => w.Column == 1).ToList();
        Assert.DoesNotContain(rows, w => w.Row < 2);
        Assert.Contains(rows, w => w.Row == 2 && w.Text.StartsWith("/help", StringComparison.Ordinal));
        Assert.Contains(rows, w => w.Row == 3 && w.Text.StartsWith("… 4 more", StringComparison.Ordinal));
    }

    // AC-9
    [Fact]
    public void Typing_NeverClearsTheScreen()
    {
        PopupRun run = new PopupRun();
        run.Console.Input.PushText("/tdoay");
        run.Console.Input.PushKey(ConsoleKey.Backspace);
        run.Console.Input.PushKey(ConsoleKey.Escape);
        run.Console.Input.PushKey(ConsoleKey.Escape);
        run.Console.Input.PushKey(ConsoleKey.Enter);

        run.Read();

        Assert.DoesNotContain("\u001b[2J", run.Console.Output);
    }
}
