using System.Text.Json;

namespace Context.Tests;

// 277: today's Times bookings in `context current` (AC-8), `current --json` (AC-9) and read-only (AC-4). A fixed
// zone, an injected today and now, FakeHttpHandler and the Times folder in the test's own temp folder.
public sealed class CliCurrentTimesTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 29);

    private readonly string parent;
    private readonly string root;
    private readonly string times;
    private DateTime now = new(2026, 10, 29, 10, 30, 0, DateTimeKind.Utc);

    public CliCurrentTimesTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-clitimes-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        times = Path.Combine(parent, "Times");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(times);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private sealed record Result(int Code, string Out, string Err)
    {
        public string[] OutLines => Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        public string[] ErrLines => Err.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private Result Run(params string[] args)
    {
        StringWriter stdout = new StringWriter();
        StringWriter stderr = new StringWriter();
        Cli cli = new Cli(
            stdout, stderr, root, () => Today, () => now, FakeHttpHandler.Ok(CalendarFeedTests.Ics), TimeZoneInfo.Utc, times);
        int code = cli.Run(args);
        return new Result(code, stdout.ToString(), stderr.ToString());
    }

    private void WriteDay(string json) => File.WriteAllText(Path.Combine(times, "2026-10-29.json"), json);

    private const string Booked =
        """
        {"entries":[
          {"kind":"arrival","time":"08:00"},
          {"kind":"checkpoint","time":"08:05","name":"Review\nnow","workItem":1234}
        ]}
        """;

    // AC-8: the section right after the day line; the rest as without Times.
    [Fact]
    public void Current_Booked_PrintsTimesSection()
    {
        Result without = Run("current");
        WriteDay(Booked);

        Result result = Run("current");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.Equal(
            ["Today, 2026-10-29", "Times", "arrival\t08:00", "left\t5:30", "checkpoint\t#1234 Review now"],
            result.OutLines.Take(5));
        Assert.Equal(without.OutLines, result.OutLines.Take(1).Concat(result.OutLines.Skip(5)));
    }

    // AC-8: a leaving line once left; no checkpoint line when none is open; a negative time left.
    [Fact]
    public void Current_Left_PrintsLeavingAndNoCheckpoint()
    {
        WriteDay(
            """
            {"entries":[
              {"kind":"arrival","time":"07:00"},
              {"kind":"checkpoint","time":"07:00","name":"Build"},
              {"kind":"leaving","time":"15:20"}
            ]}
            """);
        now = new DateTime(2026, 10, 29, 18, 0, 0, DateTimeKind.Utc);

        Result result = Run("current");

        Assert.Equal(0, result.Code);
        Assert.Equal(
            ["Today, 2026-10-29", "Times", "arrival\t07:00", "leaving\t15:20", "left\t-0:20", "Appointments"],
            result.OutLines.Take(6));
    }

    // AC-8: a checkpoint without a work item shows only its name.
    [Fact]
    public void Current_CheckpointWithoutWorkItem_ShowsName()
    {
        WriteDay("""{"entries":[{"kind":"arrival","time":"08:00"},{"kind":"checkpoint","time":"08:05","name":"Docs"}]}""");

        Assert.Contains("checkpoint\tDocs", Run("current").OutLines);
    }

    // AC-8: no section for None.
    [Theory]
    [InlineData(null)]
    [InlineData("""{"absence":"vacation"}""")]
    [InlineData("""{"entries":[]}""")]
    public void Current_None_PrintsNoSection(string? json)
    {
        if (json is not null)
        {
            WriteDay(json);
        }

        Result result = Run("current");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.Equal("Appointments", result.OutLines[1]);
        Assert.DoesNotContain("Times", result.OutLines);
    }

    // AC-8: unreadable: one stderr line, exit 1, the rest unchanged.
    [Fact]
    public void Current_Unreadable_WritesStderrAndExits1()
    {
        Result without = Run("current");
        WriteDay("{");

        Result result = Run("current");

        Assert.Equal(Cli.ExitError, result.Code);
        Assert.Equal(["Can't read Times' day file for 2026-10-29."], result.ErrLines);
        Assert.Equal(without.Out, result.Out);
    }

    // AC-9: the times object for Booked.
    [Fact]
    public void CurrentJson_Booked_HasTimesObject()
    {
        WriteDay(Booked);

        Result result = Run("current", "--json");

        Assert.Equal((0, string.Empty), (result.Code, result.Err));
        Assert.EndsWith(
            ",\"times\":{\"arrival\":\"08:00\",\"leaving\":null,\"leftMinutes\":330,"
                + "\"checkpoint\":{\"workItem\":1234,\"name\":\"Review now\"}}}\n",
            result.Out,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(result.Out);
        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("times").ValueKind);
    }

    // AC-9: leaving set, checkpoint null, negative minutes, a checkpoint without a work item.
    [Fact]
    public void CurrentJson_Left_HasLeavingAndNullCheckpoint()
    {
        WriteDay("""{"entries":[{"kind":"arrival","time":"07:00"},{"kind":"leaving","time":"15:20"}]}""");

        Result left = Run("current", "--json");
        WriteDay("""{"entries":[{"kind":"arrival","time":"08:00"},{"kind":"checkpoint","time":"08:05","name":"Docs"}]}""");
        Result open = Run("current", "--json");

        Assert.EndsWith(
            ",\"times\":{\"arrival\":\"07:00\",\"leaving\":\"15:20\",\"leftMinutes\":-20,\"checkpoint\":null}}\n",
            left.Out,
            StringComparison.Ordinal);
        Assert.EndsWith(
            "\"checkpoint\":{\"workItem\":null,\"name\":\"Docs\"}}}\n", open.Out, StringComparison.Ordinal);
    }

    // AC-9: null for None and Unreadable; Unreadable has the same stderr line and exit code as plain.
    [Fact]
    public void CurrentJson_NoneAndUnreadable_AreNull()
    {
        Result none = Run("current", "--json");
        WriteDay("{");
        Result unreadable = Run("current", "--json");

        Assert.Equal((0, string.Empty), (none.Code, none.Err));
        Assert.EndsWith(",\"times\":null}\n", none.Out, StringComparison.Ordinal);
        Assert.Equal(Cli.ExitError, unreadable.Code);
        Assert.Equal(["Can't read Times' day file for 2026-10-29."], unreadable.ErrLines);
        Assert.Equal(none.Out, unreadable.Out);
    }

    // AC-4: current changes nothing in the Times folder.
    [Fact]
    public void Current_ChangesNothingInTimesFolder()
    {
        WriteDay(Booked);
        File.WriteAllText(Path.Combine(times, TimesBalance.SettingsFileName), """{"settings":{"daily-target":480}}""");
        SortedDictionary<string, string> before = FileSnapshot.Take(times);

        Run("current");
        Run("current", "--json");

        Assert.Equal(before, FileSnapshot.Take(times));
    }
}
