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

    /// <summary>
    /// Returns only the type name: the todo's text is raw outside text, so formatting this record must never
    /// print it.
    /// </summary>
    public override string ToString() => nameof(TodoPick);

    /// <summary>The picked todo, or <c>null</c> when the query matched no todo or more than one.</summary>
    public TodoItem? Todo { get; }

    /// <summary>1 with a picked todo; else the number of todos whose text contains the query (0, or 2 and more).</summary>
    public int Matches { get; }
}

/// <summary>
/// Picks a todo by its text, the one rule of every front end: todos are shown without numbers, picked by text and
/// then removed by their id. Shared by <c>/done</c> and <c>context todo done</c>.
/// </summary>
internal static class TodoPicker
{
    /// <summary>
    /// Compares the trimmed <paramref name="query"/>, ignoring case (ordinal), with each todo's one-line text
    /// (<see cref="OneLine"/>) in the order of <paramref name="todos"/>: the first todo whose text equals it; if
    /// none does, the only todo whose text contains it; otherwise no todo and the count of todos containing it.
    /// </summary>
    public static TodoPick Pick(IReadOnlyList<TodoItem> todos, string query)
    {
        string wanted = query.Trim();
        TodoItem? only = null;
        int containing = 0;
        foreach (TodoItem todo in todos)
        {
            string text = OneLine(todo.Text);
            if (text.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return new TodoPick(todo, 1);
            }

            if (text.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            {
                only ??= todo;
                containing++;
            }
        }

        return containing == 1 ? new TodoPick(only, 1) : new TodoPick(null, containing);
    }

    /// <summary>A todo's text as every front end shows it on one line: <see cref="SafeText.Clean"/>, then line breaks become spaces.</summary>
    public static string OneLine(string text) => SafeText.Clean(text).Replace('\n', ' ');
}
