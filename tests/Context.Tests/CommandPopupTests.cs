using Spectre.Console;
using Spectre.Console.Testing;

namespace Context.Tests;

public class CommandPopupTests
{
    private static Command Cmd(string name, string description) => new(name, description, _ => CommandResult.None);

    private static List<Command> Many(int count) =>
        Enumerable.Range(1, count).Select(i => Cmd($"/c{i:00}", $"Command {i}")).ToList();

    /// <summary>Renders one markup row and returns the plain text (no ANSI) and the raw ANSI output.</summary>
    private static (string Plain, string Ansi) Render(string row, int width = 80)
    {
        TestConsole plain = new TestConsole();
        plain.Profile.Width = width;
        plain.Markup(row);

        TestConsole ansi = new TestConsole().EmitAnsiSequences();
        ansi.Profile.Width = width;
        ansi.Markup(row);

        return (plain.Output, ansi.Output);
    }

    [Fact]
    public void Row_ShowsNamePaddedToLongest_ThenDescription()
    {
        IReadOnlyList<string> rows = CommandPopup.Rows([Cmd("/help", "List all commands"), Cmd("/today", "Show today")], 80, 8);

        Assert.Equal(2, rows.Count);
        Assert.StartsWith("/help   List all commands", Render(rows[0]).Plain);
        Assert.StartsWith("/today  Show today", Render(rows[1]).Plain);
    }

    [Fact]
    public void Row_NameIsSteelBlue_DescriptionIsGrey()
    {
        string row = CommandPopup.Rows([Cmd("/help", "List all commands")], 80, 8)[0];

        Assert.StartsWith("[steelblue1]/help[/][grey]  List all commands[/]", row);
    }

    // AC-7
    [Fact]
    public void MoreMatchesThanRows_ShowsEightRows_LastIsMore()
    {
        IReadOnlyList<string> rows = CommandPopup.Rows(Many(12), 80, 20);

        Assert.Equal(CommandPopup.MaxRows, rows.Count);
        Assert.Equal(8, rows.Count);
        Assert.StartsWith("/c07", Render(rows[6]).Plain);
        Assert.Equal("… 5 more", Render(rows[7]).Plain.TrimEnd());
        Assert.StartsWith("[grey]", rows[7]);
    }

    // AC-7
    [Fact]
    public void ExactlyEightMatches_ShowsAllWithoutMoreRow()
    {
        IReadOnlyList<string> rows = CommandPopup.Rows(Many(8), 80, 8);

        Assert.Equal(8, rows.Count);
        Assert.DoesNotContain(rows, r => r.Contains("more", StringComparison.Ordinal));
    }

    // AC-7
    [Fact]
    public void SmallRowBudget_IsRespected()
    {
        IReadOnlyList<string> rows = CommandPopup.Rows(Many(5), 80, 3);

        Assert.Equal(3, rows.Count);
        Assert.Equal("… 3 more", Render(rows[2]).Plain.TrimEnd());
    }

    // AC-7
    [Theory]
    [InlineData(21, 8)]
    [InlineData(9, 8)]
    [InlineData(4, 3)]
    [InlineData(2, 1)]
    public void PopupArea_NeverReachesRowOne(int bottomRow, int expected)
    {
        Assert.Equal(expected, new PopupArea(bottomRow, 80, _ => { }).MaxRows);
    }

    // AC-8
    [Fact]
    public void NoMatches_ShowsOneGreyRow()
    {
        IReadOnlyList<string> rows = CommandPopup.Rows([], 80, 8);

        Assert.Single(rows);
        (string plain, string ansi) = Render(rows[0]);
        Assert.Equal("No matching commands", plain.TrimEnd());
        Assert.Contains("\u001b[38;5;8mNo matching commands", ansi);
    }

    // AC-10
    [Fact]
    public void MarkupCharacters_AreDrawnLiterally()
    {
        string row = CommandPopup.Rows([Cmd("/x[y]", "Tick [x] off [/]")], 80, 8)[0];

        Assert.StartsWith("/x[y]  Tick [x] off [/]", Render(row).Plain);
    }

    // AC-10
    [Theory]
    [InlineData(80)]
    [InlineData(30)]
    [InlineData(4)]
    public void EveryRow_IsExactlyTheTerminalWidth(int width)
    {
        List<Command> commands = [Cmd("/help", new string('d', 200)), Cmd("/q", "short"), Cmd("/multi", "line\nbreak")];

        foreach (string row in CommandPopup.Rows(commands, width, 8))
        {
            string plain = Render(row, width).Plain;
            Assert.DoesNotContain('\n', plain);
            Assert.Equal(width, plain.Length);
        }
    }

    // AC-10
    [Fact]
    public void LongDescription_IsCroppedToOneRow()
    {
        string row = CommandPopup.Rows([Cmd("/help", new string('d', 200))], 40, 8)[0];

        Assert.Equal("/help  " + new string('d', 33), Render(row, 40).Plain);
    }
}
