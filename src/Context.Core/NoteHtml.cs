using System.Text;

namespace Context;

/// <summary>
/// One note as a self-contained HTML page to send to people who don't use Context (job 250). The Markdown is rendered
/// (headings, paragraphs, lists, task items, quotes, code, emphasis, safe links); tags, <c>[[links]]</c> and
/// <c>@[mentions]</c> become plain text; image attachments are embedded as <c>data:</c> URLs. Every piece of note
/// text is HTML-encoded, the page has no script and its CSP allows only inline style and <c>data:</c> images, so it
/// makes no request when opened. Never reads a file itself.
/// </summary>
internal static class NoteHtml
{
    /// <summary>The page's Content-Security-Policy.</summary>
    public const string Csp = "default-src 'none'; img-src data:; style-src 'unsafe-inline'";

    /// <summary>Deeper emphasis than this is shown as its literal markers, so the work per line stays linear.</summary>
    private const int MaxDepth = 8;

    private const string Fence = "```";

    private const string PrivateTag = "private";

    private const string Style =
        "body { font-family: -apple-system, \"Segoe UI\", Roboto, Helvetica, Arial, sans-serif; line-height: 1.5; "
        + "color: #1f2328; background: #ffffff; max-width: 46rem; margin: 2rem auto; padding: 0 1rem; }\n"
        + "h1, h2, h3, h4, h5, h6 { line-height: 1.25; margin: 1.5rem 0 0.5rem; }\n"
        + "p, ul, ol, blockquote, pre { margin: 0 0 1rem; }\n"
        + "blockquote { border-left: 0.25rem solid #d0d7de; color: #57606a; margin-left: 0; padding: 0 1rem; }\n"
        + "code { font-family: ui-monospace, Menlo, Consolas, monospace; background: #f6f8fa; padding: 0.1rem 0.3rem; border-radius: 0.2rem; }\n"
        + "pre { background: #f6f8fa; padding: 0.75rem 1rem; overflow-x: auto; border-radius: 0.3rem; }\n"
        + "pre code { padding: 0; background: none; }\n"
        + "img { max-width: 100%; }\n"
        + "@media print { body { margin: 0; max-width: none; } }\n";

    /// <summary>
    /// The whole page for the note <paramref name="title"/> with the Markdown <paramref name="text"/>. An image
    /// <c>![alt](.attachments/name)</c> among the first <see cref="NoteAttachments.MaxInlineImages"/>
    /// (<see cref="NoteAttachments.ImageSources"/>) is embedded when <paramref name="resolveImage"/> gives an image for
    /// its source; any other image shows <c>[image: alt]</c>. With <paramref name="leaveOutPrivate"/> every line with
    /// the tag <c>private</c> (outside code) is left out first.
    /// </summary>
    public static string Render(string title, string text, Func<string, ResolvedAttachment?> resolveImage, bool leaveOutPrivate)
    {
        string kept = leaveOutPrivate ? LeaveOutPrivate(text) : text;
        Images images = new Images(NoteAttachments.ImageSources(kept), resolveImage);
        StringBuilder html = new StringBuilder(kept.Length * 2 + Style.Length + 512);
        html.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"").Append(Csp).Append("\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append("<title>");
        Encode(title, html);
        html.Append("</title>\n<style>\n").Append(Style).Append("</style>\n</head>\n<body>\n<main>\n");
        new Blocks(Lines(kept), images, html).Render();
        html.Append("</main>\n</body>\n</html>\n");
        return html.ToString();
    }

    /// <summary>Appends <paramref name="text"/> with <c>&amp; &lt; &gt; " '</c> encoded and NUL as U+FFFD, for text and attribute values.</summary>
    internal static void Encode(string text, StringBuilder to)
    {
        foreach (char c in text)
        {
            Encode(c, to);
        }
    }

    private static void Encode(char c, StringBuilder to)
    {
        switch (c)
        {
            case '&':
                to.Append("&amp;");
                break;
            case '<':
                to.Append("&lt;");
                break;
            case '>':
                to.Append("&gt;");
                break;
            case '"':
                to.Append("&quot;");
                break;
            case '\'':
                to.Append("&#39;");
                break;
            case '\0':
                to.Append('�');
                break;
            default:
                to.Append(c);
                break;
        }
    }

    private static List<string> Lines(string text) => text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

    // Drops every line outside a fence on which TagParser finds #private; fence lines and fenced lines stay.
    private static string LeaveOutPrivate(string text)
    {
        HashSet<int> dropped = [];
        foreach ((int number, string line) in TagParser.NumberedLinesOutsideFences(text))
        {
            if (TagParser.LineHasTag(line, PrivateTag))
            {
                dropped.Add(number);
            }
        }

        if (dropped.Count == 0)
        {
            return text;
        }

        string[] lines = text.Split('\n');
        return string.Join('\n', lines.Where((_, i) => !dropped.Contains(i + 1)));
    }

    private static bool IsFence(string line) => line.TrimStart().StartsWith(Fence, StringComparison.Ordinal);

    // The resolved images, each source resolved at most once and only when it is one of the allowed ones.
    private sealed class Images
    {
        private readonly HashSet<string> allowed;
        private readonly Func<string, ResolvedAttachment?> resolve;
        private readonly Dictionary<string, ResolvedAttachment?> cache = new(StringComparer.Ordinal);

        public Images(IReadOnlyList<string> allowed, Func<string, ResolvedAttachment?> resolve)
        {
            this.allowed = new HashSet<string>(allowed, StringComparer.Ordinal);
            this.resolve = resolve;
        }

        public ResolvedAttachment? For(string src)
        {
            if (!allowed.Contains(src))
            {
                return null;
            }

            if (!cache.TryGetValue(src, out ResolvedAttachment? found))
            {
                found = resolve(src);
                cache[src] = found;
            }

            return found is { IsImage: true } ? found : null;
        }
    }

    // A list being built: its kind, the indentation of its items and the items.
    private sealed class ListBlock
    {
        public ListBlock(bool ordered, int indent, string start)
        {
            Ordered = ordered;
            Indent = indent;
            Start = start;
        }

        public bool Ordered { get; }

        public int Indent { get; }

        public string Start { get; }

        public List<ListItem> Items { get; } = [];
    }

    private sealed class ListItem
    {
        public ListItem(string html) => Html = html;

        public string Html { get; }

        public List<ListBlock> Children { get; } = [];
    }

    // The block pass: fences, headings, quotes, lists and paragraphs, line by line.
    private sealed class Blocks
    {
        private readonly List<string> lines;
        private readonly Images images;
        private readonly StringBuilder html;

        public Blocks(List<string> lines, Images images, StringBuilder html)
        {
            this.lines = lines;
            this.images = images;
            this.html = html;
        }

        public void Render()
        {
            int i = 0;
            while (i < lines.Count)
            {
                string line = lines[i];
                if (line.Trim().Length == 0)
                {
                    i++;
                }
                else if (IsFence(line))
                {
                    i = RenderFence(i);
                }
                else if (Heading(line) is { } heading)
                {
                    html.Append("<h").Append(heading.Level).Append('>');
                    Inline.Render(heading.Text, images, html);
                    html.Append("</h").Append(heading.Level).Append(">\n");
                    i++;
                }
                else if (Quote(line) is not null)
                {
                    i = RenderQuote(i);
                }
                else if (ListLine(line) is not null)
                {
                    i = RenderList(i);
                }
                else
                {
                    i = RenderParagraph(i);
                }
            }
        }

        private int RenderFence(int i)
        {
            html.Append("<pre><code>");
            int j = i + 1;
            bool first = true;
            while (j < lines.Count && !IsFence(lines[j]))
            {
                if (!first)
                {
                    html.Append('\n');
                }

                Encode(lines[j], html);
                first = false;
                j++;
            }

            html.Append("</code></pre>\n");
            return j < lines.Count ? j + 1 : j;
        }

        private int RenderQuote(int i)
        {
            List<string> quoted = [];
            while (i < lines.Count && Quote(lines[i]) is { } inner)
            {
                quoted.Add(inner);
                i++;
            }

            html.Append("<blockquote>\n");
            int at = 0;
            while (at < quoted.Count)
            {
                if (quoted[at].Trim().Length == 0)
                {
                    at++;
                    continue;
                }

                html.Append("<p>");
                bool first = true;
                while (at < quoted.Count && quoted[at].Trim().Length > 0)
                {
                    if (!first)
                    {
                        html.Append('\n');
                    }

                    Inline.Render(quoted[at].Trim(), images, html);
                    first = false;
                    at++;
                }

                html.Append("</p>\n");
            }

            html.Append("</blockquote>\n");
            return i;
        }

        // A paragraph: this line and the next ones up to a blank line or the start of another block.
        private int RenderParagraph(int i)
        {
            html.Append("<p>");
            Inline.Render(lines[i].Trim(), images, html);
            i++;
            while (i < lines.Count && !StartsBlock(lines[i]))
            {
                html.Append('\n');
                Inline.Render(lines[i].Trim(), images, html);
                i++;
            }

            html.Append("</p>\n");
            return i;
        }

        private static bool StartsBlock(string line) =>
            line.Trim().Length == 0 || IsFence(line) || Heading(line) is not null || Quote(line) is not null || ListLine(line) is not null;

        private int RenderList(int i)
        {
            List<ListBlock> roots = [];
            List<ListBlock> stack = [];
            while (i < lines.Count && ListLine(lines[i]) is { } item)
            {
                while (stack.Count > 0 && stack[^1].Indent > item.Indent)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                if (stack.Count > 0 && stack[^1].Indent == item.Indent && stack[^1].Ordered != item.Ordered)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                if (stack.Count == 0 || stack[^1].Indent < item.Indent)
                {
                    ListBlock opened = new ListBlock(item.Ordered, item.Indent, item.Start);
                    if (stack.Count == 0)
                    {
                        roots.Add(opened);
                    }
                    else
                    {
                        List<ListItem> parentItems = stack[^1].Items;
                        if (parentItems.Count == 0)
                        {
                            parentItems.Add(new ListItem(string.Empty));
                        }

                        parentItems[^1].Children.Add(opened);
                    }

                    stack.Add(opened);
                }

                StringBuilder content = new StringBuilder();
                Inline.Render(item.Text, images, content);
                stack[^1].Items.Add(new ListItem(item.Task + content.ToString()));
                i++;
            }

            foreach (ListBlock root in roots)
            {
                Write(root);
            }

            return i;
        }

        private void Write(ListBlock list)
        {
            string tag = list.Ordered ? "ol" : "ul";
            html.Append('<').Append(tag);
            if (list.Ordered && list.Start != "1")
            {
                html.Append(" start=\"").Append(list.Start).Append('"');
            }

            html.Append(">\n");
            foreach (ListItem item in list.Items)
            {
                html.Append("<li>").Append(item.Html);
                if (item.Children.Count > 0)
                {
                    html.Append('\n');
                    foreach (ListBlock child in item.Children)
                    {
                        Write(child);
                    }
                }

                html.Append("</li>\n");
            }

            html.Append("</").Append(tag).Append(">\n");
        }

        // "# Title" up to "###### Title": at most three spaces, 1-6 '#', then a space or the end of the line.
        private static (int Level, string Text)? Heading(string line)
        {
            int at = Indent(line, out int width);
            if (width > 3)
            {
                return null;
            }

            int level = 0;
            while (at + level < line.Length && line[at + level] == '#')
            {
                level++;
            }

            int after = at + level;
            if (level is < 1 or > 6 || (after < line.Length && line[after] is not (' ' or '\t')))
            {
                return null;
            }

            string text = line[after..].Trim();

            // A closing run of '#' after a space is no part of the heading.
            int end = text.Length;
            while (end > 0 && text[end - 1] == '#')
            {
                end--;
            }

            if (end < text.Length && (end == 0 || text[end - 1] is ' ' or '\t'))
            {
                text = text[..end].TrimEnd();
            }

            return (level, text);
        }

        // The text after "> " (or ">"), or null when the line is no quote line.
        private static string? Quote(string line)
        {
            int at = Indent(line, out int width);
            if (width > 3 || at >= line.Length || line[at] != '>')
            {
                return null;
            }

            int from = at + 1;
            if (from < line.Length && line[from] == ' ')
            {
                from++;
            }

            return line[from..];
        }

        // "- x", "* x" or "1. x": the indentation, the kind, the first number, the task box and the text.
        private static (int Indent, bool Ordered, string Start, string Task, string Text)? ListLine(string line)
        {
            int at = Indent(line, out int width);
            if (at >= line.Length)
            {
                return null;
            }

            bool ordered;
            int after;
            string start = string.Empty;
            if (line[at] is '-' or '*')
            {
                ordered = false;
                after = at + 1;
            }
            else
            {
                int digits = at;
                while (digits < line.Length && digits - at < 9 && char.IsAsciiDigit(line[digits]))
                {
                    digits++;
                }

                if (digits == at || digits >= line.Length || line[digits] != '.')
                {
                    return null;
                }

                ordered = true;
                start = line[at..digits].TrimStart('0');
                start = start.Length == 0 ? "0" : start;
                after = digits + 1;
            }

            if (after < line.Length && line[after] is not (' ' or '\t'))
            {
                return null;
            }

            string text = after < line.Length ? line[(after + 1)..].Trim() : string.Empty;
            string task = string.Empty;
            if (text.Length >= 3 && text[0] == '[' && text[2] == ']' && (text.Length == 3 || text[3] is ' ' or '\t'))
            {
                if (text[1] == ' ')
                {
                    task = "☐ ";
                    text = text[3..].TrimStart();
                }
                else if (text[1] is 'x' or 'X')
                {
                    task = "☑ ";
                    text = text[3..].TrimStart();
                }
            }

            return (width, ordered, start, task, text);
        }

        // The index of the first character that is no space or tab; width counts a tab as four.
        private static int Indent(string line, out int width)
        {
            int at = 0;
            width = 0;
            while (at < line.Length && line[at] is ' ' or '\t')
            {
                width += line[at] == '\t' ? 4 : 1;
                at++;
            }

            return at;
        }
    }

    // One piece of a line for the inline pass: a literal character, or an opaque piece (a code span, a tag, a link or
    // a mention) that is already HTML and whose plain text is kept for an image's alt text.
    private readonly record struct Token(char C, string? Html, string? Plain)
    {
        public bool IsChar => Html is null;

        public bool Is(char c) => Html is null && C == c;

        public bool IsSpace => Html is null && char.IsWhiteSpace(C);

        public bool IsWordChar => Html is null && char.IsLetterOrDigit(C);
    }

    // The inline pass over one line: code spans and the parsers' tags, links and mentions are found first on the raw
    // line (so they follow the same rules as everywhere else); links, images and emphasis are then found between them,
    // each opener looked up in precomputed "next" tables so a line is scanned in linear time.
    private sealed class Inline
    {
        private readonly Token[] tokens;
        private readonly Images images;
        private readonly int[] nextCloseBracket;
        private readonly int[] nextOpenBracket;
        private readonly int[] nextCloseParen;
        private readonly int[] nextDoubleStar;
        private readonly int[] nextSingleStar;
        private readonly int[] nextUnderscore;
        private readonly int[] opaqueBefore;

        private Inline(Token[] tokens, Images images)
        {
            this.tokens = tokens;
            this.images = images;
            int n = tokens.Length;
            nextCloseBracket = Next(t => t.Is(']'));
            nextOpenBracket = Next(t => t.Is('['));
            nextCloseParen = Next(t => t.Is(')'));
            nextDoubleStar = NextAt(i => tokens[i].Is('*') && i + 1 < n && tokens[i + 1].Is('*'));
            nextSingleStar = NextAt(i => IsSingleStar(i));
            nextUnderscore = Next(t => t.Is('_'));
            opaqueBefore = new int[n + 1];
            for (int i = 0; i < n; i++)
            {
                opaqueBefore[i + 1] = opaqueBefore[i] + (tokens[i].IsChar ? 0 : 1);
            }
        }

        public static void Render(string line, Images images, StringBuilder html)
        {
            Inline inline = new Inline(Tokenize(line), images);
            inline.Render(0, inline.tokens.Length, 0, true, html);
        }

        private static Token[] Tokenize(string line)
        {
            List<(int From, int To, string Text)> atoms = [];
            TagParser.ScanLine(line, (from, to, tag) => atoms.Add((from, to, TagParser.Format(tag))));
            foreach ((int from, int to, string target) in NoteLinkParser.LineLinks(line))
            {
                atoms.Add((from, to, target));
            }

            int at = 0;
            foreach (PersonSegment segment in PersonParser.Segments(line))
            {
                if (segment.Name is { } name)
                {
                    atoms.Add((at, at + segment.Text.Length, name));
                }

                at += segment.Text.Length;
            }

            atoms.Sort((a, b) => a.From.CompareTo(b.From));
            List<Token> tokens = new List<Token>(line.Length);
            CodeSpans? spans = CodeSpans.For(line);
            int next = 0;
            int i = 0;
            while (i < line.Length)
            {
                while (next < atoms.Count && atoms[next].From < i)
                {
                    next++;
                }

                if (next < atoms.Count && atoms[next].From == i)
                {
                    StringBuilder shown = new StringBuilder();
                    Encode(atoms[next].Text, shown);
                    tokens.Add(new Token('\0', shown.ToString(), atoms[next].Text));
                    i = atoms[next].To;
                    next++;
                    continue;
                }

                if (line[i] == '`')
                {
                    int run = i;
                    while (run < line.Length && line[run] == '`')
                    {
                        run++;
                    }

                    int end = spans!.Skip(i);
                    if (end > run)
                    {
                        string code = line[run..(end - (run - i))];
                        StringBuilder shown = new StringBuilder("<code>");
                        Encode(code, shown);
                        shown.Append("</code>");
                        tokens.Add(new Token('\0', shown.ToString(), code));
                        i = end;
                        continue;
                    }

                    for (int b = i; b < run; b++)
                    {
                        tokens.Add(new Token('`', null, null));
                    }

                    i = run;
                    continue;
                }

                tokens.Add(new Token(line[i], null, null));
                i++;
            }

            return tokens.ToArray();
        }

        private bool IsSingleStar(int i) =>
            tokens[i].Is('*') && (i == 0 || !tokens[i - 1].Is('*')) && (i + 1 >= tokens.Length || !tokens[i + 1].Is('*'));

        private int[] Next(Func<Token, bool> match) => NextAt(i => match(tokens[i]));

        // For each index, the first index at or after it where match holds; the length when there is none.
        private int[] NextAt(Func<int, bool> match)
        {
            int n = tokens.Length;
            int[] next = new int[n + 1];
            next[n] = n;
            for (int i = n - 1; i >= 0; i--)
            {
                next[i] = match(i) ? i : next[i + 1];
            }

            return next;
        }

        private int NextOf(int[] table, int from) => from >= table.Length ? tokens.Length : table[from];

        private void Render(int from, int to, int depth, bool links, StringBuilder html)
        {
            int i = from;
            while (i < to)
            {
                Token t = tokens[i];
                if (!t.IsChar)
                {
                    html.Append(t.Html);
                    i++;
                    continue;
                }

                if (t.C == '!' && i + 1 < to && tokens[i + 1].Is('[') && LinkAt(i + 1, to) is { } image)
                {
                    RenderImage(i + 2, image.Close, image.Url, html);
                    i = image.End;
                    continue;
                }

                if (t.C == '[' && LinkAt(i, to) is { } link)
                {
                    if (!links)
                    {
                        html.Append('[');
                        i++;
                        continue;
                    }

                    bool live = IsSafeUrl(link.Url);
                    if (live)
                    {
                        html.Append("<a href=\"");
                        Encode(link.Url, html);
                        html.Append("\">");
                    }

                    Render(i + 1, link.Close, depth + 1, false, html);
                    if (live)
                    {
                        html.Append("</a>");
                    }

                    i = link.End;
                    continue;
                }

                if (depth < MaxDepth && t.C == '*' && i + 1 < to && tokens[i + 1].Is('*'))
                {
                    int close = NextOf(nextDoubleStar, i + 2);
                    if (close + 1 < to && close > i + 2 && !tokens[i + 2].IsSpace && !tokens[close - 1].IsSpace)
                    {
                        html.Append("<strong>");
                        Render(i + 2, close, depth + 1, links, html);
                        html.Append("</strong>");
                        i = close + 2;
                        continue;
                    }

                    html.Append("**");
                    i += 2;
                    continue;
                }

                if (depth < MaxDepth && t.C == '*' && IsSingleStar(i))
                {
                    int close = NextOf(nextSingleStar, i + 1);
                    if (close < to && close > i + 1 && !tokens[i + 1].IsSpace && !tokens[close - 1].IsSpace)
                    {
                        Emphasis(i, close, depth, links, html);
                        i = close + 1;
                        continue;
                    }
                }

                if (depth < MaxDepth && t.C == '_' && (i == 0 || !tokens[i - 1].IsWordChar) && i + 1 < to && !tokens[i + 1].Is('_'))
                {
                    int close = NextOf(nextUnderscore, i + 1);
                    if (close < to && close > i + 1 && !tokens[i + 1].IsSpace && !tokens[close - 1].IsSpace
                        && (close + 1 >= tokens.Length || !tokens[close + 1].IsWordChar))
                    {
                        Emphasis(i, close, depth, links, html);
                        i = close + 1;
                        continue;
                    }
                }

                Encode(t.C, html);
                i++;
            }
        }

        private void Emphasis(int open, int close, int depth, bool links, StringBuilder html)
        {
            html.Append("<em>");
            Render(open + 1, close, depth + 1, links, html);
            html.Append("</em>");
        }

        // "[text](url)" starting at the '[' at open, within to: the index of its ']', the end after ')' and the URL
        // (trimmed, up to the first whitespace). The text holds no other '[' and the URL only literal characters.
        private (int Close, int End, string Url)? LinkAt(int open, int to)
        {
            int close = NextOf(nextCloseBracket, open + 1);
            if (close + 1 >= to || NextOf(nextOpenBracket, open + 1) < close || !tokens[close + 1].Is('('))
            {
                return null;
            }

            int paren = NextOf(nextCloseParen, close + 2);
            if (paren >= to || opaqueBefore[paren] != opaqueBefore[close + 2])
            {
                return null;
            }

            StringBuilder url = new StringBuilder(paren - close - 2);
            for (int k = close + 2; k < paren; k++)
            {
                url.Append(tokens[k].C);
            }

            string trimmed = url.ToString().Trim();
            int space = 0;
            while (space < trimmed.Length && !char.IsWhiteSpace(trimmed[space]))
            {
                space++;
            }

            return (close, paren + 1, trimmed[..space]);
        }

        private void RenderImage(int from, int to, string src, StringBuilder html)
        {
            StringBuilder alt = new StringBuilder();
            for (int k = from; k < to; k++)
            {
                if (tokens[k].IsChar)
                {
                    alt.Append(tokens[k].C);
                }
                else
                {
                    alt.Append(tokens[k].Plain);
                }
            }

            if (images.For(src) is { } image)
            {
                html.Append("<img alt=\"");
                Encode(alt.ToString(), html);
                html.Append("\" src=\"data:").Append(image.Mime).Append(";base64,").Append(Convert.ToBase64String(image.Bytes)).Append("\">");
                return;
            }

            html.Append("[image: ");
            Encode(alt.ToString(), html);
            html.Append(']');
        }

        private static bool IsSafeUrl(string url) =>
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
    }
}
