namespace Context;

/// <summary>
/// Everything the desktop window shows at one moment, built by <see cref="DesktopState.Refresh"/>. All text in it
/// is raw outside text: components pass it through <see cref="DesktopText.OneLine"/> and Blazor's encoding.
/// </summary>
internal sealed record DesktopSnapshot
{
    public DesktopSnapshot(
        bool failed,
        DateOnly today,
        CurrentView? current,
        IReadOnlyList<TodoItem> todos,
        bool todosUnreadable,
        IReadOnlyList<NoteInfo> notes,
        bool notesUnreadable,
        string folder)
    {
        Failed = failed;
        Today = today;
        Current = current;
        Todos = todos;
        TodosUnreadable = todosUnreadable;
        Notes = notes;
        NotesUnreadable = notesUnreadable;
        Folder = folder;
    }

    /// <summary>Returns only the type name, so formatting a snapshot never prints its raw text.</summary>
    public override string ToString() => nameof(DesktopSnapshot);

    /// <summary>True when building the snapshot threw; the window shows a fixed error line.</summary>
    public bool Failed { get; }

    /// <summary>The day the snapshot is for (todo date classes compare against it).</summary>
    public DateOnly Today { get; }

    /// <summary>The <c>/current</c> view, or <c>null</c> when <see cref="Failed"/>.</summary>
    public CurrentView? Current { get; }

    /// <summary>Every todo in <see cref="TodoStore.List"/> order.</summary>
    public IReadOnlyList<TodoItem> Todos { get; }

    /// <summary>True when <c>todos.json</c> can't be read; then <see cref="Todos"/> is empty.</summary>
    public bool TodosUnreadable { get; }

    /// <summary>Every note in <see cref="NoteStore.List"/> order.</summary>
    public IReadOnlyList<NoteInfo> Notes { get; }

    /// <summary>True when the notes folder can't be listed; then <see cref="Notes"/> is empty.</summary>
    public bool NotesUnreadable { get; }

    /// <summary>The data folder (raw).</summary>
    public string Folder { get; }

    /// <summary>The snapshot shown when building one threw.</summary>
    public static DesktopSnapshot ForFailure(string folder, DateOnly today) =>
        new(true, today, null, [], false, [], false, folder);
}
