namespace Context;

/// <summary>An open <c>- [ ] ...</c> line of a plain note, as <see cref="TagIndex.NoteTodos"/> stores it.</summary>
internal sealed record NoteTodo
{
    public NoteTodo(string note, int line, string text, DateOnly? due, string raw)
    {
        Note = note;
        Line = line;
        Text = text;
        Due = due;
        Raw = raw;
    }

    /// <summary>
    /// Returns only the type name: the note name, text and line are raw outside text, so formatting this record must
    /// never print them.
    /// </summary>
    public override string ToString() => nameof(NoteTodo);

    /// <summary>The note's full name (<see cref="NoteInfo.Name"/>).</summary>
    public string Note { get; }

    /// <summary>The 1-based number of the line in the note.</summary>
    public int Line { get; }

    /// <summary>The todo's text (raw, see <see cref="ParsedNoteTodo.Text"/>).</summary>
    public string Text { get; }

    /// <summary>The due date, or <c>null</c> when the line has no <c>due:</c> token.</summary>
    public DateOnly? Due { get; }

    /// <summary>The whole line as it was when the note was indexed.</summary>
    public string Raw { get; }
}

/// <summary>
/// One row of the merged todo list: a todo from <c>todos.json</c> (<see cref="Stored"/>) or a note todo
/// (<see cref="Note"/>), never both.
/// </summary>
internal sealed record TodoEntry
{
    public TodoEntry(TodoItem stored)
    {
        Stored = stored;
    }

    public TodoEntry(NoteTodo note)
    {
        Note = note;
    }

    /// <summary>Returns only the type name, so the raw text never leaks through formatting.</summary>
    public override string ToString() => nameof(TodoEntry);

    /// <summary>The todo from <c>todos.json</c>, or <c>null</c> for a note todo.</summary>
    public TodoItem? Stored { get; }

    /// <summary>The note todo, or <c>null</c> for a todo from <c>todos.json</c>.</summary>
    public NoteTodo? Note { get; }

    /// <summary>The due date; <c>null</c> only for an undated note todo.</summary>
    public DateOnly? Due => Stored?.Due ?? Note!.Due;

    /// <summary>The raw text.</summary>
    public string Text => Stored?.Text ?? Note!.Text;
}

/// <summary>How <see cref="NoteTodos.Complete"/> ended.</summary>
internal enum NoteTodoResult
{
    /// <summary>The line's <c>{ }</c> became <c>{x}</c>.</summary>
    Done,

    /// <summary>The line is no longer the todo's line as indexed, or the file changed meanwhile; nothing was written.</summary>
    Changed,

    /// <summary>The note no longer exists; nothing was written.</summary>
    Missing,

    /// <summary>The note couldn't be read or written; nothing was changed.</summary>
    Failed,
}

/// <summary>Todos inside notes: loading them from the index, merging them with <c>todos.json</c> and ticking them.</summary>
internal static class NoteTodos
{
    /// <summary>The refusal for <see cref="NoteTodoResult.Changed"/>; <paramref name="note"/> is the shown note name.</summary>
    public static string ChangedText(string note) =>
        $"The todo's line in {note} changed. Open the note and tick it there.";

    /// <summary>The refusal for <see cref="NoteTodoResult.Missing"/>.</summary>
    public static string MissingText(string note) => $"{note} no longer exists. Nothing was done.";

    /// <summary>The failure for <see cref="NoteTodoResult.Failed"/>.</summary>
    public static string FailedText(string note) => $"Can't write {note}.";

    /// <summary>
    /// The merged list: by due date; on the same date the stored todos first (in the order given, which is
    /// <see cref="TodoStore.List"/> order), then note todos by note name ignoring case, then ordinal, then line; note
    /// todos without a due date after every dated one, in the same note and line order.
    /// </summary>
    public static IReadOnlyList<TodoEntry> Merge(IReadOnlyList<TodoItem> stored, IReadOnlyList<NoteTodo> notes)
    {
        List<NoteTodo> sortedNotes = notes
            .OrderBy(t => t.Note, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Note, StringComparer.Ordinal)
            .ThenBy(t => t.Line)
            .ToList();
        List<TodoEntry> merged = new(stored.Count + notes.Count);
        int s = 0;
        foreach (NoteTodo note in sortedNotes.Where(t => t.Due is not null).OrderBy(t => t.Due))
        {
            // OrderBy is stable, so the note/line order holds within one date.
            while (s < stored.Count && stored[s].Due <= note.Due)
            {
                merged.Add(new TodoEntry(stored[s]));
                s++;
            }

            merged.Add(new TodoEntry(note));
        }

        while (s < stored.Count)
        {
            merged.Add(new TodoEntry(stored[s]));
            s++;
        }

        merged.AddRange(sortedNotes.Where(t => t.Due is null).Select(t => new TodoEntry(t)));
        return merged;
    }

    /// <summary>
    /// Syncs <paramref name="index"/> with <paramref name="store"/> and returns its note todos; a missing data folder
    /// has none and is not created. Throws <see cref="TagIndexException"/> when <c>context.db</c> can't be read.
    /// </summary>
    public static IReadOnlyList<NoteTodo> Load(NoteStore store, TagIndex index)
    {
        if (!Directory.Exists(store.Root))
        {
            return [];
        }

        index.Sync(store);
        return index.NoteTodos();
    }

    /// <summary>
    /// Ticks <paramref name="todo"/> in its note: reads the note (<see cref="NoteStore.ReadForEdit"/>) and, only when
    /// line <see cref="NoteTodo.Line"/> is still exactly <see cref="NoteTodo.Raw"/> (ordinal) and still an open todo
    /// outside a fence, turns the first <c>{ }</c> on it into <c>{x}</c> and saves through
    /// <see cref="NoteStore.SaveIfUnchanged(EditableNote, string, out EditableNote?, NoteHistory?)"/> (every other byte,
    /// CRLF and BOM kept; the version is checked again before the move, a change in between is
    /// <see cref="NoteTodoResult.Changed"/>). With <paramref name="history"/> the previous bytes are kept as a version.
    /// Never throws for a read or write failure (<see cref="NoteTodoResult.Failed"/>).
    /// </summary>
    public static NoteTodoResult Complete(NoteStore store, NoteTodo todo, NoteHistory? history = null) =>
        Complete(store, todo, history, null);

    /// <summary>Like <see cref="Complete(NoteStore, NoteTodo, NoteHistory?)"/>; <paramref name="beforeSave"/> runs after the read, before the save.</summary>
    internal static NoteTodoResult Complete(NoteStore store, NoteTodo todo, NoteHistory? history, Action? beforeSave)
    {
        try
        {
            if (!store.TryFind(todo.Note, out NoteInfo? found) || found is null
                || !string.Equals(found.Name, todo.Note, StringComparison.Ordinal))
            {
                return NoteTodoResult.Missing;
            }

            EditableNote opened = store.ReadForEdit(found);
            string? ticked = Tick(opened.Text, todo);
            if (ticked is null)
            {
                return NoteTodoResult.Changed;
            }

            beforeSave?.Invoke();
            return store.SaveIfUnchanged(opened, ticked, out _, history) switch
            {
                NoteSaveResult.Saved => NoteTodoResult.Done,
                NoteSaveResult.Missing => NoteTodoResult.Missing,
                NoteSaveResult.TooLarge => NoteTodoResult.Failed,
                _ => NoteTodoResult.Changed,
            };
        }
        catch (FileNotFoundException)
        {
            return NoteTodoResult.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return NoteTodoResult.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NoteHistoryException)
        {
            return NoteTodoResult.Failed;
        }
    }

    // The text with the todo's line ticked, or null when that line is not the todo's line as indexed any more.
    private static string? Tick(string text, NoteTodo todo)
    {
        ParsedNoteTodo? current = NoteTodoParser.Parse(text).FirstOrDefault(t => t.Line == todo.Line);
        if (current is null || !string.Equals(current.Raw, todo.Raw, StringComparison.Ordinal))
        {
            return null;
        }

        int start = 0;
        for (int n = 1; n < todo.Line; n++)
        {
            start = text.IndexOf('\n', start) + 1;
        }

        int box = text.IndexOf(NoteTodoParser.TodoBox, start, StringComparison.Ordinal);
        return string.Concat(text.AsSpan(0, box), NoteTodoParser.DoneTodoBox, text.AsSpan(box + NoteTodoParser.TodoBox.Length));
    }
}
