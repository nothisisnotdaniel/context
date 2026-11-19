namespace Context.Tests;

// 280 AC-10: DesktopActions.OpenItem makes the items note and the work item page on demand, against real files.
public sealed class WorkItemActionsTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string Read(string relative) => File.ReadAllText(Path.Combine(data.Root, relative));

    // AC-10: with no items note both notes are created, each with only its heading.
    [Fact]
    public async Task OpenItem_NoItems_CreatesBoth()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.OpenItem(1234);

        Assert.Equal((ActionKind.Done, ActionReason.None, "items/1234"), (result.Kind, result.Reason, result.Text));
        Assert.Equal("# items\n", Read("items.md"));
        Assert.Equal("# 1234\n", Read(Path.Combine("items", "1234.md")));
    }

    // AC-10: an existing Items (any case) and page are reused; nothing is written.
    [Fact]
    public async Task OpenItem_Existing_WritesNothing()
    {
        data.WriteNote("Items", "# Items\n\nMy work.\n");
        data.WriteFile(Path.Combine("Items", "1234.md"), "# 1234\n\nNotes.\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        ActionResult result = await data.Actions!.OpenItem(1234);

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged, "Items/1234"), (result.Kind, result.Reason, result.Text));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
    }

    // AC-10: a sensitive top-level items note is refused with the fixed text: nothing written, no key-store call, and
    // a request for it offers nothing.
    [Fact]
    public async Task OpenItem_SensitiveItems_RefusedNoAgree()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("items", "# items\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        ActionResult result = await data.Actions!.OpenItem(1234);
        ActionResult requested = await data.Actions.OpenRequestedItem(1234);

        Assert.Equal((ActionKind.Refused, ActionReason.Locked), (result.Kind, result.Reason));
        Assert.Equal("items is sensitive, so no work item page can be made.", result.Text);
        Assert.Equal(ActionKind.Refused, requested.Kind);
        Assert.Null(data.State!.TakeItemPage());
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
        Assert.Equal(0, data.Keys.Agrees);
    }
}
