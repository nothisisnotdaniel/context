using System.Text;
using System.Xml.Linq;

namespace Context.Tests;

// Job 384: the Excalidraw JSON -> SVG renderer (AC-1 to AC-3).
public class ExcalidrawSvgTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private static readonly HashSet<string> AllowedElements =
        ["svg", "g", "rect", "ellipse", "polygon", "polyline", "path", "text", "tspan"];

    private const string Payload =
        "\\\"/><script>alert(1)</script><a href=\\\"javascript:alert(1)\\\" onload=\\\"x\\\" style=\\\"x\\\">"
        + "url(data:x) <!-- <?xml ?> <![CDATA[ & ]]>";

    private static string? Render(string json) => ExcalidrawSvg.Render(Encoding.UTF8.GetBytes(json));

    private static XElement Parse(string? svg)
    {
        Assert.NotNull(svg);
        XElement root = XDocument.Parse(svg).Root!;
        Assert.Equal(Svg + "svg", root.Name);
        return root;
    }

    private static string Doc(params string[] elements) =>
        "{\"type\":\"excalidraw\",\"elements\":[" + string.Join(",", elements) + "]}";

    private static List<XElement> Shapes(XElement root) =>
        root.Elements(Svg + "g").Select(g => g.Elements().First()).ToList();

    // Every element is on the allowlist, no attribute is a link, a style or a handler, no value holds a URL scheme.
    private static void AssertSafe(string svg)
    {
        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<!", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<?", svg, StringComparison.Ordinal);
        XElement root = Parse(svg);
        foreach (XElement element in root.DescendantsAndSelf())
        {
            Assert.Equal(Svg, element.Name.Namespace);
            Assert.Contains(element.Name.LocalName, AllowedElements);
            foreach (XAttribute attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                string name = attribute.Name.LocalName;
                Assert.Equal(XNamespace.None, attribute.Name.Namespace);
                Assert.NotEqual("href", name, StringComparer.OrdinalIgnoreCase);
                Assert.NotEqual("style", name, StringComparer.OrdinalIgnoreCase);
                Assert.False(name.StartsWith("on", StringComparison.OrdinalIgnoreCase), name);
                Assert.DoesNotContain("url(", attribute.Value, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("data:", attribute.Value, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("javascript:", attribute.Value, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // AC-1: each supported type is drawn in array order; deleted, unknown, image and frame elements are skipped.
    [Fact]
    public void Render_DrawsSupportedTypesInOrder_SkipsDeletedAndUnknown()
    {
        string json = "{\"type\":\"excalidraw\",\"files\":{\"f\":{\"dataURL\":\"data:image/png;base64,AAAA\"}},\"elements\":["
            + "{\"type\":\"rectangle\",\"x\":0,\"y\":0,\"width\":100,\"height\":50,\"strokeColor\":\"#ff0000\",\"backgroundColor\":\"#00ff00\",\"strokeWidth\":3},"
            + "{\"type\":\"rectangle\",\"x\":0,\"y\":0,\"width\":5,\"height\":5,\"isDeleted\":true},"
            + "{\"type\":\"image\",\"x\":0,\"y\":0,\"width\":5,\"height\":5,\"fileId\":\"f\"},"
            + "{\"type\":\"frame\",\"x\":0,\"y\":0,\"width\":5,\"height\":5},"
            + "{\"type\":\"embeddable\",\"x\":0,\"y\":0,\"width\":5,\"height\":5},"
            + "{\"type\":\"ellipse\",\"x\":10,\"y\":20,\"width\":40,\"height\":20},"
            + "{\"type\":\"diamond\",\"x\":0,\"y\":0,\"width\":20,\"height\":10},"
            + "{\"type\":\"line\",\"x\":5,\"y\":5,\"points\":[[0,0],[10,10]]},"
            + "{\"type\":\"arrow\",\"x\":0,\"y\":0,\"points\":[[0,0],[100,0]]},"
            + "{\"type\":\"freedraw\",\"x\":0,\"y\":0,\"points\":[[0,0],[1,2],[3,4]]},"
            + "{\"type\":\"text\",\"x\":0,\"y\":100,\"width\":80,\"height\":50,\"text\":\"one\\ntwo\",\"fontSize\":16,\"textAlign\":\"center\",\"opacity\":50}"
            + "]}";

        XElement root = Parse(Render(json));
        List<XElement> shapes = Shapes(root);

        Assert.Equal(["rect", "ellipse", "polygon", "polyline", "polyline", "polyline", "text"],
            shapes.Select(s => s.Name.LocalName));
        XElement rect = shapes[0];
        Assert.Equal(("0", "0", "100", "50"), ((string)rect.Attribute("x")!, (string)rect.Attribute("y")!,
            (string)rect.Attribute("width")!, (string)rect.Attribute("height")!));
        Assert.Equal(("#ff0000", "#00ff00", "3"),
            ((string)rect.Attribute("stroke")!, (string)rect.Attribute("fill")!, (string)rect.Attribute("stroke-width")!));
        Assert.Equal(("30", "30", "20", "10"), ((string)shapes[1].Attribute("cx")!, (string)shapes[1].Attribute("cy")!,
            (string)shapes[1].Attribute("rx")!, (string)shapes[1].Attribute("ry")!));
        Assert.Equal("10,0 20,5 10,10 0,5", (string)shapes[2].Attribute("points")!);
        Assert.Equal("5,5 15,15", (string)shapes[3].Attribute("points")!);
        Assert.Equal("none", (string)shapes[3].Attribute("fill")!);

        XElement arrowGroup = root.Elements(Svg + "g").ElementAt(4);
        Assert.Equal(["polyline", "polygon"], arrowGroup.Elements().Select(e => e.Name.LocalName));
        Assert.StartsWith("100,0 ", (string)arrowGroup.Elements().Last().Attribute("points")!);

        XElement text = shapes[6];
        Assert.Equal(("middle", "16", "#1e1e1e"), ((string)text.Attribute("text-anchor")!,
            (string)text.Attribute("font-size")!, (string)text.Attribute("fill")!));
        Assert.Equal(["one", "two"], text.Elements(Svg + "tspan").Select(t => t.Value));
        Assert.Equal(["116", "136"], text.Elements(Svg + "tspan").Select(t => (string)t.Attribute("y")!));
        Assert.Equal("0.5", (string)root.Elements(Svg + "g").Last().Attribute("opacity")!);
    }

    // AC-1: the viewBox is the bounding box of the drawn elements plus a 10-unit margin.
    [Fact]
    public void Render_ViewBox_IsBoundsPlusMargin()
    {
        XElement root = Parse(Render(Doc(
            "{\"type\":\"rectangle\",\"x\":10,\"y\":20,\"width\":100,\"height\":50}",
            "{\"type\":\"line\",\"x\":0,\"y\":0,\"points\":[[-5,0],[0,90]]}",
            "{\"type\":\"rectangle\",\"x\":-500,\"y\":-500,\"width\":1,\"height\":1,\"isDeleted\":true}")));

        Assert.Equal("-15 -10 135 110", (string)root.Attribute("viewBox")!);
        Assert.Equal(("135", "110"), ((string)root.Attribute("width")!, (string)root.Attribute("height")!));
    }

    // AC-1: a rotated element is drawn with a rotate transform about its centre.
    [Fact]
    public void Render_Angle_RotatesAboutTheCentre()
    {
        XElement root = Parse(Render(Doc(
            "{\"type\":\"rectangle\",\"x\":0,\"y\":0,\"width\":20,\"height\":10,\"angle\":3.141592653589793}")));

        Assert.Equal("rotate(180 10 5)", (string)root.Element(Svg + "g")!.Attribute("transform")!);
    }

    // AC-1: a drawing with no drawable element is a valid, empty svg.
    [Theory]
    [InlineData("{\"type\":\"excalidraw\",\"elements\":[]}")]
    [InlineData("{\"elements\":[{\"type\":\"image\"},{\"type\":\"rectangle\",\"isDeleted\":true},1,\"x\",null]}")]
    public void Render_NothingDrawable_EmptySvg(string json)
    {
        XElement root = Parse(Render(json));

        Assert.Empty(root.Elements());
        Assert.Equal("0 0 20 20", (string)root.Attribute("viewBox")!);
    }

    // AC-2: payloads in every string field never leave the allowlist, and text is escaped.
    [Fact]
    public void Render_CraftedFixture_StaysSafe()
    {
        string bad = "\"" + Payload + "\"";
        string fields = $"\"strokeColor\":{bad},\"backgroundColor\":{bad},\"textAlign\":{bad},\"fontFamily\":{bad},"
            + $"\"link\":{bad},\"id\":{bad},\"customData\":{{\"href\":{bad}}}";
        string json = "{\"type\":\"excalidraw\",\"source\":" + bad + ",\"appState\":{\"name\":" + bad + "},\"elements\":["
            + "{\"type\":\"rectangle\",\"x\":0,\"y\":0,\"width\":1,\"height\":1," + fields + "},"
            + "{\"type\":\"ellipse\",\"x\":0,\"y\":0,\"width\":1,\"height\":1," + fields + "},"
            + "{\"type\":\"diamond\",\"x\":0,\"y\":0,\"width\":1,\"height\":1," + fields + "},"
            + "{\"type\":\"arrow\",\"x\":0,\"y\":0,\"points\":[[0,0],[1,1]]," + fields + "},"
            + "{\"type\":\"text\",\"x\":0,\"y\":0,\"text\":" + bad + "," + fields + "},"
            + "{\"type\":" + bad + ",\"x\":0,\"y\":0}"
            + "]}";

        string? svg = Render(json);

        Assert.NotNull(svg);
        AssertSafe(svg);
        XElement root = Parse(svg);
        Assert.Equal(5, root.Elements(Svg + "g").Count());
        Assert.All(root.Descendants().Where(e => e.Attribute("stroke") is not null),
            e => Assert.Equal(ExcalidrawSvg.DefaultStroke, (string)e.Attribute("stroke")!));
        Assert.All(root.Descendants(Svg + "rect").Concat(root.Descendants(Svg + "ellipse")),
            e => Assert.Equal("none", (string)e.Attribute("fill")!));
        string shown = root.Descendants(Svg + "tspan").Single().Value;
        Assert.Equal(Payload.Replace("\\\"", "\"", StringComparison.Ordinal), shown);
        Assert.Contains("&lt;script&gt;", svg, StringComparison.Ordinal);
        Assert.Equal("start", (string)root.Descendants(Svg + "text").Single().Attribute("text-anchor")!);
    }

    // AC-2: control characters are left out of text; a text with a lone surrogate escape is skipped, the rest drawn.
    [Fact]
    public void Render_TextControlsAndLoneSurrogates_WellFormed()
    {
        XElement root = Parse(Render(Doc(
            "{\"type\":\"text\",\"x\":0,\"y\":0,\"text\":\"a\\u0000b\\u0008c\\u001f\\tö😀\\uffff\"}",
            "{\"type\":\"text\",\"x\":0,\"y\":0,\"text\":\"x\\ud800y\"}",
            "{\"type\":\"rectangle\",\"width\":1,\"height\":1}")));

        Assert.Equal("abc\tö😀", root.Descendants(Svg + "tspan").Single().Value);
        Assert.Equal(["text", "rect"], Shapes(root).Select(s => s.Name.LocalName));
    }

    // AC-2: only #rgb, #rrggbb, #rrggbbaa and 1 to 20 lower-case letters are colours.
    [Theory]
    [InlineData("#abc", true)]
    [InlineData("#AABBCC", true)]
    [InlineData("#aabbcc80", true)]
    [InlineData("transparent", true)]
    [InlineData("red", true)]
    [InlineData("abcdefghijklmnopqrst", true)]
    [InlineData("abcdefghijklmnopqrstu", false)]
    [InlineData("Red", false)]
    [InlineData("#abcd", false)]
    [InlineData("#ggg", false)]
    [InlineData("#", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("url(#x)", false)]
    [InlineData("red;x", false)]
    [InlineData("rgb(1,2,3)", false)]
    [InlineData("red\"", false)]
    public void IsSafeColour_OnlyThePlainForms(string? value, bool safe)
    {
        Assert.Equal(safe, ExcalidrawSvg.IsSafeColour(value));
    }

    // AC-2: a bad colour falls back to the default stroke and no fill.
    [Fact]
    public void Render_BadColour_UsesDefaults()
    {
        XElement rect = Shapes(Parse(Render(Doc(
            "{\"type\":\"rectangle\",\"x\":0,\"y\":0,\"width\":1,\"height\":1,\"strokeColor\":\"url(#a)\",\"backgroundColor\":5}"))))[0];

        Assert.Equal((ExcalidrawSvg.DefaultStroke, "none"), ((string)rect.Attribute("stroke")!, (string)rect.Attribute("fill")!));
    }

    // AC-2: numbers are clamped to 1e6; a non-number or out-of-range field skips the element.
    [Fact]
    public void Render_Numbers_ClampedOrElementSkipped()
    {
        XElement root = Parse(Render(Doc(
            "{\"type\":\"rectangle\",\"x\":5e12,\"y\":-5e12,\"width\":1,\"height\":1}",
            "{\"type\":\"rectangle\",\"x\":\"1\",\"y\":0,\"width\":1,\"height\":1}",
            "{\"type\":\"rectangle\",\"x\":1e400,\"y\":0,\"width\":1,\"height\":1}",
            "{\"type\":\"rectangle\",\"x\":0,\"y\":0,\"width\":true,\"height\":1}",
            "{\"type\":\"line\",\"x\":0,\"y\":0,\"points\":[[0,0],[\"a\",1]]}",
            "{\"type\":\"line\",\"x\":0,\"y\":0,\"points\":[[0,0],[1]]}",
            "{\"type\":\"text\",\"x\":0,\"y\":0,\"text\":5}",
            "{\"type\":\"rectangle\",\"x\":0,\"y\":0,\"width\":1,\"height\":1,\"opacity\":{}}")));

        XElement rect = Assert.Single(Shapes(root));
        Assert.Equal(("1000000", "-1000000"), ((string)rect.Attribute("x")!, (string)rect.Attribute("y")!));
        Assert.DoesNotContain("E", (string)root.Attribute("viewBox")!, StringComparison.OrdinalIgnoreCase);
    }

    // AC-3: invalid input, too deep nesting and every size limit give null.
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"elements\":[")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"elements\":{}}")]
    [InlineData("{\"elements\":[]} x")]
    public void Render_Invalid_Null(string json)
    {
        Assert.Null(Render(json));
    }

    // AC-3: nesting of 64 levels renders, 65 does not.
    [Fact]
    public void Render_Depth_64Allowed_65Refused()
    {
        static string Nested(int depth)
        {
            // The root object and the elements array are two levels; the rest is nested arrays in one element field.
            int inner = depth - 3;
            return "{\"elements\":[{\"type\":\"rectangle\",\"x\":0,\"y\":0,\"width\":1,\"height\":1,\"customData\":"
                + new string('[', inner) + new string(']', inner) + "}]}";
        }

        Assert.NotNull(Render(Nested(ExcalidrawSvg.MaxDepth)));
        Assert.Null(Render(Nested(ExcalidrawSvg.MaxDepth + 1)));
    }

    // AC-3: more than 5000 elements is refused, 5000 is drawn.
    [Fact]
    public void Render_ElementCount_Limit()
    {
        string Many(int n) => Doc(Enumerable.Repeat("{\"type\":\"rectangle\",\"width\":1,\"height\":1}", n).ToArray());

        Assert.Equal(ExcalidrawSvg.MaxElements, Parse(Render(Many(ExcalidrawSvg.MaxElements))).Elements().Count());
        Assert.Null(Render(Many(ExcalidrawSvg.MaxElements + 1)));
    }

    // AC-3: more than 5000 points in one element, or more than 100,000 in all, is refused.
    [Fact]
    public void Render_PointLimits()
    {
        static string Line(int points) =>
            "{\"type\":\"line\",\"points\":[" + string.Join(",", Enumerable.Repeat("[1,1]", points)) + "]}";

        Assert.NotNull(Render(Doc(Line(ExcalidrawSvg.MaxPointsPerElement))));
        Assert.Null(Render(Doc(Line(ExcalidrawSvg.MaxPointsPerElement + 1))));
        int lines = ExcalidrawSvg.MaxPoints / ExcalidrawSvg.MaxPointsPerElement;
        string[] full = Enumerable.Repeat(Line(ExcalidrawSvg.MaxPointsPerElement), lines).ToArray();
        Assert.NotNull(Render(Doc(full)));
        Assert.Null(Render(Doc([.. full, Line(1)])));
    }

    // AC-3: an SVG that would be over 5,000,000 characters is refused.
    [Fact]
    public void Render_OutputCap_Null()
    {
        string text = "{\"type\":\"text\",\"text\":\"" + new string('&', 300) + "\"}";

        Assert.Null(Render(Doc(Enumerable.Repeat(text, ExcalidrawSvg.MaxElements).ToArray())));
        Assert.NotNull(Render(Doc(Enumerable.Repeat(text, 100).ToArray())));
    }

    // AC-3: random and truncated bytes never throw.
    [Fact]
    public void Render_ArbitraryBytes_NeverThrows()
    {
        Random random = new Random(384);
        byte[] valid = Encoding.UTF8.GetBytes(Doc(
            "{\"type\":\"arrow\",\"x\":0,\"y\":0,\"points\":[[0,0],[1,1]],\"angle\":1}",
            "{\"type\":\"text\",\"text\":\"hi\"}"));
        for (int i = 0; i < 500; i++)
        {
            byte[] bytes = new byte[random.Next(0, 200)];
            random.NextBytes(bytes);
            _ = ExcalidrawSvg.Render(bytes);
            _ = ExcalidrawSvg.Render(valid.AsSpan(0, random.Next(0, valid.Length)));
        }

        Assert.NotNull(ExcalidrawSvg.Render([0xEF, 0xBB, 0xBF, .. valid]));
    }
}
