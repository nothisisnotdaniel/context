using System.Text;

namespace Context.Tests;

// 201: todos inside notes through DesktopState and DesktopActions, against real files and the fixture's fixed clocks.
public sealed class NoteTodoActionsTests : IDisposable
{
    private const string Id = "20261029T090000000Z";
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly DateTime Past = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string PathOf(string name) => Path.Combine(data.Root, name + ".md");

    // Writes the note with a last-write time in the past, so any later write is seen as a change by the index.
    private void Write(string name, byte[] bytes)
    {
        Directory.CreateDirectory(data.Root);
        File.WriteAllBytes(PathOf(name), bytes);
        File.SetLastWriteTimeUtc(PathOf(name), Past);
    }

    private static byte[] Utf8(string text, bool bom = false) =>
        bom ? [.. Bom, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text);

    private static void AssertRefused(ActionResult result, ActionKind kind, ActionReason reason, string text)
    {
        Assert.Equal(kind, result.Kind);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(text, result.Text);
    }

    // AC-3 through the state: the snapshot holds the plain notes' open todos after the refresh's sync.
    [Fact]
    public async Task Refresh_FillsNoteTodos()
    {
        Write("Meeting", Utf8("- [ ] call Anna due:2026-11-20\n- [x] done\n"));

        DesktopSnapshot snapshot = await data.RefreshAsync();

        NoteTodo todo = Assert.Single(snapshot.NoteTodos);
        Assert.Equal(("Meeting", 2026, "call Anna"), (todo.Note, todo.Due!.Value.Year, todo.Text));
        Assert.False(snapshot.NoteTodosUnreadable);
    }

    // AC-6: only the first "[ ]" of the line becomes "[x]", CRLF and BOM kept, the old bytes kept as a version, and the
    // todo is gone after the refresh.
    [Fact]
    public async Task CompleteNoteTodo_TicksTheLine_KeepsVersion_AndRefreshes()
    {
        byte[] before = Utf8("# Meeting\r\n- [ ] call Anna [ ] due:2026-11-20\r\n- [ ] other\r\n", bom: true);
        Write("Meeting", before);
        DesktopSnapshot snapshot = await data.RefreshAsync();
        NoteTodo todo = snapshot.NoteTodos.Single(t => t.Text.StartsWith("call", StringComparison.Ordinal));

        ActionResult result = await data.Actions!.CompleteNoteTodo(todo, new Confirmation());

        Assert.Equal((ActionKind.Done, ActionReason.None, "call Anna [ ]"), (result.Kind, result.Reason, result.Text));
        Assert.Equal(
            Utf8("# Meeting\r\n- [x] call Anna [ ] due:2026-11-20\r\n- [ ] other\r\n", bom: true), File.ReadAllBytes(PathOf("Meeting")));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(data.Root, ".history", "Meeting", Id + ".md")));
        Assert.Equal(["other"], data.State!.Snapshot!.NoteTodos.Select(t => t.Text).ToArray());
    }

    // AC-6: without a confirmation nothing is written.
    [Fact]
    public async Task CompleteNoteTodo_NotConfirmed_WritesNothing()
    {
        Write("Meeting", Utf8("- [ ] call Anna\n"));
        NoteTodo todo = Assert.Single((await data.RefreshAsync()).NoteTodos);
        SortedDictionary<string, string> files = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        ActionResult result = await data.Actions!.CompleteNoteTodo(todo, null);

        AssertRefused(result, ActionKind.Refused, ActionReason.NotConfirmed, DesktopActions.NotConfirmedText);
        Assert.Equal(files, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
    }

    // AC-7: an edited, moved or already ticked line is refused with the fixed text; the file bytes stay.
    [Theory]
    [InlineData("- [ ] call Anna today\n")]
    [InlineData("intro\n- [ ] call Anna\n")]
    [InlineData("- [x] call Anna\n")]
    public async Task CompleteNoteTodo_ChangedLine_RefusedAndWritesNothing(string now)
    {
        Write("Meeting", Utf8("- [ ] call Anna\n"));
        NoteTodo todo = Assert.Single((await data.RefreshAsync()).NoteTodos);
        Write("Meeting", Utf8(now));
        SortedDictionary<string, string> files = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        ActionResult result = await data.Actions!.CompleteNoteTodo(todo, new Confirmation());

        AssertRefused(
            result, ActionKind.Refused, ActionReason.Changed, "The todo's line in Meeting changed. Open the note and tick it there.");
        Assert.Equal(files, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
    }

    // AC-6: a BeforeWrite hook that edits the todo's line in between gives Changed and keeps the other writer's bytes.
    [Fact]
    public async Task CompleteNoteTodo_LineEditedInBetween_IsChanged()
    {
        Write("Meeting", Utf8("- [ ] call Anna\n"));
        NoteTodo todo = Assert.Single((await data.RefreshAsync()).NoteTodos);
        data.Actions!.BeforeWrite = () => File.WriteAllBytes(PathOf("Meeting"), Utf8("- [ ] call Anna now\n"));

        ActionResult result = await data.Actions.CompleteNoteTodo(todo, new Confirmation());

        Assert.Equal(ActionReason.Changed, result.Reason);
        Assert.Equal(Utf8("- [ ] call Anna now\n"), File.ReadAllBytes(PathOf("Meeting")));
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".history")));
    }

    // AC-7: a note that is gone is refused with the missing text.
    [Fact]
    public async Task CompleteNoteTodo_NoteGone_Missing()
    {
        Write("Meeting", Utf8("- [ ] call Anna\n"));
        NoteTodo todo = Assert.Single((await data.RefreshAsync()).NoteTodos);
        File.Delete(PathOf("Meeting"));

        ActionResult result = await data.Actions!.CompleteNoteTodo(todo, new Confirmation());

        AssertRefused(result, ActionKind.Refused, ActionReason.Missing, "Meeting no longer exists. Nothing was done.");
        Assert.False(File.Exists(PathOf("Meeting")));
    }

    // AC-7: a note that can't be written is the failure text, never an exception.
    [Fact]
    public async Task CompleteNoteTodo_ReadOnlyFolder_Failed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Write("Meeting", Utf8("- [ ] call Anna\n"));
        NoteTodo todo = Assert.Single((await data.RefreshAsync()).NoteTodos);
        UnixFileMode mode = File.GetUnixFileMode(data.Root);
        File.SetUnixFileMode(PathOf("Meeting"), UnixFileMode.UserRead);
        File.SetUnixFileMode(data.Root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            ActionResult result = await data.Actions!.CompleteNoteTodo(todo, new Confirmation());

            AssertRefused(result, ActionKind.Failed, ActionReason.WriteFailed, "Can't write Meeting.");
        }
        finally
        {
            File.SetUnixFileMode(data.Root, mode);
            File.SetUnixFileMode(PathOf("Meeting"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Assert.Equal(Utf8("- [ ] call Anna\n"), File.ReadAllBytes(PathOf("Meeting")));
    }

    // AC-8: ticking "[x]" in the editor (a save) removes the todo once the save's refresh is in.
    [Fact]
    public async Task EditorSave_TickedLine_RemovesTheTodo()
    {
        Write("Meeting", Utf8("- [ ] call Anna\n"));
        Assert.Single((await data.RefreshAsync()).NoteTodos);
        EditableNote opened = (await data.State!.ReadForEdit("Meeting"))!;

        ActionResult saved = await data.Actions!.SaveNote(opened, "- [x] call Anna\n");

        Assert.Equal(ActionKind.Done, saved.Kind);
        Assert.Empty(data.State.Snapshot!.NoteTodos);
    }

    // AC-8: an outside edit ticking the line removes the todo on the refresh the watcher starts.
    [Fact]
    public async Task OutsideEdit_TickedLine_RemovedOnRefresh()
    {
        Write("Meeting", Utf8("- [ ] call Anna\n- [ ] other\n"));
        Assert.Equal(2, (await data.RefreshAsync()).NoteTodos.Count);
        File.WriteAllText(PathOf("Meeting"), "- [x] call Anna\n- [ ] other\n");

        DesktopSnapshot after = await data.State!.Refresh();

        Assert.Equal(["other"], after.NoteTodos.Select(t => t.Text).ToArray());
    }

    // AC-9: a bad context.db: the note todos are unreadable while the todos.json todos still load.
    [Fact]
    public async Task Refresh_BadDb_NoteTodosUnreadable_StoredTodosStay()
    {
        Write("Meeting", Utf8("- [ ] call Anna\n"));
        data.AddTodo("Stored", DesktopFixture.Today);
        data.WriteFile(TagIndex.FileName, "not a database at all, not at all, really not");

        DesktopSnapshot snapshot = await data.RefreshAsync();

        Assert.True(snapshot.NoteTodosUnreadable);
        Assert.Empty(snapshot.NoteTodos);
        Assert.Equal(["Stored"], snapshot.Todos.Select(t => t.Text).ToArray());
        Assert.False(snapshot.Failed);
    }

    // AC-9: with sensitive notes set up, plain notes' todos list and tick; a sensitive note never lists and the key
    // store is never asked.
    [Fact]
    public async Task SensitiveSetUp_PlainTodosListAndTick_NoKeyStoreCall()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Secret", "- [ ] secret todo\n");
        Write("Meeting", Utf8("- [ ] call Anna\n"));

        DesktopSnapshot snapshot = await data.RefreshAsync();
        NoteTodo todo = Assert.Single(snapshot.NoteTodos);
        ActionResult result = await data.Actions!.CompleteNoteTodo(todo, new Confirmation());

        Assert.Equal("Meeting", todo.Note);
        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("- [x] call Anna\n", File.ReadAllText(PathOf("Meeting")));
        Assert.Empty(data.State!.Snapshot!.NoteTodos);
        Assert.Equal(0, data.Keys.Agrees);
    }
}
