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

    // 134 AC-3: a new note is created through NoteStore.Create, named in the result, and the state refreshes.
    [Fact]
    public async Task CreateNote_Valid_CreatesAndRefreshes()
    {
        await data.RefreshAsync();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.CreateNote("  Plan.md ");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Plan", result.Text);
        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal(builds + 1, data.State.Builds);
        Assert.Equal(["Plan"], data.State.Snapshot!.Notes.Select(n => n.Name).ToArray());
    }

    // 134 AC-3: an invalid title is refused with the ValidateTitle text and writes nothing.
    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("NUL.txt")]
    public async Task CreateNote_Invalid_RefusedWithValidateText(string title)
    {
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.CreateNote(title);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(ActionReason.Invalid, result.Reason);
        Assert.Equal(NoteStore.ValidateTitle(NoteStore.NormalizeTitle(title)), result.Text);
        Assert.Equal(before, Files());
    }

    // 134 AC-3: an existing note (ignoring case) is refused and left alone.
    [Fact]
    public async Task CreateNote_Exists_Refused()
    {
        data.WriteNote("Plan", "Mine\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.CreateNote("plan");

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(ActionReason.Exists, result.Reason);
        Assert.Equal("Note plan already exists.", result.Text);
        Assert.Equal(before, Files());
    }

    // 134 AC-3: any other error is Failed with the fixed text, never the exception's, and nothing is written.
    [Fact]
    public async Task CreateNote_FolderCantBeMade_FailedFixedText()
    {
        File.WriteAllText(data.Root, "not a folder");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CreateNote("Plan");

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal("Can't create that note. Nothing was done.", result.Text);
        Assert.Equal("not a folder", File.ReadAllText(data.Root));
    }

    // 134 AC-4: a rename moves the note, keeps its bytes, refreshes, and the list and the index know only the new name.
    [Fact]
    public async Task RenameNote_Valid_RenamesRefreshesAndIndexDropsOldName()
    {
        data.WriteNote("Plan", "# Plan\n#work\n");
        await data.RefreshAsync();
        Assert.Equal(["Plan"], await data.State!.NotesWithTag("work"));
        int builds = data.State.Builds;

        ActionResult result = await data.Actions!.RenameNote("Plan", "Project");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(ActionReason.None, result.Reason);
        Assert.Equal("Project", result.Text);
        Assert.Equal("# Plan\n#work\n", File.ReadAllText(Path.Combine(data.Root, "Project.md")));
        Assert.Equal(builds + 1, data.State.Builds);
        Assert.Equal(["Project"], data.State.Snapshot!.Notes.Select(n => n.Name).ToArray());
        Assert.Equal(["Project"], await data.State.NotesWithTag("work"));
    }

    // 134 AC-4: the same name is Done/Unchanged with no write and no refresh.
    [Fact]
    public async Task RenameNote_SameName_UnchangedNoRefresh()
    {
        data.WriteNote("Plan", "x");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.RenameNote("Plan", " Plan ");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(ActionReason.Unchanged, result.Reason);
        Assert.Equal(before, Files());
        Assert.Equal(builds, data.State.Builds);
    }

    // 134 AC-4: invalid titles, existing notes and missing sources are refused with their texts; nothing changes.
    [Theory]
    [InlineData("Plan", "a:b", nameof(ActionReason.Invalid), "A note title can't contain / \\ : or control characters.")]
    [InlineData("Plan", "OTHER", nameof(ActionReason.Exists), "Note OTHER already exists.")]
    [InlineData("Gone", "New", nameof(ActionReason.Missing), "Gone no longer exists. Nothing was done.")]
    public async Task RenameNote_Refusals_NothingChanged(string name, string title, string reason, string text)
    {
        data.WriteNote("Plan", "p");
        data.WriteNote("Other", "o");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.RenameNote(name, title);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(reason, result.Reason.ToString());
        Assert.Equal(text, result.Text);
        Assert.Equal(before, Files());
    }

    // 134 AC-5: without a confirmation nothing is deleted.
    [Fact]
    public async Task DeleteNote_NotConfirmed_DeletesNothing()
    {
        data.WriteNote("Plan", "p");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.DeleteNote("Plan", null);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(ActionReason.NotConfirmed, result.Reason);
        Assert.Equal(before, Files());
    }

    // 134 AC-5: confirmed, the note is deleted and the state refreshes.
    [Fact]
    public async Task DeleteNote_Confirmed_DeletesAndRefreshes()
    {
        data.WriteNote("Plan", "p");
        data.WriteNote("Other", "o");
        await data.RefreshAsync();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.DeleteNote("Plan", new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Plan", result.Text);
        Assert.False(File.Exists(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal(builds + 1, data.State.Builds);
        Assert.Equal(["Other"], data.State.Snapshot!.Notes.Select(n => n.Name).ToArray());
    }

    // 134 AC-5: a missing note is refused and nothing is deleted.
    [Fact]
    public async Task DeleteNote_Missing_Refused()
    {
        data.WriteNote("Plan", "p");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.DeleteNote("Gone", new Confirmation());

        Assert.Equal(ActionReason.Missing, result.Reason);
        Assert.Equal("Gone no longer exists. Nothing was done.", result.Text);
        Assert.Equal(before, Files());
    }

    // 134 AC-6: the note's own path from List() is launched once; nothing is written and the state is not refreshed.
    [Fact]
    public async Task OpenExternally_Note_LaunchesItsPathOnly()
    {
        data.WriteNote("Plan", "p");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.OpenExternally("plan.md");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal([Path.Combine(data.Root, "Plan.md")], data.Launcher.Launched);
        Assert.Equal(before, Files());
        Assert.Equal(builds, data.State.Builds);
    }

    // 134 AC-6: a launcher that throws gives Failed with the fixed text, not the exception's.
    [Fact]
    public async Task OpenExternally_LauncherThrows_FailedFixedText()
    {
        data.WriteNote("Plan", "p");
        await data.RefreshAsync();
        data.Launcher.Throws = new InvalidOperationException("secret detail");

        ActionResult result = await data.Actions!.OpenExternally("Plan");

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal("Can't open Plan in another app.", result.Text);
    }

    // 134 AC-6: a missing note is refused and nothing is launched.
    [Fact]
    public async Task OpenExternally_Missing_RefusedNotLaunched()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.OpenExternally("Gone");

        Assert.Equal(ActionReason.Missing, result.Reason);
        Assert.Empty(data.Launcher.Launched);
    }

    // 134 AC-6: a symlinked x.md (to a script outside the root, or to a note inside it) is never launched.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenExternally_Symlink_RefusedNotLaunched(bool outside)
    {
        data.WriteNote("Plan", "p");
        string target = outside ? Path.Combine(Path.GetDirectoryName(data.Root)!, "evil.sh") : Path.Combine(data.Root, "Plan.md");
        if (outside)
        {
            File.WriteAllText(target, "#!/bin/sh\necho hi\n");
        }

        try
        {
            File.CreateSymbolicLink(Path.Combine(data.Root, "x.md"), target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return; // Symlinks can't be made here (Windows without the privilege).
        }

        await data.RefreshAsync();

        ActionResult result = await data.Actions!.OpenExternally("x");

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal("Can't open x in another app.", result.Text);
        Assert.Empty(data.Launcher.Launched);
    }

    // 134 AC-6: off Windows an empty x.md (which could be a FIFO) is never launched.
    [Fact]
    public async Task OpenExternally_EmptyFileOffWindows_Refused()
    {
        data.WriteNote("Empty", string.Empty);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.OpenExternally("Empty");

        Assert.Equal(OperatingSystem.IsWindows() ? ActionKind.Done : ActionKind.Refused, result.Kind);
        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 0, data.Launcher.Launched.Count);
    }

    private void WriteChild(string name, string text) =>
        data.WriteFile(Path.Combine(name.Split('/')) + ".md", text);

    // 152 AC-15: CreateSubpage writes the child, refreshes, and gives its full name.
    [Fact]
    public async Task CreateSubpage_Valid_CreatesAndRefreshes()
    {
        data.WriteNote("Meetings", "# Meetings\n");
        await data.RefreshAsync();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.CreateSubpage("meetings", " Meeting 1.md ");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Meetings/Meeting 1", result.Text);
        Assert.Equal("# Meeting 1\n", File.ReadAllText(Path.Combine(data.Root, "Meetings", "Meeting 1.md")));
        Assert.Equal(builds + 1, data.State.Builds);
        Assert.Equal(["Meetings", "Meetings/Meeting 1"], data.State.Snapshot!.Notes.Select(n => n.Name).ToArray());
    }

    // 152 AC-15: the CreateNote-style refusals; nothing changes.
    [Theory]
    [InlineData("Meetings", "a/b", nameof(ActionReason.Invalid), "A note title can't contain / \\ : or control characters.")]
    [InlineData("Meetings", "MEETING 1", nameof(ActionReason.Exists), "Note Meetings/MEETING 1 already exists.")]
    [InlineData("Gone", "New", nameof(ActionReason.Missing), "Gone no longer exists. Nothing was done.")]
    [InlineData("calendar", "New", nameof(ActionReason.Invalid), "calendar can't have subpages. Nothing was done.")]
    public async Task CreateSubpage_Refusals_NothingChanged(string parent, string title, string reason, string text)
    {
        data.WriteNote("Meetings", "m");
        WriteChild("Meetings/Meeting 1", "1");
        data.WriteNote("calendar", "c");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.CreateSubpage(parent, title);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(reason, result.Reason.ToString());
        Assert.Equal(text, result.Text);
        Assert.Equal(before, Files());
    }

    // 152 AC-15: a full name over the limit is refused.
    [Fact]
    public async Task CreateSubpage_TooLong_Refused()
    {
        string longTitle = new string('a', NoteStore.MaxTitleLength);
        data.WriteNote(longTitle, "x");
        WriteChild(longTitle + "/" + longTitle, "x");
        WriteChild(longTitle + "/" + longTitle + "/" + longTitle, "x");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.CreateSubpage(string.Join("/", Enumerable.Repeat(longTitle, 3)), longTitle);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(DesktopActions.NameTooLongText, result.Text);
        Assert.Equal(before, Files());
    }

    // 152 AC-16: MoveNote moves the subtree under the typed parent and back to the top level.
    [Fact]
    public async Task MoveNote_Moves_AndBackToTop()
    {
        data.WriteNote("Meetings", "m");
        WriteChild("Meetings/Meeting 1", "one");
        data.WriteNote("Archive", "a");
        await data.RefreshAsync();

        ActionResult moved = await data.Actions!.MoveNote("Meetings", " archive ");

        Assert.Equal(ActionKind.Done, moved.Kind);
        Assert.Equal(ActionReason.None, moved.Reason);
        Assert.Equal("Archive/Meetings", moved.Text);
        Assert.Equal(["Archive", "Archive/Meetings", "Archive/Meetings/Meeting 1"], data.State!.Snapshot!.Notes.Select(n => n.Name).ToArray());
        Assert.Equal("one", File.ReadAllText(Path.Combine(data.Root, "Archive", "Meetings", "Meeting 1.md")));

        ActionResult back = await data.Actions.MoveNote("Archive/Meetings", "");

        Assert.Equal("Meetings", back.Text);
        Assert.Equal(["Archive", "Meetings", "Meetings/Meeting 1"], data.State.Snapshot!.Notes.Select(n => n.Name).ToArray());
    }

    // 152 AC-16: already there is Done/Unchanged with no write and no refresh.
    [Fact]
    public async Task MoveNote_SameParent_Unchanged()
    {
        data.WriteNote("Meetings", "m");
        WriteChild("Meetings/Meeting 1", "one");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.MoveNote("Meetings/Meeting 1", "MEETINGS");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(ActionReason.Unchanged, result.Reason);
        Assert.Equal(before, Files());
        Assert.Equal(builds, data.State.Builds);
    }

    // 152 AC-16: an unknown parent (also a path), a move under itself, calendar and clashes are refused.
    [Theory]
    [InlineData("Meetings", "Gone", nameof(ActionReason.Missing), "No note called Gone. Nothing was moved.")]
    [InlineData("Meetings", "Archive.md/..", nameof(ActionReason.Missing), "No note called Archive.md/... Nothing was moved.")]
    [InlineData("Meetings", "Meetings/Meeting 1", nameof(ActionReason.Invalid), DesktopActions.MoveUnderItselfText)]
    [InlineData("Meetings", "meetings", nameof(ActionReason.Invalid), DesktopActions.MoveUnderItselfText)]
    [InlineData("Meetings", "calendar", nameof(ActionReason.Invalid), "Can't move Meetings under calendar. Nothing was moved.")]
    [InlineData("Archive/Meeting 1", "Meetings", nameof(ActionReason.Exists), "Note Meetings/Meeting 1 already exists.")]
    [InlineData("Gone", "Archive", nameof(ActionReason.Missing), "Gone no longer exists. Nothing was done.")]
    public async Task MoveNote_Refusals_NothingChanged(string name, string parent, string reason, string text)
    {
        data.WriteNote("Meetings", "m");
        WriteChild("Meetings/Meeting 1", "one");
        data.WriteNote("Archive", "a");
        WriteChild("Archive/Meeting 1", "clash");
        data.WriteNote("calendar", "c");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.MoveNote(name, parent);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(reason, result.Reason.ToString());
        Assert.Equal(text, result.Text);
        Assert.Equal(before, Files());
    }

    // 152 AC-16: renaming a subpage keeps its parent and gives the new full name.
    [Fact]
    public async Task RenameNote_Child_KeepsParent()
    {
        data.WriteNote("Meetings", "m");
        WriteChild("Meetings/Meeting 1", "one");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.RenameNote("Meetings/Meeting 1", "Kickoff");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Meetings/Kickoff", result.Text);
        Assert.Equal("one", File.ReadAllText(Path.Combine(data.Root, "Meetings", "Kickoff.md")));
        Assert.Equal(["Meetings", "Meetings/Kickoff"], data.State!.Snapshot!.Notes.Select(n => n.Name).ToArray());
    }

    // 152 AC-17: deleting a note with subpages removes the subtree and keeps any other file.
    [Fact]
    public async Task DeleteNote_WithSubpages_RemovesSubtree()
    {
        data.WriteNote("Meetings", "m");
        WriteChild("Meetings/Meeting 1", "one");
        WriteChild("Meetings/Meeting 1/Notes", "n");
        data.WriteFile(Path.Combine("Meetings", "keep.txt"), "stray");
        data.WriteNote("Other", "o");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.DeleteNote("Meetings", new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(["Other"], data.State!.Snapshot!.Notes.Select(n => n.Name).ToArray());
        Assert.Equal(["Meetings/keep.txt", "Other.md"], Files().Keys.Select(k => k.Replace('\\', '/')).ToArray());
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
