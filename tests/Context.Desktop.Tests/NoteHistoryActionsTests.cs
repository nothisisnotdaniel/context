using System.Text;

namespace Context.Tests;

// 174: the note history through DesktopActions and DesktopState, against real files, the fixture's fixed clock and a
// key store in memory that counts Agree calls.
public sealed class NoteHistoryActionsTests : IDisposable
{
    private const string Id = "20261029T090000000Z";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-1: a desktop save keeps the previous bytes, unchanged, in <folder>/.history/<Title>/.
    [Fact]
    public async Task SaveNote_KeepsThePreviousBytes()
    {
        data.WriteNote("Plan", "# Plan\r\nold\r\n");
        await data.RefreshAsync();
        EditableNote opened = (await data.State!.ReadForEdit("Plan"))!;

        ActionResult result = await data.Actions!.SaveNote(opened, "# Plan\nnew\n");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("# Plan\r\nold\r\n", Read(".history/Plan/" + Id + ".md"));
        Assert.Equal("# Plan\r\nnew\r\n", Read("Plan.md"));
    }

    // AC-1: a subpage keeps its versions next to it.
    [Fact]
    public async Task SaveNote_Subpage_KeepsNextToIt()
    {
        data.WriteNote("Work", "w\n");
        data.WriteFile(Path.Combine("Work", "Projects.md"), "p\n");
        data.WriteFile(Path.Combine("Work", "Projects", "N.md"), "old\n");
        await data.RefreshAsync();
        EditableNote opened = (await data.State!.ReadForEdit("Work/Projects/N"))!;

        await data.Actions!.SaveNote(opened, "new\n");

        Assert.Equal("old\n", Read("Work/Projects/.history/N/" + Id + ".md"));
    }

    // AC-1: an unchanged save and a save refused as changed on disk keep nothing.
    [Fact]
    public async Task SaveNote_UnchangedOrChangedOnDisk_KeepsNothing()
    {
        data.WriteNote("Plan", "old\n");
        await data.RefreshAsync();
        EditableNote opened = (await data.State!.ReadForEdit("Plan"))!;

        ActionResult same = await data.Actions!.SaveNote(opened, "old\n");
        data.WriteNote("Plan", "outside\n");
        ActionResult changed = await data.Actions.SaveNote(opened, "mine\n");

        Assert.Equal(ActionReason.Unchanged, same.Reason);
        Assert.Equal(ActionReason.ChangedOnDisk, changed.Reason);
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".history")));
    }

    // AC-4: a broken .history fails the save with its own message and changes nothing.
    [Fact]
    public async Task SaveNote_KeepFails_NothingSaved()
    {
        data.WriteNote("Plan", "old\n");
        data.WriteFile(".history", "in the way");
        await data.RefreshAsync();
        EditableNote opened = (await data.State!.ReadForEdit("Plan"))!;
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.SaveNote(opened, "new\n");

        Assert.Equal(
            (ActionKind.Failed, "Can't keep the old version of Plan. Nothing was saved."), (result.Kind, result.Text));
        Assert.Equal(DesktopActions.KeepFailedText("Plan"), result.Text);
        Assert.Equal(before, Files());
    }

    // AC-4: the same for a sensitive note; the sealed file is not changed.
    [Fact]
    public async Task SaveNote_Sensitive_KeepFails_NothingSaved()
    {
        await Sealed("Plan", "old\n");
        data.WriteFile(".history", "in the way");
        EditableNote opened = (await data.State!.ReadSensitive("Plan"))!;
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.SaveNote(opened, "new\n");

        Assert.Equal((ActionKind.Failed, DesktopActions.KeepFailedText("Plan")), (result.Kind, result.Text));
        Assert.Equal(before, Files());
    }

    // AC-5: capture, the day's end, the briefing and import write no versions.
    [Fact]
    public async Task OtherWriters_KeepNoVersions()
    {
        Directory.CreateDirectory(data.TimesFolder);
        data.WriteNote(Inbox.NoteName, "# inbox\n");
        data.WriteNote("2026-10-29", "# 2026-10-29\n");
        data.WriteNote("Plan", "old\n");
        string source = data.Outside("Plan.md");
        File.WriteAllText(source, "imported\n");
        await data.RefreshAsync();

        Assert.Equal(ActionKind.Done, (await data.Actions!.CaptureText("call Anna")).Kind);
        await data.Actions.EndDay(DesktopFixture.Today);
        await data.Actions.OpenBriefing();
        Assert.Equal(ActionKind.Done, (await data.Actions.ImportNote(source, new Confirmation())).Kind);

        Assert.Contains("call Anna", Read("inbox.md"), StringComparison.Ordinal);
        Assert.Equal("imported\n", Read("Plan.md"));
        Assert.Empty(Directory.EnumerateDirectories(data.Root, ".history", SearchOption.AllDirectories));
    }

    // AC-6: a plain rename takes the history along.
    [Fact]
    public async Task RenameNote_MovesTheHistory()
    {
        data.WriteNote("Plan", "now\n");
        WriteVersion("Plan", Id, "old\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.RenameNote("Plan", "Project", null);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal([".history/Project/" + Id + ".md", "Project.md"], RootFiles());
    }

    // AC-6: the user's Delete removes the history of the note and of every subpage.
    [Fact]
    public async Task DeleteNote_Plain_RemovesTheSubtreesHistory()
    {
        data.WriteNote("Plan", "p\n");
        data.WriteFile(Path.Combine("Plan", "Step.md"), "s\n");
        data.WriteNote("Other", "o\n");
        WriteVersion("Plan", Id, "old plan\n");
        WriteVersion("Plan/Step", Id, "old step\n");
        WriteVersion("Other", Id, "old other\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.DeleteNote("Plan", new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal([".history/Other/" + Id + ".md", "Other.md"], RootFiles());
        Assert.False(Directory.Exists(Path.Combine(data.Root, "Plan")));
    }

    // AC-6: deleting a sensitive note removes its sealed versions and the emptied .history.
    [Fact]
    public async Task DeleteNote_Sensitive_RemovesTheHistory()
    {
        await Sealed("Plan", "old\n");
        EditableNote opened = (await data.State!.ReadSensitive("Plan"))!;
        await data.Actions!.SaveNote(opened, "new\n");
        Assert.Equal([".history/Plan/" + Id + "~Plan.ctxs", "Plan.ctxs"], RootFiles());

        ActionResult result = await data.Actions.DeleteNote("Plan", new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Empty(RootFiles());
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".history")));
    }

    // AC-7: a sensitive save keeps the previous sealed file byte for byte, under the note's title, with no Agree.
    [Fact]
    public async Task SaveNote_Sensitive_KeepsTheSealedBytes()
    {
        await Sealed("Plan", "old\n");
        EditableNote opened = (await data.State!.ReadSensitive("Plan"))!;
        byte[] before = File.ReadAllBytes(opened.Path);

        ActionResult result = await data.Actions!.SaveNote(opened, "new\n");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(data.Root, ".history", "Plan", Id + "~Plan.ctxs")));
        Assert.DoesNotContain("old", Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(data.Root, ".history", "Plan", Id + "~Plan.ctxs"))), StringComparison.Ordinal);
    }

    // AC-8: a sensitive rename moves every version to .history/<New>/; an old one still opens, with one Agree.
    [Fact]
    public async Task RenameNote_Sensitive_MovesTheVersions_OldOnesStillOpen()
    {
        await Sealed("Plan", "first\n");
        EditableNote opened = (await data.State!.ReadSensitive("Plan"))!;
        ActionResult saved = await data.Actions!.SaveNote(opened, "second\n");
        Assert.Equal(ActionKind.Done, saved.Kind);

        ActionResult renamed = await data.Actions.RenameNote("Plan", "Project", null);

        Assert.Equal(ActionKind.Done, renamed.Kind);
        Assert.Equal([".history/Project/" + Id + "~Plan.ctxs", "Project.ctxs"], RootFiles());
        int agrees = data.Keys.Agrees;
        Assert.Equal("first\n", await data.State.ReadVersion("Project", Id));
        Assert.Equal(agrees + 1, data.Keys.Agrees);
        HistoryView view = await data.State.History("Project");
        Assert.Equal("Plan", Assert.Single(view.Versions).SealedTitle);
    }

    // AC-8: when .history/<New>/ already exists the new file goes again and nothing else changes.
    [Fact]
    public async Task RenameNote_Sensitive_HistoryClash_RefusedAndUnchanged()
    {
        await Sealed("Plan", "first\n");
        EditableNote opened = (await data.State!.ReadSensitive("Plan"))!;
        await data.Actions!.SaveNote(opened, "second\n");
        data.WriteFile(Path.Combine(".history", "Project", "20261001T090000000Z.md"), "left over\n");
        SortedDictionary<string, string> before = Files();

        ActionResult renamed = await data.Actions.RenameNote("Plan", "Project", null);

        Assert.Equal((ActionKind.Refused, ActionReason.Exists), (renamed.Kind, renamed.Reason));
        Assert.Equal(DesktopActions.ExistsText("Project"), renamed.Text);
        Assert.Equal(before, Files());
    }

    // AC-9: marking seals every plain version under the same id, with no Agree, then removes the plain ones.
    [Fact]
    public async Task MarkSensitive_SealsTheVersions_SameIds()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteNote("Plan", "now\n");
        WriteVersion("Plan", "20261028T090000000Z", "older\n");
        WriteVersion("Plan", Id, "old\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.MarkSensitive("Plan", new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(
            [".history/Plan/20261028T090000000Z~Plan.ctxs", ".history/Plan/" + Id + "~Plan.ctxs", "Plan.ctxs"], RootFiles());
        AssertPrivate(Path.Combine(data.Root, ".history", "Plan", Id + "~Plan.ctxs"));
        Assert.Equal("old\n", Unseal(Path.Combine(data.Root, ".history", "Plan", Id + "~Plan.ctxs"), "Plan"));
        Assert.Equal("older\n", Unseal(Path.Combine(data.Root, ".history", "Plan", "20261028T090000000Z~Plan.ctxs"), "Plan"));
    }

    // AC-9 (nit): when the plain versions can't be removed after the mark, the note stays sealed and the result says so.
    [Fact]
    public async Task MarkSensitive_PlainVersionsLeft_IsReported()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        data.Keys = FakeKeyStore.Ready();
        data.WriteNote("Plan", "now\n");
        WriteVersion("Plan", Id, "old\n");
        await data.RefreshAsync();
        string folder = Path.Combine(data.Root, ".history", "Plan");
        UnixFileMode mode = File.GetUnixFileMode(folder);
        data.Actions!.AfterHistorySealed = () =>
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }
        };
        ActionResult result;
        try
        {
            result = await data.Actions.MarkSensitive("Plan", new Confirmation());
        }
        finally
        {
            File.SetUnixFileMode(folder, mode);
        }

        Assert.Equal((ActionKind.Done, ActionReason.PlainHistoryLeft, "Plan"), (result.Kind, result.Reason, result.Text));
        Assert.Equal(
            [".history/Plan/" + Id + ".md", ".history/Plan/" + Id + "~Plan.ctxs", "Plan.ctxs"], RootFiles());
        Assert.Equal(
            "Marked Plan sensitive, but readable history may remain in .history/Plan in the data folder. Check that folder, including hidden files. Secure your plaintext versions before removing them. Keep the .ctxs files.",
            DesktopActions.PlainHistoryLeftText("Plan"));
    }

    // AC-9: a version that isn't UTF-8 fails the mark; no sealed version is left and the plain ones stay byte for byte.
    [Fact]
    public async Task MarkSensitive_VersionNotUtf8_FailsAndChangesNothing()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteNote("Plan", "now\n");
        WriteVersion("Plan", "20261028T090000000Z", "older\n");
        string bad = Path.Combine(data.Root, ".history", "Plan", Id + ".md");
        File.WriteAllBytes(bad, [0xFF, 0xFE, 0x41]);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.MarkSensitive("Plan", new Confirmation());

        Assert.Equal(
            (ActionKind.Failed, "Can't seal the history of Plan. Nothing was changed."), (result.Kind, result.Text));
        Assert.Equal(DesktopActions.SealHistoryFailedText("Plan"), result.Text);
        Assert.Equal(before, Files());
    }

    // AC-9: the changed-on-disk rollback removes the sealed versions too and never the plain ones.
    [Fact]
    public async Task MarkSensitive_ChangedOnDisk_RollsBackTheSealedVersions()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteNote("Plan", "now\n");
        WriteVersion("Plan", Id, "old\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> versions = FileSnapshot.Take(Path.Combine(data.Root, ".history"));
        data.Actions!.AfterHistorySealed = () =>
        {
            Assert.Contains(Id + "~Plan.ctxs", Directory.GetFiles(Path.Combine(data.Root, ".history", "Plan")).Select(Path.GetFileName));
            File.WriteAllText(Path.Combine(data.Root, "Plan.md"), "changed outside\n");
        };

        ActionResult result = await data.Actions.MarkSensitive("Plan", new Confirmation());

        Assert.Equal((ActionKind.Refused, ActionReason.ChangedOnDisk), (result.Kind, result.Reason));
        Assert.Equal([".history/Plan/" + Id + ".md", "Plan.md"], RootFiles());
        Assert.Equal(versions, FileSnapshot.Take(Path.Combine(data.Root, ".history")));
    }

    // AC-10: Make plain removes the sealed versions; without a confirmation nothing goes.
    [Fact]
    public async Task MakePlain_RemovesTheSealedVersions_OnlyWhenConfirmed()
    {
        await Sealed("Plan", "old\n");
        EditableNote opened = (await data.State!.ReadSensitive("Plan"))!;
        ActionResult saved = await data.Actions!.SaveNote(opened, "new\n");
        SortedDictionary<string, string> before = Files();

        ActionResult cancelled = await data.Actions.MakePlain("Plan", null);
        Assert.Equal(ActionReason.NotConfirmed, cancelled.Reason);
        Assert.Equal(before, Files());

        ActionResult made = await data.Actions.MakePlain("Plan", new Confirmation());

        Assert.Equal(ActionKind.Done, made.Kind);
        Assert.Equal(["Plan.md"], RootFiles());
        Assert.Equal("new\n", Read("Plan.md"));
        Assert.Equal("new\n", saved.Note!.Text);
    }

    // AC-13: restoring saves the version's text through the save path; the current text becomes a version.
    [Fact]
    public async Task RestoreVersion_SavesTheVersion_KeepsTheCurrentText()
    {
        data.WriteNote("Plan", "first\n");
        await data.RefreshAsync();
        EditableNote opened = (await data.State!.ReadForEdit("Plan"))!;
        ActionResult saved = await data.Actions!.SaveNote(opened, "second\n");

        ActionResult restored = await data.Actions.RestoreVersion("Plan", Id, saved.Note!, new Confirmation());

        Assert.Equal((ActionKind.Done, "Plan"), (restored.Kind, restored.Text));
        Assert.Equal("first\n", restored.Note!.Text);
        Assert.Equal("first\n", Read("Plan.md"));
        Assert.Equal("second\n", Read(".history/Plan/" + Id + "-1.md"));
        HistoryView view = await data.State.History("Plan");
        Assert.Equal([Id + "-1", Id], view.Versions.Select(v => v.Id));
        Assert.Equal("first\n", view.Current);
    }

    // AC-13: without a confirmation nothing is read or written.
    [Fact]
    public async Task RestoreVersion_NotConfirmed_WritesNothing()
    {
        await Sealed("Plan", "first\n");
        EditableNote opened = (await data.State!.ReadSensitive("Plan"))!;
        ActionResult saved = await data.Actions!.SaveNote(opened, "second\n");
        SortedDictionary<string, string> before = Files();
        int agrees = data.Keys.Agrees;

        ActionResult result = await data.Actions.RestoreVersion("Plan", Id, saved.Note!, null);

        Assert.Equal((ActionKind.Refused, ActionReason.NotConfirmed), (result.Kind, result.Reason));
        Assert.Equal(before, Files());
        Assert.Equal(agrees, data.Keys.Agrees);
    }

    // AC-13: a sensitive restore opens the sealed version with one Agree and seals the result again.
    [Fact]
    public async Task RestoreVersion_Sensitive_OneAgree()
    {
        await Sealed("Plan", "first\n");
        EditableNote opened = (await data.State!.ReadSensitive("Plan"))!;
        ActionResult saved = await data.Actions!.SaveNote(opened, "second\n");
        int agrees = data.Keys.Agrees;

        ActionResult restored = await data.Actions.RestoreVersion("Plan", Id, saved.Note!, new Confirmation());

        Assert.Equal(ActionKind.Done, restored.Kind);
        Assert.Equal(agrees + 1, data.Keys.Agrees);
        Assert.Equal("first\n", Unseal(Path.Combine(data.Root, "Plan.ctxs"), "Plan"));
        Assert.Equal(
            [".history/Plan/" + Id + "-1~Plan.ctxs", ".history/Plan/" + Id + "~Plan.ctxs", "Plan.ctxs"], RootFiles());
    }

    // AC-12/AC-13: a version that can't be read is "Can't read this version." and writes nothing.
    [Fact]
    public async Task RestoreVersion_Unreadable_Fails()
    {
        data.WriteNote("Plan", "now\n");
        await data.RefreshAsync();
        EditableNote opened = (await data.State!.ReadForEdit("Plan"))!;
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.RestoreVersion("Plan", Id, opened, new Confirmation());

        Assert.Equal((ActionKind.Failed, "Can't read this version."), (result.Kind, result.Text));
        Assert.Equal(before, Files());
    }

    // AC-12: History lists newest first with the saved text; a broken .history is Unreadable; a locked note too.
    [Fact]
    public async Task History_ListsNewestFirst_OrUnreadable()
    {
        data.WriteNote("Plan", "now\r\n");
        WriteVersion("Plan", "20261028T090000000Z", "older\n");
        WriteVersion("Plan", Id, "old\n");
        data.WriteNote("Other", "o\n");
        await data.RefreshAsync();

        HistoryView view = await data.State!.History("Plan");

        Assert.Equal(HistoryKind.Shown, view.Kind);
        Assert.Equal([Id, "20261028T090000000Z"], view.Versions.Select(v => v.Id));
        Assert.Equal("now\n", view.Current);
        Assert.Empty((await data.State.History("Other")).Versions);
        Assert.Equal(HistoryKind.Unreadable, (await data.State.History("Missing")).Kind);

        Directory.Delete(Path.Combine(data.Root, ".history"), recursive: true);
        data.WriteFile(".history", "in the way");
        Assert.Equal(HistoryKind.Unreadable, (await data.State.History("Plan")).Kind);
    }

    // 203 AC-10: automatic saves within AutoVersionGap keep one version (the text as opened); once the newest version
    // is 5 minutes old the next one keeps another; explicit saves keep one every time.
    [Fact]
    public async Task SaveNote_Auto_KeepsAVersionOnlyAfterTheGap()
    {
        data.WriteNote("Plan", "opened\n");
        await data.RefreshAsync();
        EditableNote opened = (await data.State!.ReadForEdit("Plan"))!;

        foreach (string text in new[] { "one\n", "two\n", "three\n" })
        {
            ActionResult saved = await data.Actions!.SaveNote(opened, text, auto: true);
            Assert.Equal(ActionKind.Done, saved.Kind);
            opened = saved.Note!;
            data.UtcNow = data.UtcNow.AddSeconds(20);
        }

        Assert.Equal([".history/Plan/" + Id + ".md", "Plan.md"], RootFiles());
        Assert.Equal("opened\n", Read(".history/Plan/" + Id + ".md"));
        Assert.Equal("three\n", Read("Plan.md"));

        data.UtcNow = new DateTime(2026, 10, 29, 9, 5, 0, DateTimeKind.Utc);
        opened = (await data.Actions!.SaveNote(opened, "four\n", auto: true)).Note!;
        Assert.Equal(2, (await data.State.History("Plan")).Versions.Count);
        Assert.Equal("three\n", Read(".history/Plan/20261029T090500000Z.md"));

        opened = (await data.Actions.SaveNote(opened, "five\n")).Note!;
        await data.Actions.SaveNote(opened, "six\n", auto: false);
        Assert.Equal(4, (await data.State.History("Plan")).Versions.Count);
        Assert.Equal("six\n", Read("Plan.md"));
    }

    // 203 AC-10: the gap is measured from the newest version, also when it was kept by an explicit save.
    [Fact]
    public async Task SaveNote_Auto_AfterExplicitSave_WaitsForTheGap()
    {
        data.WriteNote("Plan", "opened\n");
        await data.RefreshAsync();
        EditableNote opened = (await data.State!.ReadForEdit("Plan"))!;

        opened = (await data.Actions!.SaveNote(opened, "explicit\n")).Note!;
        data.UtcNow = data.UtcNow.AddMinutes(4).AddSeconds(59);
        opened = (await data.Actions.SaveNote(opened, "auto\n", auto: true)).Note!;
        Assert.Single((await data.State.History("Plan")).Versions);

        data.UtcNow = data.UtcNow.AddSeconds(1);
        await data.Actions.SaveNote(opened, "later\n", auto: true);
        Assert.Equal(2, (await data.State.History("Plan")).Versions.Count);
    }

    // 203 AC-10/AC-11: sensitive automatic saves follow the same gap for their sealed versions, with no Agree.
    [Fact]
    public async Task SaveNote_Sensitive_Auto_KeepsASealedVersionOnlyAfterTheGap()
    {
        await Sealed("Plan", "opened\n");
        EditableNote opened = (await data.State!.ReadSensitive("Plan"))!;
        int agrees = data.Keys.Agrees;

        opened = (await data.Actions!.SaveNote(opened, "one\n", auto: true)).Note!;
        data.UtcNow = data.UtcNow.AddSeconds(30);
        opened = (await data.Actions.SaveNote(opened, "two\n", auto: true)).Note!;
        Assert.Equal([".history/Plan/" + Id + "~Plan.ctxs", "Plan.ctxs"], RootFiles());

        data.UtcNow = data.UtcNow.AddMinutes(5);
        ActionResult last = await data.Actions.SaveNote(opened, "three\n", auto: true);

        Assert.Equal(ActionKind.Done, last.Kind);
        Assert.Equal(3, RootFiles().Length);
        Assert.Equal(agrees, data.Keys.Agrees);
        Assert.Equal("opened\n", Unseal(Path.Combine(data.Root, ".history", "Plan", Id + "~Plan.ctxs"), "Plan"));
    }

    private async Task Sealed(string name, string text)
    {
        data.Keys.Dispose();
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive(name, text);
        await data.RefreshAsync();
    }

    private void WriteVersion(string name, string id, string text)
    {
        string[] parts = name.Split('/');
        string folder = Path.Combine([data.Root, .. parts[..^1], NoteHistory.FolderName, parts[^1]]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, id + ".md"), text);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine([data.Root, .. relative.Split('/')]));

    private string Unseal(string path, string title) =>
        SealedNote.Open(title, File.ReadAllBytes(path), data.Keys.MachinePublicKey()!, data.Keys.Agree);

    private string[] RootFiles() => FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName).Keys.ToArray();

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

    private static void AssertPrivate(string path)
    {
        Assert.True(File.Exists(path));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }
}
