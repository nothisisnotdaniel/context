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

    public TagsViewKind Kind { get; }

    /// <summary>
    /// The asked-for tag, normalised with <see cref="TagIndex.NormalizeTag"/> (may be empty); empty for
    /// <c>AllTags</c>.
    /// </summary>
    public string Tag { get; }

    /// <summary>Every tag with its note count (<c>AllTags</c> only).</summary>
    public IReadOnlyList<TagCount> Tags { get; }

    /// <summary>The notes with <see cref="Tag"/> (<c>NotesWithTag</c> only).</summary>
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
