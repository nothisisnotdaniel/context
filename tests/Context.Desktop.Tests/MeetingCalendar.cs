namespace Context.Tests;

/// <summary>
/// Calendar text and links for the meeting todo tests (job 287), on <see cref="DesktopFixture.Today"/> (Thursday
/// 2026-10-29; the state's clock is 10:00 local): a weekly "Sync" series (UID <see cref="SyncUid"/>) at 11:00, a
/// one-off "Once" (UID <see cref="OnceUid"/>) at 13:00 and a "Plain" appointment without a UID at 15:00.
/// </summary>
internal static class MeetingCalendar
{
    public const string SyncUid = "sync-1";

    public const string OnceUid = "once-1";

    public static readonly DateTime SyncToday = new(2026, 10, 29, 11, 0, 0);

    public static readonly DateTime OnceToday = new(2026, 10, 29, 13, 0, 0);

    /// <summary><paramref name="vevent"/> with a <c>UID</c> line and <paramref name="extra"/> lines after BEGIN.</summary>
    public static string WithUid(string vevent, string uid, params string[] extra) =>
        vevent.Replace(
            "BEGIN:VEVENT\r\n",
            "BEGIN:VEVENT\r\nUID:" + uid + "\r\n" + string.Concat(extra.Select(e => e + "\r\n")),
            StringComparison.Ordinal);

    /// <summary>The weekly Sync series, with <paramref name="extra"/> lines (for example EXDATE).</summary>
    public static string Sync(params string[] extra) =>
        WithUid(DesktopFixture.Event("Sync", "20261029T110000", "20261029T113000"), SyncUid, ["RRULE:FREQ=WEEKLY", .. extra]);

    public static string Once() => WithUid(DesktopFixture.Event("Once", "20261029T130000", "20261029T140000"), OnceUid);

    public static string Plain() => DesktopFixture.Event("Plain", "20261029T150000", "20261029T160000");

    /// <summary>Sync, Once and Plain in <c>calendar/meetings.ics</c>.</summary>
    public static void WriteAll(DesktopFixture data) =>
        data.WriteCalendar("meetings.ics", DesktopFixture.Calendar(Sync(), Once(), Plain()));

    public static Appointment At(string title, DateTime start, string? uid) =>
        new(title, null, start, start.AddMinutes(30), false, null, uid);

    /// <summary>The link of the Sync occurrence <paramref name="weeks"/> weeks after today's.</summary>
    public static MeetingLink SyncLink(int weeks = 0) => MeetingLink.For(At("Sync", SyncToday.AddDays(7 * weeks), SyncUid))!;

    public static MeetingLink OnceLink() => MeetingLink.For(At("Once", OnceToday, OnceUid))!;

    /// <summary>A store over the fixture's root whose clock ticks a minute per add, so List() order is the add order.</summary>
    public static TodoStore Store(DesktopFixture data)
    {
        DateTime created = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
        return new TodoStore(data.Root, () => created = created.AddMinutes(1));
    }
}
