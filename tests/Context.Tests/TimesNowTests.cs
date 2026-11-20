namespace Context.Tests;

// 277: today's Times bookings for the current view (AC-1..4). Each test has its own Times folder; today and now are
// passed in, so there is no clock.
public sealed class TimesNowTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 19);

    private readonly string folder;

    public TimesNowTests()
    {
        folder = Path.Combine(Path.GetTempPath(), "context-timesnow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private string DayPath => Path.Combine(folder, "2026-11-19.json");

    private void WriteDay(string json) => File.WriteAllText(DayPath, json);

    private void WriteTarget(string json) => File.WriteAllText(Path.Combine(folder, TimesBalance.SettingsFileName), json);

    private TimesNow Read(int hour, int minute) => TimesNow.Read(folder, Today, new TimeOnly(hour, minute));

    private const string Checkpoint =
        """
        {"entries":[
          {"kind":"arrival","time":"08:00"},
          {"kind":"checkpoint","time":"08:05","name":"Review","workItem":1234}
        ]}
        """;

    // AC-1: the spec's example.
    [Fact]
    public void Read_ArrivalAndCheckpoint_GivesBookedFigures()
    {
        WriteDay(Checkpoint);
        WriteTarget("""{"settings":{"daily-target":480}}""");

        TimesNow now = Read(10, 30);

        Assert.Equal(TimesNowKind.Booked, now.Kind);
        Assert.Equal(new TimeOnly(8, 0), now.Arrival);
        Assert.Null(now.Leaving);
        Assert.Equal(new TimeSpan(5, 30, 0), now.Left);
        Assert.Equal("5:30", TimesNow.FormatLeft(now.Left));
        Assert.NotNull(now.Checkpoint);
        Assert.Equal(1234, now.Checkpoint!.WorkItem);
        Assert.Equal("Review", now.Checkpoint.Name);
    }

    // AC-1: the target comes from settings.json.
    [Fact]
    public void Read_OtherTarget_UsesIt()
    {
        WriteDay(Checkpoint);
        WriteTarget("""{"settings":{"daily-target":300}}""");

        Assert.Equal(new TimeSpan(2, 30, 0), Read(10, 30).Left);
    }

    // AC-2: a break start closes the checkpoint and stops the count.
    [Fact]
    public void Read_BreakAfterCheckpoint_NoCheckpointAndCountStops()
    {
        WriteDay(
            """
            {"entries":[
              {"kind":"arrival","time":"08:00"},
              {"kind":"checkpoint","time":"08:05","name":"Review","workItem":1234},
              {"kind":"breakStart","time":"09:00"}
            ]}
            """);

        TimesNow now = Read(10, 30);

        Assert.Equal(TimesNowKind.Booked, now.Kind);
        Assert.Null(now.Checkpoint);
        Assert.Equal(TimeSpan.FromHours(7), now.Left);
    }

    // AC-2: after a leaving, Leaving is set and Left uses the work time up to it.
    [Fact]
    public void Read_Left_SetsLeavingAndUsesWorkTime()
    {
        WriteDay(
            """
            {"entries":[
              {"kind":"arrival","time":"08:00"},
              {"kind":"checkpoint","time":"08:05","name":"Review"},
              {"kind":"leaving","time":"16:00"}
            ]}
            """);

        TimesNow now = Read(18, 0);

        Assert.Equal(new TimeOnly(16, 0), now.Leaving);
        Assert.Null(now.Checkpoint);
        Assert.Equal(TimeSpan.Zero, now.Left);
        Assert.Equal("0:00", TimesNow.FormatLeft(now.Left));
    }

    // AC-2: back from a break, the count goes on; a checkpoint after the break is open again.
    [Fact]
    public void Read_AfterBreakEnd_CountsAgain()
    {
        WriteDay(
            """
            {"entries":[
              {"kind":"arrival","time":"08:00"},
              {"kind":"breakStart","time":"12:00"},
              {"kind":"breakEnd","time":"12:30"},
              {"kind":"checkpoint","time":"12:30","name":"Docs"}
            ]}
            """);

        TimesNow now = Read(13, 30);

        Assert.Equal(new TimeSpan(3, 0, 0), now.Left);
        Assert.Null(now.Checkpoint!.WorkItem);
        Assert.Equal("Docs", now.Checkpoint.Name);
    }

    // AC-2: working past the target gives a negative Left.
    [Fact]
    public void Read_PastTarget_IsNegative()
    {
        WriteDay("""{"entries":[{"kind":"arrival","time":"08:00"}]}""");

        TimesNow now = Read(16, 20);

        Assert.Equal(TimeSpan.FromMinutes(-20), now.Left);
        Assert.Equal("-0:20", TimesNow.FormatLeft(now.Left));
    }

    // AC-2: now before the last entry adds nothing.
    [Fact]
    public void Read_NowBeforeLastEntry_UsesWorkTime()
    {
        WriteDay(Checkpoint);

        Assert.Equal(new TimeSpan(7, 55, 0), Read(7, 0).Left);
    }

    [Theory]
    [InlineData(0L, "0:00")]
    [InlineData(-20L * 60, "-0:20")]
    [InlineData(5L * 3600 + 30 * 60 + 59, "5:30")]
    [InlineData(-59L, "0:00")]
    [InlineData(59L, "0:00")]
    [InlineData(-(1L * 3600 + 5 * 60 + 30), "-1:05")]
    [InlineData(25L * 3600 + 7 * 60, "25:07")]
    public void FormatLeft_IsTimesHMm(long seconds, string expected)
    {
        Assert.Equal(expected, TimesNow.FormatLeft(TimeSpan.FromSeconds(seconds)));
    }

    // AC-3: nothing to show.
    [Fact]
    public void Read_NoFolder_IsNone()
    {
        Assert.Equal(TimesNowKind.None, TimesNow.Read(null, Today, new TimeOnly(10, 0)).Kind);
        Assert.Equal(TimesNowKind.None, TimesNow.Read(Path.Combine(folder, "missing"), Today, new TimeOnly(10, 0)).Kind);
    }

    [Fact]
    public void Read_NoFile_IsNone()
    {
        WriteDay(Checkpoint);
        Assert.Equal(TimesNowKind.None, TimesNow.Read(folder, Today.AddDays(1), new TimeOnly(10, 0)).Kind);
    }

    [Fact]
    public void Read_Absence_IsNone()
    {
        WriteDay("""{"absence":"vacation"}""");
        Assert.Equal(TimesNowKind.None, Read(10, 0).Kind);
    }

    [Fact]
    public void Read_EntriesWithoutArrival_IsNone()
    {
        WriteDay("""{"entries":[{"kind":"checkpoint","time":"08:05","name":"Review"},{"kind":"breakStart","time":"09:00"}]}""");
        Assert.Equal(TimesNowKind.None, Read(10, 0).Kind);
    }

    // AC-3: a day file that can't be read.
    [Fact]
    public void Read_Directory_IsUnreadable()
    {
        Directory.CreateDirectory(DayPath);
        Assert.Equal(TimesNowKind.Unreadable, Read(10, 0).Kind);
    }

    [Fact]
    public void Read_TooBig_IsUnreadable()
    {
        WriteDay("""{"entries":[],"pad":" """ + new string('x', (int)TimesDay.MaxBytes) + "\"}");
        Assert.Equal(TimesNowKind.Unreadable, Read(10, 0).Kind);
    }

    [Fact]
    public void Read_BadJson_IsUnreadable()
    {
        WriteDay("""{"entries":[{"kind":"arrival","time":"8"}""");
        Assert.Equal(TimesNowKind.Unreadable, Read(10, 0).Kind);
    }

    [Fact]
    public async Task Read_Fifo_IsUnreadableWithoutBlocking()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using (System.Diagnostics.Process mkfifo = System.Diagnostics.Process.Start("mkfifo", DayPath))
        {
            mkfifo.WaitForExit();
        }

        Task<TimesNow> read = Task.Run(() => Read(10, 0));
        Assert.Same(read, await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(30))));
        Assert.Equal(TimesNowKind.Unreadable, (await read).Kind);
    }

    // AC-3: a missing or broken settings.json uses 480.
    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("""{"settings":{"daily-target":"x"}}""")]
    [InlineData("""{"settings":{"daily-target":2000}}""")]
    public void Read_MissingOrBrokenSettings_Uses480(string? settings)
    {
        WriteDay(Checkpoint);
        if (settings is not null)
        {
            WriteTarget(settings);
        }

        Assert.Equal(new TimeSpan(5, 30, 0), Read(10, 30).Left);
    }

    // AC-4: reading changes nothing in the Times folder, whatever it finds.
    [Theory]
    [InlineData(Checkpoint)]
    [InlineData("{")]
    [InlineData("""{"absence":"sick"}""")]
    public void Read_ChangesNothing(string json)
    {
        WriteDay(json);
        WriteTarget("""{"settings":{"daily-target":480}}""");
        SortedDictionary<string, string> before = FileSnapshot.Take(folder);
        string[] list = Directory.GetFileSystemEntries(folder).Order(StringComparer.Ordinal).ToArray();

        TimesNow.Read(folder, Today, new TimeOnly(10, 30));
        TimesNow.Read(folder, Today.AddDays(1), new TimeOnly(10, 30));

        Assert.Equal(before, FileSnapshot.Take(folder));
        Assert.Equal(list, Directory.GetFileSystemEntries(folder).Order(StringComparer.Ordinal).ToArray());
    }

    // TimesDay's new figures.
    [Fact]
    public void TimesDay_IsWorkingAndWorkTimeAt()
    {
        WriteDay(Checkpoint);
        TimesDay day = TimesDay.Read(folder, Today);

        Assert.True(day.IsWorking);
        Assert.Equal(new TimeSpan(2, 30, 0), day.WorkTimeAt(new TimeOnly(10, 30)));
        Assert.Equal(TimeSpan.FromMinutes(5), day.WorkTimeAt(new TimeOnly(8, 0)));
    }
}
