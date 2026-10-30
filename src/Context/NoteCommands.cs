using System.Globalization;
using Spectre.Console;

namespace Context;

/// <summary>The <c>/new</c>, <c>/notes</c> and <c>/open</c> commands over a <see cref="NoteStore"/>.</summary>
internal static class NoteCommands
{
    private const string DateFormat = "yyyy-MM-dd HH:mm";

    /// <summary>Adds <c>/new</c>, <c>/notes</c> and <c>/open</c> to <paramref name="registry"/>.</summary>
    public static void Register(CommandRegistry registry, NoteStore store)
    {
        registry.Register("/new", "Create a note", title => New(store, title));
        registry.Register("/notes", "List notes", _ => Render(store.List()));
        registry.Register("/open", "Open a note", name => Open(store, name));
    }

    private static CommandResult New(NoteStore store, string input)
    {
        string title = NoteStore.NormalizeTitle(input);
        if (string.IsNullOrWhiteSpace(title))
        {
            return CommandResult.Error("A note needs a title. Type [bold]/new <title>[/] to create one.");
        }

        string? error = NoteStore.ValidateTitle(title);
        if (error is not null)
        {
            return CommandResult.Error($"{Markup.Escape(error)} Type [bold]/new <title>[/] with another title.");
        }

        try
        {
            NoteInfo note = store.Create(title);
            return new CommandResult(new Markup($"Created [steelblue1]{Markup.Escape(SafeText.Clean(note.Name))}[/]"));
        }
        catch (NoteExistsException)
        {
            string shown = Markup.Escape(SafeText.Clean(title));
            return CommandResult.Error($"Note {shown} already exists. Type [bold]/open {shown}[/] to see it.");
        }
    }

    /// <summary>
    /// Draws the <c>/notes</c> list (<see cref="NoteStore.List"/> is its data step). Note names are cleaned and
    /// escaped here.
    /// </summary>
    internal static CommandResult Render(IReadOnlyList<NoteInfo> notes)
    {
        if (notes.Count == 0)
        {
            return new CommandResult(new Markup("[grey]No notes yet. Type /new <title> to create one.[/]"));
        }

        Table table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("[grey]Note[/]")
            .AddColumn("[grey]Modified[/]");

        foreach (NoteInfo note in notes)
        {
            table.AddRow(
                new Markup($"[steelblue1]{Markup.Escape(SafeText.Clean(note.Name))}[/]"),
                new Markup($"[grey]{note.Modified.ToString(DateFormat, CultureInfo.InvariantCulture)}[/]"));
        }

        return new CommandResult(table);
    }

    private static CommandResult Open(NoteStore store, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return CommandResult.Error(
                "Type [bold]/open <name>[/] to open a note. Type [bold]/notes[/] to see all notes.");
        }

        if (!store.TryOpen(name, out Note? note) || note is null)
        {
            return CommandResult.Error(
                $"No note named {Markup.Escape(name)}. Type [bold]/notes[/] to see all notes.");
        }

        Panel panel = new Panel(new Text(SafeText.Clean(note.Text)))
            .Header($"[steelblue1]{Markup.Escape(SafeText.Clean(note.Name))}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey)
            .Expand();
        return new CommandResult(panel);
    }
}
