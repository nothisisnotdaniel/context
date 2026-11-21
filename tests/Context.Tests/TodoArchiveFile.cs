using System.Globalization;
using System.Text;

namespace Context.Tests;

/// <summary>Job 361: writes <c>todos.json</c> and <c>todos-archive.json</c> entries as JSON text for tests.</summary>
internal static class TodoArchiveFile
{
    /// <summary>One todo object; <paramref name="extra"/> is appended as raw JSON members (for example <c>"repeat":"day"</c>).</summary>
    public static string Entry(Guid id, string text, DateOnly due, DateTime created, DateTime? done, string? extra = null)
    {
        StringBuilder json = new StringBuilder("{");
        json.Append(CultureInfo.InvariantCulture, $"\"id\":\"{id}\",\"text\":\"{text}\",\"due\":\"{due:yyyy-MM-dd}\"");
        json.Append(CultureInfo.InvariantCulture, $",\"created\":\"{created:yyyy-MM-ddTHH:mm:ssZ}\"");
        if (done is { } d)
        {
            json.Append(CultureInfo.InvariantCulture, $",\"done\":\"{d:yyyy-MM-ddTHH:mm:ssZ}\"");
        }

        if (extra is not null)
        {
            json.Append(',').Append(extra);
        }

        return json.Append('}').ToString();
    }

    /// <summary>Writes <paramref name="entries"/> as a JSON array to <paramref name="path"/>, creating its folder.</summary>
    public static void Write(string path, params string[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "[" + string.Join(",", entries) + "]");
    }

    /// <summary>Writes an archive with one done todo.</summary>
    public static void Archive(string root, Guid id, string text, DateTime done) =>
        Write(
            Path.Combine(root, TodoStore.ArchiveFileName),
            Entry(id, text, DateOnly.FromDateTime(done), done.AddDays(-1), done));
}
