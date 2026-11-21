using System.Text;
using System.Text.Json;

namespace Context;

/// <summary>
/// The data folder's decision id (job 349), kept in <c>&lt;root&gt;/decision-folder.json</c>: the random prefix this
/// folder's decision numbers are written with (<c>!d ab12-1234</c>), the folder's GUID (written into each numbered note
/// as <c>decision-folder:: &lt;guid&gt;</c>) and whether the one-time migration of job 343 markers is done.
/// </summary>
internal sealed record DecisionFolder(string Prefix, Guid Guid, bool Migrated)
{
    /// <summary>The file name inside the root.</summary>
    public const string FileName = "decision-folder.json";

    /// <summary>The largest file that is read; a bigger one is invalid and never overwritten.</summary>
    public const int MaxFileBytes = 4096;

    /// <summary>The 32 chars a prefix is made of: <c>0-9a-z</c> without <c>i</c>, <c>l</c>, <c>o</c> and <c>u</c>.</summary>
    public const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    /// <summary>How many chars a prefix has.</summary>
    public const int PrefixLength = 4;

    /// <summary>The attribute key a numbered note names its folder with.</summary>
    public const string AttributeKey = "decision-folder";

    /// <summary>The red line while the file can't be read: nothing is numbered.</summary>
    public const string UnreadableText = "Can't read decision-folder.json. Decisions are not numbered.";

    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    /// <summary>Returns only the type name.</summary>
    public override string ToString() => nameof(DecisionFolder);

    /// <summary>The full path of the file in <paramref name="root"/>.</summary>
    public static string PathIn(string root) => Path.Combine(Path.GetFullPath(root), FileName);

    /// <summary>Whether <paramref name="text"/> is a prefix: exactly 4 <see cref="Alphabet"/> chars, any case.</summary>
    public static bool IsPrefix(string? text) =>
        text is { Length: PrefixLength } && text.All(c => IsPrefixChar(c));

    /// <summary>Whether <paramref name="c"/> is an <see cref="Alphabet"/> char in either case.</summary>
    public static bool IsPrefixChar(char c) => c < 128 && Alphabet.Contains(char.ToLowerInvariant(c), StringComparison.Ordinal);

    /// <summary>A new random prefix: 4 chars, each <see cref="Alphabet"/>[<paramref name="randomBelow"/>(32)].</summary>
    public static string NewPrefix(Func<int, int> randomBelow)
    {
        StringBuilder builder = new(PrefixLength);
        for (int i = 0; i < PrefixLength; i++)
        {
            int at = randomBelow(Alphabet.Length);
            if (at < 0 || at >= Alphabet.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(randomBelow), "The random value is out of range.");
            }

            builder.Append(Alphabet[at]);
        }

        return builder.ToString();
    }

    /// <summary>The default random source: a cryptographic random int below the bound.</summary>
    public static int RandomBelow(int bound) => System.Security.Cryptography.RandomNumberGenerator.GetInt32(bound);

    /// <summary>
    /// The folder's id in <paramref name="root"/>: read when the file exists, else created (<see cref="FileMode.CreateNew"/>,
    /// <c>0600</c> off Windows) with a prefix from <paramref name="randomBelow"/> and the GUID from
    /// <paramref name="newGuid"/>; when another process created it first, its values are read and used. <c>null</c> when
    /// the file exists but can't be read, is over <see cref="MaxFileBytes"/> or is invalid: it is never overwritten then.
    /// </summary>
    public static DecisionFolder? Load(string root, Func<int, int>? randomBelow = null, Func<Guid>? newGuid = null)
    {
        string path = PathIn(root);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Guid guid = (newGuid ?? Guid.NewGuid)();
            if (guid == Guid.Empty)
            {
                return null;
            }

            DecisionFolder created = new(NewPrefix(randomBelow ?? RandomBelow), guid, false);
            try
            {
                PrivateFile.CreateFolder(Path.GetDirectoryName(path)!);
                PrivateFile.Write(path, Bytes(created));
                return created;
            }
            catch (IOException) when (File.Exists(path))
            {
                // Created by another process meanwhile: its values are the folder's.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return Read(root);
    }

    /// <summary>
    /// The folder's id as the file holds it, or <c>null</c> when it is missing, can't be read, is over
    /// <see cref="MaxFileBytes"/> or is invalid. Never creates or changes anything.
    /// </summary>
    public static DecisionFolder? Read(string root)
    {
        byte[] bytes;
        try
        {
            bytes = BoundedFile.Read(PathIn(root), MaxFileBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return Parse(bytes);
    }

    /// <summary>
    /// The id in <paramref name="bytes"/>, or <c>null</c> when they are no strict UTF-8 JSON object with exactly the
    /// properties <c>prefix</c> (4 lower-case <see cref="Alphabet"/> chars), <c>guid</c> (a non-empty GUID in the
    /// <c>D</c> form) and <c>migrated</c> (a boolean), each once.
    /// </summary>
    public static DecisionFolder? Parse(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxFileBytes)
        {
            return null;
        }

        try
        {
            string json = StrictUtf8.GetString(bytes);
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? prefix = null;
            Guid? guid = null;
            bool? migrated = null;
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "prefix" when property.Value.ValueKind == JsonValueKind.String:
                        prefix = property.Value.GetString();
                        break;
                    case "guid" when property.Value.ValueKind == JsonValueKind.String
                        && Guid.TryParseExact(property.Value.GetString(), "D", out Guid parsed) && parsed != Guid.Empty:
                        guid = parsed;
                        break;
                    case "migrated" when property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        migrated = property.Value.GetBoolean();
                        break;
                    default:
                        return null;
                }
            }

            if (prefix is null || !IsPrefix(prefix) || !string.Equals(prefix, prefix.ToLowerInvariant(), StringComparison.Ordinal)
                || guid is null || migrated is null)
            {
                return null;
            }

            return new DecisionFolder(prefix, guid.Value, migrated.Value);
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sets <c>migrated</c> to true in <paramref name="root"/>'s file when it still holds <paramref name="folder"/>'s
    /// prefix and GUID (the only change ever made to an existing file), through a temp file moved over it. Returns
    /// whether the file now says migrated; never throws.
    /// </summary>
    public static bool MarkMigrated(string root, DecisionFolder folder)
    {
        string path = PathIn(root);
        string temp = Path.Combine(Path.GetDirectoryName(path)!, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            DecisionFolder? current = Read(root);
            if (current is null || current.Prefix != folder.Prefix || current.Guid != folder.Guid)
            {
                return false;
            }

            if (current.Migrated)
            {
                return true;
            }

            PrivateFile.Write(temp, Bytes(current with { Migrated = true }));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left behind; it holds no note text.
            }
        }
    }

    /// <summary>
    /// The GUID of the first <c>decision-folder::</c> attribute line of <paramref name="markdown"/>
    /// (<see cref="AttributeParser.Parse"/>), <see cref="Guid.Empty"/> when its value is no GUID, or <c>null</c> when the
    /// note has no such line.
    /// </summary>
    public static Guid? NoteGuid(string markdown)
    {
        if (!markdown.Contains(AttributeKey + "::", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (PageAttribute attribute in AttributeParser.Parse(markdown))
        {
            if (attribute.Key == AttributeKey)
            {
                return Guid.TryParseExact(attribute.Value, "D", out Guid guid) ? guid : Guid.Empty;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the note <paramref name="markdown"/> belongs to another folder than <paramref name="own"/>: its first
    /// <c>decision-folder::</c> line names another GUID (or no valid one).
    /// </summary>
    public static bool IsForeign(string markdown, Guid own) => NoteGuid(markdown) is Guid guid && guid != own;

    /// <summary>
    /// <paramref name="markdown"/> with <c>decision-folder:: &lt;guid&gt;</c> added: after a leading ATX title line
    /// (<c># </c> plus text) when there is one (as the first line of the attribute block when only blank lines lie
    /// between the title and a <c>key::</c> line, so the editor keeps one attribute block; else right after the title),
    /// else as line 1, with the note's line ending (the first one in the note, else <c>\n</c>). A UTF-8 BOM stays
    /// first.
    /// </summary>
    public static string WithFolderLine(string markdown, Guid guid)
    {
        int firstNewline = markdown.IndexOf('\n', StringComparison.Ordinal);
        string ending = firstNewline > 0 && markdown[firstNewline - 1] == '\r' ? "\r\n" : "\n";
        string line = AttributeKey + ":: " + guid.ToString("D");
        int start = markdown.Length > 0 && markdown[0] == '﻿' ? 1 : 0;
        string first = firstNewline < 0 ? markdown[start..] : markdown[start..firstNewline].TrimEnd('\r');
        if (IsTitleLine(first))
        {
            if (firstNewline < 0)
            {
                return markdown + ending + line + ending;
            }

            int at = AttributeBlockAfter(markdown, firstNewline + 1) ?? firstNewline + 1;
            return markdown[..at] + line + ending + markdown[at..];
        }

        return markdown[..start] + line + ending + markdown[start..];
    }

    // The start of the first line at or after from that is not blank, when it is a column-0 "key::" line; else null.
    private static int? AttributeBlockAfter(string markdown, int from)
    {
        int at = from;
        while (at < markdown.Length)
        {
            int end = markdown.IndexOf('\n', at);
            string current = end < 0 ? markdown[at..] : markdown[at..end];
            if (current.Trim().Length > 0)
            {
                return IsAttributeShaped(current) ? at : null;
            }

            if (end < 0)
            {
                return null;
            }

            at = end + 1;
        }

        return null;
    }

    // A column-0 key (a letter, then letters, digits, '-' or '_') directly followed by "::".
    private static bool IsAttributeShaped(string line)
    {
        if (line.Length == 0 || !char.IsLetter(line[0]))
        {
            return false;
        }

        int end = 1;
        while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] is '-' or '_'))
        {
            end++;
        }

        return line.AsSpan(end).StartsWith("::", StringComparison.Ordinal);
    }

    // An ATX level-1 heading: "#", one space or tab, then some text.
    private static bool IsTitleLine(string line) =>
        line.Length > 2 && line[0] == '#' && line[1] is ' ' or '\t' && line[2..].Trim().Length > 0;

    private static byte[] Bytes(DecisionFolder folder) =>
        Encoding.UTF8.GetBytes(
            "{\"prefix\":\"" + folder.Prefix + "\",\"guid\":\"" + folder.Guid.ToString("D") + "\",\"migrated\":"
                + (folder.Migrated ? "true" : "false") + "}");
}
