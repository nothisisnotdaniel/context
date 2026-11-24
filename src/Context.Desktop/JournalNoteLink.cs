namespace Context;

/// <summary>A plain file selected in Journal. Its editor reads through the journal's verified note directory.</summary>
public sealed class JournalNoteLink : IDisposable
{
    private readonly NoteInfo note;
    private readonly JournalDirectory? source;
    private JournalDirectory? directory;

    internal JournalNoteLink(NoteInfo note, JournalDirectory? source)
    {
        this.note = note;
        this.source = source;
    }

    internal string Name => note.Name;
    internal NoteReference Target => NoteReference.Stored(Name, path: note.Path, open: OpenPlain);
    private FileStream OpenPlain() => directory?.OpenNote(note) ?? throw new IOException("Note folder unavailable.");

    // After any discard prompt, check the selected file and acquire the editor's directory handle.
    internal Task<bool> Bind() => Task.Run(() =>
    {
        try
        {
            directory = source?.Retain();
            return Target.Matches(note);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    });

    internal Task<EditableNote?> ReadForEdit() => Task.Run(() =>
    {
        try
        {
            return NoteStore.ReadSelectedForEdit(note, Target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    });

    public void Dispose() => directory?.Dispose();
}
