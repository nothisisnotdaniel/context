namespace Context;

/// <summary>A typed lookup, or a file selected from the store with its exact name, kind and optional listed path.</summary>
internal readonly struct NoteReference
{
    private readonly string? path;
    private readonly Func<FileStream>? open;

    private NoteReference(string name, bool? sensitive, string? path, Func<FileStream>? open)
    {
        Name = name;
        Sensitive = sensitive;
        this.path = path;
        this.open = open;
    }

    public string Name { get; }
    public bool? Sensitive { get; }

    public static implicit operator NoteReference(string typed) => new(typed, null, null, null);

    public static NoteReference Stored(string name, bool sensitive = false, string? path = null, Func<FileStream>? open = null) =>
        new(name, sensitive, path, open);

    public static NoteReference ForFile(string name, string path) =>
        Stored(name, string.Equals(Path.GetExtension(path), NoteStore.SensitiveExtension, StringComparison.OrdinalIgnoreCase), path);

    public NoteReference Resolved(NoteInfo note) => open is null ? note.Reference : this;

    public bool Matches(NoteInfo note)
    {
        if (Name != note.Name || (path is not null && path != note.Path))
        {
            return false;
        }
        if (open is null)
        {
            return true;
        }
        try
        {
            using FileStream file = open();
            return file.Length <= NoteStore.MaxEditBytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public byte[] ReadBytes(string listedPath, long limit)
    {
        if (open is null)
        {
            return BoundedFile.Read(listedPath, limit);
        }
        using FileStream file = open();
        return BoundedFile.Read(file, limit);
    }
}
