using System.Text;

namespace Context.Tests;

// 094 AC-5 / AC-6 / AC-10: DesktopActions.SaveNote and AddTodo, against real files.
public sealed class NoteActionsTests : IDisposable
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-5: opening and saving without an edit leaves the bytes and the last-write time alone, also CRLF + BOM.
    [Theory]
    [InlineData("# Plan\n\nText\n", false)]
    [InlineData("# Plan\r\n\r\n* lossy\r\n", true)]
    public async Task SaveNote_Unchanged_WritesNothing(string text, bool bom)
    {
        EditableNote opened = await Open("Plan", bom ? [.. Bom, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text));
        SortedDictionary<string, string> before = Files();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.SaveNote(opened, opened.Text);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(ActionReason.Unchanged, result.Reason);
        Assert.Equal("Plan", result.Text);
        Assert.Equal(before, Files());
        Assert.Equal(builds, data.State.Builds);
    }

    // AC-5: an edit saves the expected bytes, keeping CRLF and the BOM, and refreshes the state.
    [Fact]
    public async Task SaveNote_Edit_SavesKeepingCrlfAndBom()
    {
        EditableNote opened = await Open("Plan", [.. Bom, .. Encoding.UTF8.GetBytes("# Plan\r\n\r\nText\r\n")]);
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.SaveNote(opened, "# Plan\n\nText #new\n");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(ActionReason.None, result.Reason);
        Assert.Equal("Plan", result.Text);
        Assert.Equal("# Plan\n\nText #new\n", result.Note!.Text);
        Assert.Equal(
            [.. Bom, .. Encoding.UTF8.GetBytes("# Plan\r\n\r\nText #new\r\n")],
            File.ReadAllBytes(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal(builds + 1, data.State.Builds);
    }

    // AC-6: a file changed on disk since it was opened is left alone, with the red line naming the note.
    [Fact]
    public async Task SaveNote_ChangedOnDisk_RefusedNothingWritten()
    {
        EditableNote opened = await Open("Plan", Encoding.UTF8.GetBytes("Mine\n"));
        data.WriteNote("Plan", "Theirs\n");
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.SaveNote(opened, "Mine, edited\n");

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(ActionReason.ChangedOnDisk, result.Reason);
        Assert.Equal("Plan changed on disk since you opened it. Nothing was saved.", result.Text);
        Assert.Equal(before, Files());
    }

    // AC-6: a deleted note is not written again.
    [Fact]
    public async Task SaveNote_Deleted_RefusedNothingWritten()
    {
        EditableNote opened = await Open("Plan", Encoding.UTF8.GetBytes("Mine\n"));
        File.Delete(Path.Combine(data.Root, "Plan.md"));
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.SaveNote(opened, "Mine, edited\n");

        Assert.Equal(ActionReason.Missing, result.Reason);
        Assert.Equal("Plan no longer exists. Nothing was saved.", result.Text);
        Assert.Equal(before, Files());
    }

    // AC-6: a write that fails leaves the file alone and says so.
    [Fact]
    public async Task SaveNote_WriteFails_FailedNothingWritten()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        EditableNote opened = await Open("Plan", Encoding.UTF8.GetBytes("Mine\n"));
        SortedDictionary<string, string> before = Files();
        UnixFileMode mode = File.GetUnixFileMode(data.Root);
        File.SetUnixFileMode(data.Root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        ActionResult result;
        try
        {
            result = await data.Actions!.SaveNote(opened, "Mine, edited\n");
        }
        finally
        {
            File.SetUnixFileMode(data.Root, mode);
        }

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal(ActionReason.WriteFailed, result.Reason);
        Assert.Equal("Can't write Plan. Nothing was saved.", result.Text);
        Assert.Equal(before, Files());
    }

    // AC-6: text over 5 MB is refused before anything is written.
    [Fact]
    public async Task SaveNote_OverFiveMegabytes_RefusedNothingWritten()
    {
        EditableNote opened = await Open("Plan", Encoding.UTF8.GetBytes("Mine\n"));
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.SaveNote(opened, new string('x', (int)DesktopActions.MaxNoteBytes + 1));

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal("Plan is larger than 5 MB. Nothing was saved.", result.Text);
        Assert.Equal(before, Files());
    }

    // AC-10: a todo is added through TodoStore.Add with its due date.
    [Fact]
    public async Task AddTodo_Adds()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.AddTodo("  Buy milk ", DesktopFixture.Today.AddDays(1));

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Buy milk", result.Text);
        TodoItem added = Assert.Single(data.Todos!.List());
        Assert.Equal("Buy milk", added.Text);
        Assert.Equal(DesktopFixture.Today.AddDays(1), added.Due);
        Assert.Equal(["Buy milk"], data.State!.Snapshot!.Todos.Select(t => t.Text).ToArray());
    }

    // AC-10: empty text writes nothing.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddTodo_Empty_RefusedNothingWritten(string text)
    {
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.AddTodo(text, DesktopFixture.Today);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal("Write the todo first.", result.Text);
        Assert.Equal(before, Files());
        Assert.False(File.Exists(Path.Combine(data.Root, TodoStore.FileName)));
    }

    // AC-10: text over 200 characters writes nothing.
    [Fact]
    public async Task AddTodo_TooLong_RefusedNothingWritten()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.AddTodo(new string('x', 201), DesktopFixture.Today);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(DesktopActions.TodoTooLongText, result.Text);
        Assert.False(File.Exists(Path.Combine(data.Root, TodoStore.FileName)));
    }

    // AC-10: an unreadable todos.json is not overwritten.
    [Fact]
    public async Task AddTodo_Unreadable_FailedNothingWritten()
    {
        data.WriteFile(TodoStore.FileName, "[ {");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.AddTodo("New", DesktopFixture.Today);

        Assert.Equal(ActionReason.Unreadable, result.Reason);
        Assert.Equal(DesktopActions.TodosUnreadableText, result.Text);
        Assert.Equal(before, Files());
    }

    private async Task<EditableNote> Open(string name, byte[] bytes)
    {
        Directory.CreateDirectory(data.Root);
        File.WriteAllBytes(Path.Combine(data.Root, name + ".md"), bytes);
        await data.RefreshAsync();
        EditableNote? opened = await data.State!.ReadForEdit(name);
        Assert.NotNull(opened);
        return opened;
    }

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName);
}
