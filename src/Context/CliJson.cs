using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Context;

/// <summary>
/// The JSON side of <see cref="Cli"/>: one compact document per read command, written with
/// <see cref="Utf8JsonWriter"/> and its default encoder, so every control, non-ASCII and HTML character is escaped
/// and the output is plain ASCII. Property names and their order are a public contract for scripts.
/// </summary>
internal static class CliJson
{
    /// <summary>The flag that turns JSON on (matched ignoring case, as an exact token).</summary>
    public const string Flag = "--json";

    private const string TimeFormat = "yyyy-MM-ddTHH:mm:sszzz";

    /// <summary>True when <paramref name="arg"/> is the <c>--json</c> flag.</summary>
    public static bool IsFlag(string arg) => string.Equals(arg, Flag, StringComparison.OrdinalIgnoreCase);

    /// <summary>One compact JSON object written by <paramref name="body"/>, followed by one <c>\n</c>.</summary>
    public static string Document(Action<Utf8JsonWriter> body)
    {
        using MemoryStream stream = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>A local time in <paramref name="zone"/> as ISO 8601 with its offset.</summary>
    public static string Time(DateTime local, TimeZoneInfo zone)
    {
        DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        DateTimeOffset withOffset = new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
        return withOffset.ToString(TimeFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>The camelCase name of a feed status.</summary>
    public static string Status(CalendarFeedStatus status) => status switch
    {
        CalendarFeedStatus.Fresh => "fresh",
        CalendarFeedStatus.Stale => "stale",
        CalendarFeedStatus.Failed => "failed",
        _ => "notConfigured",
    };

    /// <summary>The lower-case name of a link direction.</summary>
    public static string Direction(NoteLinkDirection direction) => direction switch
    {
        NoteLinkDirection.To => "to",
        NoteLinkDirection.From => "from",
        NoteLinkDirection.Both => "both",
        _ => "none",
    };

    /// <summary>Writes <paramref name="name"/> as an array of strings.</summary>
    public static void Strings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}
