namespace Context;

/// <summary>
/// What renaming a note does to the <c>[[links]]</c> in the plain notes, worked out before anything is written: the
/// names before and after, and the plain notes whose text <see cref="NoteLinkRewriter.Rewrite"/> would change. Reads
/// only (<see cref="NoteStore.List"/>, <see cref="NoteStore.ListSensitive"/> and <see cref="NoteStore.ReadForEdit"/>
/// of plain notes); a sensitive note is never read. Used by <see cref="DesktopState.LinkUpdateCount"/> and by the
/// desktop rename action.
/// </summary>
internal sealed class LinkRenamePlan
{
    private static readonly LinkRenamePlan Empty = new(string.Empty, string.Empty, new NameLookup([]), new NameLookup([]), [], false);

    private LinkRenamePlan(
        string oldName, string newName, NameLookup before, NameLookup after, IReadOnlyList<string> notes, bool invalid)
    {
        OldName = oldName;
        NewName = newName;
        Before = before;
        After = after;
        Notes = notes;
        Invalid = invalid;
    }

    /// <summary>The renamed note's full name before the rename.</summary>
    public string OldName { get; }

    /// <summary>The renamed note's full name after the rename.</summary>
    public string NewName { get; }

    /// <summary>Every note name (plain and sensitive) before the rename.</summary>
    public NameLookup Before { get; }

    /// <summary>Every note name (plain and sensitive) after the rename.</summary>
    public NameLookup After { get; }

    /// <summary>The plain notes (names before the rename, <see cref="NoteStore.List"/> order) whose links change.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>True when a rewritten link would not read back as a link to the new name (see <see cref="NoteLinkRewriter.TryRewrite"/>).</summary>
    public bool Invalid { get; }

    /// <summary>
    /// The plan for renaming <paramref name="name"/> (a plain note found through <see cref="NoteStore.TryFind"/>, else
    /// a sensitive one through <see cref="NoteStore.TryFindSensitive"/>) to <paramref name="newTitle"/>. No note
    /// changes for an invalid title, a missing note, the same name or a case-only rename, or a new name another note
    /// already has (the rename itself refuses those). A note that can't be read is left out.
    /// </summary>
    public static LinkRenamePlan For(NoteStore notes, NoteReference name, string newTitle)
    {
        string title = NoteStore.NormalizeTitle(newTitle);
        if (NoteStore.ValidateTitle(title) is not null)
        {
            return Empty;
        }

        IReadOnlyList<NoteInfo> plain = notes.List();
        IReadOnlyList<NoteInfo> sensitive = notes.ListSensitive();
        NoteInfo? source = null;
        if (!notes.TryFind(name, out source) || source is null)
        {
            if (!notes.TryFindSensitive(name, out source) || source is null)
            {
                return Empty;
            }
        }

        string oldName = source.Name;
        string newName = source.Parent is null ? title : source.Parent + "/" + title;
        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            return Empty;
        }

        List<string> names = plain.Concat(sensitive).Select(n => n.Name).ToList();
        if (names.Any(n => string.Equals(n, newName, StringComparison.OrdinalIgnoreCase)))
        {
            return Empty;
        }

        NameLookup before = new(names);
        NameLookup after = NoteLinkRewriter.Renamed(names, oldName, newName);
        List<(string Name, bool Valid)> changed = Changed(notes, plain, before, after, oldName, newName).ToList();
        bool invalid = changed.Any(c => !c.Valid);
        return new LinkRenamePlan(
            oldName, newName, before, after, changed.Select(c => c.Name).ToList(), invalid);
    }

    /// <summary>
    /// The plan for merging the plain note <paramref name="source"/> into the plain note <paramref name="target"/>:
    /// links to the source are rewritten to the target's full name, with the source gone from the names after.
    /// <see cref="Notes"/> holds the other plain notes whose links change (never the two notes themselves); the plan
    /// is <see cref="Invalid"/> when any rewrite, the two notes' own included, fails the check. Empty when either note
    /// is not a plain note or they are the same note ignoring case.
    /// </summary>
    public static LinkRenamePlan ForMerge(NoteStore notes, string target, string source)
    {
        if (!notes.TryFind(target, out NoteInfo? kept) || kept is null
            || !notes.TryFind(source, out NoteInfo? merged) || merged is null
            || string.Equals(kept.Name, merged.Name, StringComparison.OrdinalIgnoreCase))
        {
            return Empty;
        }

        IReadOnlyList<NoteInfo> plain = notes.List();
        List<string> names = plain.Concat(notes.ListSensitive()).Select(n => n.Name).ToList();
        NameLookup before = new(names);
        NameLookup after = new(names.Where(n => !string.Equals(n, merged.Name, StringComparison.Ordinal)));
        List<(string Name, bool Valid)> changed = Changed(notes, plain, before, after, merged.Name, kept.Name).ToList();
        bool invalid = changed.Any(c => !c.Valid);
        List<string> others = changed
            .Select(c => c.Name)
            .Where(n => !string.Equals(n, merged.Name, StringComparison.Ordinal) && !string.Equals(n, kept.Name, StringComparison.Ordinal))
            .ToList();
        return new LinkRenamePlan(merged.Name, kept.Name, before, after, others, invalid);
    }

    // The plain notes whose links change, with false when their rewrite fails the check; unreadable notes are left out.
    private static IEnumerable<(string Name, bool Valid)> Changed(
        NoteStore notes, IReadOnlyList<NoteInfo> plain, NameLookup before, NameLookup after, string oldName, string newName)
    {
        foreach (NoteInfo note in plain)
        {
            EditableNote opened;
            try
            {
                opened = notes.ReadForEdit(note);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                continue;
            }

            if (!NoteLinkRewriter.TryRewrite(opened.Text, before, after, oldName, newName, out string rewritten))
            {
                yield return (note.Name, false);
            }
            else if (!ReferenceEquals(rewritten, opened.Text))
            {
                yield return (note.Name, true);
            }
        }
    }

    /// <summary>Only the type name: the plan holds note names.</summary>
    public override string ToString() => nameof(LinkRenamePlan);
}
