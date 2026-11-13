using System.Globalization;
using Spectre.Console;

namespace Context;

/// <summary>
/// The <c>/new</c>, <c>/notes</c>, <c>/open</c>, <c>/export</c> and <c>/import</c> commands over a
/// <see cref="NoteStore"/>.
/// </summary>
internal static class NoteCommands
{
    private const string DateFormat = "yyyy-MM-dd HH:mm";

    /// <summary>Adds <c>/new</c>, <c>/notes</c>, <c>/open</c>, <c>/export</c> and <c>/import</c> to <paramref name="registry"/>.</summary>
    public static void Register(CommandRegistry registry, NoteStore store)
    {
        registry.Register("/new", "Create a note", title => New(store, title));
        registry.Register("/notes", "List notes", _ => Render(store.List(), store.ListSensitive()));
        registry.Register("/open", "Open a note", name => Open(store, name));
        registry.Register("/export", "Write a note to a plain file", argument => Export(store, argument));
        registry.Register("/import", "Store a Markdown file as a note", path => Import(store, path));
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
    internal static CommandResult Render(IReadOnlyList<NoteInfo> notes) => Render(notes, []);

    /// <summary>
    /// Like <see cref="Render(IReadOnlyList{NoteInfo})"/>, with the <paramref name="sensitive"/> notes
    /// (<see cref="NoteStore.ListSensitive"/>) in the same list, sorted by name, each marked with a grey <c>locked</c>.
    /// </summary>
    internal static CommandResult Render(IReadOnlyList<NoteInfo> notes, IReadOnlyList<NoteInfo> sensitive)
    {
        if (notes.Count == 0 && sensitive.Count == 0)
        {
            return new CommandResult(new Markup("[grey]No notes yet. Type /new <title> to create one.[/]"));
        }

        Table table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("[grey]Note[/]")
            .AddColumn("[grey]Modified[/]");

        IEnumerable<(NoteInfo Note, bool Locked)> all = notes.Select(n => (n, false))
            .Concat(sensitive.Select(n => (n, true)))
            .OrderBy(r => r.Item1.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Item1.Name, StringComparer.Ordinal);
        foreach ((NoteInfo note, bool locked) in all)
        {
            table.AddRow(
                new Markup($"[steelblue1]{Markup.Escape(SafeText.Clean(note.Name))}[/]{(locked ? " [grey]locked[/]" : string.Empty)}"),
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

        Note? note;
        try
        {
            if (!store.TryOpen(name, out note) || note is null)
            {
                return CommandResult.Error(Sensitive(store, name)
                    ?? $"No note named {Markup.Escape(name)}. Type [bold]/notes[/] to see all notes.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Error($"Can't read {Markup.Escape(SafeText.Clean(name.Trim()))}.");
        }

        Panel panel = new Panel(new Text(SafeText.Clean(note.Text)))
            .Header($"[steelblue1]{Markup.Escape(SafeText.Clean(note.Name))}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey)
            .Expand();
        return new CommandResult(panel);
    }

    // /export <note> <path>: the note name is the longest leading part (split at whitespace) that names a note,
    // the rest is the path. Writes a new plain file outside the Context folder, never over an existing one.
    private static CommandResult Export(NoteStore store, string argument)
    {
        const string Usage = "Type [bold]/export <note> <path>[/] to write a note to a file outside Context.";
        if (string.IsNullOrWhiteSpace(argument))
        {
            return CommandResult.Error(Usage);
        }

        NoteInfo? match = null;
        string path = string.Empty;
        for (int split = argument.Length - 1; split > 0; split--)
        {
            if (!char.IsWhiteSpace(argument[split]))
            {
                continue;
            }

            string rest = argument[(split + 1)..].Trim();
            if (rest.Length > 0 && store.TryFind(argument[..split], out match) && match is not null)
            {
                path = rest;
                break;
            }
        }

        if (match is null)
        {
            return CommandResult.Error(SensitiveAtStart(store, argument)
                ?? $"No note at the start of {Markup.Escape(SafeText.Clean(argument))}. {Usage}");
        }

        string target = FullPath(path);
        string shownTarget = Markup.Escape(SafeText.Clean(target));
        string shownName = Markup.Escape(SafeText.Clean(match.Name));
        if (store.Contains(target))
        {
            return CommandResult.Error("Export to a path outside the Context folder. Nothing was written.");
        }

        string text;
        try
        {
            text = store.Read(match);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Error($"Can't read {shownName}.");
        }

        try
        {
            NoteStore.WritePlainCopy(target, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return File.Exists(target)
                ? CommandResult.Error($"That file already exists; choose a new name: {shownTarget}")
                : CommandResult.Error($"Can't write {shownTarget}.");
        }

        return new CommandResult(
            hint: new Markup($"[grey]Exported [steelblue1]{shownName}[/] to {shownTarget}[/]"));
    }

    // /import <path>: stores a plain Markdown file as the note named after it. The source is only read.
    private static CommandResult Import(NoteStore store, string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return CommandResult.Error("Type [bold]/import <path>[/] to store a Markdown file as a note.");
        }

        string source = FullPath(argument.Trim());
        string shownSource = Markup.Escape(SafeText.Clean(source));
        if (store.Contains(source))
        {
            return CommandResult.Error("That file is already in the Context folder. Nothing changed.");
        }

        string title = NoteStore.NormalizeTitle(Path.GetFileName(source));
        string? error = NoteStore.ValidateTitle(title);
        if (error is not null)
        {
            return CommandResult.Error($"{Markup.Escape(error)} Rename the file and import it again.");
        }

        if (Sensitive(store, title) is { } sensitive)
        {
            return CommandResult.Error(sensitive);
        }

        byte[] content;
        try
        {
            content = NoteStore.ReadImport(source);
        }
        catch (FileNotFoundException)
        {
            return CommandResult.Error($"No such file: {shownSource}");
        }
        catch (InvalidDataException)
        {
            return CommandResult.Error($"Can't import a file over 5 MB: {shownSource}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Error($"Can't read {shownSource}.");
        }

        NoteInfo note;
        bool replaced;
        try
        {
            (note, replaced) = store.Save(title, content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Error($"Can't save {Markup.Escape(SafeText.Clean(title))}. Nothing changed.");
        }

        string how = replaced ? "Replaced " : "Imported ";
        return new CommandResult(hint: new Markup(
            $"[grey]{how}[steelblue1]{Markup.Escape(SafeText.Clean(note.Name))}[/]. "
            + $"The source is unchanged; delete it if you don't need the plain copy: {shownSource}[/]"));
    }

    // The red text (as markup) for a sensitive note called name, or null when there is none. Its text is never read.
    private static string? Sensitive(NoteStore store, string name) =>
        store.TryFindSensitive(name, out NoteInfo? locked) && locked is not null
            ? $"{Markup.Escape(SafeText.Clean(locked.Name))} is sensitive. Open it in the desktop app."
            : null;

    // Like Sensitive, for /export's argument: the longest leading part (split at whitespace) that names one.
    private static string? SensitiveAtStart(NoteStore store, string argument)
    {
        for (int split = argument.Length - 1; split > 0; split--)
        {
            if (char.IsWhiteSpace(argument[split]) && argument[(split + 1)..].Trim().Length > 0
                && Sensitive(store, argument[..split]) is { } found)
            {
                return found;
            }
        }

        return Sensitive(store, argument);
    }

    // The absolute form of a typed path; a leading ~ means the home folder.
    private static string FullPath(string path)
    {
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal)
            || path.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, path.Length > 2 ? path[2..] : string.Empty);
        }

        return Path.GetFullPath(path);
    }
}
