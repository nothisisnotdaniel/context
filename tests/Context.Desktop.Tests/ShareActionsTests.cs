using System.Security.Cryptography;
using System.Text;

namespace Context.Tests;

// 250 AC-8, AC-9: DesktopActions.ShareNote writes the note as an HTML page outside the data folder, refusing like
// ExportNote; a sensitive note is shared only while open in the session and with a confirmation.
public sealed class ShareActionsTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private readonly DesktopFixture data = new();

    public ShareActionsTests()
    {
        data.Keys = FakeKeyStore.Ready();
    }

    public void Dispose() => data.Dispose();

    // AC-8: a plain note is written as a page; its image attachment is embedded; the data folder is unchanged.
    [Fact]
    public async Task ShareNote_PlainNote_WritesPage()
    {
        data.WriteNote("Plan", "# Plan\n\nText #work\n\nsecret #private\n");
        AttachmentAdd added = new NoteAttachments(new NoteStore(data.Root)).Add("Plan", "shot.png", Png);
        File.AppendAllText(Path.Combine(data.Root, "Plan.md"), $"\n![shot]({added.Src})\n");
        string target = data.Outside("Plan.html");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.ShareNote("plan", target, leaveOutPrivate: true, null);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal($"Shared Plan as HTML: {target}", result.Text);
        string html = File.ReadAllText(target, Encoding.UTF8);
        Assert.Contains("<title>Plan</title>", html, StringComparison.Ordinal);
        Assert.Contains("<h1>Plan</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<p>Text #work</p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", html, StringComparison.Ordinal);
        Assert.Contains($"src=\"data:image/png;base64,{Convert.ToBase64String(Png)}\"", html, StringComparison.Ordinal);
        Assert.Equal(before, Files());
        Assert.Equal(builds, data.State.Builds);
    }

    // AC-8: without leaving out, the #private line is kept as text.
    [Fact]
    public async Task ShareNote_KeepPrivate_KeepsLine()
    {
        data.WriteNote("Plan", "secret #private\n");
        string target = data.Outside("Plan.html");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.ShareNote("Plan", target, leaveOutPrivate: false, null);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Contains("<p>secret #private</p>", File.ReadAllText(target), StringComparison.Ordinal);
    }

    // AC-8: a path that is not fully qualified.
    [Theory]
    [InlineData("Plan.html")]
    [InlineData("../Plan.html")]
    [InlineData("")]
    public async Task ShareNote_RelativePath_Refused(string path)
    {
        data.WriteNote("Plan", "text\n");
        await data.RefreshAsync();
        ActionResult exported = await data.Actions!.ExportNote("Plan", path);

        ActionResult shared = await Refused("Plan", path, exported.Text);

        Assert.Equal(exported.Reason, shared.Reason);
    }

    // AC-8: a path inside the data folder, also in another case.
    [Fact]
    public async Task ShareNote_InsideTheFolder_Refused()
    {
        data.WriteNote("Plan", "text\n");

        await Refused("Plan", Path.Combine(data.Root, "Plan.html"), "Export to a path outside the Context folder. Nothing was written.");
        await Refused(
            "Plan", Path.Combine(data.Root.ToUpperInvariant(), "sub", "Plan.html"), "Export to a path outside the Context folder. Nothing was written.");
        Assert.False(File.Exists(Path.Combine(data.Root, "Plan.html")));
    }

    // AC-8: an existing file or directory is never overwritten.
    [Fact]
    public async Task ShareNote_TargetExists_RefusedUnchanged()
    {
        data.WriteNote("Plan", "text\n");
        string file = data.Outside("Plan.html");
        File.WriteAllText(file, "theirs\n");
        string folder = data.Outside("Folder.html");
        Directory.CreateDirectory(folder);
        SortedDictionary<string, string> outside = FileSnapshot.Take(data.Outside(string.Empty), "Context/");

        await Refused("Plan", file, "That file already exists; choose a new name: " + file);
        await Refused("Plan", folder, "That file already exists; choose a new name: " + folder);

        Assert.Equal(outside, FileSnapshot.Take(data.Outside(string.Empty), "Context/"));
    }

    // AC-8: a note that is gone.
    [Fact]
    public async Task ShareNote_UnknownNote_Refused()
    {
        data.WriteNote("Plan", "text\n");
        string target = data.Outside("Ghost.html");

        await Refused("Ghost", target, "Ghost no longer exists. Nothing was done.");
    }

    // AC-8: a note over 5 MB can't be read.
    [Fact]
    public async Task ShareNote_NoteOver5Mb_Failed()
    {
        Directory.CreateDirectory(data.Root);
        File.WriteAllBytes(Path.Combine(data.Root, "Big.md"), new byte[NoteStore.MaxEditBytes + 1]);

        await Refused("Big", data.Outside("Big.html"), "Can't read Big.", ActionKind.Failed);
    }

    // AC-8: a target that can't be written.
    [Fact]
    public async Task ShareNote_UnwritableTarget_Failed()
    {
        data.WriteNote("Plan", "text\n");
        string target = data.Outside(Path.Combine("missing", "Plan.html"));
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.ShareNote("Plan", target, leaveOutPrivate: true, null);

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal($"Can't write {target}.", result.Text);
        Assert.False(File.Exists(target));
    }

    // AC-9: a locked sensitive note asks to be unlocked first and never asks for the key.
    [Fact]
    public async Task ShareNote_LockedSensitive_UnlockFirst()
    {
        data.WriteSensitive("Secret", "sealed text\n");
        await data.RefreshAsync();
        int agrees = data.Keys.Agrees;

        await Refused("secret", data.Outside("Secret.html"), "Unlock Secret first.", confirmed: new Confirmation());

        Assert.Equal(agrees, data.Keys.Agrees);
    }

    // AC-9: an open sensitive note without a confirmation is not shared.
    [Fact]
    public async Task ShareNote_OpenSensitiveWithoutConfirmation_NotConfirmed()
    {
        data.WriteSensitive("Secret", "sealed text\n");
        await data.RefreshAsync();
        Assert.NotNull(await data.State!.ReadSensitive("Secret"));
        int agrees = data.Keys.Agrees;

        ActionResult result = await Refused("Secret", data.Outside("Secret.html"), "Not confirmed. Nothing was done.");

        Assert.Equal(ActionReason.NotConfirmed, result.Reason);
        Assert.Equal(agrees, data.Keys.Agrees);
    }

    // AC-9: confirmed, the session text is rendered without images; the .ctxs file and context.db stay unchanged and
    // the key is not asked for again.
    [Fact]
    public async Task ShareNote_OpenSensitiveConfirmed_RendersSessionTextWithoutImages()
    {
        data.WriteNote("Plan", "x\n");
        AttachmentAdd added = new NoteAttachments(new NoteStore(data.Root)).Add("Plan", "shot.png", Png);
        data.WriteSensitive("Secret", $"# Secret\n\nThe plan #private\n\nseen\n\n![shot]({added.Src})\n");
        await data.RefreshAsync();
        Assert.NotNull(await data.State!.ReadSensitive("Secret"));
        string sealedPath = Path.Combine(data.Root, "Secret" + NoteStore.SensitiveExtension);
        byte[] sealedBytes = File.ReadAllBytes(sealedPath);
        string? db = DbHash();
        int agrees = data.Keys.Agrees;
        string target = data.Outside("Secret.html");

        ActionResult result = await data.Actions!.ShareNote("Secret", target, leaveOutPrivate: true, new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal($"Shared Secret as HTML: {target}", result.Text);
        string html = File.ReadAllText(target);
        Assert.Contains("<h1>Secret</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<p>seen</p>", html, StringComparison.Ordinal);
        Assert.Contains("<p>[image: shot]</p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("The plan", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Equal(agrees, data.Keys.Agrees);
        Assert.Equal(sealedBytes, File.ReadAllBytes(sealedPath));
        Assert.Equal(db, DbHash());
    }

    private async Task<ActionResult> Refused(
        string name, string path, string text, ActionKind kind = ActionKind.Refused, Confirmation? confirmed = null)
    {
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();
        bool existed = Path.IsPathFullyQualified(path) && (File.Exists(path) || Directory.Exists(path));

        ActionResult result = await data.Actions!.ShareNote(name, path, leaveOutPrivate: true, confirmed);

        Assert.Equal(kind, result.Kind);
        Assert.Equal(text, result.Text);
        Assert.Equal(before, Files());
        if (Path.IsPathFullyQualified(path) && !existed)
        {
            Assert.False(File.Exists(path) || Directory.Exists(path));
        }

        return result;
    }

    private string? DbHash()
    {
        string path = Path.Combine(data.Root, TagIndex.FileName);
        return File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
    }

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);
}
