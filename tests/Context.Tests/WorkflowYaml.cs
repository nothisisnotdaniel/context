using System.Text;
using System.Text.RegularExpressions;

namespace Context.Tests;

/// <summary>
/// A node of the parsed YAML: a <see cref="YamlMapping"/>, a <see cref="YamlSequence"/> or a
/// <see cref="YamlScalar"/>.
/// </summary>
internal abstract class YamlNode
{
    protected YamlNode(int line)
    {
        Line = line;
    }

    /// <summary>The 1-based source line the node starts on.</summary>
    public int Line { get; }
}

internal sealed class YamlScalar : YamlNode
{
    public YamlScalar(string value, string? comment, int line)
        : base(line)
    {
        Value = value;
        Comment = comment;
    }

    public string Value { get; }

    /// <summary>The trailing <c> # comment</c> on the scalar's line (text after <c>#</c>, trimmed), or null.</summary>
    public string? Comment { get; }

    public override string ToString() => Value;
}

internal sealed class YamlSequence : YamlNode
{
    public YamlSequence(List<YamlNode> items, int line)
        : base(line)
    {
        Items = items;
    }

    public IReadOnlyList<YamlNode> Items { get; }
}

internal sealed class YamlMapping : YamlNode
{
    private readonly List<KeyValuePair<string, YamlNode>> entries;

    public YamlMapping(List<KeyValuePair<string, YamlNode>> entries, int line)
        : base(line)
    {
        this.entries = entries;
    }

    public IReadOnlyList<KeyValuePair<string, YamlNode>> Entries => entries;

    /// <summary>The keys in source order.</summary>
    public IReadOnlyList<string> Keys => entries.Select(entry => entry.Key).ToList();

    public bool ContainsKey(string key) => entries.Any(entry => entry.Key == key);

    public YamlNode this[string key]
    {
        get
        {
            foreach (KeyValuePair<string, YamlNode> entry in entries)
            {
                if (entry.Key == key)
                {
                    return entry.Value;
                }
            }

            throw new KeyNotFoundException("no key '" + key + "' in the mapping on line " + Line);
        }
    }
}

/// <summary>
/// A minimal YAML reader for the workflow tests: block mappings and sequences, plain, single- and
/// double-quoted scalars, <c>|</c> and <c>&gt;</c> block scalars, blank lines and full-line or trailing
/// <c> #</c> comments. Every scalar stays a string. Anything else (tabs in indentation, flow
/// collections, anchors, aliases, tags, directives, duplicate keys, multi-line plain or quoted
/// scalars) throws a <see cref="FormatException"/>.
/// </summary>
internal sealed class WorkflowYaml
{
    private static readonly Regex KeyPattern = new Regex(@"^([A-Za-z0-9_][A-Za-z0-9_.\-/]*):(?: |$)");

    private readonly string[] lines;
    private int position;

    private WorkflowYaml(string text)
    {
        lines = text.ReplaceLineEndings("\n").Split('\n');
    }

    public static YamlNode Parse(string text)
    {
        WorkflowYaml reader = new WorkflowYaml(text);
        reader.SkipInsignificant();
        if (reader.AtEnd)
        {
            throw new FormatException("the document is empty");
        }

        YamlNode root = reader.ParseBlock(0);
        reader.SkipInsignificant();
        if (!reader.AtEnd)
        {
            throw reader.Error("unexpected indentation");
        }

        return root;
    }

    private bool AtEnd => position >= lines.Length;

    private string Current => lines[position];

    private int LineNumber => position + 1;

    private static int Indent(string line)
    {
        int indent = 0;
        while (indent < line.Length && line[indent] == ' ')
        {
            indent++;
        }

        return indent;
    }

    private static bool IsInsignificant(string line)
    {
        string trimmed = line.Trim(' ');
        return trimmed.Length == 0 || trimmed.StartsWith('#');
    }

    private FormatException Error(string message)
    {
        return new FormatException("line " + LineNumber + ": " + message);
    }

    /// <summary>
    /// Skips blank and full-line comment lines; throws on a tab in the indentation of a content line.
    /// </summary>
    private void SkipInsignificant()
    {
        while (!AtEnd)
        {
            string line = Current;
            int indent = Indent(line);
            if (indent < line.Length && line[indent] == '\t')
            {
                if (line.Trim().Length == 0)
                {
                    position++;
                    continue;
                }

                throw Error("tab in indentation");
            }

            if (!IsInsignificant(line))
            {
                return;
            }

            position++;
        }
    }

    private static bool IsSequenceItem(string content) =>
        content == "-" || content.StartsWith("- ", StringComparison.Ordinal);

    private YamlNode ParseBlock(int minIndent)
    {
        SkipInsignificant();
        int indent = Indent(Current);
        if (indent < minIndent)
        {
            throw Error("expected content indented by at least " + minIndent);
        }

        if (IsSequenceItem(Current[indent..]))
        {
            return ParseSequence(indent);
        }

        return ParseMapping(indent);
    }

    private YamlSequence ParseSequence(int indent)
    {
        int start = LineNumber;
        List<YamlNode> items = [];
        while (true)
        {
            SkipInsignificant();
            if (AtEnd || Indent(Current) < indent)
            {
                break;
            }

            if (Indent(Current) > indent)
            {
                throw Error("unexpected indentation in a sequence");
            }

            string content = Current[indent..];
            if (!IsSequenceItem(content))
            {
                break;
            }

            items.Add(ParseItem(indent));
        }

        return new YamlSequence(items, start);
    }

    /// <summary>
    /// Parses one <c>- </c> item. Inline block content is read by blanking the dash, so its column becomes its
    /// indent.
    /// </summary>
    private YamlNode ParseItem(int indent)
    {
        string line = Current;
        string rest = line[(indent + 1)..];
        int column = indent + 1 + Indent(rest);
        string content = line[column..];
        if (content.Length == 0 || content.StartsWith('#'))
        {
            int itemLine = LineNumber;
            position++;
            return ParseNested(indent, itemLine, allowSameIndentSequence: false);
        }

        if (content.StartsWith('\t'))
        {
            throw Error("tab after '-'");
        }

        if (KeyPattern.IsMatch(content) || IsSequenceItem(content))
        {
            lines[position] = new string(' ', column) + content;
            return ParseBlock(column);
        }

        return ParseValue(content, indent);
    }

    private YamlMapping ParseMapping(int indent)
    {
        int start = LineNumber;
        List<KeyValuePair<string, YamlNode>> entries = [];
        while (true)
        {
            SkipInsignificant();
            if (AtEnd || Indent(Current) < indent)
            {
                break;
            }

            if (Indent(Current) > indent)
            {
                throw Error("unexpected indentation in a mapping");
            }

            string content = Current[indent..];
            if (IsSequenceItem(content) && entries.Count > 0)
            {
                throw Error("a sequence item where a mapping key was expected");
            }

            Match match = KeyPattern.Match(content);
            if (!match.Success)
            {
                throw Error("expected a plain 'key:' but found '" + content + "'");
            }

            string key = match.Groups[1].Value;
            if (entries.Any(entry => entry.Key == key))
            {
                throw Error("duplicate key '" + key + "'");
            }

            string value = content[match.Length..].TrimStart(' ');
            if (value.StartsWith('\t'))
            {
                throw Error("tab after ':'");
            }

            YamlNode node;
            if (value.Length == 0 || value.StartsWith('#'))
            {
                int keyLine = LineNumber;
                position++;
                node = ParseNested(indent, keyLine, allowSameIndentSequence: true);
            }
            else
            {
                node = ParseValue(value, indent);
            }

            entries.Add(new KeyValuePair<string, YamlNode>(key, node));
        }

        return new YamlMapping(entries, start);
    }

    /// <summary>
    /// The value of a <c>key:</c> or <c>-</c> with nothing after it: a deeper block, a same-indent sequence (for
    /// keys), or an empty scalar.
    /// </summary>
    private YamlNode ParseNested(int indent, int ownerLine, bool allowSameIndentSequence)
    {
        SkipInsignificant();
        if (!AtEnd)
        {
            int next = Indent(Current);
            if (next > indent)
            {
                return ParseBlock(indent + 1);
            }

            if (allowSameIndentSequence && next == indent && IsSequenceItem(Current[indent..]))
            {
                return ParseSequence(indent);
            }
        }

        return new YamlScalar(string.Empty, null, ownerLine);
    }

    /// <summary>
    /// Parses an inline value that starts on the current line; <paramref name="indent"/> is the owner's indent.
    /// </summary>
    private YamlNode ParseValue(string value, int indent)
    {
        int line = LineNumber;
        char first = value[0];
        if (first == '{' || first == '[')
        {
            throw Error("flow collections are not supported");
        }

        if (first == '&')
        {
            throw Error("anchors are not supported");
        }

        if (first == '*')
        {
            throw Error("aliases are not supported");
        }

        if (first == '!')
        {
            throw Error("tags are not supported");
        }

        if (first == '%' || first == '@' || first == '`' || first == '?' || IsSequenceItem(value))
        {
            throw Error("unsupported value '" + value + "'");
        }

        if (first == '|' || first == '>')
        {
            string indicator = value[1..];
            if (indicator.Length > 0 && !(indicator[0] == ' ' && indicator.TrimStart(' ').StartsWith('#')))
            {
                throw Error("only plain '|' and '>' block scalars are supported");
            }

            position++;
            return ParseBlockScalar(indent, folded: first == '>', line);
        }

        YamlScalar scalar;
        if (first == '"')
        {
            scalar = ParseDoubleQuoted(value, line);
        }
        else if (first == '\'')
        {
            scalar = ParseSingleQuoted(value, line);
        }
        else
        {
            scalar = ParsePlain(value, line);
        }

        position++;
        SkipInsignificant();
        if (!AtEnd && Indent(Current) > indent)
        {
            throw Error("multi-line scalars are not supported");
        }

        return scalar;
    }

    private YamlScalar ParsePlain(string value, int line)
    {
        string text = value;
        string? comment = null;
        int hash = text.IndexOf(" #", StringComparison.Ordinal);
        int tabHash = text.IndexOf("\t#", StringComparison.Ordinal);
        if (tabHash >= 0 && (hash < 0 || tabHash < hash))
        {
            hash = tabHash;
        }

        if (hash >= 0)
        {
            comment = text[(hash + 2)..].Trim();
            text = text[..hash];
        }

        text = text.TrimEnd(' ', '\t');
        if (text.Contains(": ", StringComparison.Ordinal) || text.EndsWith(':'))
        {
            throw Error("a ': ' inside a plain scalar (nested mapping on one line?)");
        }

        return new YamlScalar(text, comment, line);
    }

    private YamlScalar ParseSingleQuoted(string value, int line)
    {
        StringBuilder builder = new StringBuilder();
        int i = 1;
        while (true)
        {
            if (i >= value.Length)
            {
                throw Error("unterminated single-quoted scalar");
            }

            if (value[i] == '\'')
            {
                if (i + 1 < value.Length && value[i + 1] == '\'')
                {
                    builder.Append('\'');
                    i += 2;
                    continue;
                }

                break;
            }

            builder.Append(value[i]);
            i++;
        }

        return new YamlScalar(builder.ToString(), TrailingComment(value[(i + 1)..]), line);
    }

    private YamlScalar ParseDoubleQuoted(string value, int line)
    {
        StringBuilder builder = new StringBuilder();
        int i = 1;
        while (true)
        {
            if (i >= value.Length)
            {
                throw Error("unterminated double-quoted scalar");
            }

            char c = value[i];
            if (c == '"')
            {
                break;
            }

            if (c == '\\')
            {
                if (i + 1 >= value.Length)
                {
                    throw Error("unterminated escape");
                }

                char escape = value[i + 1];
                builder.Append(escape switch
                {
                    '\\' => '\\',
                    '"' => '"',
                    '/' => '/',
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '0' => '\0',
                    ' ' => ' ',
                    _ => throw Error("unsupported escape '\\" + escape + "'"),
                });
                i += 2;
                continue;
            }

            builder.Append(c);
            i++;
        }

        return new YamlScalar(builder.ToString(), TrailingComment(value[(i + 1)..]), line);
    }

    /// <summary>What may follow a closing quote: nothing, or whitespace and a <c>#</c> comment.</summary>
    private string? TrailingComment(string rest)
    {
        if (rest.Trim(' ', '\t').Length == 0)
        {
            return null;
        }

        string trimmed = rest.TrimStart(' ', '\t');
        if (trimmed.Length == rest.Length || !trimmed.StartsWith('#'))
        {
            throw Error("unexpected text after a quoted scalar");
        }

        return trimmed[1..].Trim();
    }

    /// <summary>
    /// Reads the body of a <c>|</c> or <c>&gt;</c> block: every following line that is blank or indented more
    /// than <paramref name="owner"/>, with the first body line's indent removed. Clip chomping: one final line break.
    /// </summary>
    private YamlScalar ParseBlockScalar(int owner, bool folded, int line)
    {
        List<string> body = [];
        int blockIndent = -1;
        while (!AtEnd)
        {
            string text = Current;
            if (text.Trim(' ').Length == 0)
            {
                body.Add(string.Empty);
                position++;
                continue;
            }

            int indent = Indent(text);
            if (indent <= owner)
            {
                break;
            }

            if (blockIndent < 0)
            {
                blockIndent = indent;
            }

            if (indent < blockIndent)
            {
                throw Error("block scalar line indented less than its first line");
            }

            body.Add(text[blockIndent..]);
            position++;
        }

        while (body.Count > 0 && body[^1].Length == 0)
        {
            body.RemoveAt(body.Count - 1);
        }

        if (body.Count == 0)
        {
            return new YamlScalar(string.Empty, null, line);
        }

        string value = folded ? Fold(body) : string.Join('\n', body);
        return new YamlScalar(value + "\n", null, line);
    }

    /// <summary>
    /// Folds <c>&gt;</c> lines: neighbouring normal lines join with a space, each blank line between them is one
    /// line break, and more-indented lines keep their breaks.
    /// </summary>
    private static string Fold(List<string> body)
    {
        StringBuilder builder = new StringBuilder(body[0]);
        int previous = 0;
        for (int i = 1; i < body.Count; i++)
        {
            if (body[i].Length == 0)
            {
                continue;
            }

            int blanks = i - previous - 1;
            bool normal = !body[i].StartsWith(' ') && !body[previous].StartsWith(' ');
            if (normal)
            {
                builder.Append(blanks == 0 ? " " : new string('\n', blanks));
            }
            else
            {
                builder.Append(new string('\n', blanks + 1));
            }

            builder.Append(body[i]);
            previous = i;
        }

        return builder.ToString();
    }
}
