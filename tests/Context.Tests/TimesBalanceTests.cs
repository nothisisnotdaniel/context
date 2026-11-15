namespace Context.Tests;

// 172 AC-2: Times' flexitime balance through a day, read only.
public sealed class TimesBalanceTests : IDisposable
{
    private static readonly DateOnly Through = new(2026, 11, 14);

    private readonly string folder;

    public TimesBalanceTests()
    {
        folder = Path.Combine(Path.GetTempPath(), "context-timesbalance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    internal static string WorkDay(string arrival, string leaving, string corrections = "") =>
        "{\"entries\":[{\"kind\":\"arrival\",\"time\":\"" + arrival + "\"},{\"kind\":\"leaving\",\"time\":\"" + leaving
        + "\"}]" + (corrections.Length == 0 ? string.Empty : ",\"corrections\":" + corrections) + "}";

    private void Write(string name, string json) => File.WriteAllText(Path.Combine(folder, name), json);

    // The spec's example: 8:30 + 2:00 correction, 7:00, vacation, only -0:30, one unreadable, one after through.
    private void WriteExample()
    {
        Write("2026-11-09.json", WorkDay("08:00", "16:30", "[{\"minutes\":120,\"reason\":\"overtime\"}]"));
        Write("2026-11-10.json", WorkDay("08:00", "15:00"));
        Write("2026-11-11.json", "{\"absence\":\"vacation\"}");
        Write("2026-11-12.json", "{\"corrections\":[{\"minutes\":-30,\"reason\":\"fix\"}]}");
        Write("2026-11-13.json", "not json");
        Write("2026-11-15.json", WorkDay("06:00", "20:00"));
    }

    [Fact]
    public void Compute_Example_GivesTotalCountedSkipped()
    {
        WriteExample();

        TimesBalance balance = TimesBalance.Compute(folder, Through);

        Assert.Equal(TimesBalanceKind.Ok, balance.Kind);
        Assert.Equal(TimeSpan.FromHours(1), balance.Total);
        Assert.Equal(4, balance.Counted);
        Assert.Equal(1, balance.Skipped);
    }

    [Fact]
    public void Compute_SettingsTarget_ChangesResult()
    {
        WriteExample();
        Write("settings.json", "{\"settings\":{\"daily-target\":420,\"other\":true},\"more\":1}");

        TimesBalance balance = TimesBalance.Compute(folder, Through);

        Assert.Equal(TimeSpan.FromHours(3), balance.Total);
        Assert.Equal(420, TimesBalance.ReadTarget(folder));
    }

    [Theory]
    [InlineData("{\"settings\":{\"daily-target\":2000}}")]
    [InlineData("{\"settings\":{\"daily-target\":-1}}")]
    [InlineData("{\"settings\":{\"daily-target\":\"420\"}}")]
    [InlineData("{\"settings\":{\"daily-target\":420.5}}")]
    [InlineData("{\"settings\":{\"daily-target\":420,\"daily-target\":300}}")]
    [InlineData("{\"daily-target\":420}")]
    [InlineData("{\"settings\":[]}")]
    [InlineData("[]")]
    [InlineData("broken")]
    public void ReadTarget_BadSettings_Gives480(string json)
    {
        WriteExample();
        Write("settings.json", json);

        Assert.Equal(TimesBalance.DefaultTargetMinutes, TimesBalance.ReadTarget(folder));
        Assert.Equal(TimeSpan.FromHours(1), TimesBalance.Compute(folder, Through).Total);
    }

    [Fact]
    public void ReadTarget_MissingOversizedOrFolder_Gives480()
    {
        Assert.Equal(480, TimesBalance.ReadTarget(folder));
        Write("settings.json", "{\"settings\":{\"daily-target\":420}}" + new string(' ', (int)TimesBalance.MaxSettingsBytes));
        Assert.Equal(480, TimesBalance.ReadTarget(folder));
        File.Delete(Path.Combine(folder, "settings.json"));
        Directory.CreateDirectory(Path.Combine(folder, "settings.json"));
        Assert.Equal(480, TimesBalance.ReadTarget(folder));
    }

    [Fact]
    public void ReadTarget_Bom_IsRead()
    {
        File.WriteAllBytes(
            Path.Combine(folder, "settings.json"),
            [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes("{\"settings\":{\"daily-target\":0}}")]);

        Assert.Equal(0, TimesBalance.ReadTarget(folder));
    }

    [Fact]
    public void Compute_IgnoresOtherNamesAndSubfolders()
    {
        string day = WorkDay("08:00", "18:00");
        Write("x.json", day);
        Write("2026-13-01.json", day);
        Write("2026-11-14.json.bak", day);
        Write("2026-11-1.json", day);
        Write("2026-11-14.JSON", day);
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "sub", "2026-11-14.json"), day);
        Directory.CreateDirectory(Path.Combine(folder, "2026-11-13.json"));

        TimesBalance balance = TimesBalance.Compute(folder, Through);

        Assert.Equal(TimesBalanceKind.Ok, balance.Kind);
        Assert.Equal(0, balance.Counted);
        Assert.Equal(0, balance.Skipped);
        Assert.Equal(TimeSpan.Zero, balance.Total);
    }

    [Fact]
    public void Compute_DayWithoutArrival_DoesNotCount()
    {
        Write("2026-11-14.json", "{\"entries\":[]}");

        TimesBalance balance = TimesBalance.Compute(folder, Through);

        Assert.Equal(0, balance.Counted);
        Assert.Equal(0, balance.Skipped);
    }

    [Fact]
    public void Compute_NoFolder()
    {
        Assert.Equal(TimesBalanceKind.NoFolder, TimesBalance.Compute(null, Through).Kind);
        Assert.Equal(TimesBalanceKind.NoFolder, TimesBalance.Compute(Path.Combine(folder, "missing"), Through).Kind);
    }

    [Fact]
    public void Compute_WritesNothing()
    {
        WriteExample();
        Write("settings.json", "{\"settings\":{\"daily-target\":420}}");
        Dictionary<string, (DateTime, long)> before = Snapshot();

        TimesBalance.Compute(folder, Through);

        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("2026-11-14.json", true)]
    [InlineData("2024-02-29.json", true)]
    [InlineData("2026-02-29.json", false)]
    [InlineData("2026-11-14.jsonx", false)]
    [InlineData("x2026-11-14.json", false)]
    public void TryParseDayFile_ExactNames(string name, bool expected)
    {
        Assert.Equal(expected, TimesBalance.TryParseDayFile(name, out _));
    }

    private Dictionary<string, (DateTime, long)> Snapshot() =>
        Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, p => (File.GetLastWriteTimeUtc(p), File.Exists(p) ? new FileInfo(p).Length : -1));
}
