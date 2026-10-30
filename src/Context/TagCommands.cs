using Spectre.Console;

namespace Context;

/// <summary>
/// The <c>/tags</c> and <c>/links</c> commands: <see cref="TagsView"/> and <see cref="LinksView"/> read the
/// <see cref="TagIndex"/>, the <c>Render</c> methods draw them with Spectre.
/// </summary>
internal static class TagCommands
{
    private const string Unreadable =
        "Can't read context.db. Move it out of the Context folder and it is rebuilt from your notes.";

    private const string LinksForm = "[bold]/links <note>[/]";

    private const string LinksUsage =
        $"Type {LinksForm} to see the notes that share a tag with it. Type [bold]/notes[/] to see all notes.";

    /// <summary>Adds <c>/tags</c> and <c>/links</c> to <paramref name="registry"/>.</summary>
    public static void Register(CommandRegistry registry, TagIndex index, NoteStore store)
    {
        registry.Register(
            "/tags", "List tags, or notes with a tag: /tags [tag]", tag => Render(TagsView.Build(index, store, tag)));
        registry.Register(
            "/links",
            "Notes sharing a tag with a note: /links <note>",
            name => Render(LinksView.Build(index, store, name)));
    }

    /// <summary>Draws a <c>/tags</c> view. Every tag and note name is cleaned and escaped here.</summary>
    internal static CommandResult Render(TagsView view)
    {
        switch (view.Kind)
        {
            case TagsViewKind.IndexUnreadable:
                return CommandResult.Error(Unreadable);
            case TagsViewKind.AllTags:
                return AllTags(view.Tags);
            case TagsViewKind.NoSuchTag:
                return CommandResult.Error(
                    $"No notes tagged {Shown(TagParser.Format(view.Tag))}. Type [bold]/tags[/] to see all tags.");
            default:
                Table table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .AddColumn($"[grey]Notes tagged {Shown(TagParser.Format(view.Tag))}[/]");
                foreach (string note in view.Notes)
                {
                    table.AddRow(new Markup($"[steelblue1]{Shown(note)}[/]"));
                }

                return new CommandResult(table);
        }
    }

    private static CommandResult AllTags(IReadOnlyList<TagCount> tags)
    {
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
                new Markup($"[steelblue1]{Shown(TagParser.Format(tag.Tag))}[/]"),
                new Markup($"[grey]{tag.Count}[/]"));
        }

        return new CommandResult(table);
    }

    /// <summary>Draws a <c>/links</c> view. The typed name, note names and tags are cleaned and escaped here.</summary>
    internal static CommandResult Render(LinksView view)
    {
        string note = view.Note ?? string.Empty;
        switch (view.Kind)
        {
            case LinksViewKind.MissingArgument:
                return CommandResult.Error(LinksUsage);
            case LinksViewKind.IndexUnreadable:
                return CommandResult.Error(Unreadable);
            case LinksViewKind.NoSuchNote:
                return CommandResult.Error(
                    $"No note named {Shown(view.Typed)}. Type [bold]/notes[/] to see all notes.");
            case LinksViewKind.NoLinks:
                return new CommandResult(new Markup($"[grey]{Shown(note)} has no linked notes.[/]"));
            default:
                Table table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .Title($"[steelblue1]Linked to {Shown(note)}[/]")
                    .AddColumn("[grey]Note[/]")
                    .AddColumn("[grey]Shared tags[/]");
                foreach (NoteLink link in view.Links)
                {
                    table.AddRow(
                        new Markup($"[steelblue1]{Shown(link.Note)}[/]"),
                        new Markup($"[grey]{Shown(string.Join(", ", link.SharedTags.Select(TagParser.Format)))}[/]"));
                }

                return new CommandResult(table);
        }
    }

    // Text from disk or the user, safe for markup.
    private static string Shown(string text) => Markup.Escape(SafeText.Clean(text));
}
