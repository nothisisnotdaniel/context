using Spectre.Console;

namespace Context;

/// <summary>The <c>/tags</c> and <c>/links</c> commands over a <see cref="TagIndex"/>.</summary>
internal static class TagCommands
{
    private const string Unreadable =
        "Can't read context.db. Move it out of the Context folder and it is rebuilt from your notes.";

    private const string LinksForm = "[bold]/links <note>[/]";

    /// <summary>Adds <c>/tags</c> and <c>/links</c> to <paramref name="registry"/>.</summary>
    public static void Register(CommandRegistry registry, TagIndex index, NoteStore store)
    {
        registry.Register("/tags", "List tags, or notes with a tag: /tags [tag]", tag => Tags(index, store, tag));
        registry.Register("/links", "Notes sharing a tag with a note: /links <note>", name => Links(index, store, name));
    }

    private static CommandResult Tags(TagIndex index, NoteStore store, string argument)
    {
        try
        {
            index.Sync(store);
            if (string.IsNullOrWhiteSpace(argument))
            {
                return AllTags(index);
            }

            string tag = TagIndex.NormalizeTag(argument);
            IReadOnlyList<string> notes = tag.Length == 0 ? [] : index.NotesWithTag(tag);
            if (notes.Count == 0)
            {
                return CommandResult.Error(
                    $"No notes tagged {Shown(tag)}. Type [bold]/tags[/] to see all tags.");
            }

            Table table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn($"[grey]Notes tagged #{Shown(tag)}[/]");
            foreach (string note in notes)
            {
                table.AddRow(new Markup($"[steelblue1]{Shown(note)}[/]"));
            }

            return new CommandResult(table);
        }
        catch (TagIndexException)
        {
            return CommandResult.Error(Unreadable);
        }
    }

    private static CommandResult AllTags(TagIndex index)
    {
        IReadOnlyList<TagCount> tags = index.Tags();
        if (tags.Count == 0)
        {
            return new CommandResult(new Markup("[grey]No tags yet. Add #tag to a note to link it.[/]"));
        }

        Table table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("[grey]Tag[/]")
            .AddColumn("[grey]Notes[/]");
        foreach (TagCount tag in tags)
        {
            table.AddRow(
                new Markup($"[steelblue1]#{Shown(tag.Tag)}[/]"),
                new Markup($"[grey]{tag.Count}[/]"));
        }

        return new CommandResult(table);
    }

    private static CommandResult Links(TagIndex index, NoteStore store, string argument)
    {
        string wanted = argument.Trim();
        if (wanted.Length == 0)
        {
            return CommandResult.Error(
                $"Type {LinksForm} to see the notes that share a tag with it. Type [bold]/notes[/] to see all notes.");
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
                return CommandResult.Error(
                    $"No note named {Shown(wanted)}. Type [bold]/notes[/] to see all notes.");
            }

            IReadOnlyList<NoteLink> links = index.Links(note);
            if (links.Count == 0)
            {
                return new CommandResult(new Markup($"[grey]{Shown(note)} has no linked notes.[/]"));
            }

            Table table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .Title($"[steelblue1]Linked to {Shown(note)}[/]")
                .AddColumn("[grey]Note[/]")
                .AddColumn("[grey]Shared tags[/]");
            foreach (NoteLink link in links)
            {
                table.AddRow(
                    new Markup($"[steelblue1]{Shown(link.Note)}[/]"),
                    new Markup($"[grey]{Shown(string.Join(", ", link.SharedTags.Select(t => "#" + t)))}[/]"));
            }

            return new CommandResult(table);
        }
        catch (TagIndexException)
        {
            return CommandResult.Error(Unreadable);
        }
    }

    // Text from disk or the user, safe for markup.
    private static string Shown(string text) => Markup.Escape(SafeText.Clean(text));
}
