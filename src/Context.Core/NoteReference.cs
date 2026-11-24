namespace Context;

internal interface INoteFileSource
{
    public FileStream OpenNote(NoteInfo note);
    public void Remove(NoteInfo note, string path, bool directory);
    public void RememberParent(NoteInfo note);
}

/// <summary>A typed lookup, or a file selected from the store with its exact name, kind and optional listed path.</summary>
internal readonly struct NoteReference
{
    private readonly string? path;
    private readonly NoteInfo? boundNote;

    private NoteReference(string name, bool? sensitive, string? path, NoteInfo? boundNote, INoteFileSource? source)
    {
        Name = name;
        Sensitive = sensitive;
        this.path = path;
        this.boundNote = boundNote;
        Source = source;
        Remove = boundNote is not null && source is not null ? (child, folder) => source.Remove(boundNote, child, folder) : null;
    }

    public string Name { get; }
    public bool? Sensitive { get; }
    public INoteFileSource? Source { get; }
    // A selected Journal file removes entries relative to its verified containing directory.
    public Action<string, bool>? Remove { get; }

    public static implicit operator NoteReference(string typed) => new(typed, null, null, null, null);

    public static NoteReference Stored(string name, bool sensitive = false, string? path = null) => new(name, sensitive, path, null, null);

    public static NoteReference Bound(NoteInfo note, INoteFileSource source) => new(note.Name, false, note.Path, note, source);

    public void RememberDestination(NoteInfo note) => Source?.RememberParent(note);

    public static NoteReference ForFile(string name, string path) =>
        Stored(name, string.Equals(Path.GetExtension(path), NoteStore.SensitiveExtension, StringComparison.OrdinalIgnoreCase), path);

    public NoteReference Resolved(NoteInfo note) => Source is null ? note.Reference : this;

    public bool Matches(NoteInfo note)
    {
        if (Name != note.Name || (path is not null && path != note.Path))
        {
            return false;
        }
        if (Source is null)
        {
            return true;
        }
        try
        {
            using FileStream file = Source.OpenNote(boundNote!);
            return file.Length <= NoteStore.MaxEditBytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public byte[] ReadBytes(string listedPath, long limit)
    {
        if (Source is null)
        {
            return BoundedFile.Read(listedPath, limit);
        }
        using FileStream file = Source.OpenNote(boundNote!);
        return BoundedFile.Read(file, limit);
    }
}
