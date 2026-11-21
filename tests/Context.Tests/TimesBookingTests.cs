namespace Context.Tests;

// 383: the Times panel's arguments, name clean-up and texts. Pure functions; no runner, no clock, no files.
public sealed class TimesBookingTests
{
    private static readonly TimeOnly At = new(9, 5);

    // AC-4: one command and the time as H:mm per action.
    [Theory]
    [InlineData("Arrive", "arrive")]
    [InlineData("Break", "break")]
    [InlineData("BreakEnd", "breakend")]
    [InlineData("Leave", "leave")]
    public void Arguments_CommandAndTime(string actionName, string command)
    {
        TimesAction action = Enum.Parse<TimesAction>(actionName);
        Assert.Equal([command, "9:05"], TimesBooking.Arguments(action, At));
        Assert.Equal([command, "14:30"], TimesBooking.Arguments(action, new TimeOnly(14, 30)));
    }

    // AC-6: checkpoint <t> [#<position>] <name>, each one argument.
    [Fact]
    public void Arguments_Checkpoint_PositionAndName()
    {
        Assert.Equal(["checkpoint", "9:05", "#2", "Review"], TimesBooking.Arguments(TimesAction.Checkpoint, At, 2, "Review"));
        Assert.Equal(["checkpoint", "9:05", "Review"], TimesBooking.Arguments(TimesAction.Checkpoint, At, null, "Review"));
        Assert.Equal(["checkpoint", "9:05", "Review"], TimesBooking.Arguments(TimesAction.Checkpoint, At, 0, "Review"));
        Assert.Equal(
            ["checkpoint", "9:05", "x y"],
            TimesBooking.Arguments(TimesAction.Checkpoint, At, null, "-x \n y"));
        Assert.Equal(["checkpoint", "9:05", "3 y"], TimesBooking.Arguments(TimesAction.Checkpoint, At, null, "#3 y"));
    }

    // AC-6: one line, collapsed, leading # and - dropped, at most 200; blank gives empty.
    [Theory]
    [InlineData("Review", "Review")]
    [InlineData("  a \t\n b  ", "a b")]
    [InlineData("--version", "version")]
    [InlineData("## - #05 Weekly", "05 Weekly")]
    [InlineData("Weekly #05", "Weekly #05")]
    [InlineData("#", "")]
    [InlineData(" - ", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void CleanName_Rules(string? name, string expected)
    {
        Assert.Equal(expected, TimesBooking.CleanName(name));
    }

    [Fact]
    public void CleanName_CappedAt200_NeverInsideASurrogatePair()
    {
        Assert.Equal(new string('x', 200), TimesBooking.CleanName(new string('x', 300)));
        string cut = TimesBooking.CleanName(new string('x', 199) + "😀" + "tail");
        Assert.Equal(new string('x', 199), cut);
    }

    // AC-7: the booked line per action.
    [Theory]
    [InlineData("Arrive", null, "Booked arrival at 9:05 in Times.")]
    [InlineData("Break", null, "Booked break at 9:05 in Times.")]
    [InlineData("BreakEnd", null, "Booked break end at 9:05 in Times.")]
    [InlineData("Leave", null, "Booked leaving at 9:05 in Times.")]
    [InlineData("Checkpoint", " -Plan\nit ", "Booked checkpoint Plan it at 9:05 in Times.")]
    public void BookedText_PerAction(string actionName, string? name, string expected)
    {
        TimesAction action = Enum.Parse<TimesAction>(actionName);
        Assert.Equal(expected, TimesBooking.BookedText(action, At, name));
    }

    // AC-8: the failure texts, as job 381's.
    [Fact]
    public void Outcome_TextsPerKind()
    {
        Assert.True(TimesBooking.Outcome(new TimesRunResult(TimesRunKind.Done, 0, "ok", "warn"), out string text));
        Assert.Equal(string.Empty, text);
        Assert.False(TimesBooking.Outcome(TimesRunResult.NotFound(), out text));
        Assert.Equal(MeetingNote.TimesNotFoundText, text);
        Assert.False(TimesBooking.Outcome(TimesRunResult.TimedOut(), out text));
        Assert.Equal(MeetingNote.TimesTimedOutText, text);
        Assert.False(TimesBooking.Outcome(new TimesRunResult(TimesRunKind.Done, 1, "out", "\n \u001b[31mno\u001b[0m way\nmore"), out text));
        Assert.Equal("Times refused the booking: �[31mno�[0m way", text);
        Assert.False(TimesBooking.Outcome(new TimesRunResult(TimesRunKind.Done, 4, string.Empty, string.Empty), out text));
        Assert.Equal("Times refused the booking: exit code 4", text);
    }
}
