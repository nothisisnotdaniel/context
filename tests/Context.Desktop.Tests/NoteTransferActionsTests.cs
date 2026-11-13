using System.Text;

namespace Context.Tests;

// 139 AC-1 to AC-5, AC-11: DesktopActions.ImportNote and ExportNote, against real files next to the data folder.
public sealed class NoteTransferActionsTests : IDisposable
{
    private const string Hint = " The source is unchanged; delete it if you don't need the plain copy: ";

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly DesktopFixture data = new();

    public NoteTransferActionsTests()
    {
        data.Keys = FakeKeyStore.Ready();
    }

    public void Dispose() => data.Dispose();

    // AC-1: a new title is stored with the file's exact bytes; the source keeps its bytes and its time.
    [Fact]
    public async Task ImportNote_NewTitle_StoresExactBytes_SourceUnchanged()
    {
        byte[] bytes = [.. Bom, .. Encoding.UTF8.GetBytes("# Plan\r\n\r\nText\r\n"), 0xFF];
        string source = Source("Plan.md", bytes);
        DateTime stamp = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, stamp);
        await data.RefreshAsync();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.ImportNote(source, null);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Imported Plan." + Hint + source, result.Text);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal(bytes, File.ReadAllBytes(source));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(source));
        Assert.Equal(builds + 1, data.State.Builds);
        Assert.Contains(data.State.Snapshot!.Notes, n => n.Name == "Plan");
    }

    // AC-2: an existing plain note (ignoring case) is replaced only with a confirmation.
    [Fact]
    public async Task ImportNote_ExistingNote_NotConfirmedThenReplaced()
    {
        data.WriteNote("Plan", "old\n");
        string source = Source("PLAN.md", Encoding.UTF8.GetBytes("new\n"));
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult refused = await data.Actions!.ImportNote(source, null);

        Assert.Equal(ActionKind.Refused, refused.Kind);
        Assert.Equal(ActionReason.NotConfirmed, refused.Reason);
        Assert.Equal("Plan", refused.Text);
        Assert.Equal(before, Files());

        ActionResult replaced = await data.Actions.ImportNote(source, new Confirmation());

        Assert.Equal(ActionKind.Done, replaced.Kind);
        Assert.Equal("Replaced Plan." + Hint + source, replaced.Text);
        Assert.Equal("new\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal(["Plan.md"], Files().Keys);
        Assert.Equal("new\n", File.ReadAllText(source));
    }

    // AC-3: a source inside the data folder.
    [Fact]
    public async Task ImportNote_InsideTheFolder_Refused()
    {
        data.WriteNote("Plan", "text\n");
        data.WriteFile(Path.Combine("sub", "Other.md"), "other\n");

        await ImportRefused(Path.Combine(data.Root, "sub", "Other.md"), "That file is already in the Context folder. Nothing changed.");
        await ImportRefused(Path.Combine(data.Root.ToUpperInvariant(), "Plan.md"), "That file is already in the Context folder. Nothing changed.");
    }

    // AC-3: a source that reaches the data folder through a symbolic link.
    [Fact]
    public async Task ImportNote_InsideThroughASymlink_Refused()
    {
        data.WriteNote("Plan", "text\n");
        string link = data.Outside("link");
        if (!TryLink(link, data.Root))
        {
            return; // Symlinks can't be made here (Windows without the privilege).
        }

        await ImportRefused(Path.Combine(link, "Plan.md"), "That file is already in the Context folder. Nothing changed.");
    }

    // AC-3: a file name that is no valid title.
    [Fact]
    public async Task ImportNote_InvalidTitle_Refused()
    {
        data.WriteNote("Plan", "text\n");
        string source = Source(".hidden.md", Encoding.UTF8.GetBytes("x\n"));

        await ImportRefused(source, NoteStore.ValidateTitle(".hidden") + " Rename the file and import it again.");
    }

    // AC-3: a missing file and a directory are both "No such file".
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportNote_MissingOrDirectory_Refused(bool directory)
    {
        data.WriteNote("Plan", "text\n");
        string source = data.Outside("Gone.md");
        if (directory)
        {
            Directory.CreateDirectory(source);
        }

        await ImportRefused(source, "No such file: " + source);
    }

    // AC-3: a file over 5 MB.
    [Fact]
    public async Task ImportNote_Over5Mb_Refused()
    {
        data.WriteNote("Plan", "text\n");
        string source = Source("Big.md", new byte[NoteStore.MaxImportBytes + 1]);

        await ImportRefused(source, "Can't import a file over 5 MB: " + source);
    }

    // AC-3: a file that exists but can't be read.
    [Fact]
    public async Task ImportNote_Unreadable_Failed()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            return; // Unix permissions only, and root reads everything.
        }

        data.WriteNote("Plan", "text\n");
        string source = Source("Locked.md", Encoding.UTF8.GetBytes("x\n"));
        File.SetUnixFileMode(source, UnixFileMode.None);

        await ImportRefused(source, $"Can't read {source}.", ActionKind.Failed);
    }

    // AC-3: a title that names a sensitive note (ignoring case), with or without a confirmation.
    [Fact]
    public async Task ImportNote_SensitiveName_Refused()
    {
        data.WriteSensitive("Secret", "sealed\n");
        string source = Source("SECRET.md", Encoding.UTF8.GetBytes("plain\n"));

        await ImportRefused(source, "Secret is sensitive. Nothing was imported.");
        await ImportRefused(source, "Secret is sensitive. Nothing was imported.", confirmed: new Confirmation());
        Assert.Equal(0, data.Keys.Agrees);
    }

    // AC-3: a path that is not fully qualified is never resolved against the current directory.
    [Theory]
    [InlineData("Plan.md")]
    [InlineData("../Plan.md")]
    [InlineData("")]
    public async Task ImportNote_NotFullyQualified_Refused(string path)
    {
        data.WriteNote("Plan", "text\n");

        await ImportRefused(path, $"Can't read {path}.");
    }

    // AC-11: the result holds the path as it is; DesktopText.OneLine then shows a control character replaced.
    [Fact]
    public async Task ImportNote_ControlCharacterInThePath_ShownOnOneLine()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows paths can't hold control characters.
        }

        string folder = data.Outside("odd\u0007\nfolder");
        Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "Plan.md");
        File.WriteAllText(source, "text\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.ImportNote(source, null);

        Assert.Equal(ActionKind.Done, result.Kind);
        string shown = DesktopText.OneLine(result.Text);
        Assert.DoesNotContain('\n', shown);
        Assert.DoesNotContain('\u0007', shown);
        Assert.EndsWith("odd� folder" + Path.DirectorySeparatorChar + "Plan.md", shown, StringComparison.Ordinal);
    }

    // AC-4: the saved text goes to a new file as UTF-8 without a BOM; the data folder is untouched, no refresh.
    [Fact]
    public async Task ExportNote_PlainNote_WritesTextWithoutBom()
    {
        Directory.CreateDirectory(data.Root);
        File.WriteAllBytes(Path.Combine(data.Root, "Plan.md"), [.. Bom, .. Encoding.UTF8.GetBytes("# Plan\r\n\r\nÄ text\r\n")]);
        string target = data.Outside("Copy.md");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.ExportNote("Plan", target);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal($"Exported Plan to {target}", result.Text);
        Assert.Equal(Encoding.UTF8.GetBytes("# Plan\r\n\r\nÄ text\r\n"), File.ReadAllBytes(target));
        Assert.Equal(before, Files());
        Assert.Equal(builds, data.State.Builds);
    }

    // AC-5: a target inside the data folder, also in another case.
    [Fact]
    public async Task ExportNote_InsideTheFolder_Refused()
    {
        data.WriteNote("Plan", "text\n");

        await ExportRefused("Plan", Path.Combine(data.Root, "Copy.md"), "Export to a path outside the Context folder. Nothing was written.");
        await ExportRefused(
            "Plan", Path.Combine(data.Root.ToUpperInvariant(), "sub", "Copy.md"), "Export to a path outside the Context folder. Nothing was written.");
    }

    // AC-5: a target that reaches the data folder through a symbolic link.
    [Fact]
    public async Task ExportNote_InsideThroughASymlink_Refused()
    {
        data.WriteNote("Plan", "text\n");
        string link = data.Outside("link");
        if (!TryLink(link, data.Root))
        {
            return; // Symlinks can't be made here (Windows without the privilege).
        }

        await ExportRefused("Plan", Path.Combine(link, "Copy.md"), "Export to a path outside the Context folder. Nothing was written.");
    }

    // AC-5: an existing file is never overwritten.
    [Fact]
    public async Task ExportNote_TargetIsAFile_RefusedFileUnchanged()
    {
        data.WriteNote("Plan", "text\n");
        string target = Source("Copy.md", Encoding.UTF8.GetBytes("theirs\n"));
        SortedDictionary<string, string> outside = FileSnapshot.Take(data.Outside(string.Empty), "Context/");

        await ExportRefused("Plan", target, "That file already exists; choose a new name: " + target);

        Assert.Equal(outside, FileSnapshot.Take(data.Outside(string.Empty), "Context/"));
    }

    // AC-5: an existing directory gets the same answer, and nothing is put in it.
    [Fact]
    public async Task ExportNote_TargetIsADirectory_RefusedDirectoryUnchanged()
    {
        data.WriteNote("Plan", "text\n");
        string target = data.Outside("Copy.md");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "inner.txt"), "inner");
        SortedDictionary<string, string> outside = FileSnapshot.Take(target);

        await ExportRefused("Plan", target, "That file already exists; choose a new name: " + target);

        Assert.True(Directory.Exists(target));
        Assert.Equal(outside, FileSnapshot.Take(target));
    }

    // AC-5: a note that is gone.
    [Fact]
    public async Task ExportNote_UnknownNote_Refused()
    {
        data.WriteNote("Plan", "text\n");
        string target = data.Outside("Copy.md");

        await ExportRefused("Ghost", target, "Ghost no longer exists. Nothing was done.");

        Assert.False(File.Exists(target));
    }

    // AC-5: a sensitive note is refused, locked or open, and the private key is never asked for.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportNote_Sensitive_RefusedWithoutAgree(bool open)
    {
        data.WriteSensitive("Secret", "sealed text\n");
        string target = data.Outside("Copy.md");
        await data.RefreshAsync();
        if (open)
        {
            Assert.NotNull(await data.State!.ReadSensitive("Secret"));
        }

        int agrees = data.Keys.Agrees;

        ActionResult result = await ExportRefused("secret", target, "Secret is sensitive. Make it plain to export it.");

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(agrees, data.Keys.Agrees);
        Assert.False(File.Exists(target));
    }

    // AC-5: a note over 5 MB can't be read.
    [Fact]
    public async Task ExportNote_NoteOver5Mb_Failed()
    {
        Directory.CreateDirectory(data.Root);
        File.WriteAllBytes(Path.Combine(data.Root, "Big.md"), new byte[NoteStore.MaxEditBytes + 1]);
        string target = data.Outside("Copy.md");

        await ExportRefused("Big", target, "Can't read Big.", ActionKind.Failed);

        Assert.False(File.Exists(target));
    }

    // AC-5: a note that can't be read.
    [Fact]
    public async Task ExportNote_UnreadableNote_Failed()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            return; // Unix permissions only, and root reads everything.
        }

        data.WriteNote("Plan", "text\n");
        string target = data.Outside("Copy.md");
        await data.RefreshAsync();
        File.SetUnixFileMode(Path.Combine(data.Root, "Plan.md"), UnixFileMode.None);

        ActionResult result = await data.Actions!.ExportNote("Plan", target);

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal("Can't read Plan.", result.Text);
        Assert.False(File.Exists(target));
        Assert.Equal(["Plan.md"], Directory.EnumerateFiles(data.Root).Select(f => Path.GetFileName(f)).Where(n => n != TagIndex.FileName).ToArray());
    }

    // AC-5: a target that can't be written (its folder is missing), and one that is not fully qualified.
    [Fact]
    public async Task ExportNote_CannotWrite_Failed()
    {
        data.WriteNote("Plan", "text\n");
        string target = Path.Combine(data.Outside("missing"), "Copy.md");

        await ExportRefused("Plan", target, $"Can't write {target}.", ActionKind.Failed);
        await ExportRefused("Plan", "Copy.md", "Can't write Copy.md.");

        Assert.False(Directory.Exists(data.Outside("missing")));
        Assert.False(File.Exists("Copy.md"));
    }

    // No answer carries an exception's message (AC-3): the texts are the fixed ones asserted above, and every
    // refusal leaves the data folder as it was.
    private async Task<ActionResult> ImportRefused(
        string path, string text, ActionKind kind = ActionKind.Refused, Confirmation? confirmed = null)
    {
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.ImportNote(path, confirmed);

        Assert.Equal(text, result.Text);
        Assert.Equal(kind, result.Kind);
        Assert.NotEqual(ActionReason.NotConfirmed, result.Reason);
        Assert.Equal(before, Files());
        Assert.Equal(builds, data.State.Builds);
        return result;
    }

    private async Task<ActionResult> ExportRefused(string name, string path, string text, ActionKind kind = ActionKind.Refused)
    {
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.ExportNote(name, path);

        Assert.Equal(text, result.Text);
        Assert.Equal(kind, result.Kind);
        Assert.Equal(before, Files());
        return result;
    }

    private string Source(string name, byte[] bytes)
    {
        string path = data.Outside(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static bool TryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName);
}
