using System.Text;

namespace Context.Tests;

// 233 AC-1 to AC-6: DesktopActions.CreateLinkedNote makes the note a clicked link to a missing note names, against real files.
public sealed class LinkCreateActionsTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-1: a missing top-level note is created with only its heading.
    [Fact]
    public async Task CreateLinkedNote_Missing_CreatesTheNote()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CreateLinkedNote("New topic");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(ActionReason.None, result.Reason);
        Assert.Equal("New topic", result.Text);
        Assert.Equal("# New topic\n", Read("New topic.md"));
        AssertNoKeyCalls();
    }

    // AC-2: a missing parent is created first, then the subpage.
    [Fact]
    public async Task CreateLinkedNote_Subpage_CreatesTheParentToo()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CreateLinkedNote(" Projects / Alpha ");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(ActionReason.None, result.Reason);
        Assert.Equal("Projects/Alpha", result.Text);
        Assert.Equal("# Projects\n", Read("Projects.md"));
        Assert.Equal("# Alpha\n", Read(Path.Combine("Projects", "Alpha.md")));
    }

    // AC-2: an existing parent in another case is reused with its own spelling and its bytes are kept.
    [Fact]
    public async Task CreateLinkedNote_ExistingParentOtherCase_IsReused()
    {
        data.WriteNote("projects", "# projects\n\nMine.\n");
        await data.RefreshAsync();
        byte[] before = File.ReadAllBytes(Path.Combine(data.Root, "projects.md"));

        ActionResult result = await data.Actions!.CreateLinkedNote("Projects/Alpha");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("projects/Alpha", result.Text);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(data.Root, "projects.md")));
        Assert.Equal("# Alpha\n", Read(Path.Combine("projects", "Alpha.md")));
        Assert.Equal(["projects.md", Path.Combine("projects", "Alpha.md")], NoteFiles());
    }

    // AC-3: a heading and one trailing .md are not part of the new note's title.
    [Theory]
    [InlineData("Ghost#Goals")]
    [InlineData("Ghost.md")]
    [InlineData("Ghost.MD")]
    public async Task CreateLinkedNote_HeadingOrExtension_CreatesTheNotePart(string target)
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CreateLinkedNote(target);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Ghost", result.Text);
        Assert.Equal("# Ghost\n", Read("Ghost.md"));
        Assert.Equal(["Ghost.md"], NoteFiles());
    }

    // AC-4: a note that exists by now (in any case) is opened as it is: Done/Unchanged, nothing written.
    [Theory]
    [InlineData("Plan", "Plan")]
    [InlineData("plan", "Plan")]
    [InlineData("PROJECTS/alpha", "Projects/Alpha")]
    public async Task CreateLinkedNote_Exists_Unchanged(string target, string expected)
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Projects", "# Projects\n");
        data.WriteFile(Path.Combine("Projects", "Alpha.md"), "# Alpha\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        ActionResult result = await data.Actions!.CreateLinkedNote(target);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(ActionReason.Unchanged, result.Reason);
        Assert.Equal(expected, result.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
    }

    // AC-4: an existing top-level sensitive note (any case) is opened as it is: nothing written, no key-store call.
    [Fact]
    public async Task CreateLinkedNote_ExistsSensitive_Unchanged()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Diary", "# Diary\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        ActionResult result = await data.Actions!.CreateLinkedNote("diary");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(ActionReason.Unchanged, result.Reason);
        Assert.Equal("Diary", result.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
        AssertNoKeyCalls();
    }

    // AC-5: an invalid segment is refused with that segment's ValidateTitle text; nothing written, no key call.
    [Theory]
    [InlineData("a//b", "A note needs a title.")]
    [InlineData("Projects/.hidden", "A note title can't start with a dot.")]
    [InlineData("a:b", "A note title can't contain / \\ : or control characters.")]
    [InlineData("Projects/a\\b", "A note title can't contain / \\ : or control characters.")]
    public async Task CreateLinkedNote_InvalidSegment_Refused(string target, string text)
    {
        await AssertRefused(target, ActionReason.Invalid, text);
    }

    // AC-5: a segment one character over the title limit is refused; one at the limit is created.
    [Fact]
    public async Task CreateLinkedNote_LongSegment_RefusedOverTheLimit()
    {
        await AssertRefused("Projects/" + new string('x', NoteStore.MaxTitleLength + 1), ActionReason.Invalid,
            "A note title can be at most 100 characters.");

        ActionResult atLimit = await data.Actions!.CreateLinkedNote(new string('y', NoteStore.MaxTitleLength));
        Assert.Equal(ActionKind.Done, atLimit.Kind);
    }

    // AC-5: a full name over 400 characters is refused before anything is written.
    [Fact]
    public async Task CreateLinkedNote_FullNameTooLong_Refused()
    {
        string target = string.Join("/", Enumerable.Repeat(new string('n', 99), 5));

        await AssertRefused(target, ActionReason.Invalid, "A note's full name can be at most 400 characters.");
    }

    // AC-5: nothing is created under the top-level calendar note, whether it exists or not.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateLinkedNote_UnderCalendar_Refused(bool exists)
    {
        if (exists)
        {
            data.WriteNote("calendar", "# calendar\n");
        }

        await AssertRefused("Calendar/Meeting", ActionReason.Invalid,
            (exists ? "calendar" : "Calendar") + " can't have subpages. Nothing was done.");
    }

    // AC-5: nothing is created under a sensitive top-level note, and no key is asked for.
    [Fact]
    public async Task CreateLinkedNote_UnderSensitive_Refused()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Diary", "# Diary\n");

        await AssertRefused("diary/Secret", ActionReason.Locked, "Diary can't have subpages. Nothing was done.");
    }

    // AC-6: a failed write is Failed with the create text; the parents made before it stay, and a second click finishes.
    [Fact]
    public async Task CreateLinkedNote_WriteFails_ParentsStayAndRetryFinishes()
    {
        // A plain file where the Sub folder must go makes the last write fail.
        data.WriteFile(Path.Combine("Projects", "Sub"), "blocker");
        await data.RefreshAsync();

        ActionResult failed = await data.Actions!.CreateLinkedNote("Projects/Sub/Leaf");

        Assert.Equal(ActionKind.Failed, failed.Kind);
        Assert.Equal("Can't create that note. Nothing was done.", failed.Text);
        Assert.Equal("# Projects\n", Read("Projects.md"));
        Assert.Equal("# Sub\n", Read(Path.Combine("Projects", "Sub.md")));
        Assert.False(File.Exists(Path.Combine(data.Root, "Projects", "Sub", "Leaf.md")));

        File.Delete(Path.Combine(data.Root, "Projects", "Sub"));
        ActionResult done = await data.Actions.CreateLinkedNote("Projects/Sub/Leaf");

        Assert.Equal(ActionKind.Done, done.Kind);
        Assert.Equal(ActionReason.None, done.Reason);
        Assert.Equal("Projects/Sub/Leaf", done.Text);
        Assert.Equal("# Leaf\n", Read(Path.Combine("Projects", "Sub", "Leaf.md")));
        AssertNoKeyCalls();
    }

    private async Task AssertRefused(string target, ActionReason reason, string text)
    {
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        ActionResult result = await data.Actions!.CreateLinkedNote(target);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(text, result.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
        AssertNoKeyCalls();
    }

    private void AssertNoKeyCalls()
    {
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(0, data.Keys.MachineKeysCreated);
        Assert.Equal(0, data.Keys.DetailsReads);
    }

    private string Read(string relative) =>
        Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(data.Root, relative)));

    private string[] NoteFiles() =>
        [.. Directory.EnumerateFiles(data.Root, "*.md", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(data.Root, p))
            .Order(StringComparer.Ordinal)];
}
