using System.Diagnostics;
using System.Text;

namespace Context.Tests;

// 250 AC-1 to AC-7: NoteHtml.Render, the page Share writes. Pure: images come from a fake resolver, except in one test
// with real attachments in a temp folder.
public sealed class NoteHtmlTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static readonly Func<string, ResolvedAttachment?> NoImages = _ => null;

    // AC-1: the page's frame: doctype, charset, the CSP, the encoded title, one inline style and no script.
    [Fact]
    public void Render_Page_HasDoctypeCharsetCspTitleAndOneStyle()
    {
        string html = NoteHtml.Render("Plan <b>&</b> \"x\"", "Text\n", NoImages, leaveOutPrivate: true);

        Assert.StartsWith("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n", html, StringComparison.Ordinal);
        Assert.Contains(
            "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'\">",
            html,
            StringComparison.Ordinal);
        Assert.Contains("<title>Plan &lt;b&gt;&amp;&lt;/b&gt; &quot;x&quot;</title>", html, StringComparison.Ordinal);
        Assert.Equal(1, Count(html, "<style>"));
        Assert.Equal(1, Count(html, "</style>"));
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("</html>\n", html, StringComparison.Ordinal);
    }

    // AC-2: headings h1 to h6; seven # is a paragraph.
    [Fact]
    public void Render_Headings()
    {
        string body = Body("# One\n## Two\n### Three\n#### Four\n##### Five\n###### Six\n####### Seven\n");

        Assert.Equal(
            "<h1>One</h1>\n<h2>Two</h2>\n<h3>Three</h3>\n<h4>Four</h4>\n<h5>Five</h5>\n<h6>Six</h6>\n<p>####### Seven</p>\n",
            body);
    }

    // AC-2: paragraphs are split by blank lines; lines in one paragraph stay together.
    [Fact]
    public void Render_Paragraphs()
    {
        Assert.Equal("<p>one\ntwo</p>\n<p>three</p>\n", Body("one\ntwo\n\nthree\n"));
    }

    // AC-2: lists nest by indentation, numbered lists keep their start, task items show ☐ and ☑.
    [Fact]
    public void Render_Lists_NestedOrderedAndTasks()
    {
        string body = Body("- a\n  - b\n- c\n\n3. x\n4. y\n\n- [ ] open\n- [x] done\n");

        Assert.Equal(
            "<ul>\n<li>a\n<ul>\n<li>b</li>\n</ul>\n</li>\n<li>c</li>\n</ul>\n"
            + "<ol start=\"3\">\n<li>x</li>\n<li>y</li>\n</ol>\n"
            + "<ul>\n<li>☐ open</li>\n<li>☑ done</li>\n</ul>\n",
            body);
    }

    // AC-2: quotes, fenced code, inline code, strong and emphasis.
    [Fact]
    public void Render_QuoteCodeStrongEmphasis()
    {
        string body = Body("> said\n\n```\nif (a < b)\n  **x**\n```\n\nuse `a<b` and **bold** and *it* and _it2_\n");

        Assert.Equal(
            "<blockquote>\n<p>said</p>\n</blockquote>\n"
            + "<pre><code>if (a &lt; b)\n  **x**</code></pre>\n"
            + "<p>use <code>a&lt;b</code> and <strong>bold</strong> and <em>it</em> and <em>it2</em></p>\n",
            body);
    }

    // AC-2: a table, or anything else not listed, is a paragraph of its text.
    [Fact]
    public void Render_Table_IsAParagraph()
    {
        Assert.Equal("<p>| a | b |\n| - | - |\n| 1 | 2 |</p>\n", Body("| a | b |\n| - | - |\n| 1 | 2 |\n"));
    }

    // AC-3: raw HTML and script-like text are shown as text, in every kind of block.
    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("# <script>alert(1)</script>")]
    [InlineData("- <iframe src=\"https://evil.example\"></iframe>")]
    [InlineData("> <svg onload=alert(1)>")]
    [InlineData("```\n</code></pre><script>alert(1)</script>\n```")]
    [InlineData("`</code><script>x</script>`")]
    [InlineData("**<b onclick=alert(1)>x</b>**")]
    [InlineData("[<script>x</script>](https://example.com)")]
    [InlineData("[x](https://example.com/\"onmouseover=\"alert(1))")]
    [InlineData("![<script>](.attachments/a.png)")]
    [InlineData("<!-- comment --> &amp; &lt;")]
    [InlineData("#[<script>] @[<b>Ann</b>] [[<i>Note</i>]]")]
    public void Render_RawHtml_IsEncoded(string text)
    {
        string body = Body(text + "\n");

        Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b ", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<i>", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<!--", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"onmouseover", body, StringComparison.Ordinal);
    }

    // AC-3: the encoded text reads back as written.
    [Fact]
    public void Render_Text_EncodedExactly()
    {
        Assert.Equal("<p>a &lt;b&gt; &amp; &quot;c&quot; &#39;d&#39;</p>\n", Body("a <b> & \"c\" 'd'\n"));
    }

    // AC-4: tags show as TagParser.Format, links as their target, mentions as the name; none is a link.
    [Fact]
    public void Render_TagsLinksMentions_ArePlainText()
    {
        string body = Body("See #Work and #[big plan], [[Plan]], [[Plan#Goals]] with @[Ann Lee].\n");

        Assert.Equal("<p>See #work and #[big plan], Plan, Plan#Goals with Ann Lee.</p>\n", body);
        Assert.DoesNotContain("<a", body, StringComparison.Ordinal);
    }

    // AC-4: inside code they stay as written.
    [Fact]
    public void Render_TagsLinksMentions_InCode_StayAsWritten()
    {
        string body = Body("`#Work [[Plan]] @[Ann]`\n\n```\n#Work [[Plan]] @[Ann]\n```\n");

        Assert.Equal(
            "<p><code>#Work [[Plan]] @[Ann]</code></p>\n<pre><code>#Work [[Plan]] @[Ann]</code></pre>\n", body);
    }

    // 263 AC-10: a Mermaid fence is still shared as plain code with its text, never as a diagram.
    [Fact]
    public void Render_MermaidFence_IsPlainCode()
    {
        Assert.Equal("<pre><code>graph TD\n  A --&gt; B</code></pre>\n", Body("```mermaid\ngraph TD\n  A --> B\n```\n"));
    }

    // AC-5: http, https and mailto (any case) become links; the text inside is encoded.
    [Theory]
    [InlineData("https://example.com/a?b=1&c=2", "https://example.com/a?b=1&amp;c=2")]
    [InlineData("HTTP://example.com", "HTTP://example.com")]
    [InlineData("mailto:ann@example.com", "mailto:ann@example.com")]
    [InlineData("MailTo:ann@example.com", "MailTo:ann@example.com")]
    public void Render_SafeLink_IsAnAnchor(string url, string href)
    {
        Assert.Equal($"<p>go <a href=\"{href}\">there</a></p>\n", Body($"go [there]({url})\n"));
    }

    // AC-5: any other scheme, or no scheme, shows only the text.
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html,<script>x</script>")]
    [InlineData("vbscript:x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("Other note")]
    [InlineData("//evil.example")]
    [InlineData(" javascript:alert(1)")]
    public void Render_UnsafeLink_ShowsOnlyText(string url)
    {
        string body = Body($"go [there]({url})\n");

        // A ")" inside the target ends it early, as in Markdown; what is left is text.
        Assert.StartsWith("<p>go there", body, StringComparison.Ordinal);
        Assert.DoesNotContain("<a", body, StringComparison.Ordinal);
        Assert.DoesNotContain("href", body, StringComparison.Ordinal);
        Assert.DoesNotContain(":", body, StringComparison.Ordinal);
    }

    // AC-6: a resolved attachment image is embedded as a data: URL with its alt.
    [Fact]
    public void Render_ResolvedImage_IsDataUrl()
    {
        List<string> asked = [];
        string body = Body("![A <shot>](.attachments/shot.png)\n", src =>
        {
            asked.Add(src);
            return new ResolvedAttachment("unused", AttachmentType.Png, Png);
        });

        Assert.Equal(
            $"<p><img alt=\"A &lt;shot&gt;\" src=\"data:image/png;base64,{Convert.ToBase64String(Png)}\"></p>\n", body);
        Assert.Equal([".attachments/shot.png"], asked);
    }

    // AC-6: an unresolved image, a PDF and a remote image show [image: alt]; a remote one is never asked for.
    [Fact]
    public void Render_UnresolvedPdfAndRemoteImages_ShowAlt()
    {
        List<string> asked = [];
        string body = Body("![gone](.attachments/gone.png)\n\n![doc](.attachments/doc.pdf)\n\n![far](https://example.com/x.png)\n", src =>
        {
            asked.Add(src);
            return src.EndsWith(".pdf", StringComparison.Ordinal)
                ? new ResolvedAttachment("unused", AttachmentType.Pdf, Encoding.ASCII.GetBytes("%PDF-1.7"))
                : null;
        });

        Assert.Equal("<p>[image: gone]</p>\n<p>[image: doc]</p>\n<p>[image: far]</p>\n", body);
        Assert.DoesNotContain("https://example.com/x.png", asked);
        Assert.DoesNotContain("<img", body, StringComparison.Ordinal);
    }

    // AC-6: images beyond the 20-image cap show [image: alt].
    [Fact]
    public void Render_ImagesOverTheCap_ShowAlt()
    {
        StringBuilder text = new StringBuilder();
        for (int i = 1; i <= NoteAttachments.MaxInlineImages + 2; i++)
        {
            text.Append("![i").Append(i).Append("](.attachments/i").Append(i).Append(".png)\n\n");
        }

        string body = Body(text.ToString(), _ => new ResolvedAttachment("unused", AttachmentType.Png, Png));

        Assert.Equal(NoteAttachments.MaxInlineImages, Count(body, "<img "));
        Assert.Contains("[image: i21]", body, StringComparison.Ordinal);
        Assert.Contains("[image: i22]", body, StringComparison.Ordinal);
        Assert.DoesNotContain("[image: i20]", body, StringComparison.Ordinal);
    }

    // AC-6: with real attachments in a temp folder: the PNG is embedded, the PDF shows its alt.
    [Fact]
    public void Render_RealAttachments_EmbedsImageOnly()
    {
        string parent = Path.Combine(Path.GetTempPath(), "context-share-" + Guid.NewGuid().ToString("N"));
        try
        {
            string root = Path.Combine(parent, "Context");
            Directory.CreateDirectory(root);
            NoteStore store = new NoteStore(root);
            File.WriteAllText(Path.Combine(root, "Plan.md"), "x\n");
            NoteAttachments attachments = new NoteAttachments(store);
            AttachmentAdd image = attachments.Add("Plan", "shot.png", Png);
            AttachmentAdd pdf = attachments.Add("Plan", "doc.pdf", Encoding.ASCII.GetBytes("%PDF-1.7\n%..."));

            string body = Body($"![shot]({image.Src})\n\n![doc]({pdf.Src})\n", src => attachments.TryResolve("Plan", src));

            Assert.Equal(
                $"<p><img alt=\"shot\" src=\"data:image/png;base64,{Convert.ToBase64String(Png)}\"></p>\n<p>[image: doc]</p>\n",
                body);
        }
        finally
        {
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }

    // AC-7: with leaveOutPrivate the lines tagged #private are left out; code lines are not tags and stay.
    [Fact]
    public void Render_LeaveOutPrivate_DropsTaggedLines()
    {
        const string Text = "keep\nsecret #private\nalso #Private here\n- item #[private]\n`#private` in code\n```\n#private\n```\n";

        string body = Body(Text, NoImages, leaveOutPrivate: true);

        Assert.Equal("<p>keep\n<code>#private</code> in code</p>\n<pre><code>#private</code></pre>\n", body);
    }

    // AC-7: without it the lines stay, with the tag as plain text.
    [Fact]
    public void Render_KeepPrivate_ShowsTagAsText()
    {
        string body = Body("keep\nsecret #private\n", NoImages, leaveOutPrivate: false);

        Assert.Equal("<p>keep\nsecret #private</p>\n", body);
    }

    // AC-7 / AC-2: a 10,000-line note of #private lines and backticks renders in linear work: the output grows with
    // the input and every kept line is there. No timer: the counts are the check.
    [Fact]
    public void Render_LargeNote_RendersEveryLine()
    {
        StringBuilder text = new StringBuilder();
        for (int i = 0; i < 10_000; i++)
        {
            text.Append(i % 2 == 0 ? "line #private `a` `" : "kept ` * _ [ ** [[ @[ ").Append(i).Append('\n');
        }

        string dropped = Body(text.ToString(), NoImages, leaveOutPrivate: true);
        string kept = Body(text.ToString(), NoImages, leaveOutPrivate: false);

        Assert.Equal(5_000, Count(dropped, "kept "));
        Assert.Equal(0, Count(dropped, "line "));
        Assert.Equal(5_000, Count(kept, "#private"));
        Assert.Equal(5_000, Count(kept, "line "));
        Assert.True(kept.Length < text.Length * 8, "The page grows linearly with the note.");
    }

    // AC-6: nothing is fetched: a remote image source is never passed to the resolver.
    [Fact]
    public void Render_RemoteImage_NotResolved()
    {
        int calls = 0;
        Body("![x](http://example.com/a.png) ![y](//example.com/b.png)\n", _ =>
        {
            calls++;
            return null;
        });

        Assert.Equal(0, calls);
    }

    private static string Body(string text) => Body(text, NoImages, leaveOutPrivate: true);

    private static string Body(string text, Func<string, ResolvedAttachment?> resolve, bool leaveOutPrivate = true)
    {
        string html = NoteHtml.Render("Note", text, resolve, leaveOutPrivate);
        const string Open = "<main>\n";
        int start = html.IndexOf(Open, StringComparison.Ordinal) + Open.Length;
        int end = html.IndexOf("</main>", StringComparison.Ordinal);
        Debug.Assert(start >= Open.Length && end >= start, "The page has a main element.");
        return html[start..end];
    }

    private static int Count(string text, string part)
    {
        int count = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
