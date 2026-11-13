using System.Text;

namespace Context.Tests;

// 136 AC-3 to AC-8: the sensitive-note actions, against real files and a key store in memory.
public sealed class SensitiveActionsTests : IDisposable
{
    private const string Marker = "marker-4f9a2c-in-the-text";
    private const string Password = "long enough password";

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-3: the note becomes <name>.ctxs, wrapped for the machine and the recovery key, without one private-key call.
    [Fact]
    public async Task MarkSensitive_SealsForBothKeys_AndDeletesThePlainFile()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteNote("Plan", "# Plan\n\n" + Marker + " #work [[Other]]\n");
        data.WriteNote("Other", "other\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.MarkSensitive("Plan", new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Plan", result.Text);
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(["Other.md", "Plan.ctxs"], RootFiles());
        byte[] file = File.ReadAllBytes(Path.Combine(data.Root, "Plan.ctxs"));
        Assert.True(SealedNote.HasKey(file, data.Keys.MachinePublicKey()!));
        Assert.True(SealedNote.HasKey(file, data.Keys.RecoveryPublicKey()!));
        Assert.Equal("# Plan\n\n" + Marker + " #work [[Other]]\n", SealedNote.Open("Plan", file, data.Keys.MachinePublicKey()!, data.Keys.Agree));
        AssertPrivate(Path.Combine(data.Root, "Plan.ctxs"));
        AssertMarkerNowhere();
        Assert.Equal(["Other"], data.State!.Snapshot!.Notes.Select(n => n.Name).ToArray());
        Assert.Equal(["Plan"], data.State.Snapshot.Sensitive.Select(n => n.Name).ToArray());
    }

    // AC-3: after the next sync the note is in no index table: no tag, no link, no name.
    [Fact]
    public async Task MarkSensitive_NoteLeavesTheIndex()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteNote("Plan", "#secret [[Other]]\n");
        data.WriteNote("Other", "#kept [[Plan]]\n");
        await data.RefreshAsync();
        TagIndex index = new TagIndex(data.Root);
        index.Sync(data.Notes!);
        Assert.Equal(["Plan"], index.NotesWithTag("secret"));

        await data.Actions!.MarkSensitive("Plan", new Confirmation());
        index.Sync(data.Notes!);

        Assert.Equal(["kept"], index.Tags().Select(t => t.Tag).ToArray());
        Assert.Empty(index.NotesWithTag("secret"));
        Assert.Null(index.FindNote("Plan"));
        NoteLink link = Assert.Single(index.Links("Other"));
        Assert.Equal("Plan", link.Note);
        Assert.True(link.Missing);
    }

    // AC-3: a byte order mark and CRLF survive mark, open, save and make plain.
    [Fact]
    public async Task MarkSensitive_KeepsMarkAndLineEndings()
    {
        data.Keys = FakeKeyStore.Ready();
        byte[] plain = [.. Bom, .. Encoding.UTF8.GetBytes("# Plan\r\n\r\nText\r\n")];
        Directory.CreateDirectory(data.Root);
        File.WriteAllBytes(Path.Combine(data.Root, "Plan.md"), plain);
        await data.RefreshAsync();

        await data.Actions!.MarkSensitive("Plan", new Confirmation());
        EditableNote? opened = await data.State!.ReadSensitive("Plan");

        Assert.NotNull(opened);
        Assert.Equal("# Plan\n\nText\n", opened.Text);
        Assert.True(opened.HadBom);
        Assert.True(opened.HadCrlf);
        ActionResult made = await data.Actions.MakePlain("Plan", new Confirmation());
        Assert.Equal(ActionKind.Done, made.Kind);
        Assert.Equal(plain, File.ReadAllBytes(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal(["Plan.md"], RootFiles());
    }

    // AC-3: refused with nothing written or deleted.
    [Theory]
    [InlineData("no confirmation", nameof(ActionReason.NotConfirmed), DesktopActions.NotConfirmedText)]
    [InlineData("inbox", nameof(ActionReason.Invalid), DesktopActions.InboxSensitiveText)]
    [InlineData("no recovery", nameof(ActionReason.NoKeys), "Set up recovery first.")]
    [InlineData("gone", nameof(ActionReason.Missing), "Gone no longer exists. Nothing was done.")]
    [InlineData("large", nameof(ActionReason.Invalid), "Plan is larger than 5 MB. Nothing was done.")]
    [InlineData("unsupported", nameof(ActionReason.NoKeys), "Sensitive notes need macOS or Windows.")]
    [InlineData("not utf-8", nameof(ActionReason.Invalid), "Can't mark Plan sensitive. Nothing was done.")]
    public async Task MarkSensitive_Refused_NothingWrittenOrDeleted(string scenario, string reason, string text)
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote(Inbox.NoteName, "captured\n");
        string name = "Plan";
        Confirmation? confirmed = new Confirmation();
        switch (scenario)
        {
            case "no confirmation":
                confirmed = null;
                break;
            case "inbox":
                name = "Inbox";
                break;
            case "no recovery":
                data.Keys.LoseRecoveryKey();
                break;
            case "gone":
                name = "Gone";
                break;
            case "large":
                File.WriteAllBytes(Path.Combine(data.Root, "Plan.md"), new byte[NoteStore.MaxEditBytes + 1]);
                break;
            case "unsupported":
                data.Keys.Supported = false;
                break;
            default:
                File.WriteAllBytes(Path.Combine(data.Root, "Plan.md"), [0x61, 0xFF, 0xFE, 0x62]);
                break;
        }

        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.MarkSensitive(name, confirmed);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(reason, result.Reason.ToString());
        Assert.Equal(text, result.Text);
        Assert.Equal(before, Files());
        Assert.Equal(0, data.Keys.Agrees);
    }

    // AC-3 / AC-10: on an OS without a key store nothing about sensitive notes is offered or done.
    [Fact]
    public async Task UnsupportedKeyStore_RefusesEverything()
    {
        UnsupportedKeyStore keys = new UnsupportedKeyStore();

        Assert.False(keys.Supported);
        Assert.Null(keys.MachinePublicKey());
        Assert.Null(keys.RecoveryPublicKey());
        Assert.Throws<KeyStoreException>(keys.CreateMachineKey);
        Assert.Throws<KeyStoreException>(() => keys.StoreRecoveryPublicKey([1]));
        Assert.Throws<KeyStoreException>(() => keys.Agree([1]));

        data.Keys.Supported = false;
        DesktopSnapshot snapshot = await data.RefreshAsync();
        Assert.False(snapshot.SensitiveSupported);
        Assert.False(snapshot.RecoveryReady);
        ActionResult setup = await data.Actions!.SetUpSensitive(Password, data.RecoveryPath);
        ActionResult restore = await data.Actions.RestoreFromRecovery(data.RecoveryPath, Password);
        Assert.Equal((ActionKind.Refused, DesktopActions.UnsupportedText), (setup.Kind, setup.Text));
        Assert.Equal((ActionKind.Refused, DesktopActions.UnsupportedText), (restore.Kind, restore.Text));
        Assert.False(File.Exists(data.RecoveryPath));
    }

    // AC-4: setup makes the machine key, writes the recovery file and only then stores the recovery public key.
    [Fact]
    public async Task SetUpSensitive_CreatesTheKeyAndTheRecoveryFile()
    {
        await data.RefreshAsync();
        Assert.True(data.State!.Snapshot!.SensitiveSupported);
        Assert.False(data.State.Snapshot.RecoveryReady);

        ActionResult result = await data.Actions!.SetUpSensitive(Password, data.RecoveryPath);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Recovery file saved. Keep it and its password safe.", result.Text);
        Assert.Equal(1, data.Keys.MachineKeysCreated);
        Assert.NotNull(data.Keys.MachinePublicKey());
        using System.Security.Cryptography.ECDiffieHellman key = RecoveryFile.Read(File.ReadAllBytes(data.RecoveryPath), Password);
        Assert.Equal(data.Keys.RecoveryPublicKey(), key.ExportSubjectPublicKeyInfo());
        AssertPrivate(data.RecoveryPath);
        Assert.Empty(FileSnapshot.Take(data.Root, TagIndex.FileName));
        Assert.Equal(["recovery.pfx"], FileSnapshot.Take(Parent(), "Context/").Keys.ToArray());
        Assert.DoesNotContain(Password, result.ToString(), StringComparison.Ordinal);
        Assert.True((await data.RefreshAsync()).RecoveryReady);
        Assert.Equal(0, data.Keys.Agrees);
    }

    // AC-4: an existing machine key is kept.
    [Fact]
    public async Task SetUpSensitive_KeepsAnExistingMachineKey()
    {
        data.Keys.CreateMachineKey();
        byte[] machine = data.Keys.MachinePublicKey()!;
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.SetUpSensitive(Password, data.RecoveryPath);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(1, data.Keys.MachineKeysCreated);
        Assert.Equal(machine, data.Keys.MachinePublicKey());
    }

    // AC-4: refused, with no file written and no key made or stored.
    [Theory]
    [InlineData("short", nameof(ActionReason.Invalid), "The password needs at least 12 characters.")]
    [InlineData("inside", nameof(ActionReason.Invalid), DesktopActions.RecoveryPathText)]
    [InlineData("empty path", nameof(ActionReason.Invalid), DesktopActions.RecoveryPathText)]
    [InlineData("exists", nameof(ActionReason.Exists), DesktopActions.RecoveryFileExistsText)]
    [InlineData("already set up", nameof(ActionReason.Exists), DesktopActions.RecoveryExistsText)]
    public async Task SetUpSensitive_Refused_WritesNothing(string scenario, string reason, string text)
    {
        data.WriteNote("Plan", "# Plan\n");
        string password = Password;
        string path = data.RecoveryPath;
        switch (scenario)
        {
            case "short":
                password = "elevenchars";
                break;
            case "inside":
                path = Path.Combine(data.Root, "sub", "recovery.pfx");
                break;
            case "empty path":
                path = " ";
                break;
            case "exists":
                File.WriteAllText(path, "mine");
                break;
            default:
                data.Keys.Dispose();
                data.Keys = FakeKeyStore.Ready();
                break;
        }

        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(Parent(), "Context/" + TagIndex.FileName);
        byte[]? recovery = data.Keys.RecoveryPublicKey();

        ActionResult result = await data.Actions!.SetUpSensitive(password, path);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(reason, result.Reason.ToString());
        Assert.Equal(text, result.Text);
        Assert.Equal(before, FileSnapshot.Take(Parent(), "Context/" + TagIndex.FileName));
        Assert.Equal(0, data.Keys.MachineKeysCreated);
        Assert.Equal(recovery, data.Keys.RecoveryPublicKey());
        Assert.Equal(11, "elevenchars".Length);
    }

    // AC-4: when the key store fails, no recovery file is left behind that protects nothing.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetUpSensitive_KeyStoreFails_LeavesNoFile(bool createFails)
    {
        data.Keys.CreateThrows = createFails;
        data.Keys.StoreThrows = !createFails;
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.SetUpSensitive(Password, data.RecoveryPath);

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal(DesktopActions.MachineKeyFailedText, result.Text);
        Assert.False(File.Exists(data.RecoveryPath));
        Assert.Null(data.Keys.RecoveryPublicKey());
    }

    // AC-5: opening asks for the private key exactly once; while the note is open it is not asked again.
    [Fact]
    public async Task ReadSensitive_AgreesOnce_AndKeepsTheNoteInTheSession()
    {
        await Sealed("Plan", "# Plan\r\n\r\n" + Marker + "\r\n");

        EditableNote? opened = await data.State!.ReadSensitive("Plan");

        Assert.NotNull(opened);
        Assert.Equal("# Plan\n\n" + Marker + "\n", opened.Text);
        Assert.True(opened.HadCrlf);
        Assert.EndsWith("Plan.ctxs", opened.Path, StringComparison.Ordinal);
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Same(opened, data.Session!.Note);
        Assert.Same(opened, await data.State.ReadSensitive("plan"));
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Null(await data.State.ReadForEdit("Plan"));
        AssertMarkerNowhere();
    }

    // AC-5: a refused or failing private-key call, or a file that doesn't open, gives nothing.
    [Theory]
    [InlineData("agree throws", 1)]
    [InlineData("changed file", 1)]
    [InlineData("not a sealed file", 0)]
    [InlineData("no machine key", 0)]
    [InlineData("missing", 0)]
    public async Task ReadSensitive_Fails_ReturnsNothing(string scenario, int agrees)
    {
        await Sealed("Plan", Marker);
        string path = Path.Combine(data.Root, "Plan.ctxs");
        string name = "Plan";
        switch (scenario)
        {
            case "agree throws":
                data.Keys.AgreeThrows = true;
                break;
            case "changed file":
                File.WriteAllText(path, File.ReadAllText(path).Replace("\"text\":\"", "\"text\":\"AAAA", StringComparison.Ordinal));
                break;
            case "not a sealed file":
                File.WriteAllText(path, "# plain text in a .ctxs file\n");
                break;
            case "no machine key":
                data.Keys.LoseMachineKey();
                break;
            default:
                name = "Gone";
                break;
        }

        EditableNote? opened = await data.State!.ReadSensitive(name);

        Assert.Null(opened);
        Assert.Null(data.Session!.Note);
        Assert.Equal(agrees, data.Keys.Agrees);
    }

    // AC-5: a save seals again with a new note key and no private-key call, and writes no plain file.
    [Fact]
    public async Task SaveNote_Sensitive_ReSealsWithoutAgree()
    {
        await Sealed("Plan", "# Plan\r\n");
        EditableNote opened = (await data.State!.ReadSensitive("Plan"))!;
        byte[] before = File.ReadAllBytes(opened.Path);

        ActionResult result = await data.Actions!.SaveNote(opened, "# Plan\n\n" + Marker + "\n");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Plan", result.Text);
        Assert.Equal("# Plan\n\n" + Marker + "\n", result.Note!.Text);
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Equal(["Plan.ctxs"], RootFiles());
        byte[] after = File.ReadAllBytes(opened.Path);
        Assert.Empty(SealedValues(before).Intersect(SealedValues(after)));
        Assert.Equal("# Plan\r\n\r\n" + Marker + "\r\n", SealedNote.Open("Plan", after, data.Keys.MachinePublicKey()!, data.Keys.Agree));
        Assert.True(SealedNote.HasKey(after, data.Keys.RecoveryPublicKey()!));
        Assert.Same(result.Note, data.Session!.Note);
        AssertPrivate(opened.Path);
        AssertMarkerNowhere();

        // The saved note saves again: the session holds the new version.
        ActionResult again = await data.Actions.SaveNote(result.Note, "second edit\n");
        Assert.Equal(ActionKind.Done, again.Kind);
        Assert.Equal(2, data.Keys.Agrees);
    }

    // AC-5: an unchanged text writes nothing.
    [Fact]
    public async Task SaveNote_Sensitive_Unchanged_WritesNothing()
    {
        await Sealed("Plan", "# Plan\n");
        EditableNote opened = (await data.State!.ReadSensitive("Plan"))!;
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.SaveNote(opened, "# Plan\n");

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (result.Kind, result.Reason));
        Assert.Equal(before, Files());
    }

    // AC-5: the changed-on-disk check of plain notes also holds for sealed ones.
    [Fact]
    public async Task SaveNote_Sensitive_ChangedOnDisk_Refused()
    {
        await Sealed("Plan", "Mine\n");
        EditableNote opened = (await data.State!.ReadSensitive("Plan"))!;
        data.WriteSensitive("Plan", "Theirs\n");
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.SaveNote(opened, "Mine, edited\n");

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(ActionReason.ChangedOnDisk, result.Reason);
        Assert.Equal("Plan changed on disk since you opened it. Nothing was saved.", result.Text);
        Assert.Equal(before, Files());
    }

    // AC-5 / AC-6: after a lock the text is gone, so a save is refused; a deleted note can't be saved either.
    [Fact]
    public async Task SaveNote_Sensitive_LockedOrGone_Refused()
    {
        await Sealed("Plan", "Mine\n");
        EditableNote opened = (await data.State!.ReadSensitive("Plan"))!;
        data.Session!.Lock();
        SortedDictionary<string, string> before = Files();

        ActionResult locked = await data.Actions!.SaveNote(opened, "edit\n");

        Assert.Equal((ActionKind.Refused, ActionReason.Locked, "Unlock Plan first."), (locked.Kind, locked.Reason, locked.Text));
        Assert.Equal(before, Files());

        File.Delete(opened.Path);
        ActionResult gone = await data.Actions.SaveNote(opened, "edit\n");
        Assert.Equal((ActionKind.Refused, ActionReason.Missing), (gone.Kind, gone.Reason));
        Assert.Empty(RootFiles());
    }

    // AC-6: after a lock, opening asks for the private key again.
    [Fact]
    public async Task Lock_ThenOpen_AgreesAgain()
    {
        await Sealed("Plan", "text\n");
        await data.State!.ReadSensitive("Plan");

        data.Session!.Lock();
        Assert.Null(data.Session.Note);
        EditableNote? again = await data.State.ReadSensitive("Plan");

        Assert.NotNull(again);
        Assert.Equal(2, data.Keys.Agrees);

        data.UtcNow = data.UtcNow.AddMinutes(5);
        Assert.True(data.Session.Tick());
        Assert.Null(data.Session.Note);
        await data.State.ReadSensitive("Plan");
        Assert.Equal(3, data.Keys.Agrees);
    }

    // AC-7: rename seals the open text under the new name; only <new>.ctxs is left.
    [Fact]
    public async Task RenameNote_Sensitive_Open_ReSealsUnderTheNewName()
    {
        await Sealed("Plan", "﻿# Plan\r\n" + Marker + "\r\n");
        await data.State!.ReadSensitive("Plan");

        ActionResult result = await data.Actions!.RenameNote("Plan", "Project");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Project", result.Text);
        Assert.Equal(["Project.ctxs"], RootFiles());
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Equal("Project", data.Session!.Note!.Name);
        byte[] file = File.ReadAllBytes(Path.Combine(data.Root, "Project.ctxs"));
        Assert.Equal("﻿# Plan\r\n" + Marker + "\r\n", SealedNote.Open("Project", file, data.Keys.MachinePublicKey()!, data.Keys.Agree));
        Assert.Throws<SealedNoteException>(() => SealedNote.Open("Plan", file, data.Keys.MachinePublicKey()!, data.Keys.Agree));
        Assert.Equal(["Project"], data.State.Snapshot!.Sensitive.Select(n => n.Name).ToArray());
        AssertPrivate(Path.Combine(data.Root, "Project.ctxs"));
        AssertMarkerNowhere();

        // The renamed note is still open: it saves without another private-key call.
        int agrees = data.Keys.Agrees;
        EditableNote open = (await data.State.ReadSensitive("Project"))!;
        ActionResult saved = await data.Actions.SaveNote(open, "edited\n");
        Assert.Equal(ActionKind.Done, saved.Kind);
        Assert.Equal(agrees, data.Keys.Agrees);
    }

    // AC-7: a locked sensitive note can't be renamed, and a taken or invalid name is refused; nothing changes.
    [Theory]
    [InlineData(false, "Project", nameof(ActionReason.Locked), "Unlock Plan first.")]
    [InlineData(true, "Other", nameof(ActionReason.Exists), "Note Other already exists.")]
    [InlineData(true, "SEALED", nameof(ActionReason.Exists), "Note SEALED already exists.")]
    [InlineData(true, "plan", nameof(ActionReason.Exists), "Note plan already exists.")]
    public async Task RenameNote_Sensitive_Refused_NothingChanges(bool open, string title, string reason, string text)
    {
        await Sealed("Plan", "text\n");
        data.WriteNote("Other", "other\n");
        data.WriteSensitive("Sealed", "sealed\n");
        await data.RefreshAsync();
        if (open)
        {
            await data.State!.ReadSensitive("Plan");
        }

        SortedDictionary<string, string> before = Files();
        int agrees = data.Keys.Agrees;

        ActionResult result = await data.Actions!.RenameNote("Plan", title);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(reason, result.Reason.ToString());
        Assert.Equal(text, result.Text);
        Assert.Equal(before, Files());
        Assert.Equal(agrees, data.Keys.Agrees);
    }

    // AC-7: a plain note can't take a sensitive note's name, by create or rename.
    [Fact]
    public async Task CreateAndRename_OntoASensitiveName_Refused()
    {
        await Sealed("Plan", "text\n");
        data.WriteNote("Other", "other\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult created = await data.Actions!.CreateNote("PLAN");
        ActionResult renamed = await data.Actions.RenameNote("Other", "Plan");

        Assert.Equal((ActionKind.Refused, ActionReason.Exists), (created.Kind, created.Reason));
        Assert.Equal((ActionKind.Refused, ActionReason.Exists), (renamed.Kind, renamed.Reason));
        Assert.Equal(before, Files());
    }

    // AC-7: delete removes the .ctxs, locked or open, without a private-key call; without a confirmation it stays.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteNote_Sensitive_RemovesTheSealedFile(bool open)
    {
        await Sealed("Plan", "text\n");
        data.WriteNote("Other", "other\n");
        await data.RefreshAsync();
        if (open)
        {
            await data.State!.ReadSensitive("Plan");
        }

        int agrees = data.Keys.Agrees;
        SortedDictionary<string, string> before = Files();

        ActionResult refused = await data.Actions!.DeleteNote("Plan", null);

        Assert.Equal((ActionKind.Refused, ActionReason.NotConfirmed), (refused.Kind, refused.Reason));
        Assert.Equal(before, Files());

        ActionResult result = await data.Actions.DeleteNote("Plan", new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Plan", result.Text);
        Assert.Equal(["Other.md"], RootFiles());
        Assert.Equal(agrees, data.Keys.Agrees);
        Assert.Null(data.Session!.Note);
        Assert.Empty(data.State!.Snapshot!.Sensitive);
    }

    // AC-7: a sensitive note is never handed to another app.
    [Fact]
    public async Task OpenExternally_Sensitive_Refused()
    {
        await Sealed("Plan", "text\n");

        ActionResult result = await data.Actions!.OpenExternally("Plan");

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal("Can't open Plan in another app.", result.Text);
        Assert.Empty(data.Launcher.Launched);
        Assert.Equal(0, data.Keys.Agrees);
    }

    // AC-7: make plain writes <name>.md and removes the .ctxs; it needs a confirmation and the open note.
    [Fact]
    public async Task MakePlain_WritesThePlainNote_AndDeletesTheSealedOne()
    {
        await Sealed("Plan", "# Plan\n\nText\n");
        SortedDictionary<string, string> before = Files();

        ActionResult locked = await data.Actions!.MakePlain("Plan", new Confirmation());
        Assert.Equal((ActionKind.Refused, ActionReason.Locked, "Unlock Plan first."), (locked.Kind, locked.Reason, locked.Text));
        await data.State!.ReadSensitive("Plan");
        ActionResult unconfirmed = await data.Actions.MakePlain("Plan", null);
        Assert.Equal((ActionKind.Refused, ActionReason.NotConfirmed), (unconfirmed.Kind, unconfirmed.Reason));
        ActionResult gone = await data.Actions.MakePlain("Gone", new Confirmation());
        Assert.Equal((ActionKind.Refused, ActionReason.Missing), (gone.Kind, gone.Reason));
        Assert.Equal(before, Files());

        ActionResult result = await data.Actions.MakePlain("Plan", new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Plan", result.Text);
        Assert.Equal(["Plan.md"], RootFiles());
        Assert.Equal("# Plan\n\nText\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Null(data.Session!.Note);
        Assert.Equal(["Plan"], data.State.Snapshot!.Notes.Select(n => n.Name).ToArray());
        Assert.Empty(data.State.Snapshot.Sensitive);
    }

    // AC-8: on a machine that lost its key, the recovery file gives every sensitive note a wrap for a new machine key.
    [Fact]
    public async Task RestoreFromRecovery_AddsAMachineWrapToEveryNoteItCanUnwrap()
    {
        data.WriteNote("Plan", "plan text\n");
        data.WriteNote("Second", "second text\n");
        await data.RefreshAsync();
        Assert.Equal(ActionKind.Done, (await data.Actions!.SetUpSensitive(Password, data.RecoveryPath)).Kind);
        await data.Actions.MarkSensitive("Plan", new Confirmation());
        await data.Actions.MarkSensitive("Second", new Confirmation());
        using (FakeKeyStore foreign = FakeKeyStore.Ready())
        {
            File.WriteAllBytes(
                Path.Combine(data.Root, "Foreign.ctxs"),
                SealedNote.Seal("Foreign", "foreign", [new SealedRecipient(SealedRecipient.Machine, foreign.MachinePublicKey()!)]));
        }

        File.WriteAllText(Path.Combine(data.Root, "Broken.ctxs"), "not sealed");
        byte[] foreignBytes = File.ReadAllBytes(Path.Combine(data.Root, "Foreign.ctxs"));
        byte[] recoveryPublic = data.Keys.RecoveryPublicKey()!;
        byte[] recoveryFile = File.ReadAllBytes(data.RecoveryPath);
        data.Keys.LoseMachineKey();
        data.Keys.LoseRecoveryKey();
        Assert.Null(await data.State!.ReadSensitive("Plan"));

        ActionResult result = await data.Actions.RestoreFromRecovery(data.RecoveryPath, Password);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Restored 2 of 4 notes.", result.Text);
        Assert.Equal(2, data.Keys.MachineKeysCreated);
        Assert.Equal(recoveryPublic, data.Keys.RecoveryPublicKey());
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal("plan text\n", (await data.State.ReadSensitive("Plan"))!.Text);
        data.Session!.Lock();
        Assert.Equal("second text\n", (await data.State.ReadSensitive("Second"))!.Text);
        Assert.Equal(foreignBytes, File.ReadAllBytes(Path.Combine(data.Root, "Foreign.ctxs")));
        Assert.Equal("not sealed", File.ReadAllText(Path.Combine(data.Root, "Broken.ctxs")));
        Assert.Equal(recoveryFile, File.ReadAllBytes(data.RecoveryPath));
        Assert.Equal(["Broken.ctxs", "Foreign.ctxs", "Plan.ctxs", "Second.ctxs"], RootFiles());
        AssertPrivate(Path.Combine(data.Root, "Plan.ctxs"));
        Assert.DoesNotContain(Password, result.ToString(), StringComparison.Ordinal);

        // A second restore finds the wraps and rewrites nothing.
        SortedDictionary<string, string> before = Files();
        ActionResult again = await data.Actions.RestoreFromRecovery(data.RecoveryPath, Password);
        Assert.Equal("Restored 2 of 4 notes.", again.Text);
        Assert.Equal(before, Files());
        Assert.Equal(2, data.Keys.MachineKeysCreated);
    }

    // AC-8: a wrong password or file fails with the fixed text and changes no file and no key.
    [Theory]
    [InlineData("wrong password")]
    [InlineData("not a recovery file")]
    [InlineData("missing file")]
    [InlineData("too large")]
    public async Task RestoreFromRecovery_WrongPasswordOrFile_ChangesNothing(string scenario)
    {
        data.WriteNote("Plan", "plan text\n");
        await data.RefreshAsync();
        await data.Actions!.SetUpSensitive(Password, data.RecoveryPath);
        await data.Actions.MarkSensitive("Plan", new Confirmation());
        data.Keys.LoseMachineKey();
        string path = data.RecoveryPath;
        string password = Password;
        switch (scenario)
        {
            case "wrong password":
                password = "another long password";
                break;
            case "not a recovery file":
                path = Path.Combine(Parent(), "other.pfx");
                File.WriteAllText(path, "not a recovery file");
                break;
            case "missing file":
                path = Path.Combine(Parent(), "nothing.pfx");
                break;
            default:
                path = Path.Combine(Parent(), "large.pfx");
                File.WriteAllBytes(path, new byte[RecoveryFile.MaxFileBytes + 1]);
                break;
        }

        SortedDictionary<string, string> before = FileSnapshot.Take(Parent(), "Context/" + TagIndex.FileName);

        ActionResult result = await data.Actions.RestoreFromRecovery(path, password);

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal("Can't read that recovery file.", result.Text);
        Assert.Equal(before, FileSnapshot.Take(Parent(), "Context/" + TagIndex.FileName));
        Assert.Equal(1, data.Keys.MachineKeysCreated);
        Assert.Null(data.Keys.MachinePublicKey());
        Assert.DoesNotContain(password, result.ToString(), StringComparison.Ordinal);
    }

    // AC-5: the watcher refreshes when a sealed note changes.
    [Theory]
    [InlineData("Plan.ctxs", true)]
    [InlineData("Plan.CTXS", true)]
    [InlineData("sub/Plan.ctxs", false)]
    [InlineData(".Plan.ctxs.0123.tmp", false)]
    public void DataWatcher_SeesSealedNotes(string relative, bool relevant)
    {
        Assert.Equal(relevant, DataWatcher.IsRelevant(data.Root, Path.Combine(data.Root, relative.Replace('/', Path.DirectorySeparatorChar))));
    }

    // Seals name for the fixture's keys (made ready here) and refreshes.
    private async Task Sealed(string name, string text)
    {
        data.Keys.Dispose();
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive(name, text);
        await data.RefreshAsync();
    }

    private string Parent() => Path.GetDirectoryName(data.Root)!;

    private string[] RootFiles() => FileSnapshot.Take(data.Root, TagIndex.FileName).Keys.ToArray();

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName);

    // Every base64 value of a sealed file.
    private static List<string> SealedValues(byte[] file)
    {
        System.Text.Json.Nodes.JsonObject root = System.Text.Json.Nodes.JsonNode.Parse(Encoding.UTF8.GetString(file))!.AsObject();
        List<string> values = [root["nonce"]!.GetValue<string>(), root["tag"]!.GetValue<string>(), root["text"]!.GetValue<string>()];
        foreach (System.Text.Json.Nodes.JsonNode? key in root["keys"]!.AsArray())
        {
            values.AddRange(new[] { "epk", "nonce", "tag", "key" }.Select(p => key![p]!.GetValue<string>()));
        }

        return values;
    }

    // Off Windows a sealed note and the recovery file are readable by the user only.
    private static void AssertPrivate(string path)
    {
        Assert.True(File.Exists(path));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }

    // The marker is in no file of the fixture's folder (data folder, index, recovery file) and in no file this
    // process could have left in the temp folder.
    private void AssertMarkerNowhere()
    {
        byte[] marker = Encoding.UTF8.GetBytes(Marker);
        List<string> files = [.. Directory.EnumerateFiles(Parent(), "*", SearchOption.AllDirectories)];
        Assert.NotEmpty(files);
        foreach (string file in Directory.EnumerateFiles(Path.GetTempPath()))
        {
            try
            {
                FileInfo info = new FileInfo(file);
                // A pipe or socket has no length; reading one would wait forever.
                if (info.Length > 0 && info.Length <= 1024 * 1024 && info.LastWriteTimeUtc > DateTime.UtcNow.AddMinutes(-5))
                {
                    files.Add(file);
                }
            }
            catch (IOException)
            {
                // Someone else's file went away.
            }
        }

        foreach (string file in files)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Assert.False(file.StartsWith(Parent(), StringComparison.Ordinal), file);
                continue;
            }

            Assert.True(bytes.AsSpan().IndexOf(marker) < 0, "The marker is in " + file);
        }
    }
}
