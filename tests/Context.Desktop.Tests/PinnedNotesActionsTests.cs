using Bunit;
using Microsoft.AspNetCore.Components;

namespace Context.Tests;

// 278: the sidebar's pinned notes through DesktopActions and the notes panel, against real files in the fixture's own
// temp folder.
public sealed class PinnedNotesActionsTests : IDisposable
{
    // Every action runs on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-8: renaming a pinned note keeps the pin (and its pinned subpages) under the new name, in place.
    [Fact]
    public async Task Rename_KeepsThePin_UnderTheNewName()
    {
        data.WriteNote("A", "# A\n");
        data.WriteFile(Path.Combine("A", "Sub.md"), "# Sub\n");
        data.WriteNote("B", "# B\n");
        data.WritePinned("A", "B", "A/Sub");
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();
        WaitPinned(cut, "A", "B", "A/Sub");

        ActionResult result = await data.Actions!.RenameNote("A", "D", null).WaitAsync(Wait);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(["D", "B", "D/Sub"], new PinnedNotesStore(data.Root).Read());
        WaitPinned(cut, "D", "B", "D/Sub");
    }

    // AC-8: moving a pinned note keeps the pin under the new full name.
    [Fact]
    public async Task Move_KeepsThePin_UnderTheNewName()
    {
        data.WriteNote("A", "# A\n");
        data.WriteNote("C", "# C\n");
        data.WritePinned("C", "A");
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();
        WaitPinned(cut, "C", "A");

        ActionResult result = await data.Actions!.MoveNote("A", "C").WaitAsync(Wait);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(["C", "C/A"], new PinnedNotesStore(data.Root).Read());
        WaitPinned(cut, "C", "C/A");
    }

    // AC-8: deleting a pinned note removes it and its subpages from pinned.json and the sidebar.
    [Fact]
    public async Task Delete_RemovesThePin()
    {
        data.WriteNote("A", "# A\n");
        data.WriteFile(Path.Combine("A", "Sub.md"), "# Sub\n");
        data.WriteNote("B", "# B\n");
        data.WritePinned("A/Sub", "B", "A");
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();
        WaitPinned(cut, "A/Sub", "B", "A");

        ActionResult result = await data.Actions!.DeleteNote("A", new Confirmation()).WaitAsync(Wait);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(["B"], new PinnedNotesStore(data.Root).Read());
        WaitPinned(cut, "B");
    }

    // AC-8: deleting a pinned sensitive note removes the pin as well.
    [Fact]
    public async Task Delete_Sensitive_RemovesThePin()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Secret", "text\n");
        data.WriteNote("B", "# B\n");
        data.WritePinned("Secret", "B");
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();
        WaitPinned(cut, "Secret", "B");

        ActionResult result = await data.Actions!.DeleteNote("Secret", new Confirmation()).WaitAsync(Wait);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(["B"], new PinnedNotesStore(data.Root).Read());
        WaitPinned(cut, "B");
    }

    // AC-8: merging removes the merged-in (source) note's pin; the kept note stays pinned.
    [Fact]
    public async Task Merge_RemovesTheSourcePin()
    {
        data.WriteNote("Target", "# Target\n");
        data.WriteNote("Source", "# Source\n");
        data.WritePinned("Source", "Target");
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();
        WaitPinned(cut, "Source", "Target");

        ActionResult result = await data.Actions!.MergeNotes("Target", "Source", new Confirmation()).WaitAsync(Wait);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.False(File.Exists(Path.Combine(data.Root, "Source.md")));
        Assert.Equal(["Target"], new PinnedNotesStore(data.Root).Read());
        WaitPinned(cut, "Target");
    }

    // AC-8: an action on an unpinned note never writes pinned.json.
    [Fact]
    public async Task Rename_Unpinned_NoFileWritten()
    {
        data.WriteNote("A", "# A\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.RenameNote("A", "D", null).WaitAsync(Wait);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.False(File.Exists(Path.Combine(data.Root, PinnedNotesStore.FileName)));
    }

    // AC-1, AC-3: pinning an already pinned note, or unpinning one that is not pinned, changes nothing; an invalid
    // name is refused.
    [Fact]
    public async Task SetPinned_Unchanged_And_Invalid()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();

        ActionResult unpin = await data.Actions!.SetPinned("Plan", false).WaitAsync(Wait);
        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (unpin.Kind, unpin.Reason));
        Assert.False(File.Exists(Path.Combine(data.Root, PinnedNotesStore.FileName)));

        ActionResult pin = await data.Actions.SetPinned("Plan", true).WaitAsync(Wait);
        Assert.Equal(ActionKind.Done, pin.Kind);
        ActionResult again = await data.Actions.SetPinned("Plan", true).WaitAsync(Wait);
        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (again.Kind, again.Reason));
        Assert.Equal(["Plan"], new PinnedNotesStore(data.Root).Read());

        ActionResult invalid = await data.Actions.SetPinned("a\\b", true).WaitAsync(Wait);
        Assert.Equal((ActionKind.Refused, ActionReason.Invalid), (invalid.Kind, invalid.Reason));
        Assert.Equal(["Plan"], data.State!.Pinned);
    }

    // AC-9: reopened over the same root, the pins come back from pinned.json in pinned order.
    [Fact]
    public async Task Reopen_RestoresThePins()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Café", "# Café\n");
        await data.RefreshAsync();
        await data.Actions!.SetPinned("Café", true).WaitAsync(Wait);
        await data.Actions.SetPinned("Plan", true).WaitAsync(Wait);

        using DesktopFixture again = new DesktopFixture(data);
        IRenderedComponent<NotesPanel> reopened = await again.RenderAsync<NotesPanel>();

        WaitPinned(reopened, "Café", "Plan");
        Assert.Equal(["Café", "Plan"], again.State!.Pinned);
    }

    // AC-9: pins written before start (WritePinned) show at start.
    [Fact]
    public async Task WritePinned_ShownAtStart()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WritePinned("Plan");

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        WaitPinned(cut, "Plan");
    }

    // 451 AC-2: the moved note takes the target's place in pinned.json and State.Pinned; Changed is raised once.
    [Fact]
    public async Task MovePinned_WritesTheNewOrder_RaisesChangedOnce()
    {
        data.WriteNote("A", "# A\n");
        data.WriteNote("B", "# B\n");
        data.WriteNote("C", "# C\n");
        data.WritePinned("A", "B", "C");
        await data.RefreshAsync();
        int changed = 0;
        data.State!.Changed += () => Interlocked.Increment(ref changed);

        ActionResult result = await data.Actions!.MovePinned("C", "A").WaitAsync(Wait);

        Assert.Equal((ActionKind.Done, ActionReason.None, "C"), (result.Kind, result.Reason, result.Text));
        Assert.Equal("{\"pinned\":[\"C\",\"A\",\"B\"]}", File.ReadAllText(Path.Combine(data.Root, PinnedNotesStore.FileName)).Trim());
        Assert.Equal(["C", "A", "B"], data.State.Pinned);
        Assert.Equal(1, Volatile.Read(ref changed));
    }

    // 451 AC-3: the same name, or a name that is not pinned, is Done/Unchanged and writes nothing.
    [Theory]
    [InlineData("B", "B")]
    [InlineData("X", "B")]
    [InlineData("B", "X")]
    public async Task MovePinned_SameOrUnpinned_Unchanged_NothingWritten(string name, string target)
    {
        data.WriteNote("A", "# A\n");
        data.WriteNote("B", "# B\n");
        data.WritePinned("A", "B");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.MovePinned(name, target).WaitAsync(Wait);

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (result.Kind, result.Reason));
        Assert.Equal(before, Files());
        Assert.Equal(["A", "B"], data.State!.Pinned);
    }

    // 451 AC-4: pinned.json as a folder: Failed/WriteFailed with the write text; the in-memory order stays.
    [Fact]
    public async Task MovePinned_WriteFails_Failed_OrderKept()
    {
        data.WriteNote("A", "# A\n");
        data.WriteNote("B", "# B\n");
        data.WritePinned("A", "B");
        await data.RefreshAsync();
        string file = Path.Combine(data.Root, PinnedNotesStore.FileName);
        File.Delete(file);
        Directory.CreateDirectory(file);

        ActionResult result = await data.Actions!.MovePinned("B", "A").WaitAsync(Wait);

        Assert.Equal((ActionKind.Failed, ActionReason.WriteFailed), (result.Kind, result.Reason));
        Assert.Equal("Can't write pinned.json.", result.Text);
        Assert.Equal(["A", "B"], data.State!.Pinned);
    }

    // 451 AC-9: the moved order survives a restart: a new state on the same root shows it.
    [Fact]
    public async Task MovePinned_Reopen_ShowsTheMovedOrder()
    {
        data.WriteNote("A", "# A\n");
        data.WriteNote("B", "# B\n");
        data.WriteNote("C", "# C\n");
        data.WritePinned("A", "B", "C");
        await data.RefreshAsync();
        await data.Actions!.MovePinned("A", "C").WaitAsync(Wait);

        using DesktopFixture again = new DesktopFixture(data);
        IRenderedComponent<NotesPanel> reopened = await again.RenderAsync<NotesPanel>();

        WaitPinned(reopened, "B", "C", "A");
        Assert.Equal(["B", "C", "A"], again.State!.Pinned);
    }

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

    private static void WaitPinned<T>(IRenderedComponent<T> cut, params string[] names)
        where T : IComponent =>
        cut.WaitForAssertion(() => Assert.Equal(
            names, cut.FindAll("ul.pinned-notes button.name").Select(b => b.GetAttribute("title")!).ToArray()), Wait);
}
