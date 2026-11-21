using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Context;

/// <summary>
/// Draws an Excalidraw file (job 384) as a plain SVG that Context writes itself: only <c>svg</c>, <c>g</c>,
/// <c>rect</c>, <c>ellipse</c>, <c>polygon</c>, <c>polyline</c>, <c>path</c>, <c>text</c> and <c>tspan</c>, from a short
/// list of known fields. Nothing in the file is ever copied into the markup except checked numbers, colours that match a
/// fixed shape and XML-escaped text content, so the SVG holds no script, link, style or URL whatever the file says.
/// Pure: reads no file and never throws.
/// </summary>
internal static class ExcalidrawSvg
{
    /// <summary>The deepest JSON nesting read.</summary>
    public const int MaxDepth = 64;

    /// <summary>The most elements a drawing may have.</summary>
    public const int MaxElements = 5000;

    /// <summary>The most points one element may have.</summary>
    public const int MaxPointsPerElement = 5000;

    /// <summary>The most points a whole drawing may have.</summary>
    public const int MaxPoints = 100_000;

    /// <summary>The longest SVG written, in characters.</summary>
    public const int MaxSvgChars = 5_000_000;

    /// <summary>The largest absolute value of a number; anything bigger is clamped to it.</summary>
    public const double MaxCoordinate = 1_000_000;

    /// <summary>The space around the drawn elements, on every side.</summary>
    public const double Margin = 10;

    /// <summary>The stroke colour used when the file gives none, or one of another shape.</summary>
    public const string DefaultStroke = "#1e1e1e";

    /// <summary>The length of an arrowhead's sides.</summary>
    public const double ArrowHeadLength = 12;

    /// <summary>The MIME type of the rendered drawing.</summary>
    public const string Mime = "image/svg+xml";

    private const double ArrowHeadHalfAngle = Math.PI / 7;
    private const double LineHeight = 1.25;
    private const double DefaultStrokeWidth = 2;
    private const double DefaultFontSize = 20;
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private static readonly System.Buffers.SearchValues<char> HexDigits =
        System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>
    /// The SVG of the Excalidraw JSON in <paramref name="json"/> (a UTF-8 BOM is allowed), or <c>null</c> when it is not
    /// valid JSON, nests deeper than <see cref="MaxDepth"/>, has no <c>elements</c> array, has more than
    /// <see cref="MaxElements"/> elements, an element with more than <see cref="MaxPointsPerElement"/> points, more than
    /// <see cref="MaxPoints"/> points in all, or the SVG would be longer than <see cref="MaxSvgChars"/>.
    /// </summary>
    public static string? Render(ReadOnlySpan<byte> json)
    {
        try
        {
            if (json.StartsWith(Bom))
            {
                json = json[Bom.Length..];
            }

            using JsonDocument document = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions { MaxDepth = MaxDepth });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("elements", out JsonElement elements)
                || elements.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            if (elements.GetArrayLength() > MaxElements || !PointsWithinLimits(elements))
            {
                return null;
            }

            StringBuilder body = new StringBuilder();
            Bounds bounds = new Bounds();
            foreach (JsonElement element in elements.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object && !IsDeleted(element))
                {
                    DrawElement(element, body, ref bounds);
                    if (body.Length > MaxSvgChars)
                    {
                        return null;
                    }
                }
            }

            string svg = Wrap(body, bounds);
            return svg.Length > MaxSvgChars ? null : svg;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// True when <paramref name="value"/> may be written as a colour: <c>#rgb</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>
    /// (hex digits) or 1 to 20 lower-case ASCII letters (<c>transparent</c>, <c>red</c>).
    /// </summary>
    public static bool IsSafeColour(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value[0] == '#')
        {
            return value.Length is 4 or 7 or 9 && !value.AsSpan(1).ContainsAnyExcept(HexDigits);
        }

        return value.Length <= 20 && !value.AsSpan().ContainsAnyExceptInRange('a', 'z');
    }

    // Counts the points of every element before anything is drawn, so a huge file is refused cheaply.
    private static bool PointsWithinLimits(JsonElement elements)
    {
        long total = 0;
        foreach (JsonElement element in elements.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("points", out JsonElement points)
                && points.ValueKind == JsonValueKind.Array)
            {
                int count = points.GetArrayLength();
                total += count;
                if (count > MaxPointsPerElement || total > MaxPoints)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsDeleted(JsonElement element) =>
        element.TryGetProperty("isDeleted", out JsonElement deleted) && deleted.ValueKind == JsonValueKind.True;

    private static string Wrap(StringBuilder body, Bounds bounds)
    {
        double minX = bounds.Any ? bounds.MinX - Margin : 0;
        double minY = bounds.Any ? bounds.MinY - Margin : 0;
        double width = bounds.Any ? bounds.MaxX - bounds.MinX + (2 * Margin) : 2 * Margin;
        double height = bounds.Any ? bounds.MaxY - bounds.MinY + (2 * Margin) : 2 * Margin;
        StringBuilder svg = new StringBuilder(body.Length + 200);
        svg.Append("<svg xmlns=\"").Append(SvgNamespace).Append("\" viewBox=\"")
            .Append(Num(minX)).Append(' ').Append(Num(minY)).Append(' ')
            .Append(Num(width)).Append(' ').Append(Num(height))
            .Append("\" width=\"").Append(Num(width)).Append("\" height=\"").Append(Num(height)).Append("\">");
        svg.Append(body);
        svg.Append("</svg>");
        return svg.ToString();
    }

    // Appends one element (or nothing, for an unknown type or a bad field) and widens the bounds by what it drew.
    private static void DrawElement(JsonElement element, StringBuilder body, ref Bounds bounds)
    {
        string? type = element.TryGetProperty("type", out JsonElement t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;
        if (type is not ("rectangle" or "ellipse" or "diamond" or "line" or "arrow" or "freedraw" or "text"))
        {
            return;
        }

        if (!TryNumber(element, "x", 0, out double x) || !TryNumber(element, "y", 0, out double y)
            || !TryNumber(element, "width", 0, out double width) || !TryNumber(element, "height", 0, out double height)
            || !TryNumber(element, "angle", 0, out double angle)
            || !TryNumber(element, "strokeWidth", DefaultStrokeWidth, out double strokeWidth)
            || !TryNumber(element, "opacity", 100, out double opacity))
        {
            return;
        }

        string stroke = Colour(element, "strokeColor") ?? DefaultStroke;
        string fill = Colour(element, "backgroundColor") ?? "none";
        strokeWidth = Math.Max(0, strokeWidth);
        opacity = Math.Clamp(opacity / 100, 0, 1);

        string shape;
        Box box;
        switch (type)
        {
            case "rectangle":
            case "ellipse":
            case "diamond":
                box = Box.Of(x, y, width, height);
                shape = Shape(type, box, stroke, fill, strokeWidth);
                break;
            case "text":
                if (!TryText(element, x, y, width, height, stroke, out shape, out box))
                {
                    return;
                }

                break;
            default:
                if (!TryPoints(element, x, y, out List<(double X, double Y)> points) || points.Count == 0)
                {
                    return;
                }

                box = Box.Around(points);
                shape = Linear(type, points, stroke, strokeWidth);
                break;
        }

        double cx = box.X + (box.Width / 2);
        double cy = box.Y + (box.Height / 2);
        bool rotated = angle != 0;
        body.Append("<g");
        if (opacity < 1)
        {
            body.Append(" opacity=\"").Append(Num(opacity)).Append('"');
        }

        if (rotated)
        {
            body.Append(" transform=\"rotate(").Append(Num(angle * 180 / Math.PI)).Append(' ')
                .Append(Num(cx)).Append(' ').Append(Num(cy)).Append(")\"");
        }

        body.Append('>').Append(shape).Append("</g>");

        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        foreach ((double px, double py) in new[]
        {
            (box.X, box.Y), (box.X + box.Width, box.Y), (box.X, box.Y + box.Height), (box.X + box.Width, box.Y + box.Height),
        })
        {
            if (rotated)
            {
                double dx = px - cx;
                double dy = py - cy;
                bounds.Add(cx + (dx * cos) - (dy * sin), cy + (dx * sin) + (dy * cos));
            }
            else
            {
                bounds.Add(px, py);
            }
        }
    }

    private static string Shape(string type, Box box, string stroke, string fill, double strokeWidth)
    {
        string paint = $" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{Num(strokeWidth)}\"";
        return type switch
        {
            "rectangle" => $"<rect x=\"{Num(box.X)}\" y=\"{Num(box.Y)}\" width=\"{Num(box.Width)}\" height=\"{Num(box.Height)}\"{paint}/>",
            "ellipse" => $"<ellipse cx=\"{Num(box.X + (box.Width / 2))}\" cy=\"{Num(box.Y + (box.Height / 2))}\" rx=\"{Num(box.Width / 2)}\" ry=\"{Num(box.Height / 2)}\"{paint}/>",
            _ => "<polygon points=\""
                + Pair(box.X + (box.Width / 2), box.Y) + " " + Pair(box.X + box.Width, box.Y + (box.Height / 2)) + " "
                + Pair(box.X + (box.Width / 2), box.Y + box.Height) + " " + Pair(box.X, box.Y + (box.Height / 2))
                + "\"" + paint + "/>",
        };
    }

    private static string Linear(string type, List<(double X, double Y)> points, string stroke, double strokeWidth)
    {
        StringBuilder line = new StringBuilder();
        line.Append("<polyline points=\"");
        for (int i = 0; i < points.Count; i++)
        {
            line.Append(i == 0 ? string.Empty : " ").Append(Pair(points[i].X, points[i].Y));
        }

        line.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(Num(strokeWidth))
            .Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
        if (type == "arrow" && points.Count >= 2)
        {
            (double ex, double ey) = points[^1];
            (double sx, double sy) = points[^2];
            double direction = Math.Atan2(ey - sy, ex - sx);
            double leftX = ex - (ArrowHeadLength * Math.Cos(direction - ArrowHeadHalfAngle));
            double leftY = ey - (ArrowHeadLength * Math.Sin(direction - ArrowHeadHalfAngle));
            double rightX = ex - (ArrowHeadLength * Math.Cos(direction + ArrowHeadHalfAngle));
            double rightY = ey - (ArrowHeadLength * Math.Sin(direction + ArrowHeadHalfAngle));
            line.Append("<polygon points=\"").Append(Pair(ex, ey)).Append(' ').Append(Pair(leftX, leftY)).Append(' ')
                .Append(Pair(rightX, rightY)).Append("\" fill=\"").Append(stroke).Append("\" stroke=\"").Append(stroke)
                .Append("\" stroke-width=\"").Append(Num(strokeWidth)).Append("\" stroke-linejoin=\"round\"/>");
        }

        return line.ToString();
    }

    private static bool TryText(
        JsonElement element, double x, double y, double width, double height, string stroke, out string shape, out Box box)
    {
        shape = string.Empty;
        box = Box.Of(x, y, width, height);
        if (!element.TryGetProperty("text", out JsonElement textValue) || textValue.ValueKind != JsonValueKind.String
            || !TryNumber(element, "fontSize", DefaultFontSize, out double fontSize))
        {
            return false;
        }

        fontSize = Math.Max(0, fontSize);
        string align = element.TryGetProperty("textAlign", out JsonElement a) && a.ValueKind == JsonValueKind.String
            ? a.GetString() ?? string.Empty
            : string.Empty;
        (string anchor, double anchorX) = align switch
        {
            "center" => ("middle", box.X + (box.Width / 2)),
            "right" => ("end", box.X + box.Width),
            _ => ("start", box.X),
        };

        string raw;
        try
        {
            raw = textValue.GetString() ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            // A lone surrogate escape (\ud800) can't be read as a string; the element is skipped.
            return false;
        }

        string[] lines = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        StringBuilder text = new StringBuilder();
        text.Append("<text x=\"").Append(Num(anchorX)).Append("\" y=\"").Append(Num(box.Y))
            .Append("\" font-family=\"sans-serif\" font-size=\"").Append(Num(fontSize)).Append("\" fill=\"").Append(stroke)
            .Append("\" text-anchor=\"").Append(anchor).Append("\">");
        for (int i = 0; i < lines.Length; i++)
        {
            double lineY = box.Y + fontSize + (i * fontSize * LineHeight);
            text.Append("<tspan x=\"").Append(Num(anchorX)).Append("\" y=\"").Append(Num(Clamp(lineY))).Append("\">");
            AppendEscaped(text, lines[i]);
            text.Append("</tspan>");
        }

        text.Append("</text>");
        shape = text.ToString();
        return true;
    }

    // The points of a line, arrow or freedraw, made absolute; false when any point is not a pair of finite numbers.
    private static bool TryPoints(JsonElement element, double x, double y, out List<(double X, double Y)> points)
    {
        points = [];
        if (!element.TryGetProperty("points", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement point in list.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 2
                || !TryFinite(point[0], out double px) || !TryFinite(point[1], out double py))
            {
                return false;
            }

            points.Add((x + px, y + py));
        }

        return true;
    }

    // A missing or null field is the default; a number must be finite and is clamped; anything else fails the element.
    private static bool TryNumber(JsonElement element, string name, double fallback, out double value)
    {
        if (!element.TryGetProperty(name, out JsonElement field) || field.ValueKind == JsonValueKind.Null)
        {
            value = fallback;
            return true;
        }

        return TryFinite(field, out value);
    }

    private static bool TryFinite(JsonElement field, out double value)
    {
        value = 0;
        if (field.ValueKind != JsonValueKind.Number || !field.TryGetDouble(out double raw) || !double.IsFinite(raw))
        {
            return false;
        }

        value = Clamp(raw);
        return true;
    }

    private static double Clamp(double value) => Math.Clamp(value, -MaxCoordinate, MaxCoordinate);

    private static string? Colour(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement field) || field.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? value = field.GetString();
        return IsSafeColour(value) ? value : null;
    }

    // XML-escapes text content and leaves out every character XML 1.0 can't hold (controls but tab, lone surrogates).
    private static void AppendEscaped(StringBuilder text, string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                text.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }

            switch (c)
            {
                case '&':
                    text.Append("&amp;");
                    break;
                case '<':
                    text.Append("&lt;");
                    break;
                case '>':
                    text.Append("&gt;");
                    break;
                case '\t':
                    text.Append(c);
                    break;
                default:
                    if (c >= 0x20 && !char.IsSurrogate(c) && c is not ('￾' or '￿'))
                    {
                        text.Append(c);
                    }

                    break;
            }
        }
    }

    private static string Pair(double x, double y) => Num(x) + "," + Num(y);

    // Invariant, at most three decimals, never an exponent (every value is clamped to MaxCoordinate first).
    private static string Num(double value)
    {
        double rounded = Math.Round(value, 3);
        return (rounded == 0 ? 0 : rounded).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private readonly record struct Box(double X, double Y, double Width, double Height)
    {
        // A negative width or height is drawn the other way from x or y.
        public static Box Of(double x, double y, double width, double height) =>
            new Box(width < 0 ? x + width : x, height < 0 ? y + height : y, Math.Abs(width), Math.Abs(height));

        public static Box Around(List<(double X, double Y)> points)
        {
            double minX = points.Min(p => p.X);
            double minY = points.Min(p => p.Y);
            return new Box(minX, minY, points.Max(p => p.X) - minX, points.Max(p => p.Y) - minY);
        }
    }

    private struct Bounds
    {
        public bool Any;
        public double MinX;
        public double MinY;
        public double MaxX;
        public double MaxY;

        public void Add(double x, double y)
        {
            if (!Any)
            {
                (Any, MinX, MinY, MaxX, MaxY) = (true, x, y, x, y);
                return;
            }

            MinX = Math.Min(MinX, x);
            MinY = Math.Min(MinY, y);
            MaxX = Math.Max(MaxX, x);
            MaxY = Math.Max(MaxY, y);
        }
    }
}
