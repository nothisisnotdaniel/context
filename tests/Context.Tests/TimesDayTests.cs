using System.Text;

namespace Context.Tests;

// 171: Times' day file, read only (AC-1) and the Times folder (AC-2).
public sealed class TimesDayTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 11, 14);

    private readonly string folder;

    public TimesDayTests()
    {
        folder = Path.Combine(Path.GetTempPath(), "context-timesday-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    internal const string Example =
        """
        {"entries":[
          {"kind":"arrival","time":"08:00"},
          {"kind":"checkpoint","time":"08:00","name":"A","workItem":12},
          {"kind":"breakStart","time":"12:00"},
          {"kind":"breakEnd","time":"12:30"},
          {"kind":"checkpoint","time":"12:30","name":"B"},
          {"kind":"leaving","time":"17:00"}
        ]}
        """;

    private string Write(string json)
    {
        string path = Path.Combine(folder, "2026-11-14.json");
        File.WriteAllText(path, json);
        return path;
    }

    private TimesDay Read(string json)
    {
        Write(json);
        return TimesDay.Read(folder, Day);
    }

    // AC-1: the spec's example: 8:30 worked, #12 A 4:00, B 4:30.
    [Fact]
    public void Read_Example_GivesTimesFigures()
    {
        TimesDay day = Read(Example);

        Assert.Equal(TimesDayKind.Entries, day.Kind);
        Assert.Equal(new TimeOnly(8, 0), day.Arrival);
        Assert.Equal(new TimeOnly(17, 0), day.Leaving);
        Assert.True(day.HasLeft);
        Assert.Equal(new TimeSpan(8, 30, 0), day.WorkTime);
        Assert.Collection(
            day.Groups,
            g =>
            {
                Assert.Equal(12, g.WorkItem);
                Assert.Equal(["A"], g.Names);
                Assert.Equal(TimeSpan.FromHours(4), g.Total);
                Assert.False(g.Open);
            },
            g =>
            {
                Assert.Null(g.WorkItem);
                Assert.Equal(["B"], g.Names);
                Assert.Equal(new TimeSpan(4, 30, 0), g.Total);
                Assert.False(g.Open);
            });
    }

    // AC-1: missing file, no folder.
    [Fact]
    public void Read_MissingFileOrFolder_IsMissing()
    {
        Assert.Equal(TimesDayKind.Missing, TimesDay.Read(folder, Day).Kind);
        Assert.Equal(TimesDayKind.Missing, TimesDay.Read(null, Day).Kind);
        Assert.Equal(TimesDayKind.Missing, TimesDay.Read(Path.Combine(folder, "nope"), Day).Kind);
    }

    // AC-1: a file over 64 KiB and a directory are unreadable.
    [Fact]
    public void Read_OversizedOrDirectory_IsUnreadable()
    {
        Write("{\"entries\":[]," + new string(' ', (int)TimesDay.MaxBytes) + "}");
        Assert.Equal(TimesDayKind.Unreadable, TimesDay.Read(folder, Day).Kind);

        File.Delete(Path.Combine(folder, "2026-11-14.json"));
        Directory.CreateDirectory(Path.Combine(folder, "2026-11-14.json"));
        Assert.Equal(TimesDayKind.Unreadable, TimesDay.Read(folder, Day).Kind);
    }

    // AC-1: bad JSON, an unknown kind or absence, a bad time and Times' other invalid shapes.
    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"entries\":[{\"kind\":\"lunch\",\"time\":\"12:00\"}]}")]
    [InlineData("{\"entries\":[{\"kind\":\"arrival\",\"time\":\"8:00\"}]}")]
    [InlineData("{\"entries\":[{\"kind\":\"arrival\",\"time\":\"25:00\"}]}")]
    [InlineData("{\"entries\":[{\"kind\":\"arrival\"}]}")]
    [InlineData("{\"entries\":[{\"kind\":\"checkpoint\",\"time\":\"09:00\"}]}")]
    [InlineData("{\"entries\":[{\"kind\":\"checkpoint\",\"time\":\"09:00\",\"name\":\"A\",\"workItem\":0}]}")]
    [InlineData("{\"entries\":{}}")]
    [InlineData("{\"absence\":\"holiday\"}")]
    [InlineData("{\"absence\":\"sick\",\"entries\":[{\"kind\":\"arrival\",\"time\":\"08:00\"}]}")]
    [InlineData("{\"absence\":\"sick\",\"arrival\":\"08:00\"}")]
    [InlineData("{\"entries\":[],\"arrival\":\"08:00\"}")]
    [InlineData("{\"arrival\":\"08:00\",\"arrival\":\"09:00\"}")]
    public void Read_Invalid_IsUnreadable(string json)
    {
        Assert.Equal(TimesDayKind.Unreadable, Read(json).Kind);
    }

    // AC-1: "absence" gives the absence.
    [Theory]
    [InlineData("vacation", 0)]
    [InlineData("sick", 1)]
    [InlineData("publicHoliday", 2)]
    public void Read_Absence_GivesKind(string value, int kind)
    {
        TimesAbsence expected = (TimesAbsence)kind;
        TimesDay day = Read("{\"absence\":\"" + value + "\"}");

        Assert.Equal(TimesDayKind.Absence, day.Kind);
        Assert.Equal(expected, day.Absence);
        Assert.Empty(day.Entries);
    }

    // AC-1: the legacy format.
    [Fact]
    public void Read_Legacy_GivesArrivalLeavingAndWorkTime()
    {
        TimesDay day = Read("{\"arrival\":\"08:15\",\"leaving\":\"16:45\"}");

        Assert.Equal(TimesDayKind.Entries, day.Kind);
        Assert.Equal(new TimeOnly(8, 15), day.Arrival);
        Assert.Equal(new TimeOnly(16, 45), day.Leaving);
        Assert.Equal(new TimeSpan(8, 30, 0), day.WorkTime);
        Assert.True(day.HasLeft);
        Assert.Empty(day.Groups);

        TimesDay open = Read("{\"arrival\":\"08:15\",\"leaving\":null}");
        Assert.False(open.HasLeft);
        Assert.Null(open.Leaving);
    }

    // AC-1: a BOM, unknown properties and a name on another kind are fine; entries are sorted by time, stable.
    [Fact]
    public void Read_SortsStableAndIgnoresExtras()
    {
        string json = "{\"corrections\":[1],\"entries\":[" +
            "{\"kind\":\"leaving\",\"time\":\"17:00\",\"name\":\"x\"}," +
            "{\"kind\":\"arrival\",\"time\":\"08:00\",\"extra\":true}," +
            "{\"kind\":\"checkpoint\",\"time\":\"08:00\",\"name\":\"first\"}," +
            "{\"kind\":\"checkpoint\",\"time\":\"08:00\",\"name\":\"second\"}]}";
        File.WriteAllBytes(Path.Combine(folder, "2026-11-14.json"), [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(json)]);

        TimesDay day = TimesDay.Read(folder, Day);

        Assert.Equal(TimesDayKind.Entries, day.Kind);
        Assert.Equal(
            [TimesEntryKind.Arrival, TimesEntryKind.Checkpoint, TimesEntryKind.Checkpoint, TimesEntryKind.Leaving],
            day.Entries.Select(e => e.Kind));
        Assert.Equal(["first", "second"], day.Entries.Where(e => e.Name is not null).Select(e => e.Name));
        Assert.Null(day.Entries[^1].Name);
    }

    // AC-1: a work item gathers its names in first-booked order; a lunch leaving and the arrival after it.
    [Fact]
    public void Groups_WorkItemNamesAndLunchLeaving()
    {
        TimesDay day = Read(
            """
            {"entries":[
              {"kind":"arrival","time":"08:00"},
              {"kind":"checkpoint","time":"08:00","name":"Spec","workItem":5},
              {"kind":"checkpoint","time":"09:00","name":"Mail"},
              {"kind":"checkpoint","time":"10:00","name":"Build","workItem":5},
              {"kind":"leaving","time":"12:00"},
              {"kind":"arrival","time":"13:00"},
              {"kind":"checkpoint","time":"13:00","name":"Spec","workItem":5},
              {"kind":"leaving","time":"15:00"}
            ]}
            """);

        Assert.True(day.HasLeft);
        Assert.Equal(new TimeOnly(15, 0), day.Leaving);
        Assert.Equal(TimeSpan.FromHours(6), day.WorkTime);
        Assert.Collection(
            day.Groups,
            g =>
            {
                Assert.Equal(5, g.WorkItem);
                Assert.Equal(["Spec", "Build"], g.Names);
                Assert.Equal(TimeSpan.FromHours(5), g.Total);
            },
            g =>
            {
                Assert.Equal(["Mail"], g.Names);
                Assert.Equal(TimeSpan.FromHours(1), g.Total);
            });
    }

    // AC-1: a checkpoint nothing ends is open; an arrival after a leaving means not left; an open break leaving.
    [Fact]
    public void HasLeftAndOpenGroups()
    {
        TimesDay working = Read(
            "{\"entries\":[{\"kind\":\"arrival\",\"time\":\"08:00\"},{\"kind\":\"leaving\",\"time\":\"12:00\"}," +
            "{\"kind\":\"arrival\",\"time\":\"13:00\"},{\"kind\":\"checkpoint\",\"time\":\"13:00\",\"name\":\"X\"}]}");
        Assert.False(working.HasLeft);
        Assert.True(Assert.Single(working.Groups).Open);
        Assert.Equal(TimeSpan.Zero, working.Groups[0].Total);

        TimesDay onBreak = Read(
            "{\"entries\":[{\"kind\":\"arrival\",\"time\":\"08:00\"},{\"kind\":\"breakStart\",\"time\":\"12:00\"}," +
            "{\"kind\":\"leaving\",\"time\":\"13:00\"}]}");
        Assert.True(onBreak.HasLeft);
        Assert.Equal(TimeSpan.FromHours(4), onBreak.WorkTime);

        Assert.False(Read("{\"entries\":[]}").HasLeft);
    }

    // AC-8: reading never changes the folder.
    [Fact]
    public void Read_WritesNothing()
    {
        Write(Example);
        SortedDictionary<string, string> before = FileSnapshot.Take(folder);

        TimesDay.Read(folder, Day);
        TimesDay.Read(folder, Day.AddDays(1));

        Assert.Equal(before, FileSnapshot.Take(folder));
    }

    // AC-2: the folder from TIMES_DATA_DIR.
    [Fact]
    public void Resolve_ChecksTheValue()
    {
        string absolute = Path.Combine(Path.GetTempPath(), "times-data");
        string appData = Path.Combine(Path.GetTempPath(), "appdata");

        Assert.Equal(absolute, TimesFolder.Resolve(absolute, appData));
        Assert.Equal(Path.Combine(appData, "Times"), TimesFolder.Resolve(null, appData));
        Assert.Equal(Path.Combine(appData, "Times"), TimesFolder.Resolve(string.Empty, appData));
        Assert.Null(TimesFolder.Resolve("relative/times", appData));
        Assert.Null(TimesFolder.Resolve(" " + absolute, appData));
        Assert.Null(TimesFolder.Resolve(absolute + " ", appData));
        Assert.Null(TimesFolder.Resolve(null, string.Empty));
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Times"),
            TimesFolder.Resolve(null));
    }
}
