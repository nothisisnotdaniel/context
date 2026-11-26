using System.Text.Json;

namespace Context.Tests;

// 435 AC-1, AC-2: which pasted clipboard files go to OnPasteImages, run in Jint on the committed editor.js through the
// DOM-free pastedImagesJson hook; anything with text keeps going through smart paste.
public sealed class PasteImageBundleTests : IClassFixture<EditorBundle>
{
    private readonly EditorBundle bundle;

    public PasteImageBundleTests(EditorBundle bundle)
    {
        this.bundle = bundle;
    }

    // AC-1: every allowed image type is picked when there is no text, in clipboard order; other types are left out.
    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t ")]
    [InlineData(null)]
    public void NoText_PicksImageFiles(string? text)
    {
        Assert.Equal<string[]?>(
            ["a.png", "b.jpg", "c.gif", "d.webp"],
            Picked(
                [("a.png", "image/png"), ("x.pdf", "application/pdf"), ("b.jpg", "image/jpeg"), ("c.gif", "image/gif"),
                    ("s.svg", "image/svg+xml"), ("d.webp", "image/webp"), ("t.tif", "image/tiff"), ("h.heic", "image/heic")],
                text));
    }

    // AC-1: text on the clipboard, no files, or no file of an allowed type gives null.
    [Fact]
    public void TextOrNoImage_GivesNull()
    {
        Assert.Null(Picked([("a.png", "image/png")], "a\tb\n1\t2"));
        Assert.Null(Picked([("a.png", "image/png")], " x "));
        Assert.Null(Picked([], ""));
        Assert.Null(Picked(null, ""));
        Assert.Null(Picked([("x.pdf", "application/pdf"), ("s.svg", "image/svg+xml"), ("n", "")], ""));
    }

    // AC-2: a paste with text but no image still goes through smart paste: a table, a lone link and plain text give the
    // same results as before.
    [Fact]
    public void TextPaste_StillSmartPaste()
    {
        Assert.Null(Picked([], "a\tb\n1\t2"));
        Assert.Equal(
            "{\"markdown\":\"| a | b |\\n| --- | --- |\\n| 1 | 2 |\",\"link\":null}",
            bundle.Json("JSON.parse(ContextEditor.pastedMarkdown('a\\tb\\n1\\t2', false, ''))"));
        Assert.Equal(
            "{\"markdown\":\"https://example.com/a\",\"link\":\"https://example.com/a\"}",
            bundle.Json("JSON.parse(ContextEditor.pastedMarkdown('https://example.com/a', false, ''))"));
        Assert.Equal(
            "{\"markdown\":null,\"link\":null}",
            bundle.Json("JSON.parse(ContextEditor.pastedMarkdown('plain words', false, ''))"));
    }

    // AC-1, AC-2: handlePaste asks pastedImages before smart paste, in the source and the committed bundle, and the
    // pictures go out only through OnPasteImages.
    [Theory]
    [InlineData("editor/src/view.js")]
    [InlineData("wwwroot/editor/editor.js")]
    public void HandlePaste_ChecksImagesFirst(string file)
    {
        string text = File.ReadAllText(Path.Combine([RepoFiles.Root(), RepoFiles.DesktopFolder, .. file.Split('/')]));

        Assert.Contains("\"OnPasteImages\"", text, StringComparison.Ordinal);
        if (!file.StartsWith("editor/", StringComparison.Ordinal))
        {
            Assert.Contains("image/webp", text, StringComparison.Ordinal);
        }
        else
        {
            int images = text.IndexOf("pastedImages(data.files, data.getData(\"text/plain\"))", StringComparison.Ordinal);
            int smart = text.IndexOf("smartPaste(view.state, data.getData(\"text/plain\"),", StringComparison.Ordinal);
            Assert.True(images > 0 && smart > images, "pastedImages must run before smartPaste");
        }
    }

    private string[]? Picked((string Name, string Type)[]? files, string? text)
    {
        string filesJson = files is null
            ? "null"
            : JsonSerializer.Serialize(JsonSerializer.Serialize(files.Select(f => new { name = f.Name, type = f.Type })));
        string textJs = text is null ? "null" : JsonSerializer.Serialize(text);
        string result = JsonSerializer.Deserialize<string>(bundle.Json($"ContextEditor.pastedImagesJson({filesJson}, {textJs})"))!;
        return JsonSerializer.Deserialize<string[]?>(result);
    }
}
