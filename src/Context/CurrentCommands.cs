using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Context;

/// <summary>
/// The <c>/current</c> view: every todo due today or earlier, and the notes linked to them. A todo links
/// to every note that has one of the tags in its text (parsed with <see cref="TagParser.Parse"/>).
/// </summary>
internal static class CurrentCommands
{
    private const string DateFormat = "yyyy-MM-dd";

    private const string TodosUnreadable = "Can't read todos.json. Fix or move the file and try again.";

    private const string IndexUnreadable =
        "Can't read context.db. Move it out of the Context folder and it is rebuilt from your notes.";

    /// <summary>Adds <c>/current</c> to <paramref name="registry"/>.</summary>
    public static void Register(
        CommandRegistry registry, TodoStore todos, TagIndex index, NoteStore notes, Func<DateOnly> today)
    {
        registry.Register(
            "/current",
            "Today's due todos and linked notes",
            _ => Current(todos, index, notes, today()));
    }

    private static CommandResult Current(TodoStore store, TagIndex index, NoteStore notes, DateOnly today)
    {
        IReadOnlyList<TodoItem> todos;
        try
        {
            todos = store.List();
        }
        catch (TodoStoreException)
        {
            return CommandResult.Error(TodosUnreadable);
        }

        List<IRenderable> parts =
        [
            new Markup($"[steelblue1]Today, {today.ToString(DateFormat, CultureInfo.InvariantCulture)}[/]"),
        ];

        List<TodoItem> due = [];
        Table dueTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[grey]Due[/]")
            .AddColumn("[grey]#[/]")
            .AddColumn("[grey]Date[/]")
            .AddColumn("[grey]Todo[/]");
        for (int i = 0; i < todos.Count; i++)
        {
            TodoItem todo = todos[i];
            if (todo.Due > today)
            {
                continue;
            }

            due.Add(todo);
            string colour = todo.Due < today ? "red" : "steelblue1";
            dueTable.AddRow(
                new Markup((i + 1).ToString(CultureInfo.InvariantCulture)),
                new Markup($"[{colour}]{todo.Due.ToString(DateFormat, CultureInfo.InvariantCulture)}[/]"),
                new Markup(Markup.Escape(TodoSidebar.OneLine(todo.Text))));
        }

        if (due.Count == 0)
        {
            parts.Add(new Markup("[grey]Nothing due today. Add one with /todo today <text>.[/]"));
            return new CommandResult(new Rows(parts));
        }

        parts.Add(dueTable);
        parts.Add(LinkedNotes(index, notes, due));
        return new CommandResult(new Rows(parts));
    }

    private static IRenderable LinkedNotes(TagIndex index, NoteStore notes, IReadOnlyList<TodoItem> due)
    {
        Dictionary<string, SortedSet<string>> linked = new(StringComparer.Ordinal);
        try
        {
            index.Sync(notes);
            IEnumerable<string> tags = due
                .SelectMany(t => TagParser.Parse(t.Text))
                .Distinct(StringComparer.Ordinal);
            foreach (string tag in tags)
            {
                foreach (string note in index.NotesWithTag(tag))
                {
                    if (!linked.TryGetValue(note, out SortedSet<string>? noteTags))
                    {
                        noteTags = new SortedSet<string>(StringComparer.Ordinal);
                        linked[note] = noteTags;
                    }

                    noteTags.Add(tag);
                }
            }
        }
        catch (TagIndexException)
        {
            return new Markup($"[red]{IndexUnreadable}[/]");
        }

        if (linked.Count == 0)
        {
            return new Markup("[grey]No linked notes. Add the same #tag to a todo and a note to link them.[/]");
        }

        Table table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[grey]Linked notes[/]")
            .AddColumn("[grey]Note[/]")
            .AddColumn("[grey]Tags[/]");
        IEnumerable<string> names = linked.Keys
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n, StringComparer.Ordinal);
        foreach (string name in names)
        {
            table.AddRow(
                new Markup($"[steelblue1]{Shown(name)}[/]"),
                new Markup($"[grey]{Shown(string.Join(", ", linked[name].Select(TagParser.Format)))}[/]"));
        }

        return table;
    }

    // Text from disk or the user, safe for markup.
    private static string Shown(string text) => Markup.Escape(SafeText.Clean(text));
}
