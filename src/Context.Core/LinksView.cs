namespace Context;

/// <summary>What <see cref="LinksView.Build"/> found.</summary>
internal enum LinksViewKind
{
    /// <summary>No note name was given.</summary>
    MissingArgument,

    /// <summary>No indexed note is called <see cref="LinksView.Typed"/>.</summary>
    NoSuchNote,

    /// <summary><see cref="LinksView.Note"/> shares no tag with another note.</summary>
    NoLinks,

    /// <summary><see cref="LinksView.Links"/> holds the notes that share a tag with the note.</summary>
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

    public LinksViewKind Kind { get; }

    /// <summary>The name as typed, trimmed.</summary>
    public string Typed { get; }

    /// <summary>The indexed note that matched (<c>NoLinks</c>, <c>Links</c>), else <c>null</c>.</summary>
    public string? Note { get; }

    /// <summary>The linked notes and their shared tags (<c>Links</c> only).</summary>
    public IReadOnlyList<NoteLink> Links { get; }

    /// <summary>
    /// Syncs the index and finds the note called <paramref name="argument"/> (as typed, then without one trailing
    /// <c>.md</c>) and the notes that share a tag with it.
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
