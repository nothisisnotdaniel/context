using System.Text;
using System.Text.Json;

namespace Context;

/// <summary>
/// Times' project quicklist (job 282), <c>&lt;Times folder&gt;/projects.json</c>, read-only: the work item and the title
/// of each project in file order, so a project's 1-based position is its Times number (<c>#2</c>). Context never writes
/// the Times folder.
/// </summary>
internal static class TimesProjects
{
    /// <summary>The quicklist's file name in the Times folder.</summary>
    public const string FileName = "projects.json";

    /// <summary>The largest file that is read (Times' own cap).</summary>
    public const long MaxBytes = 64 * 1024;

    /// <summary>
    /// The quicklist rows of <paramref name="timesFolder"/> in file order, each title through <see cref="SafeText.Clean"/>
    /// as one line. A missing folder or file, a file over <see cref="MaxBytes"/>, one that can't be read, or one that is
    /// not a valid list (any row without a positive whole <c>workItem</c> or a string <c>title</c>) gives an empty list,
    /// so positions never shift. Never throws, never writes.
    /// </summary>
    public static IReadOnlyList<(int WorkItem, string Title)> Read(string? timesFolder)
    {
        if (string.IsNullOrEmpty(timesFolder))
        {
            return [];
        }

        try
        {
            byte[] bytes = BoundedFile.Read(Path.Combine(timesFolder, FileName), MaxBytes);
            if (bytes.Length == 0)
            {
                return [];
            }

            using JsonDocument document = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(bytes));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("projects", out JsonElement projects)
                || projects.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            List<(int WorkItem, string Title)> rows = [];
            foreach (JsonElement project in projects.EnumerateArray())
            {
                if (project.ValueKind != JsonValueKind.Object
                    || !project.TryGetProperty("workItem", out JsonElement workItem)
                    || workItem.ValueKind != JsonValueKind.Number
                    || !workItem.TryGetInt32(out int number)
                    || number <= 0
                    || !project.TryGetProperty("title", out JsonElement title)
                    || title.ValueKind != JsonValueKind.String)
                {
                    return [];
                }

                rows.Add((number, OneLine(title.GetString() ?? string.Empty)));
            }

            return rows;
        }
        catch (Exception)
        {
            // Missing, too large, unreadable or not JSON: no quicklist.
            return [];
        }
    }

    private static string OneLine(string text) => SafeText.Clean(text).Replace('\n', ' ');
}
