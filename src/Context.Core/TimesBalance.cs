using System.Globalization;
using System.Text.Json;

namespace Context;

/// <summary>How <see cref="TimesBalance.Compute"/> went.</summary>
internal enum TimesBalanceKind
{
    /// <summary>The folder was listed; the figures hold.</summary>
    Ok,

    /// <summary>There is no Times folder (none configured, or it doesn't exist).</summary>
    NoFolder,

    /// <summary>The Times folder exists but can't be listed.</summary>
    Unlistable,
}

/// <summary>
/// Times' flexitime balance through a day, by Times' <c>FlexitimeBalance</c> rules without the live day: the sum
/// of work time minus the daily target over the days with an arrival (an absence day counts with zero), plus every
/// readable day's corrections. Computing it only reads: nothing in the Times folder is created or changed.
/// </summary>
internal sealed record TimesBalance
{
    /// <summary>Times' daily target when <c>settings.json</c> gives none (minutes).</summary>
    public const int DefaultTargetMinutes = 480;

    /// <summary>The largest daily target Times accepts (minutes).</summary>
    public const int MaxTargetMinutes = 1440;

    /// <summary>The largest Times <c>settings.json</c> that is read (64 KiB).</summary>
    public const long MaxSettingsBytes = 64 * 1024;

    /// <summary>Times' settings file, next to its day files.</summary>
    public const string SettingsFileName = "settings.json";

    private const string DayFileExtension = ".json";

    // About 1900 years: far beyond any real balance, far inside TimeSpan's range.
    private const long MaxCorrectionTotalMinutes = 1_000_000_000;

    public TimesBalance(TimesBalanceKind kind, TimeSpan total, int counted, int skipped)
    {
        Kind = kind;
        Total = total;
        Counted = counted;
        Skipped = skipped;
    }

    public TimesBalanceKind Kind { get; }

    /// <summary>Work time minus target over the counted days, plus the corrections.</summary>
    public TimeSpan Total { get; }

    /// <summary>The days with an arrival, an absence or at least one correction.</summary>
    public int Counted { get; }

    /// <summary>The day files on or before the day that can't be read.</summary>
    public int Skipped { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(TimesBalance);

    /// <summary>
    /// The balance from <paramref name="folder"/>'s top-level files named exactly <c>yyyy-MM-dd.json</c> (a real
    /// date) dated on or before <paramref name="through"/>, each read through <see cref="TimesDay.Read"/>, with the
    /// target from <see cref="ReadTarget"/>. A <c>null</c> or missing folder is <see cref="TimesBalanceKind.NoFolder"/>,
    /// one that can't be listed <see cref="TimesBalanceKind.Unlistable"/>. Never throws, never writes.
    /// </summary>
    public static TimesBalance Compute(string? folder, DateOnly through)
    {
        if (folder is null || !Directory.Exists(folder))
        {
            return new TimesBalance(TimesBalanceKind.NoFolder, TimeSpan.Zero, 0, 0);
        }

        List<DateOnly> days = [];
        try
        {
            foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                if (TryParseDayFile(Path.GetFileName(path), out DateOnly day) && day <= through)
                {
                    days.Add(day);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or System.Security.SecurityException)
        {
            return new TimesBalance(TimesBalanceKind.Unlistable, TimeSpan.Zero, 0, 0);
        }

        days.Sort();
        TimeSpan target = TimeSpan.FromMinutes(ReadTarget(folder));
        TimeSpan total = TimeSpan.Zero;
        long correctionMinutes = 0;
        int counted = 0;
        int skipped = 0;
        foreach (DateOnly day in days)
        {
            TimesDay record = TimesDay.Read(folder, day);
            if (record.Kind == TimesDayKind.Unreadable)
            {
                skipped++;
                continue;
            }

            bool counts = record.Corrections.Count > 0;
            correctionMinutes += record.Corrections.Sum(m => (long)m);
            if (record.Kind == TimesDayKind.Absence)
            {
                counts = true;
            }
            else if (record.Arrival is not null)
            {
                total += record.WorkTime - target;
                counts = true;
            }

            if (counts)
            {
                counted++;
            }
        }

        // Whole minutes in a long, clamped, so even crafted files full of corrections can't overflow the TimeSpan.
        total += TimeSpan.FromMinutes(Math.Clamp(correctionMinutes, -MaxCorrectionTotalMinutes, MaxCorrectionTotalMinutes));
        return new TimesBalance(TimesBalanceKind.Ok, total, counted, skipped);
    }

    /// <summary>
    /// Times' daily target in minutes: <c>settings.daily-target</c> from <c>&lt;folder&gt;/settings.json</c> (read
    /// through <see cref="BoundedFile"/>, at most <see cref="MaxSettingsBytes"/>), an integer from 0 to
    /// <see cref="MaxTargetMinutes"/>. A missing or broken file, or a missing or bad value, gives
    /// <see cref="DefaultTargetMinutes"/>. Never throws, never writes.
    /// </summary>
    public static int ReadTarget(string folder)
    {
        try
        {
            string path = Path.Combine(folder, SettingsFileName);
            if (Directory.Exists(path))
            {
                return DefaultTargetMinutes;
            }

            ReadOnlyMemory<byte> json = BoundedFile.Read(path, MaxSettingsBytes);
            if (json.Length >= 3 && json.Span[0] == 0xEF && json.Span[1] == 0xBB && json.Span[2] == 0xBF)
            {
                json = json[3..];
            }

            using JsonDocument document = JsonDocument.Parse(json);
            if (Single(document.RootElement, "settings") is not { ValueKind: JsonValueKind.Object } settings
                || Single(settings, "daily-target") is not { ValueKind: JsonValueKind.Number } value
                || !value.TryGetInt32(out int minutes)
                || minutes < 0
                || minutes > MaxTargetMinutes)
            {
                return DefaultTargetMinutes;
            }

            return minutes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException or JsonException
            or InvalidOperationException)
        {
            return DefaultTargetMinutes;
        }
    }

    /// <summary>True when <paramref name="fileName"/> is exactly <c>yyyy-MM-dd.json</c> for a real date.</summary>
    internal static bool TryParseDayFile(string fileName, out DateOnly day)
    {
        day = default;
        return fileName.Length == 10 + DayFileExtension.Length
            && fileName.EndsWith(DayFileExtension, StringComparison.Ordinal)
            && DateOnly.TryParseExact(
                fileName[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }

    // The value of the one property called name of an object, or null when it is missing, repeated or not an object.
    private static JsonElement? Single(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        JsonElement? found = null;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                if (found is not null)
                {
                    return null;
                }

                found = property.Value;
            }
        }

        return found;
    }
}
