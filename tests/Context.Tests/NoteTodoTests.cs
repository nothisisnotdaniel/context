using System.Text;

namespace Context.Tests;

// 201: ticking a todo inside a note (AC-6, AC-7) and the merged order of todos.json and note todos (AC-4).
public sealed class NoteTodoTests : IDisposable
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly DateTime Created = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string root = Path.Combine(Path.GetTempPath(), "context-notetodo-" + Guid.NewGuid().ToString("N"));
    private readonly NoteStore store;
    private readonly TagIndex index;

    public NoteTodoTests()
    {
        Directory.CreateDirectory(root);
        store = new NoteStore(root);
        index = new TagIndex(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string PathOf(string name) => Path.Combine(root, name + ".md");

    private void Write(string name, byte[] bytes) => File.WriteAllBytes(PathOf(name), bytes);

    private static byte[] Utf8(string text, bool bom = false) =>
        bom ? [.. Bom, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text);

    private NoteTodo Only() => Assert.Single(NoteTodos.Load(store, index));

    // AC-6: only the first "{ }" on the line becomes "{x}"; CRLF, BOM and every other byte stay.
    [Fact]
    public void Complete_UnchangedLine_TicksOnlyItsBox_KeepingCrlfAndBom()
    {
        Write("Meeting", Utf8("# Meeting\r\n\r\n  - { } call Anna { } due:2026-11-20\r\nend { }\r\n", bom: true));
        NoteTodo todo = Only();

        Assert.Equal(NoteTodoResult.Done, NoteTodos.Complete(store, todo));

        Assert.Equal(
            Utf8("# Meeting\r\n\r\n  - {x} call Anna { } due:2026-11-20\r\nend { }\r\n", bom: true),
            File.ReadAllBytes(PathOf("Meeting")));
        Assert.Empty(NoteTodos.Load(store, index));
    }

    // AC-6: a later line of several is the one ticked.
    [Fact]
    public void Complete_PicksTheIndexedLine_AmongEqualOnes()
    {
        Write("Meeting", Utf8("- { } same\n- { } same\n"));
        NoteTodo second = NoteTodos.Load(store, index)[1];

        Assert.Equal(NoteTodoResult.Done, NoteTodos.Complete(store, second));

        Assert.Equal(Utf8("- { } same\n- {x} same\n"), File.ReadAllBytes(PathOf("Meeting")));
    }

    // AC-7: an edited, moved or already ticked line writes nothing.
    [Theory]
    [InlineData("- { } call Anna today\n")]
    [InlineData("intro\n- { } call Anna\n")]
    [InlineData("- {x} call Anna\n")]
    [InlineData("```\n- { } call Anna\n```\n")]
    [InlineData("")]
    public void Complete_ChangedLine_WritesNothing(string now)
    {
        Write("Meeting", Utf8("- { } call Anna\n"));
        NoteTodo todo = Only();
        Write("Meeting", Utf8(now));

        Assert.Equal(NoteTodoResult.Changed, NoteTodos.Complete(store, todo));

        Assert.Equal(Utf8(now), File.ReadAllBytes(PathOf("Meeting")));
    }

    // AC-7: a note that is gone (or now only differs in case) is Missing.
    [Fact]
    public void Complete_MissingNote_IsMissing()
    {
        Write("Meeting", Utf8("- { } call Anna\n"));
        NoteTodo todo = Only();
        File.Delete(PathOf("Meeting"));

        Assert.Equal(NoteTodoResult.Missing, NoteTodos.Complete(store, todo));
        Assert.Equal(
            NoteTodoResult.Missing, NoteTodos.Complete(store, new NoteTodo("meeting", 1, "call Anna", null, "- { } call Anna")));
        Assert.False(File.Exists(PathOf("Meeting")));
    }

    // AC-6: the file changing between the read and the save gives Changed, and the other writer's bytes stay.
    [Fact]
    public void Complete_FileChangedBeforeSave_IsChanged()
    {
        Write("Meeting", Utf8("- { } call Anna\n"));
        NoteTodo todo = Only();
        byte[] other = Utf8("- { } call Anna\nmore\n");

        NoteTodoResult result = NoteTodos.Complete(store, todo, null, () => Write("Meeting", other));

        Assert.Equal(NoteTodoResult.Changed, result);
        Assert.Equal(other, File.ReadAllBytes(PathOf("Meeting")));
    }

    // AC-7: a note that can't be read is Failed, never an exception.
    [Fact]
    public void Complete_UnreadableNote_IsFailed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Write("Meeting", Utf8("- { } call Anna\n"));
        NoteTodo todo = Only();
        File.SetUnixFileMode(PathOf("Meeting"), UnixFileMode.None);
        try
        {
            Assert.Equal(NoteTodoResult.Failed, NoteTodos.Complete(store, todo));
        }
        finally
        {
            File.SetUnixFileMode(PathOf("Meeting"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    // 329 AC-2: a note with a checkbox and a todo lists only the "{ }" todo.
    [Fact]
    public void Load_CheckboxAndTodo_ListsOnlyTheTodo()
    {
        Write("Meeting", Utf8("- [ ] a\n- { } b\n"));

        NoteTodo todo = Only();

        Assert.Equal(("b", 2), (todo.Text, todo.Line));
    }

    // 329 AC-3: ticking writes "{x}", keeps CRLF and BOM, and the old bytes become a history version.
    [Fact]
    public void Complete_WithHistory_WritesBraceX_KeepsOldBytesAsVersion()
    {
        byte[] before = Utf8("# Meeting\r\n- [ ] keep\r\n- { } call Anna\r\n", bom: true);
        Write("Meeting", before);
        NoteTodo todo = Only();
        NoteHistory history = new NoteHistory(store, () => Created);

        Assert.Equal(NoteTodoResult.Done, NoteTodos.Complete(store, todo, history));

        Assert.Equal(Utf8("# Meeting\r\n- [ ] keep\r\n- {x} call Anna\r\n", bom: true), File.ReadAllBytes(PathOf("Meeting")));
        NoteInfo note = store.List().Single(n => n.Name == "Meeting");
        NoteVersion version = Assert.Single(history.Versions(note));
        Assert.True(history.TryRead(note, version.Id, out byte[]? kept));
        Assert.Equal(before, kept);
    }

    // AC-4: by due date; stored todos first on a date; note todos by name ignoring case, then ordinal, then line;
    // undated note todos last.
    [Fact]
    public void Merge_OrdersByDue_StoredFirst_ThenNoteAndLine_UndatedLast()
    {
        DateOnly d1 = new(2026, 11, 1);
        DateOnly d2 = new(2026, 11, 2);
        DateOnly d3 = new(2026, 11, 3);
        TodoItem[] stored =
        [
            new(Guid.NewGuid(), "s1", d1, Created),
            new(Guid.NewGuid(), "s2a", d2, Created),
            new(Guid.NewGuid(), "s2b", d2, Created),
            new(Guid.NewGuid(), "s3", d3, Created),
        ];
        NoteTodo[] notes =
        [
            new("beta", 1, "n-undated-beta", null, "r"),
            new("b", 9, "n2-b9", d2, "r"),
            new("B", 3, "n2-B3", d2, "r"),
            new("a", 5, "n2-a5", d2, "r"),
            new("a", 2, "n2-a2", d2, "r"),
            new("Alpha", 1, "n-undated-Alpha", null, "r"),
            new("z", 1, "n0", new DateOnly(2026, 10, 30), "r"),
            new("z", 2, "n4", new DateOnly(2026, 11, 4), "r"),
        ];

        IReadOnlyList<TodoEntry> merged = NoteTodos.Merge(stored, notes);

        Assert.Equal(
            ["n0", "s1", "s2a", "s2b", "n2-a2", "n2-a5", "n2-B3", "n2-b9", "s3", "n4", "n-undated-Alpha", "n-undated-beta"],
            merged.Select(e => e.Text));
        Assert.Same(stored[0], merged[1].Stored);
        Assert.Null(merged[1].Note);
        Assert.Same(notes[6], merged[0].Note);
        Assert.Null(merged[0].Stored);
        Assert.Null(merged[^1].Due);
    }

    [Fact]
    public void Merge_EmptyLists_IsEmpty()
    {
        Assert.Empty(NoteTodos.Merge([], []));
    }

    // AC-11: the picker's rules over the merged list.
    [Fact]
    public void PickEntry_PicksNoteTodosByText()
    {
        TodoEntry[] entries =
        [
            new(new TodoItem(Guid.NewGuid(), "Buy milk", new DateOnly(2026, 11, 1), Created)),
            new(new NoteTodo("Meeting", 2, "call Anna", null, "- { } call Anna")),
            new(new NoteTodo("Meeting", 3, "call Anna back", null, "- { } call Anna back")),
        ];

        TodoPick equal = TodoPicker.PickEntry(entries, "CALL ANNA");
        TodoPick only = TodoPicker.PickEntry(entries, "back");
        TodoPick several = TodoPicker.PickEntry(entries, "a");

        Assert.Same(entries[1], equal.Entry);
        Assert.Null(equal.Todo);
        Assert.Same(entries[2], only.Entry);
        Assert.Null(several.Entry);
        Assert.Equal(2, several.Matches);
        Assert.Same(entries[0].Stored, TodoPicker.PickEntry(entries, "milk").Todo);
    }

    // A missing data folder has no note todos and is not created.
    [Fact]
    public void Load_MissingFolder_EmptyAndNotCreated()
    {
        string missing = Path.Combine(root, "missing");

        Assert.Empty(NoteTodos.Load(new NoteStore(missing), new TagIndex(missing)));
        Assert.False(Directory.Exists(missing));
    }
}
