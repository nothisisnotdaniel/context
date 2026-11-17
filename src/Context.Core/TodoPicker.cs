namespace Context;

/// <summary>
/// The answer of <see cref="TodoPicker.Pick"/>: the picked todo, or no todo and how many todos the query matched
/// (0, or 2 and more).
/// </summary>
internal sealed record TodoPick
{
    public TodoPick(TodoItem? todo, int matches)
    {
        Todo = todo;
        Matches = matches;
    }

    public TodoPick(TodoEntry? entry, int matches)
    {
        Entry = entry;
        Todo = entry?.Stored;
        Matches = matches;
    }

    /// <summary>
    /// Returns only the type name: the todo's text is raw outside text, so formatting this record must never
    /// print it.
    /// </summary>
    public override string ToString() => nameof(TodoPick);

    /// <summary>The picked todo, or <c>null</c> when the query matched no todo or more than one.</summary>
    public TodoItem? Todo { get; }

    /// <summary>
    /// The picked row of a merged list (<see cref="TodoPicker.PickEntry"/>), or
    /// <c>null</c>; <see cref="Todo"/> is then its stored todo (<c>null</c> for a note todo).
    /// </summary>
    public TodoEntry? Entry { get; }

    /// <summary>1 with a picked todo; else the number of todos whose text contains the query (0, or 2 and more).</summary>
    public int Matches { get; }
}

/// <summary>
/// Picks a todo by its text, the one rule of every front end: todos are shown without numbers, picked by text and
/// then removed by their id. Shared by <c>/done</c> and <c>context todo done</c>.
/// </summary>
internal static class TodoPicker
{
    /// <summary>The line when there is no open todo to pick.</summary>
    public const string NoTodosText = "There are no todos to mark done.";

    /// <summary>
    /// The line when <paramref name="pick"/> found no single todo; <paramref name="shown"/> is the query as it may be
    /// shown (already cleaned to one line).
    /// </summary>
    public static string NoPickText(TodoPick pick, string shown) =>
        pick.Matches == 0
            ? $"No todo matches \"{shown}\"."
            : $"{pick.Matches.ToString(System.Globalization.CultureInfo.InvariantCulture)} todos match \"{shown}\". Type more of the todo's text.";

    /// <summary>
    /// Compares the trimmed <paramref name="query"/>, ignoring case (ordinal), with each todo's one-line text
    /// (<see cref="OneLine"/>) in the order of <paramref name="todos"/>: the first todo whose text equals it; if
    /// none does, the only todo whose text contains it; otherwise no todo and the count of todos containing it.
    /// </summary>
    public static TodoPick Pick(IReadOnlyList<TodoItem> todos, string query)
    {
        (TodoItem? todo, int matches) = PickBy(todos, t => t.Text, query);
        return new TodoPick(todo, matches);
    }

    /// <summary>
    /// The same rules over a merged list (<see cref="NoteTodos.Merge"/>, todos.json todos and note todos alike), in
    /// its order; the pick is in <see cref="TodoPick.Entry"/>.
    /// </summary>
    public static TodoPick PickEntry(IReadOnlyList<TodoEntry> entries, string query)
    {
        (TodoEntry? entry, int matches) = PickBy(entries, e => e.Text, query);
        return new TodoPick(entry, matches);
    }

    private static (T? Item, int Matches) PickBy<T>(IReadOnlyList<T> items, Func<T, string> textOf, string query)
        where T : class
    {
        string wanted = query.Trim();
        T? only = null;
        int containing = 0;
        foreach (T item in items)
        {
            string text = OneLine(textOf(item));
            if (text.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return (item, 1);
            }

            if (text.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            {
                only ??= item;
                containing++;
            }
        }

        return containing == 1 ? (only, 1) : (null, containing);
    }

    /// <summary>A todo's text as every front end shows it on one line: <see cref="SafeText.Clean"/>, then line breaks become spaces.</summary>
    public static string OneLine(string text) => SafeText.Clean(text).Replace('\n', ' ');
}
