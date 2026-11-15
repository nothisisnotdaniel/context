using System.Text;

namespace Context.Tests;

// 165: note templates in Core (listing, matching, filling, preparing).
public sealed class NoteTemplatesTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 5, 8, 7, 0, DateTimeKind.Local);

    private readonly string parent;
    private readonly string root;

    public NoteTemplatesTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-templates-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private NoteStore Notes() => new(root);

    private NoteTemplates Templates() => new(Notes());

    private string TemplatePath(string file) => Path.Combine(root, NoteTemplates.FolderName, file);

    private void WriteTemplate(string file, string text) => WriteBytes(Path.Combine(NoteTemplates.FolderName, file), Encoding.UTF8.GetBytes(text));

    private void WriteBytes(string relative, byte[] bytes)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    // AC-1: the titles of the regular .md files directly in templates/, sorted ignoring case, then ordinal.
    [Fact]
    public void List_SortedTitlesOfDirectMdFiles()
    {
        WriteTemplate("meeting.md", "m");
        WriteTemplate("Review.md", "r");
        WriteTemplate("alpha.MD", "a");
        WriteTemplate("beta.md", "b");

        Assert.Equal(["alpha", "beta", "meeting", "Review"], Templates().List());
    }

    // AC-1: subfolders, .ctxs files, other extensions, dot files and invalid titles are skipped.
    [Fact]
    public void List_SkipsSubfoldersCtxsDotFilesAndInvalidTitles()
    {
        WriteTemplate("ok.md", "ok");
        WriteTemplate(Path.Combine("sub", "deep.md"), "deep");
        WriteTemplate("sealed.ctxs", "x");
        WriteTemplate("notes.txt", "x");
        WriteTemplate(".hidden.md", "x");
        WriteTemplate("CON.md", "x");
        WriteTemplate(new string('a', 101) + ".md", "x");
        Directory.CreateDirectory(TemplatePath("folder.md"));

        Assert.Equal(["ok"], Templates().List());
    }

    // AC-1: a symlinked template file is skipped (where the OS allows creating one).
    [Fact]
    public void List_SkipsSymlinkedFile()
    {
        WriteTemplate("ok.md", "ok");
        string target = Path.Combine(parent, "secret.md");
        File.WriteAllText(target, "secret");
        try
        {
            File.CreateSymbolicLink(TemplatePath("link.md"), target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Equal(["ok"], Templates().List());
        Assert.Equal(TemplateReadKind.Missing, Templates().TryRead("link", out string text));
        Assert.Equal(string.Empty, text);
    }

    // AC-1: a templates folder that is itself a symlink lists as empty.
    [Fact]
    public void List_FolderIsSymlink_Empty()
    {
        string target = Path.Combine(parent, "elsewhere");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "x.md"), "x");
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, NoteTemplates.FolderName), target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Empty(Templates().List());
        Assert.Equal(TemplateReadKind.Missing, Templates().TryRead("x", out _));
    }

    // AC-1: a missing folder (or a missing root) lists as empty and nothing is created.
    [Fact]
    public void List_Missing_EmptyAndNothingCreated()
    {
        Assert.Empty(Templates().List());
        Assert.False(Directory.Exists(root));

        Directory.CreateDirectory(root);
        Assert.Empty(Templates().List());
        Assert.False(Directory.Exists(Path.Combine(root, NoteTemplates.FolderName)));
    }

    // AC-2: a typed name is normalized (trimmed, .md stripped) and matched ignoring case.
    [Theory]
    [InlineData("meeting")]
    [InlineData("meeting.md")]
    [InlineData("  meeting.MD ")]
    [InlineData("MEETING")]
    public void TryRead_NormalizedAndIgnoringCase(string typed)
    {
        WriteTemplate("meeting.md", "exact");

        Assert.Equal(TemplateReadKind.Found, Templates().TryRead(typed, out string text));
        Assert.Equal("exact", text);
    }

    // AC-2: an exact match wins over one ignoring case (only where the file system keeps both files).
    [Fact]
    public void TryRead_ExactMatchFirst()
    {
        WriteTemplate("meeting.md", "lower");
        WriteTemplate("Meeting.md", "upper");
        if (Templates().List().Count < 2)
        {
            return;
        }

        Assert.Equal(["Meeting", "meeting"], Templates().List());
        Assert.Equal(TemplateReadKind.Found, Templates().TryRead("meeting", out string lower));
        Assert.Equal("lower", lower);
        Assert.Equal(TemplateReadKind.Found, Templates().TryRead("Meeting", out string upper));
        Assert.Equal("upper", upper);
        Assert.Equal(TemplateReadKind.Found, Templates().TryRead("MEETING", out string first));
        Assert.Equal("upper", first);
    }

    // AC-2: a name is never turned into a path, so traversal and absolute names find nothing.
    [Fact]
    public void TryRead_PathLikeNames_Missing()
    {
        WriteTemplate("ok.md", "ok");
        File.WriteAllText(Path.Combine(root, "x.md"), "outside templates");
        File.WriteAllText(Path.Combine(parent, "x.md"), "outside root");
        WriteTemplate(Path.Combine("a", "b.md"), "nested");

        foreach (string name in new[] { "../x", "..\\x", "a/b", "a\\b", Path.Combine(root, "x"), Path.Combine(parent, "x.md"), "../../x" })
        {
            Assert.Equal(TemplateReadKind.Missing, Templates().TryRead(name, out string text));
            Assert.Equal(string.Empty, text);
        }
    }

    // AC-5 (Core): a template over MaxEditBytes is unreadable.
    [Fact]
    public void TryRead_TooLarge_Unreadable()
    {
        WriteBytes(Path.Combine(NoteTemplates.FolderName, "big.md"), new byte[NoteStore.MaxEditBytes + 1]);

        Assert.Equal(TemplateReadKind.Unreadable, Templates().TryRead("big", out string text));
        Assert.Equal(string.Empty, text);
    }

    // AC-7 (Core): a sensitive top-level templates note means no template is read.
    [Theory]
    [InlineData("templates")]
    [InlineData("Templates")]
    public void TryRead_TemplatesSensitive(string name)
    {
        WriteTemplate("meeting.md", "m");
        WriteBytes(name + NoteStore.SensitiveExtension, [1, 2, 3]);

        Assert.Equal(TemplateReadKind.TemplatesSensitive, Templates().TryRead("meeting", out string text));
        Assert.Equal(string.Empty, text);
    }

    // AC-3: date, time and title are filled from one clock reading, invariantly.
    [Fact]
    public void Fill_ReplacesEveryPlaceholder()
    {
        string filled = NoteTemplates.Fill("# {{title}}\n{{date}} {{time}}\n{{date}}{{title}}\n", "Weekly sync", Now);

        Assert.Equal("# Weekly sync\n2026-11-05 08:07\n2026-11-05Weekly sync\n", filled);
    }

    // AC-3: the title is normalized and inserted literally; it is never expanded again.
    [Fact]
    public void Fill_TitleInsertedLiterally()
    {
        Assert.Equal("[{{date}}] 2026-11-05", NoteTemplates.Fill("[{{title}}] {{date}}", " {{date}}.md ", Now));
        Assert.Equal("{{time}}", NoteTemplates.Fill("{{title}}", "{{time}}", Now));
    }

    // AC-3: other {{...}} stay as written.
    [Theory]
    [InlineData("{{Date}}")]
    [InlineData("{{ date }}")]
    [InlineData("{{date:yyyy}}")]
    [InlineData("{{other}}")]
    [InlineData("{{")]
    [InlineData("{date}")]
    [InlineData("{{date}")]
    [InlineData("}}")]
    public void Fill_OtherBracesUnchanged(string text)
    {
        Assert.Equal(text, NoteTemplates.Fill(text, "T", Now));
    }

    // AC-3: an extra brace before a placeholder stays, the placeholder is still filled.
    [Fact]
    public void Fill_ExtraBraceBeforePlaceholder()
    {
        Assert.Equal("{2026-11-05}", NoteTemplates.Fill("{{{date}}}", "T", Now));
    }

    // AC-3: CRLF and LF line endings are kept, and a leading BOM is dropped.
    [Fact]
    public void Fill_KeepsLineEndingsAndDropsBom()
    {
        Assert.Equal("# T\r\nx\ny\r\n", NoteTemplates.Fill("﻿# {{title}}\r\nx\ny\r\n", "T", Now));
    }

    // AC-3: the clock is read invariantly under any culture.
    [Fact]
    public void Fill_InvariantUnderAnotherCulture()
    {
        System.Globalization.CultureInfo before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            Assert.Equal("2026-11-05 08:07", NoteTemplates.Fill("{{date}} {{time}}", "T", Now));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    // AC-3, AC-4: a template with a BOM and CRLF gives the filled text without the BOM; the template stays as it was.
    [Fact]
    public void Prepare_BomAndCrlfTemplate_FilledWithoutBom()
    {
        byte[] template = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("# {{title}}\r\n{{date}}\r\n")];
        WriteBytes(Path.Combine(NoteTemplates.FolderName, "meeting.md"), template);

        TemplateNote prepared = Templates().Prepare("Sync.md", "Meeting", Now);

        Assert.Equal(TemplateNoteKind.Ready, prepared.Kind);
        Assert.Equal("Sync", prepared.Title);
        Assert.Equal("meeting", prepared.Template);
        Assert.Equal("# Sync\r\n2026-11-05\r\n", prepared.Text);
        Assert.Equal(template, File.ReadAllBytes(TemplatePath("meeting.md")));

        NoteInfo created = Notes().CreateWithText(prepared.Title, prepared.Text);
        Assert.Equal(Encoding.UTF8.GetBytes("# Sync\r\n2026-11-05\r\n"), File.ReadAllBytes(created.Path));
        Assert.Equal(Path.Combine(root, "Sync.md"), created.Path);
    }

    // AC-6 (Core): an invalid title or an existing note (plain or sensitive, ignoring case) is answered before the
    // template is read.
    [Theory]
    [InlineData("a/b", "InvalidTitle")]
    [InlineData("  ", "InvalidTitle")]
    [InlineData("plan", "Exists")]
    [InlineData("SECRET", "Exists")]
    public void Prepare_BadOrTakenTitle_TemplateNotRead(string title, string kind)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Plan.md"), "# Plan\n");
        WriteBytes("secret" + NoteStore.SensitiveExtension, [1]);
        WriteBytes(Path.Combine(NoteTemplates.FolderName, "big.md"), new byte[NoteStore.MaxEditBytes + 1]);

        // An unreadable template would be Unreadable if it were read; the title check comes first.
        TemplateNote prepared = Templates().Prepare(title, "big", Now);

        Assert.Equal(kind, prepared.Kind.ToString());
        if (prepared.Kind == TemplateNoteKind.InvalidTitle)
        {
            Assert.Equal(NoteStore.ValidateTitle(title.Trim()), prepared.Text);
        }
    }

    // AC-5 (Core): a missing template gives the listed names.
    [Fact]
    public void Prepare_Missing_ListsTemplates()
    {
        WriteTemplate("review.md", "r");
        WriteTemplate("Meeting.md", "m");

        TemplateNote prepared = Templates().Prepare("Plan", "standup.md", Now);

        Assert.Equal(TemplateNoteKind.Missing, prepared.Kind);
        Assert.Equal("standup", prepared.Template);
        Assert.Equal(["Meeting", "review"], prepared.Templates);
    }

    // AC-5 (Core): a filled text over MaxEditBytes is too big, though the template itself fits.
    [Fact]
    public void Prepare_FilledTooBig()
    {
        int count = (int)(NoteStore.MaxEditBytes / "{{title}}".Length);
        string template = string.Concat(Enumerable.Repeat("{{title}}", count));
        Assert.True(Encoding.UTF8.GetByteCount(template) <= NoteStore.MaxEditBytes);
        WriteTemplate("long.md", template);

        TemplateNote prepared = Templates().Prepare("A long title", "long", Now);

        Assert.Equal(TemplateNoteKind.TooBig, prepared.Kind);
        Assert.Equal(string.Empty, prepared.Text);
    }

    // The record never leaks its text through formatting.
    [Fact]
    public void TemplateNote_ToStringIsTypeName()
    {
        Assert.Equal(nameof(TemplateNote), new TemplateNote(TemplateNoteKind.Ready, "t", "n", "secret", []).ToString());
    }
}
