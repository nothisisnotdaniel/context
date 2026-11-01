using System.Text.RegularExpressions;

namespace Context.Tests;

// Source and file scans of Context.Desktop: raw markup, risky elements, the page's CSP and read-only access.
public sealed class DesktopSourceTests
{
    private const string Csp =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; "
        + "connect-src 'self'; base-uri 'none'; form-action 'none'";

    // The Core calls that change data (AC-11). CalendarLink's real names are Set/Clear; Save/Delete are listed too.
    private static readonly Regex Writes = new(
        @"\.\s*(Create|Save|Add|Complete|Set|Clear|Delete|ClearCache|Migrate|DecryptAll)\s*\(",
        RegexOptions.CultureInvariant);

    private static readonly string[] RawMarkup = ["MarkupString", "AddMarkupContent", "@(("];

    private static readonly string[] RazorForbidden =
        ["style=", "<a ", "<form", "<input", "<button", "<textarea", "<script"];

    // AC-5: no raw markup anywhere in the Desktop sources.
    [Fact]
    public void Sources_UseNoRawMarkup()
    {
        string root = RepoFiles.Root();
        List<string> files = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor");
        Assert.Contains("src/Context.Desktop/CurrentPanel.razor", files);

        List<string> found = [];
        foreach (string file in files)
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            found.AddRange(RawMarkup.Where(m => text.Contains(m, StringComparison.Ordinal)).Select(m => $"{file}: {m}"));
        }

        Assert.True(found.Count == 0, "Raw markup in Context.Desktop: " + string.Join(", ", found));
    }

    // AC-5: no inline styles, links, forms, inputs, buttons or scripts in the components.
    [Fact]
    public void Razor_HasNoRiskyElements()
    {
        string root = RepoFiles.Root();
        List<string> files = RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".razor");
        Assert.True(files.Count >= 6, "Expected the six components, found " + files.Count);

        List<string> found = [];
        foreach (string file in files)
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            found.AddRange(RazorForbidden
                .Where(m => text.Contains(m, StringComparison.OrdinalIgnoreCase))
                .Select(m => $"{file}: {m}"));
        }

        Assert.True(found.Count == 0, "Forbidden markup in .razor files: " + string.Join(", ", found));
    }

    // AC-6: exactly the CSP meta tag, one external script (blazor.webview.js), no inline script or style.
    [Fact]
    public void IndexHtml_HasCspAndNoInlineCode()
    {
        string html = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "index.html"));

        MatchCollection metas = Regex.Matches(
            html, "<meta\\s+http-equiv=\"Content-Security-Policy\"\\s+content=\"([^\"]*)\"\\s*/?>",
            RegexOptions.IgnoreCase);
        Match meta = Assert.Single(metas);
        Assert.Equal(Csp, meta.Groups[1].Value);
        Assert.Single(Regex.Matches(html, "Content-Security-Policy", RegexOptions.IgnoreCase));

        MatchCollection scripts = Regex.Matches(html, "<script\\b([^>]*)>(.*?)</script>", RegexOptions.IgnoreCase
            | RegexOptions.Singleline);
        Match script = Assert.Single(scripts);
        Assert.Equal(" src=\"_framework/blazor.webview.js\"", script.Groups[1].Value);
        Assert.Equal(string.Empty, script.Groups[2].Value);
        Assert.Single(Regex.Matches(html, "<script", RegexOptions.IgnoreCase));

        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("\\son[a-z]+\\s*=", html);
        Assert.DoesNotContain("http:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https:", html, StringComparison.OrdinalIgnoreCase);
    }

    // AC-6: the stylesheet loads nothing from outside (no CDN, no external fonts).
    [Fact]
    public void AppCss_LoadsNothingExternal()
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        Assert.DoesNotContain("@import", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url(", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@font-face", css, StringComparison.OrdinalIgnoreCase);
    }

    // AC-11: the Desktop never calls a Core method that changes data.
    [Fact]
    public void Sources_CallNoWritingMethod()
    {
        string root = RepoFiles.Root();
        List<string> found = [];
        foreach (string file in RepoFiles.Sources(root, RepoFiles.DesktopFolder, ".cs", ".razor"))
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            found.AddRange(Writes.Matches(text).Select(m => $"{file}: {m.Value}"));
        }

        Assert.True(found.Count == 0, "Context.Desktop calls writing methods: " + string.Join(", ", found));
    }

    // AC-11 (control): the pattern catches each forbidden call.
    [Theory]
    [InlineData("notes.Create(title)")]
    [InlineData("notes.Save(title, bytes)")]
    [InlineData("todos.Add(text, due)")]
    [InlineData("todos.Complete(1)")]
    [InlineData("link.Set(value)")]
    [InlineData("link.Clear()")]
    [InlineData("link.Save(value)")]
    [InlineData("link.Delete()")]
    [InlineData("feed.ClearCache()")]
    [InlineData("encryption.Migrate()")]
    [InlineData("encryption.DecryptAll()")]
    [InlineData("Vault.Create(root, pass)")]
    public void Writes_Pattern_CatchesCalls(string call)
    {
        Assert.Matches(Writes, call);
    }

    // AC-11 (control): the pattern doesn't flag the reads the Desktop needs.
    [Theory]
    [InlineData("PhotinoBlazorAppBuilder.CreateDefault(args)")]
    [InlineData("builder.Services.AddSingleton(state)")]
    [InlineData("todos.List()")]
    [InlineData("Directory.CreateDirectory(root)")]
    public void Writes_Pattern_AllowsReads(string call)
    {
        Assert.DoesNotMatch(Writes, call);
    }
}
