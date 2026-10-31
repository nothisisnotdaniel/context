namespace Context;

/// <summary>What <see cref="LinksView.Build"/> found.</summary>
internal enum LinksViewKind
{
    /// <summary>No note name was given.</summary>
    MissingArgument,

    /// <summary>No indexed note is called <see cref="LinksView.Typed"/>.</summary>
    NoSuchNote,

    /// <summary>
    /// <see cref="LinksView.Note"/> shares no tag with another note, links to no note with <c>[[...]]</c> (not
    /// even a missing one) and no note links to it.
    /// </summary>
    NoLinks,

    /// <summary>
    /// <see cref="LinksView.Links"/> holds the notes that share a tag with the note, that it links to with
    /// <c>[[...]]</c> (including missing ones) or that link to it.
    /// </summary>
    Links,

    /// <summary><c>context.db</c> can't be read.</summary>
    IndexUnreadable,
}

/// <summary>
/// The data of <c>/links &lt;note&gt;</c>, with no rendering. The typed name, note names and tags are raw text:
/// every front end must clean them (<see cref="SafeText.Clean"/>) and escape them for its own output.
/// </summary>
internal sealed record LinksView
{
    public LinksView(LinksViewKind kind, string typed, string? note, IReadOnlyList<NoteLink> links)
    {
        Kind = kind;
        Typed = typed;
        Note = note;
        Links = links;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never
    /// print them. Front ends draw it through their <c>Render</c> step.
    /// </summary>
    public override string ToString() => nameof(LinksView);

    /// <summary>
    /// What was found; read it first to pick the fields to use: <c>MissingArgument</c> → no field (draw a usage
    /// hint); <c>NoSuchNote</c> → <see cref="Typed"/>; <c>NoLinks</c> → <see cref="Note"/>; <c>Links</c> →
    /// <see cref="Note"/> and <see cref="Links"/>; <c>IndexUnreadable</c> → no field (draw an error).
    /// </summary>
    public LinksViewKind Kind { get; }

    /// <summary>
    /// The name as typed, trimmed (raw: clean and escape it before showing it). Set for every kind; empty
    /// (<c>""</c>) for <c>MissingArgument</c>. Shown for <c>NoSuchNote</c>.
    /// </summary>
    public string Typed { get; }

    /// <summary>
    /// The name of the indexed note that matched (raw: clean and escape it before showing it). Non-null exactly for
    /// <c>NoLinks</c> and <c>Links</c>; every other kind gives <c>null</c>.
    /// </summary>
    public string? Note { get; }

    /// <summary>
    /// The linked notes with their shared tags, <see cref="NoteLink.Direction"/> and <see cref="NoteLink.Missing"/>
    /// (raw: clean and escape names and tags before showing them; a missing note's name is the link target as
    /// written). Non-empty only for <c>Links</c>; every other kind gives empty (<c>[]</c>), never null.
    /// </summary>
    public IReadOnlyList<NoteLink> Links { get; }

    /// <summary>
    /// Syncs the index and finds the note called <paramref name="argument"/> (as typed, then without one trailing
    /// <c>.md</c>) and the notes linked to it by shared tags or <c>[[note links]]</c> (<see cref="TagIndex.Links"/>).
    /// </summary>
    public static LinksView Build(TagIndex index, NoteStore store, string argument)
    {
        string wanted = argument.Trim();
        if (wanted.Length == 0)
        {
            return new LinksView(LinksViewKind.MissingArgument, wanted, null, []);
        }

        try
        {
            index.Sync(store);
            string? note = index.FindNote(wanted);
            if (note is null && wanted.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && wanted.Length > 3)
            {
                note = index.FindNote(wanted[..^3]);
            }

            if (note is null)
            {
                return new LinksView(LinksViewKind.NoSuchNote, wanted, null, []);
            }

            IReadOnlyList<NoteLink> links = index.Links(note);
            LinksViewKind kind = links.Count == 0 ? LinksViewKind.NoLinks : LinksViewKind.Links;
            return new LinksView(kind, wanted, note, links);
        }
        catch (TagIndexException)
        {
            return new LinksView(LinksViewKind.IndexUnreadable, wanted, null, []);
        }
    }
}
