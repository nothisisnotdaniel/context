namespace Context.Tests;

// 214 AC-3: on today the last past appointment plus every one not yet past; on any other day all of them.
public sealed class AppointmentListTests
{
    private static readonly DateTime Day = new(2026, 11, 18);

    private static Appointment At(string title, int fromHour, int toHour) =>
        new(title, null, Day.AddHours(fromHour), Day.AddHours(toHour), false);

    private static Appointment AllDay(string title) => new(title, null, Day, Day.AddDays(1), true);

    private static string[] Titles(IReadOnlyList<Appointment> shown) => shown.Select(a => a.Title).ToArray();

    [Fact]
    public void Shown_Today_LastPastPlusUpcoming()
    {
        Appointment[] day = [At("A", 8, 9), At("B", 9, 10), At("C", 11, 12), At("D", 13, 14)];

        Assert.Equal(["B", "C", "D"], Titles(AppointmentList.Shown(day, Day.AddHours(10).AddMinutes(30), true)));
    }

    [Fact]
    public void Shown_EndEqualsNow_CountsAsPast()
    {
        Appointment[] day = [At("A", 8, 9), At("B", 9, 10), At("C", 10, 11)];

        Assert.Equal(["B", "C"], Titles(AppointmentList.Shown(day, Day.AddHours(10), true)));
    }

    [Fact]
    public void Shown_NothingPast_ShowsAll()
    {
        Appointment[] day = [At("A", 8, 9), At("B", 9, 10)];

        Assert.Equal(["A", "B"], Titles(AppointmentList.Shown(day, Day.AddHours(7), true)));
    }

    [Fact]
    public void Shown_AllPast_OnlyTheLast()
    {
        Appointment[] day = [At("A", 8, 9), At("B", 9, 10), At("C", 10, 11)];

        Assert.Equal(["C"], Titles(AppointmentList.Shown(day, Day.AddHours(20), true)));
    }

    // The last past one is the last in day order, not the one that ended latest; a running one is not past.
    [Fact]
    public void Shown_LastPastIsLastInOrder()
    {
        Appointment[] day = [At("Long", 7, 10), At("Short", 8, 9), At("Running", 10, 12)];

        Assert.Equal(["Short", "Running"], Titles(AppointmentList.Shown(day, Day.AddHours(11), true)));
    }

    [Fact]
    public void Shown_AllDay_StaysAllDayLong()
    {
        Appointment[] day = [AllDay("Holiday"), At("A", 8, 9), At("B", 9, 10)];

        Assert.Equal(["Holiday", "B"], Titles(AppointmentList.Shown(day, Day.AddHours(23).AddMinutes(59), true)));
    }

    [Fact]
    public void Shown_OtherDay_ShowsAll()
    {
        Appointment[] day = [At("A", 8, 9), At("B", 9, 10), At("C", 10, 11)];

        Assert.Equal(["A", "B", "C"], Titles(AppointmentList.Shown(day, Day.AddDays(3), false)));
    }

    [Fact]
    public void Shown_Empty_Empty()
    {
        Assert.Empty(AppointmentList.Shown([], Day, true));
    }
}
