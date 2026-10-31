namespace Context;

/// <summary>What <see cref="TagsView.Build"/> found.</summary>
internal enum TagsViewKind
{
    /// <summary>No argument: <see cref="TagsView.Tags"/> holds every tag (it may be empty).</summary>
    AllTags,

    /// <summary><see cref="TagsView.Notes"/> holds the notes with <see cref="TagsView.Tag"/>.</summary>
    NotesWithTag,

    /// <summary>No note has <see cref="TagsView.Tag"/>.</summary>
    NoSuchTag,

    /// <summary><c>context.db</c> can't be read.</summary>
    IndexUnreadable,
}

/// <summary>
/// The data of <c>/tags [tag]</c>, with no rendering. Tags and note names are raw text from the notes: every
/// front end must clean them (<see cref="SafeText.Clean"/>) and escape them for its own output.
/// </summary>
internal sealed record TagsView
{
    public TagsView(TagsViewKind kind, string tag, IReadOnlyList<TagCount> tags, IReadOnlyList<string> notes)
    {
        Kind = kind;
        Tag = tag;
        Tags = tags;
        Notes = notes;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never
    /// print them. Front ends draw it through their <c>Render</c> step.
    /// </summary>
    public override string ToString() => nameof(TagsView);

    /// <summary>
    /// What was found; read it first to pick the fields to use: <c>AllTags</c> → <see cref="Tags"/>;
    /// <c>NotesWithTag</c> → <see cref="Tag"/> and <see cref="Notes"/>; <c>NoSuchTag</c> → <see cref="Tag"/>;
    /// <c>IndexUnreadable</c> → no field (draw an error). The fields a kind doesn't use are empty, never null.
    /// </summary>
    public TagsViewKind Kind { get; }

    /// <summary>
    /// The asked-for tag, normalised with <see cref="TagIndex.NormalizeTag"/> (raw: clean and escape it before
    /// showing it). Filled for <c>NotesWithTag</c> and <c>NoSuchTag</c> (for <c>NoSuchTag</c> it may be empty when
    /// the argument held no valid tag); every other kind (<c>AllTags</c>, <c>IndexUnreadable</c>) gives empty
    /// (<c>""</c>), never null.
    /// </summary>
    public string Tag { get; }

    /// <summary>
    /// Every tag with its note count, for <c>AllTags</c> (it may be empty when no note has a tag). Every other
    /// kind gives empty (<c>[]</c>), never null. The tags are raw text: clean and escape them before showing them.
    /// </summary>
    public IReadOnlyList<TagCount> Tags { get; }

    /// <summary>
    /// The names of the notes with <see cref="Tag"/>, for <c>NotesWithTag</c> (never empty then). Every other kind
    /// gives empty (<c>[]</c>), never null. The names are raw text: clean and escape them before showing them.
    /// </summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>Syncs the index, then lists every tag (blank <paramref name="argument"/>) or a tag's notes.</summary>
    public static TagsView Build(TagIndex index, NoteStore store, string argument)
    {
        try
        {
            index.Sync(store);
            if (string.IsNullOrWhiteSpace(argument))
            {
                return new TagsView(TagsViewKind.AllTags, string.Empty, index.Tags(), []);
            }

            string tag = TagIndex.NormalizeTag(argument);
            IReadOnlyList<string> notes = tag.Length == 0 ? [] : index.NotesWithTag(tag);
            TagsViewKind kind = notes.Count == 0 ? TagsViewKind.NoSuchTag : TagsViewKind.NotesWithTag;
            return new TagsView(kind, tag, [], notes);
        }
        catch (TagIndexException)
        {
            return new TagsView(TagsViewKind.IndexUnreadable, string.Empty, [], []);
        }
    }
}
