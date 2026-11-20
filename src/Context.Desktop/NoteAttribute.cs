using System.Text.Json;

namespace Context;

/// <summary>
/// One row of the open note's attribute block (the leading <c>key:: value</c> lines), from the editor's
/// <c>attributes</c> list: the key as written and the value trimmed (empty for a line like <c>role::</c>).
/// </summary>
public sealed record NoteAttribute
{
    /// <summary>The most rows read from one note; a longer list is not shown at all.</summary>
    public const int MaxRows = 1000;

    public NoteAttribute(string key, string value)
    {
        Key = key;
        Value = value;
    }

    /// <summary>The key as written in the note (raw note text).</summary>
    public string Key { get; }

    /// <summary>The value, trimmed (raw note text); may be empty.</summary>
    public string Value { get; }

    /// <summary>Returns only the type name: key and value are raw note text, so formatting this record must never print them.</summary>
    public override string ToString() => nameof(NoteAttribute);

    /// <summary>
    /// Reads the editor's JSON <c>[{key,value}]</c>. All or nothing: anything malformed (not an array, an entry that is
    /// no object, a missing or non-string key or value, more than <see cref="MaxRows"/> entries) gives an empty list.
    /// </summary>
    public static IReadOnlyList<NoteAttribute> Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() > MaxRows)
            {
                return [];
            }

            NoteAttribute[] list = new NoteAttribute[doc.RootElement.GetArrayLength()];
            int index = 0;
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("key", out JsonElement key)
                    || key.ValueKind != JsonValueKind.String
                    || !item.TryGetProperty("value", out JsonElement value)
                    || value.ValueKind != JsonValueKind.String)
                {
                    return [];
                }

                list[index] = new NoteAttribute(key.GetString()!, value.GetString()!);
                index++;
            }

            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
