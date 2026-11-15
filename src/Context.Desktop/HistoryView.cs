namespace Context;

/// <summary>What <see cref="DesktopState.History"/> found.</summary>
internal enum HistoryKind
{
    /// <summary>The versions (maybe none) and the saved text are known.</summary>
    Shown,

    /// <summary>The note, its saved text or its history can't be read.</summary>
    Unreadable,
}

/// <summary>
/// The kept versions of one note, newest first, and the note's saved text as the editor holds it (<c>\n</c> line
/// endings, no byte order mark), which each version is compared to.
/// </summary>
internal sealed record HistoryView
{
    public HistoryView(HistoryKind kind, IReadOnlyList<NoteVersion> versions, string current)
    {
        Kind = kind;
        Versions = versions;
        Current = current;
    }

    public HistoryKind Kind { get; }

    public IReadOnlyList<NoteVersion> Versions { get; }

    public string Current { get; }

    /// <summary>The view for a history that can't be read.</summary>
    public static HistoryView Unreadable { get; } = new HistoryView(HistoryKind.Unreadable, [], string.Empty);

    /// <summary>Returns only the type name, so formatting the view never prints the note's text.</summary>
    public override string ToString() => nameof(HistoryView);
}
