using AngleSharp.Dom;
using Bunit;

namespace Context.Tests;

// 277 AC-4 to AC-7: today's Times line in the right sidebar, right after the appointments, through Main. The Times
// folder is the fixture's own; the state's UTC clock (09:30 UTC, 10:30 in the fixture's zone) gives today and now.
public sealed class TimesTodayPanelTests : IDisposable
{
    // The panel and the sidebar load on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private const string Booked =
        """
        {"entries":[
          {"kind":"arrival","time":"08:00"},
          {"kind":"checkpoint","time":"08:05","name":"Review","workItem":1234}
        ]}
        """;

    private readonly DesktopFixture data = new();

    public TimesTodayPanelTests()
    {
        data.UtcNow = new DateTime(2026, 10, 29, 9, 30, 0, DateTimeKind.Utc);
        Directory.CreateDirectory(data.TimesFolder);
    }

    public void Dispose() => data.Dispose();

    private void WriteDay(string json) =>
        File.WriteAllText(Path.Combine(data.TimesFolder, "2026-10-29.json"), json);

    private static string Line(IRenderedComponent<Main> cut) =>
        cut.Find("section.times-today").TextContent.Trim();

    private static void WaitForLine(IRenderedComponent<Main> cut, string line) =>
        cut.WaitForAssertion(() => Assert.Equal(line, Line(cut)), Wait);

    // Waits until the sidebar's own loads (appointments, todos) have rendered.
    private static void WaitForSidebar(IRenderedComponent<Main> cut) =>
        cut.WaitForAssertion(
            () =>
            {
                Assert.Contains("No appointments.", cut.Find("section.day-appointments").TextContent, StringComparison.Ordinal);
                Assert.Contains("No todos yet", cut.Find("section.todos").TextContent, StringComparison.Ordinal);
            },
            Wait);

    // AC-5: right after the appointments, one grey line with arrival, time left and the open checkpoint.
    [Fact]
    public async Task Booked_ShowsOneLineAfterAppointments()
    {
        WriteDay(Booked);

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForLine(cut, "Arrived 08:00 · Time left 5:30 · #1234 Review");
        WaitForSidebar(cut);

        IElement aside = cut.Find("aside.todos-region");
        Assert.Contains("day-appointments", aside.Children[0].ClassList);
        IElement section = aside.Children[1];
        Assert.Equal("SECTION", section.TagName);
        Assert.Contains("times-today", section.ClassList);
        Assert.Equal("Times", section.GetAttribute("aria-label"));
        IElement line = Assert.Single(section.Children);
        Assert.Equal("P", line.TagName);
        Assert.Contains("grey", line.ClassList);
        Assert.Equal("Arrived 08:00 · Time left 5:30 · #1234 Review", line.GetAttribute("title"));
        Assert.Empty(section.QuerySelectorAll("button, input, a, [tabindex]"));
    }

    // AC-5: once left: arrival, leaving and time left; no checkpoint part.
    [Fact]
    public async Task Left_ShowsLeavingAndNoCheckpoint()
    {
        WriteDay(
            """
            {"entries":[
              {"kind":"arrival","time":"08:00"},
              {"kind":"checkpoint","time":"08:00","name":"Review","workItem":1234},
              {"kind":"breakStart","time":"12:00"},
              {"kind":"breakEnd","time":"12:55"},
              {"kind":"leaving","time":"16:40"}
            ]}
            """);

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        WaitForLine(cut, "Arrived 08:00 · Left 16:40 · Time left 0:15");
    }

    // AC-5: a checkpoint without a work item shows only its name, cleaned onto one line.
    [Fact]
    public async Task CheckpointWithoutWorkItem_ShowsCleanName()
    {
        WriteDay(
            """
            {"entries":[
              {"kind":"arrival","time":"08:00"},
              {"kind":"checkpoint","time":"08:05","name":"Docs\nand <b>more</b>\u0007"}
            ]}
            """);

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        string expected = "Arrived 08:00 · Time left 5:30 · " + DesktopText.OneLine("Docs\nand <b>more</b>\u0007");
        WaitForLine(cut, expected);
        Assert.Empty(cut.FindAll("section.times-today b"));
        Assert.DoesNotContain('\n', Line(cut));
        Assert.DoesNotContain('\u0007', Line(cut));
    }

    // AC-6: unreadable: one grey line, and every other sidebar panel still renders.
    [Fact]
    public async Task Unreadable_ShowsGreyLine_AndTheRestRenders()
    {
        WriteDay("{");
        data.WriteNote("Plan", "# Plan\nreview:2026-10-01\n");

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        WaitForLine(cut, "Can't read Times' day file.");
        WaitForSidebar(cut);
        cut.WaitForAssertion(
            () => Assert.Contains("Plan", cut.Find("section.to-review").TextContent, StringComparison.Ordinal), Wait);
        IElement line = Assert.Single(cut.Find("section.times-today").Children);
        Assert.Contains("grey", line.ClassList);
    }

    // AC-6, AC-7: None renders nothing: after the day file becomes an absence and Tick runs, the section goes.
    [Fact]
    public async Task None_RendersNothing()
    {
        WriteDay(Booked);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForLine(cut, "Arrived 08:00 · Time left 5:30 · #1234 Review");
        WaitForSidebar(cut);

        WriteDay("""{"absence":"vacation"}""");
        data.State!.Tick();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("section.times-today")), Wait);
        Assert.Contains("day-appointments", cut.Find("aside.todos-region").Children[0].ClassList);
        Assert.DoesNotContain("times-today", cut.Find("aside.todos-region").Children[1].ClassList);
    }

    // AC-7: after the file changes and Tick runs, the new line shows; Loading… never shows in between.
    [Fact]
    public async Task Tick_ReloadsWithoutLoadingFlash()
    {
        WriteDay(Booked);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        List<string> seen = [];
        cut.OnMarkupUpdated += (_, _) =>
        {
            foreach (IElement section in cut.FindAll("section.times-today"))
            {
                lock (seen)
                {
                    seen.Add(section.TextContent);
                }
            }
        };
        WaitForLine(cut, "Arrived 08:00 · Time left 5:30 · #1234 Review");
        WaitForSidebar(cut);

        WriteDay(
            """
            {"entries":[
              {"kind":"arrival","time":"08:00"},
              {"kind":"checkpoint","time":"08:05","name":"Review","workItem":1234},
              {"kind":"checkpoint","time":"09:00","name":"Plan","workItem":42}
            ]}
            """);
        data.UtcNow = data.UtcNow.AddMinutes(1);
        data.State!.Tick();

        WaitForLine(cut, "Arrived 08:00 · Time left 5:29 · #42 Plan");
        lock (seen)
        {
            Assert.DoesNotContain(seen, text => text.Contains("Loading", StringComparison.Ordinal));
        }

        Assert.DoesNotContain("Loading", cut.Find("aside.todos-region").Children[1].TextContent, StringComparison.Ordinal);
    }

    // AC-7: a state change (refresh) reloads it too.
    [Fact]
    public async Task Refresh_Reloads()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForSidebar(cut);

        WriteDay(Booked);
        await data.State!.Refresh();

        WaitForLine(cut, "Arrived 08:00 · Time left 5:30 · #1234 Review");
    }

    // AC-4: loading the panel changes nothing in the Times folder.
    [Fact]
    public async Task Load_ChangesNothingInTimesFolder()
    {
        WriteDay(Booked);
        File.WriteAllText(Path.Combine(data.TimesFolder, TimesBalance.SettingsFileName), """{"settings":{"daily-target":480}}""");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.TimesFolder);

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForLine(cut, "Arrived 08:00 · Time left 5:30 · #1234 Review");
        WaitForSidebar(cut);
        data.State!.Tick();
        await data.State.TimesNow();

        Assert.Equal(before, FileSnapshot.Take(data.TimesFolder));
    }

    // The state's read: today and now from its UTC clock in Zone; never throws.
    [Fact]
    public async Task State_TimesNow_UsesClockInZone()
    {
        // 23:30 UTC is 00:30 the next day in the fixture's zone: that day's file counts, not this one.
        WriteDay(Booked);
        data.UtcNow = new DateTime(2026, 10, 29, 23, 30, 0, DateTimeKind.Utc);
        DesktopState state = data.CreateState();

        Assert.Equal(TimesNowKind.None, (await state.TimesNow()).Kind);

        data.UtcNow = new DateTime(2026, 10, 29, 9, 30, 0, DateTimeKind.Utc);
        TimesNow now = await state.TimesNow();
        Assert.Equal(TimesNowKind.Booked, now.Kind);
        Assert.Equal(new TimeSpan(5, 30, 0), now.Left);
    }
}
