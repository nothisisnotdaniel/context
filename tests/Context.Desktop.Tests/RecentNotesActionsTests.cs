using System.Text;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 240: the sidebar's recently opened notes through Main and DesktopActions, against real files in the fixture's own
// temp folder, with the fixture's clocks.
public sealed class RecentNotesActionsTests : IDisposable
{
    // Opening a note, recording it and writing recent.json run on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-2: every route (palette /open, a linked note, Today, the sidebar) puts the note first; a listed note moves
    // to the top and appears once.
    [Fact]
    public async Task Open_AnyRoute_PutsItFirst_Once()
    {
        data.WriteNote("Plan", "# Plan\n\n[[Other]]\n");
        data.WriteNote("Other", "# Other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.Equal("No recently opened notes.", cut.Find(".recent-empty").TextContent);

        await Open(cut, js, "Plan");
        WaitListed(cut, "Plan");

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".links-panel button.name")), Wait);
        await cut.Find(".links-panel button.name").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Other", cut.Find(".note-title").TextContent), Wait);
        WaitListed(cut, "Other", "Plan");

        await cut.Find("button.today-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("2026-10-29", cut.Find(".note-title").TextContent), Wait);
        WaitListed(cut, "2026-10-29", "Other", "Plan");

        await cut.Find("ul.recent-notes button.name[title='Plan']").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Plan", cut.Find(".note-title").TextContent), Wait);
        WaitListed(cut, "Plan", "2026-10-29", "Other");
        Assert.Equal("page", cut.Find("ul.recent-notes button.name[title='Plan']").GetAttribute("aria-current"));
    }

    // AC-2: an 11th distinct note pushes the oldest one out.
    [Fact]
    public async Task Open_Eleventh_PushesTheOldestOut()
    {
        string[] names = Enumerable.Range(1, 11).Select(i => $"N{i:00}").ToArray();
        foreach (string name in names)
        {
            data.WriteNote(name, "x\n");
        }

        await data.RefreshAsync();
        foreach (string name in names[..10])
        {
            await data.Actions!.RecordOpened(name).WaitAsync(Wait);
        }

        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitListed(cut, names[..10].Reverse().ToArray());

        await Open(cut, js, "N11");

        WaitListed(cut, names[1..].Reverse().ToArray());
        data.WaitForRecentWrites();
        Assert.Equal(names[1..].Reverse().ToArray(), new RecentNotesStore(data.Root).Read());
    }

    // AC-4: renaming, then moving, the open note updates it and its subpages in place; it does not jump to the top.
    [Fact]
    public async Task RenameThenMove_UpdatesEntriesInPlace()
    {
        data.WriteNote("A", "# A\n");
        data.WriteFile(Path.Combine("A", "Sub.md"), "# Sub\n");
        data.WriteNote("C", "# C\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "A/Sub");
        await Open(cut, js, "C");
        await Open(cut, js, "A");
        WaitListed(cut, "A", "C", "A/Sub");

        await MainTests.ChooseAsync(cut, "rename");
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "D" });
        await cut.Find(".rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Equal("D", cut.Find(".note-title").TextContent), Wait);
        WaitListed(cut, "D", "C", "D/Sub");
        data.WaitForRecentWrites();
        Assert.Equal(["D", "C", "D/Sub"], new RecentNotesStore(data.Root).Read());

        await MainTests.ChooseAsync(cut, "move");
        await cut.Find(".move-parent").InputAsync(new ChangeEventArgs { Value = "C" });
        await cut.Find(".move-parent").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Equal("Moved to C/D", cut.Find(".main-status").TextContent), Wait);
        WaitListed(cut, "C/D", "C", "C/D/Sub");
        Assert.Equal("page", cut.Find("ul.recent-notes button.name[title='C/D']").GetAttribute("aria-current"));
        data.WaitForRecentWrites();
        Assert.Equal(["C/D", "C", "C/D/Sub"], new RecentNotesStore(data.Root).Read());
    }

    // AC-4: the reopen after renaming or moving the open note records nothing, so an open note that is not first
    // keeps its place.
    [Fact]
    public async Task RenameOrMove_OpenNoteNotFirst_KeepsItsPlace()
    {
        data.WriteNote("A", "# A\n");
        data.WriteNote("C", "# C\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "A");
        await data.Actions!.RecordOpened("C").WaitAsync(Wait);
        WaitListed(cut, "C", "A");

        await MainTests.ChooseAsync(cut, "rename");
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "D" });
        await cut.Find(".rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Equal("D", cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        WaitListed(cut, "C", "D");

        await MainTests.ChooseAsync(cut, "move");
        await cut.Find(".move-parent").InputAsync(new ChangeEventArgs { Value = "C" });
        await cut.Find(".move-parent").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Equal("Moved to C/D", cut.Find(".main-status").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        WaitListed(cut, "C", "C/D");
        Assert.Equal(["C", "C/D"], data.State!.Recent);
    }

    // AC-4: deleting a listed note removes it and its subpages.
    [Fact]
    public async Task Delete_RemovesTheNoteAndItsSubpages()
    {
        data.WriteNote("A", "# A\n");
        data.WriteFile(Path.Combine("A", "Sub.md"), "# Sub\n");
        data.WriteNote("B", "# B\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "A/Sub");
        await Open(cut, js, "B");
        await Open(cut, js, "A");
        WaitListed(cut, "A", "B", "A/Sub");

        await MainTests.ChooseAsync(cut, "delete");
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.False(File.Exists(Path.Combine(data.Root, "A.md"))), Wait);
        WaitListed(cut, "B");
        data.WaitForRecentWrites();
        Assert.Equal(["B"], new RecentNotesStore(data.Root).Read());
    }

    // AC-4: a note deleted or renamed outside the app is left out of the list.
    [Fact]
    public async Task ChangedOutsideTheApp_LeftOut()
    {
        data.WriteNote("A", "# A\n");
        data.WriteNote("B", "# B\n");
        data.WriteNote("C", "# C\n");
        data.WriteRecent("C", "B", "A");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitListed(cut, "C", "B", "A");

        File.Delete(Path.Combine(data.Root, "A.md"));
        File.Move(Path.Combine(data.Root, "B.md"), Path.Combine(data.Root, "E.md"));
        await data.State!.Refresh();

        WaitListed(cut, "C");
    }

    // AC-5: reopened over the same root, the earlier order comes back from recent.json, which has the stated format
    // (UTF-8 without BOM, non-ASCII as it is, newest first, one line).
    [Fact]
    public async Task Reopen_RestoresTheOrder_FromTheStatedFile()
    {
        data.WriteNote("Café", "# Café\n");
        data.WriteNote("B", "# B\n");
        data.WriteFile(Path.Combine("B", "Sub.md"), "# Sub\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "B/Sub");
        await Open(cut, js, "Café");
        await Open(cut, js, "B");
        WaitListed(cut, "B", "Café", "B/Sub");
        byte[] expected = Encoding.UTF8.GetBytes("{\"recent\":[\"B\",\"Café\",\"B/Sub\"]}\n");
        string file = Path.Combine(data.Root, RecentNotesStore.FileName);
        data.WaitForRecentWrites();
        Assert.Equal(expected, File.ReadAllBytes(file));
        cut.Dispose();

        using DesktopFixture again = new DesktopFixture(data);
        _ = new EditorJs(again.Bunit);
        IRenderedComponent<Main> reopened = await again.RenderAsync<Main>();

        WaitListed(reopened, "B", "Café", "B/Sub");
        Assert.Equal(expected, File.ReadAllBytes(file));
    }

    // AC-6: a missing or broken recent.json shows the grey empty state and no error; the next open writes a valid file.
    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("{\"recent\":[\"Plan\",\"Plan\"]}")]
    [InlineData("{\"recent\":[\"Plan\"],\"more\":1}")]
    [InlineData("{\"recent\":[\"a\\\\b\"]}")]
    public async Task MissingOrBroken_EmptyState_NextOpenWritesAValidFile(string? json)
    {
        data.WriteNote("Plan", "# Plan\n");
        if (json is not null)
        {
            data.WriteFile(RecentNotesStore.FileName, json);
        }

        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        IElement line = cut.Find(".notes-region p.recent-empty");
        Assert.Equal("No recently opened notes.", line.TextContent);
        Assert.True(line.ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll(".notes-region .red"));

        await Open(cut, js, "Plan");

        WaitListed(cut, "Plan");
        data.WaitForRecentWrites();
        Assert.Equal("{\"recent\":[\"Plan\"]}\n", File.ReadAllText(Path.Combine(data.Root, RecentNotesStore.FileName)));
    }

    // AC-7: recording an open is not refused while another action is Busy; the note is listed first meanwhile.
    [Fact]
    public async Task Record_WhileAnotherActionIsBusy_StillListed()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(Wait);
        };

        Task<ActionResult> busy = data.Actions.CaptureText("held");
        Assert.True(entered.Wait(Wait));
        try
        {
            await NoteOpener.OpenAsync(cut, "Plan");
            WaitListed(cut, "Plan");
            data.WaitForRecentWrites();
            Assert.Equal(["Plan"], new RecentNotesStore(data.Root).Read());
            Assert.False(busy.IsCompleted);
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(ActionKind.Done, (await busy.WaitAsync(Wait)).Kind);
    }

    // AC-7: recording never refreshes (no rebuild, no notes-folder rescan), raises Changed and writes recent.json;
    // a name that could not name a note is ignored.
    [Fact]
    public async Task Record_NoRefresh_RaisesChanged_Writes()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        int builds = data.State!.Builds;
        int changed = 0;
        data.State.Changed += () => Interlocked.Increment(ref changed);

        await data.Actions!.RecordOpened("Plan").WaitAsync(Wait);
        await data.Actions.RecordOpened("a\\b").WaitAsync(Wait);
        await data.Actions.RecordOpened(string.Empty).WaitAsync(Wait);

        Assert.Equal(builds, data.State.Builds);
        Assert.Equal(1, Volatile.Read(ref changed));
        Assert.Equal(["Plan"], data.State.Recent);
        Assert.Equal(["Plan"], new RecentNotesStore(data.Root).Read());
    }

    // 246 AC-1: a held write is counted and has not written yet; once the gate is released the count reaches 0 and
    // recent.json lists the note first.
    [Fact]
    public async Task Record_PendingWrites_CountedUntilWritten()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        Assert.Equal(0, data.Actions!.PendingRecentWrites);
        string file = Path.Combine(data.Root, RecentNotesStore.FileName);

        Task recorded;
        using (data.HoldRecentWrites())
        {
            recorded = data.Actions.RecordOpened("Plan");

            Assert.Equal(1, data.Actions.PendingRecentWrites);
            Assert.False(File.Exists(file));
        }

        await recorded.WaitAsync(Wait);
        Assert.Equal(0, data.Actions.PendingRecentWrites);
        Assert.Equal("Plan", new RecentNotesStore(data.Root).Read()[0]);
    }

    // 246 AC-2: an invalid name is never counted; a write that fails (the root is gone) still brings the count back
    // to 0, and nothing is thrown.
    [Fact]
    public async Task Record_PendingWrites_InvalidOrFailed_BackToZero()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();

        await data.Actions!.RecordOpened("a\\b").WaitAsync(Wait);
        Assert.Equal(0, data.Actions.PendingRecentWrites);
        await data.Actions.RecordOpened(string.Empty).WaitAsync(Wait);
        Assert.Equal(0, data.Actions.PendingRecentWrites);

        Task recorded;
        using (data.HoldRecentWrites())
        {
            recorded = data.Actions.RecordOpened("Plan");
            Assert.Equal(1, data.Actions.PendingRecentWrites);
            Directory.Delete(data.Root, recursive: true);
        }

        await recorded.WaitAsync(Wait);
        Assert.Equal(0, data.Actions.PendingRecentWrites);
        Assert.False(Directory.Exists(data.Root));
        Assert.Equal(["Plan"], data.State!.Recent);
    }

    // AC-7: with a read-only root the write fails; nothing is thrown and the sidebar still lists the note first.
    [Fact]
    public async Task Record_WriteFails_SidebarUpdated_NothingThrown()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix permissions only; Windows ACLs would need their own setup.
        }

        if (Environment.IsPrivilegedProcess)
        {
            return; // Root ignores the read-only mode, so the write would succeed.
        }

        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "# Other\n");
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();
        File.SetUnixFileMode(data.Root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            await data.Actions!.RecordOpened("Plan").WaitAsync(Wait);
            await data.Actions.RecordOpened("Other").WaitAsync(Wait);

            WaitListed(cut, "Other", "Plan");
            Assert.False(File.Exists(Path.Combine(data.Root, RecentNotesStore.FileName)));
            Assert.Empty(Directory.GetFiles(data.Root, "*.tmp"));
        }
        finally
        {
            File.SetUnixFileMode(data.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // AC-9: the New note field still creates a note and opens it; it is then listed first.
    [Fact]
    public async Task NewNote_CreatesOpensAndListsItFirst()
    {
        data.WriteNote("Other", "# Other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Other");

        await cut.Find(".note-new-title").InputAsync(new ChangeEventArgs { Value = "Fresh" });
        await cut.Find(".note-new-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Equal("Fresh", cut.Find(".note-title").TextContent), Wait);
        Assert.Equal("# Fresh\n", File.ReadAllText(Path.Combine(data.Root, "Fresh.md")));
        WaitListed(cut, "Fresh", "Other");
    }

    // AC-9: saved searches still show in the sidebar and run.
    [Fact]
    public async Task SavedSearch_StillShownAndRuns()
    {
        data.WriteNote("Plan", "# Plan\n\nbudget\n");
        data.WriteFile(SettingsStore.FileName, "{\"savedSearches\":[{\"name\":\"Money\",\"query\":\"budget\"}]}");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await cut.Find(".notes-region button.saved-search").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".search-panel")), Wait);
        cut.WaitForAssertion(() => Assert.Contains("Plan", cut.Find(".search-panel").TextContent, StringComparison.Ordinal), Wait);
    }

    // 338 AC-2: opening a person page through /open with no recent.json yet records nothing and creates no file.
    [Fact]
    public async Task OpenPersonPage_NoFile_RecordsNothing_WritesNothing()
    {
        data.WriteNote("people", "# people\n");
        data.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await Open(cut, js, "people/Anna");
        data.WaitForRecentWrites();

        Assert.Empty(data.State!.Recent);
        Assert.False(File.Exists(Path.Combine(data.Root, RecentNotesStore.FileName)));
        Assert.Empty(cut.FindAll("ul.recent-notes button.name"));
        Assert.Equal("No recently opened notes.", cut.Find(".recent-empty").TextContent);
    }

    // 338 AC-2: with 10 other notes recently opened, opening a person page through /open and through the People panel
    // keeps all 10 listed and leaves recent.json's bytes unchanged.
    [Fact]
    public async Task OpenPersonPage_TenListed_AllStay_FileUnchanged()
    {
        string[] names = Enumerable.Range(1, 10).Select(i => $"N{i:00}").ToArray();
        foreach (string name in names)
        {
            data.WriteNote(name, "x\n");
        }

        data.WriteNote("people", "# people\n");
        data.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\n");
        data.WriteFile(Path.Combine("people", "Bob.md"), "# Bob\n");
        await data.RefreshAsync();
        foreach (string name in names)
        {
            await data.Actions!.RecordOpened(name).WaitAsync(Wait);
        }

        string path = Path.Combine(data.Root, RecentNotesStore.FileName);
        byte[] before = File.ReadAllBytes(path);
        string[] listed = names.Reverse().ToArray();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitListed(cut, listed);

        await Open(cut, js, "people/Anna");
        data.WaitForRecentWrites();
        Assert.Equal(listed, data.State!.Recent);
        WaitListed(cut, listed);

        int mounts = js.Calls("ContextEditor.mount").Count;
        await cut.Find("button.people-open").ClickAsync(new MouseEventArgs());
        // The panel re-renders once its attributes load, which gives each row a new handler, so find the row after.
        cut.WaitForState(() => cut.FindComponent<PeoplePanel>().Instance.Loaded, Wait);
        await cut.FindAll(".people-panel button.person-open").Single(b => b.TextContent == "Bob").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.Equal("people/Bob", cut.Find(".note-title").GetAttribute("title")), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
        data.WaitForRecentWrites();

        Assert.Equal(listed, data.State.Recent);
        WaitListed(cut, listed);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private static void WaitListed<T>(IRenderedComponent<T> cut, params string[] names)
        where T : IComponent =>
        cut.WaitForAssertion(() => Assert.Equal(
            names, cut.FindAll("ul.recent-notes button.name").Select(b => b.GetAttribute("title")!).ToArray()), Wait);

    private static async Task Open(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NoteOpener.OpenAsync(cut, name);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        cut.WaitForAssertion(() => Assert.Equal(name, cut.Find(".note-title").GetAttribute("title")), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
    }
}
