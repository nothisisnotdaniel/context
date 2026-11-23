using System.Text;

namespace Context.Tests;

// 397: DesktopActions.CreateAppointmentNote makes a note under the appointment page Meetings/<title part>, against real
// files in the fixture's temp folder.
public sealed class AppointmentNoteActionsTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 29, 11, 0, 0);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private static Appointment Budget(string title = "Budget") => new(title, null, Start, Start.AddHours(1), false);

    // AC-1, AC-2: with no Meetings at all, both parents are created first, then the note under Meetings/<part>.
    [Fact]
    public async Task NoParents_CreatesMeetingsAndPageThenNote()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CreateAppointmentNote(Budget("Budget: Q4"), " Follow-up.md ");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Meetings/Budget Q4/Follow-up", result.Text);
        Assert.Equal("# Meetings\n", Read("Meetings.md"));
        Assert.Equal("# Budget Q4\n", Read(Path.Combine("Meetings", "Budget Q4.md")));
        Assert.Equal("# Follow-up\n", Read(Path.Combine("Meetings", "Budget Q4", "Follow-up.md")));
        Assert.Equal(3, NoteFiles().Length);
    }

    // AC-2: existing parents in another case are reused unchanged, in their own spelling.
    [Fact]
    public async Task ExistingParentsOtherCase_Reused()
    {
        data.WriteNote("meetings", "# meetings\n\nkept\n");
        data.WriteFile(Path.Combine("meetings", "budget.md"), "# budget\n\nkept\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CreateAppointmentNote(Budget(), "Plan");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("meetings/budget/Plan", result.Text);
        Assert.Equal("# meetings\n\nkept\n", Read("meetings.md"));
        Assert.Equal("# budget\n\nkept\n", Read(Path.Combine("meetings", "budget.md")));
        Assert.Equal("# Plan\n", Read(Path.Combine("meetings", "budget", "Plan.md")));
        Assert.Equal(3, NoteFiles().Length);
    }

    // AC-5: a top-level note with the same title does not block the note under the page.
    [Fact]
    public async Task TopLevelNoteSameTitle_NotAClash()
    {
        data.WriteNote("Budget 2027", "# Budget 2027\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CreateAppointmentNote(Budget(), "Budget 2027");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Meetings/Budget/Budget 2027", result.Text);
        Assert.Equal("# Budget 2027\n", Read(Path.Combine("Meetings", "Budget", "Budget 2027.md")));
    }

    // AC-3: an invalid title (a slash, over 100 characters) is refused with the ValidateTitle text before any parent
    // is written.
    [Theory]
    [InlineData("a/b")]
    [InlineData("   ")]
    [InlineData("tttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttt")]
    public async Task InvalidTitle_RefusedNothingWritten(string title)
    {
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, ".desktop");

        ActionResult result = await data.Actions!.CreateAppointmentNote(Budget(), title);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(ActionReason.Invalid, result.Reason);
        Assert.Equal(NoteStore.ValidateTitle(NoteStore.NormalizeTitle(title)), result.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root, ".desktop"));
        Assert.False(File.Exists(Path.Combine(data.Root, "Meetings.md")));
    }

    // AC-3: a sibling with that title under the page, in any case, the dated meeting note included, is refused with the
    // usual text and nothing is written.
    [Theory]
    [InlineData("2026-10-29", "Note Meetings/Budget/2026-10-29 already exists.")]
    [InlineData("notes", "Note Meetings/Budget/notes already exists.")]
    public async Task SiblingClash_Refused(string title, string text)
    {
        data.WriteNote("Meetings", "# Meetings\n");
        data.WriteFile(Path.Combine("Meetings", "Budget.md"), "# Budget\n");
        data.WriteFile(Path.Combine("Meetings", "Budget", "2026-10-29.md"), "# 2026-10-29\n");
        data.WriteFile(Path.Combine("Meetings", "Budget", "Notes.md"), "# Notes\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, ".desktop");

        ActionResult result = await data.Actions!.CreateAppointmentNote(Budget(), title);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(ActionReason.Exists, result.Reason);
        Assert.Equal(text, result.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root, ".desktop"));
    }

    // AC-4: a sensitive top-level Meetings refuses, writes nothing and asks for no key.
    [Fact]
    public async Task SensitiveMeetings_Refused()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Meetings", "# Meetings\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, ".desktop");

        ActionResult result = await data.Actions!.CreateAppointmentNote(Budget(), "Plan");

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(ActionReason.Locked, result.Reason);
        Assert.Equal("Meetings can't have subpages. Nothing was done.", result.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root, ".desktop"));
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(0, data.Keys.MachineKeysCreated);
    }

    private string Read(string relative) =>
        Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(data.Root, relative)));

    private string[] NoteFiles() =>
        [.. Directory.EnumerateFiles(data.Root, "*.md", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(data.Root, f).Replace('\\', '/'))];
}
