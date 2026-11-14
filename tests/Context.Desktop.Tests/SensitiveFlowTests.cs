using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 136 AC-5 to AC-7: sensitive notes through Main, with the page's editor mocked and a key store in memory.
public sealed class SensitiveFlowTests : IDisposable
{
    // 147: opening a sensitive note runs real ECDH and AES-GCM on Task.Run, which can take over bUnit's 1 s default on a
    // busy runner. Every wait passes this bounded timeout; a real failure still fails within it.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public SensitiveFlowTests()
    {
        data.Keys = FakeKeyStore.Ready();
    }

    public void Dispose() => data.Dispose();

    // AC-7: sensitive notes are in the list with the plain ones, sorted by name, with a text marker.
    [Fact]
    public async Task NotesPanel_ListsSensitiveNotesSortedWithALockedMarker()
    {
        data.WriteNote("Beta", "b");
        data.WriteSensitive("alpha", "a");
        data.WriteSensitive("Gamma", "c");

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        Assert.Equal(["alpha", "Beta", "Gamma"], cut.FindAll("li.note button.name").Select(b => b.TextContent).ToArray());
        Assert.Equal(
            ["locked", string.Empty, "locked"],
            cut.FindAll("li.note").Select(l => l.QuerySelector(".locked")?.TextContent ?? string.Empty).ToArray());
        Assert.Equal(0, data.Keys.Agrees);
    }

    // AC-5: opening asks for the private key once and mounts the unsealed text.
    [Fact]
    public async Task Open_Sensitive_AgreesOnce_AndMountsTheText()
    {
        data.WriteSensitive("Plan", "# Plan\r\n\r\nSecret\r\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await Open(cut, js, "Plan");

        Assert.Equal("# Plan\n\nSecret\n", js.MountedText());
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Empty(cut.FindAll(".unreadable"));
    }

    // AC-7: a sensitive note's toolbar has Make plain and Lock, and nothing that hands the file to another app.
    [Fact]
    public async Task Toolbar_Sensitive_HasNoOpenInDefaultEditor()
    {
        data.WriteSensitive("Plan", "text\n");
        data.WriteNote("Other", "other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await Open(cut, js, "Plan");

        Assert.Equal(["Rename", "Delete", "Make plain", "Lock"], ToolbarButtons(cut));
        Assert.Empty(cut.FindAll("button.open-external"));
        Assert.All(cut.FindAll(".note-toolbar button"), b => Assert.Equal("button", b.GetAttribute("type")));

        await Open(cut, js, "Other");
        Assert.Equal(["Rename", "Delete", "Open in default editor", "Export", "Mark sensitive"], ToolbarButtons(cut));
    }

    // AC-5: a refused OS prompt, a lost key or a changed file shows one red line and no editor.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Open_Sensitive_CannotUnlock_ShowsRedLine(bool refused)
    {
        data.WriteSensitive("Plan", "Secret\n");
        if (refused)
        {
            data.Keys.AgreeThrows = true;
        }
        else
        {
            data.Keys.LoseMachineKey();
        }

        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await NameButton(cut, "Plan").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Can't unlock Plan.", cut.Find(".unreadable").TextContent), Wait);
        Assert.Contains("red", cut.Find(".unreadable").ClassList);
        Assert.Empty(js.Calls("ContextEditor.mount"));
        Assert.True(cut.Find("button.save").HasAttribute("disabled"));
        Assert.DoesNotContain("Secret", cut.Markup, StringComparison.Ordinal);
    }

    // 143 AC-6: a sensitive note that unlocks but whose page editor fails to start says so, not "Can't unlock".
    [Fact]
    public async Task Open_Sensitive_MountThrows_ShowsEditorFailedLine()
    {
        data.WriteSensitive("Plan", "Secret\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.Fail("ContextEditor.mount", new Microsoft.JSInterop.JSException("boom-143"));
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await NameButton(cut, "Plan").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(
            "The editor couldn't start. Plan was not changed.", Assert.Single(cut.FindAll(".editor-failed")).TextContent), Wait);
        Assert.Contains("red", cut.Find(".editor-failed").ClassList);
        Assert.Equal("alert", cut.Find(".editor-failed").GetAttribute("role"));
        Assert.Empty(cut.FindAll(".unreadable"));
        Assert.DoesNotContain("Can't unlock", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("boom-143", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(1, data.Keys.Agrees);
        Assert.True(cut.Find("button.save").HasAttribute("disabled"));
    }

    // AC-5: saving in the editor seals again without a private-key call.
    [Fact]
    public async Task Save_Sensitive_ReSeals_WithoutAgree()
    {
        data.WriteSensitive("Plan", "old\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("new text\n");

        await cut.Find("button.save").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Saved Plan.", cut.Find(".note-editor .status").TextContent), Wait);
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Equal("new text\n", Unsealed("Plan"));
        Assert.Equal(["Plan.ctxs"], RootFiles());
    }

    // AC-6: the Lock button unmounts the editor; opening again asks for the private key again.
    [Fact]
    public async Task Lock_Button_UnmountsTheEditor_AndReopenAgreesAgain()
    {
        data.WriteSensitive("Plan", "Secret\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await cut.Find("button.lock").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.destroy")), Wait);
        Assert.Empty(cut.FindAll(".note-editor"));
        Assert.Single(cut.FindAll(".current-region .current"));
        Assert.Equal("Locked Plan.", cut.Find(".main-status").TextContent);
        Assert.Contains("grey", cut.Find(".main-status").ClassList);
        Assert.Null(data.Session!.Note);
        Assert.Equal(1, data.Keys.Agrees);

        await Open(cut, js, "Plan");
        Assert.Equal(2, data.Keys.Agrees);
        Assert.Equal("Secret\n", js.MountedText());
    }

    // AC-6: five minutes without input lock the note at the next tick; a tick before that does nothing.
    [Fact]
    public async Task Tick_AfterFiveMinutesIdle_Locks()
    {
        data.WriteSensitive("Plan", "Secret\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        data.UtcNow = data.UtcNow.AddMinutes(5).AddSeconds(-1);
        Assert.False(data.Session!.Tick());
        Assert.Single(cut.FindAll(".note-editor"));

        data.UtcNow = data.UtcNow.AddSeconds(1);
        Assert.True(data.Session.Tick());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".note-editor")), Wait);
        Assert.Single(js.Calls("ContextEditor.destroy"));
        Assert.Equal("Locked Plan.", cut.Find(".main-status").TextContent);
        Assert.Null(data.Session.Note);
        await Open(cut, js, "Plan");
        Assert.Equal(2, data.Keys.Agrees);
    }

    // AC-6: typing in the editor counts as use and moves the idle time on.
    [Fact]
    public async Task Input_InTheEditor_RestartsTheIdleTime()
    {
        data.WriteSensitive("Plan", "Secret\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        data.UtcNow = data.UtcNow.AddMinutes(4);
        await cut.Find(".note-editor").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "x" });
        data.UtcNow = data.UtcNow.AddMinutes(4);

        Assert.False(data.Session!.Tick());
        Assert.Single(cut.FindAll(".note-editor"));
    }

    // AC-6: an idle lock saves a note with unsaved changes first.
    [Fact]
    public async Task Tick_Dirty_SavesThenLocks()
    {
        data.WriteSensitive("Plan", "old\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("typed before idle\n");
        data.UtcNow = data.UtcNow.AddMinutes(5);

        Assert.True(data.Session!.Tick());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".note-editor")), Wait);
        Assert.Equal("Locked Plan.", cut.Find(".main-status").TextContent);
        Assert.Null(data.Session.Note);
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Equal("typed before idle\n", Unsealed("Plan"));
        Assert.Equal(["Plan.ctxs"], RootFiles());
    }

    // AC-6: when that save is refused, the note stays open with the refusal in red and nothing is overwritten.
    [Fact]
    public async Task Tick_Dirty_SaveRefused_KeepsTheNoteOpen()
    {
        data.WriteSensitive("Plan", "old\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("typed before idle\n");
        data.WriteSensitive("Plan", "changed elsewhere\n");
        data.UtcNow = data.UtcNow.AddMinutes(5);

        data.Session!.Tick();

        cut.WaitForAssertion(() => Assert.Equal(
            "Plan changed on disk since you opened it. Nothing was saved.", cut.Find(".note-editor .status").TextContent), Wait);
        Assert.Contains("red", cut.Find(".note-editor .status").ClassList);
        Assert.Single(cut.FindAll(".note-editor"));
        Assert.Empty(js.Calls("ContextEditor.destroy"));
        Assert.NotNull(data.Session.Note);
        Assert.Equal("changed elsewhere\n", Unsealed("Plan"));
    }

    // AC-6: closing the window locks the session, which unmounts the editor.
    [Fact]
    public async Task SessionLock_AsOnWindowClose_UnmountsTheEditor()
    {
        data.WriteSensitive("Plan", "Secret\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        data.Session!.Lock();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".note-editor")), Wait);
        Assert.Single(js.Calls("ContextEditor.destroy"));
        Assert.Null(data.Session.Note);
        Assert.DoesNotContain("Secret", cut.Markup, StringComparison.Ordinal);
    }

    // AC-6: opening another note closes the sensitive one, so coming back asks for the private key again.
    [Fact]
    public async Task OpenOther_ClosesTheSensitiveNote()
    {
        data.WriteSensitive("Plan", "Secret\n");
        data.WriteNote("Other", "other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await Open(cut, js, "Other");

        Assert.Null(data.Session!.Note);
        await Open(cut, js, "Plan");
        Assert.Equal(2, data.Keys.Agrees);
    }

    // AC-3: Mark sensitive asks first; Cancel changes nothing, the confirm button seals the note and closes it.
    [Fact]
    public async Task MarkSensitive_AsksFirst_ThenSealsAndCloses()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await cut.Find("button.mark-sensitive").ClickAsync(new MouseEventArgs());

        IElement dialog = cut.Find("[role=dialog]");
        Assert.Equal(
            "Mark Plan sensitive? It will open only in this app, with this machine's key or the recovery file.",
            dialog.QuerySelector(".question")!.TextContent);
        Assert.Equal("Mark sensitive", dialog.QuerySelector("button.confirm")!.TextContent);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(["Plan.md"], RootFiles());

        await cut.Find("button.mark-sensitive").ClickAsync(new MouseEventArgs());
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Marked Plan sensitive.", cut.Find(".main-status").TextContent), Wait);
        Assert.Contains("accent", cut.Find(".main-status").ClassList);
        Assert.Equal(["Plan.ctxs"], RootFiles());
        Assert.Empty(cut.FindAll(".note-editor"));
        Assert.Equal("locked", cut.Find("li.note .locked").TextContent);
        Assert.Equal(0, data.Keys.Agrees);
    }

    // AC-3: without a recovery key the refusal is shown and the note stays plain and open.
    [Fact]
    public async Task MarkSensitive_NoRecovery_ShowsSetUpRecoveryFirst()
    {
        data.Keys.LoseRecoveryKey();
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await cut.Find("button.mark-sensitive").ClickAsync(new MouseEventArgs());
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Set up recovery first.", cut.Find(".main-status").TextContent), Wait);
        Assert.Contains("red", cut.Find(".main-status").ClassList);
        Assert.Equal(["Plan.md"], RootFiles());
        Assert.Single(cut.FindAll(".note-editor"));
    }

    // AC-3 / AC-7: with unsaved changes neither question is asked.
    [Theory]
    [InlineData(false, "button.mark-sensitive")]
    [InlineData(true, "button.make-plain")]
    public async Task MarkAndMakePlain_Dirty_AskToSaveFirst(bool sensitive, string button)
    {
        if (sensitive)
        {
            data.WriteSensitive("Plan", "text\n");
        }
        else
        {
            data.WriteNote("Plan", "text\n");
        }

        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("unsaved\n");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await cut.Find(button).ClickAsync(new MouseEventArgs());

        Assert.Equal("Save Plan first.", cut.Find(".main-status").TextContent);
        Assert.Contains("red", cut.Find(".main-status").ClassList);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // AC-7: Make plain asks first, then the note is a plain note again, open in the editor.
    [Fact]
    public async Task MakePlain_AsksFirst_ThenWritesThePlainNote()
    {
        data.WriteSensitive("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await cut.Find("button.make-plain").ClickAsync(new MouseEventArgs());

        Assert.Equal(
            "Make Plan plain? It will be stored as readable text again.", cut.Find("[role=dialog] .question").TextContent);
        Assert.Equal("Make plain", cut.Find("[role=dialog] button.confirm").TextContent);
        Assert.Equal(["Plan.ctxs"], RootFiles());
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Made Plan plain.", cut.Find(".main-status").TextContent), Wait);
        Assert.Equal(["Plan.md"], RootFiles());
        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Empty(cut.FindAll("li.note .locked"));
        cut.WaitForAssertion(() => Assert.Equal(["Rename", "Delete", "Open in default editor", "Export", "Mark sensitive"], ToolbarButtons(cut)), Wait);
        Assert.Equal(1, data.Keys.Agrees);
    }

    // AC-7: renaming the open sensitive note leaves only <new>.ctxs, still open, without another private-key call.
    [Fact]
    public async Task Rename_Sensitive_ReSealsUnderTheNewName()
    {
        data.WriteSensitive("Plan", "Secret\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await cut.Find("button.rename").ClickAsync(new MouseEventArgs());
        await cut.Find("input.rename-title").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "Project" });
        await cut.Find("button.save-name").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Project", cut.Find(".note-title").TextContent), Wait);
        Assert.Equal(["Project.ctxs"], RootFiles());
        Assert.Equal(["Rename", "Delete", "Make plain", "Lock"], ToolbarButtons(cut));
        cut.WaitForAssertion(() => Assert.Equal(2, js.Calls("ContextEditor.mount").Count), Wait);
        Assert.Equal("Secret\n", js.MountedText());
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Equal("Secret\n", Unsealed("Project"));
    }

    // AC-7: deleting the open sensitive note asks, then removes the .ctxs and the note leaves the list.
    [Fact]
    public async Task Delete_Sensitive_RemovesTheSealedFile()
    {
        data.WriteSensitive("Plan", "Secret\n");
        data.WriteNote("Other", "other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await cut.Find("button.delete").ClickAsync(new MouseEventArgs());
        Assert.Equal(["Other.md", "Plan.ctxs"], RootFiles());
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(["Other.md"], RootFiles()), Wait);
        cut.WaitForAssertion(() => Assert.Equal(["Other"], cut.FindAll("li.note button.name").Select(b => b.TextContent).ToArray()), Wait);
        Assert.Empty(cut.FindAll(".note-editor"));
        Assert.Equal(1, data.Keys.Agrees);
    }

    // AC-10: where the OS has no key store, nothing about sensitive notes is offered.
    [Fact]
    public async Task Unsupported_OffersNothingSensitive()
    {
        data.Keys.Supported = false;
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        Assert.Equal(["Rename", "Delete", "Open in default editor", "Export"], ToolbarButtons(cut));
        Assert.Empty(cut.FindAll(".sensitive-entry"));
        Assert.Empty(cut.FindAll("button.sensitive-setup-open"));
        Assert.Empty(cut.FindAll("button.sensitive-restore-open"));
    }

    // 139 AC-8 / AC-9: a sensitive note has no Export button, and /export refuses it without a dialog or a key call.
    [Fact]
    public async Task Export_Sensitive_NoButton_PaletteRefused()
    {
        data.WriteSensitive("Plan", "Secret\n");
        data.Dialog.ExportPath = data.Outside("Copy.md");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        int agrees = data.Keys.Agrees;

        Assert.Empty(cut.FindAll(".note-toolbar button.export"));
        Assert.DoesNotContain("Export", ToolbarButtons(cut));

        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "/export" });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Plan is sensitive. Make it plain to export it.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Equal(0, data.Dialog.ExportSaves);
        Assert.Equal(agrees, data.Keys.Agrees);
        Assert.False(File.Exists(data.Outside("Copy.md")));
    }

    private static string[] ToolbarButtons(IRenderedComponent<Main> cut) =>
        cut.FindAll(".note-toolbar button").Select(b => b.TextContent).ToArray();

    private string[] RootFiles() => FileSnapshot.Take(data.Root, TagIndex.FileName).Keys.ToArray();

    // The note's text, opened with the machine key outside the app's session.
    private string Unsealed(string name) => SealedNote.Open(
        name, File.ReadAllBytes(Path.Combine(data.Root, name + ".ctxs")), data.Keys.MachinePublicKey()!, data.Keys.Agree);

    private static IElement NameButton(IRenderedComponent<Main> cut, string name) =>
        cut.FindAll(".notes-region button.name").Single(b => b.TextContent == name);

    private static async Task Open(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NameButton(cut, name).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.save").HasAttribute("disabled")), Wait);
    }
}
