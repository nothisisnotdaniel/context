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
        string folder,
        IReadOnlyList<NoteInfo>? sensitive = null,
        bool sensitiveSupported = false,
        bool recoveryReady = false)
    {
        Failed = failed;
        Today = today;
        Current = current;
        Todos = todos;
        TodosUnreadable = todosUnreadable;
        Notes = notes;
        NotesUnreadable = notesUnreadable;
        Folder = folder;
        Sensitive = sensitive ?? [];
        SensitiveSupported = sensitiveSupported;
        RecoveryReady = recoveryReady;
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

    /// <summary>Every sensitive note in <see cref="NoteStore.ListSensitive"/> order: names and times only, never text.</summary>
    public IReadOnlyList<NoteInfo> Sensitive { get; }

    /// <summary>True when this OS has a key store for sensitive notes (macOS, Windows).</summary>
    public bool SensitiveSupported { get; }

    /// <summary>True when the key store holds a recovery public key, so notes can be marked sensitive.</summary>
    public bool RecoveryReady { get; }

    /// <summary>The snapshot shown when building one threw.</summary>
    public static DesktopSnapshot ForFailure(string folder, DateOnly today) =>
        new(true, today, null, [], false, [], false, folder);
}
