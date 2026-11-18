using System.Text.Json;

namespace Context;

/// <summary>One heading of the open note, from the editor's <c>headings</c> list: its level (1 to 6) and plain text.</summary>
public sealed record OutlineHeading
{
    /// <summary>The most headings read from one note; a longer list is not shown at all.</summary>
    public const int MaxHeadings = 1000;

    public OutlineHeading(int level, string text)
    {
        Level = level;
        Text = text;
    }

    /// <summary>The heading level, 1 to 6.</summary>
    public int Level { get; }

    /// <summary>The heading's text (raw note text): text nodes and chips' raw text, marks dropped.</summary>
    public string Text { get; }

    /// <summary>Returns only the type name: the text is raw note text, so formatting this record must never print it.</summary>
    public override string ToString() => nameof(OutlineHeading);

    /// <summary>
    /// Reads the editor's JSON <c>[{level,text}]</c>. All or nothing: anything malformed (not an array, a level outside
    /// 1 to 6, a missing or non-string text, more than <see cref="MaxHeadings"/> entries) gives an empty list.
    /// </summary>
    public static IReadOnlyList<OutlineHeading> Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() > MaxHeadings)
            {
                return [];
            }

            OutlineHeading[] list = new OutlineHeading[doc.RootElement.GetArrayLength()];
            int index = 0;
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("level", out JsonElement level)
                    || level.ValueKind != JsonValueKind.Number
                    || !level.TryGetInt32(out int number)
                    || number < 1
                    || number > 6
                    || !item.TryGetProperty("text", out JsonElement text)
                    || text.ValueKind != JsonValueKind.String)
                {
                    return [];
                }

                list[index] = new OutlineHeading(number, text.GetString()!);
                index++;
            }

            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// The index of the first heading whose text matches <paramref name="heading"/>: both trimmed, every whitespace run
    /// as one space, case ignored. -1 if none.
    /// </summary>
    public static int IndexOf(IReadOnlyList<OutlineHeading> headings, string heading)
    {
        string wanted = Fold(heading);
        for (int i = 0; i < headings.Count; i++)
        {
            if (string.Equals(Fold(headings[i].Text), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Fold(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
