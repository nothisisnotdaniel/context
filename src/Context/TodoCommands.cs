using System.Globalization;
using Spectre.Console;

namespace Context;

/// <summary>The <c>/todo</c> and <c>/done</c> commands over a <see cref="TodoStore"/>.</summary>
internal static class TodoCommands
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string TodoForm = "[bold]/todo <yyyy-MM-dd|today|tomorrow> <text>[/]";
    private const string DoneForm = "[bold]/done <text>[/]";
    private const string Unreadable =
        "Can't read todos.json, so nothing was changed. Fix or move the file and try again.";

    /// <summary>
    /// Adds <c>/todo</c> and <c>/done</c> to <paramref name="registry"/>. <paramref name="beforeComplete"/> is a
    /// test hook (like <c>DesktopActions.BeforeWrite</c>): <c>/done</c> calls it once after the pick and just
    /// before <see cref="TodoStore.Complete"/>. Production code never passes it.
    /// </summary>
    public static void Register(
        CommandRegistry registry, TodoStore store, Func<DateOnly> today, Action? beforeComplete = null)
    {
        registry.Register("/todo", "Add a todo: /todo <date> <text>", argument => Add(store, today, argument));
        registry.Register("/done", "Mark a todo done: /done <text>", argument => Done(store, argument, beforeComplete));
    }

    private static CommandResult Add(TodoStore store, Func<DateOnly> today, string argument)
    {
        string input = argument.Trim();
        int split = input.IndexOfAny([' ', '\t']);
        if (input.Length == 0 || split < 0)
        {
            return CommandResult.Error($"A todo needs a date and text. Type {TodoForm}.");
        }

        string token = input[..split];
        string text = input[(split + 1)..].Trim();
        if (text.Length == 0)
        {
            return CommandResult.Error($"A todo needs a date and text. Type {TodoForm}.");
        }

        if (!TodoInput.TryParseDate(token, today(), out DateOnly due))
        {
            return CommandResult.Error($"That date isn't valid. Type {TodoForm}.");
        }

        if (text.Length > TodoInput.MaxTextLength)
        {
            return CommandResult.Error($"A todo can be at most {TodoInput.MaxTextLength} characters. Type {TodoForm}.");
        }

        TodoItem todo;
        try
        {
            todo = store.Add(text, due);
        }
        catch (TodoStoreException)
        {
            return CommandResult.Error(Unreadable);
        }

        return new CommandResult(new Markup(
            $"Added [steelblue1]{Shown(todo.Text)}[/] (due {todo.Due.ToString(DateFormat, CultureInfo.InvariantCulture)})"));
    }

    private static CommandResult Done(TodoStore store, string argument, Action? beforeComplete)
    {
        string query = argument.Trim();
        if (query.Length == 0)
        {
            return CommandResult.Error($"Type {DoneForm} with the todo's text, or a part of it.");
        }

        IReadOnlyList<TodoItem> todos;
        try
        {
            todos = store.List();
        }
        catch (TodoStoreException)
        {
            return CommandResult.Error(Unreadable);
        }

        if (todos.Count == 0)
        {
            return CommandResult.Error("There are no todos to mark done.");
        }

        TodoPick pick = TodoPicker.Pick(todos, query);
        if (pick.Todo is null)
        {
            string shown = Shown(query);
            return CommandResult.Error(pick.Matches == 0
                ? $"No todo matches \"{shown}\"."
                : $"{pick.Matches.ToString(CultureInfo.InvariantCulture)} todos match \"{shown}\". Type more of the todo's text.");
        }

        // The text only picks the todo; it is removed by its id, so a list that changed in between can't make
        // another todo go away.
        beforeComplete?.Invoke();
        TodoItem? done;
        try
        {
            done = store.Complete(pick.Todo.Id);
        }
        catch (TodoStoreException)
        {
            return CommandResult.Error(Unreadable);
        }

        if (done is null)
        {
            return CommandResult.Error("The todo list changed. Nothing was done.");
        }

        return new CommandResult(new Markup($"Done: [steelblue1]{Shown(done.Text)}[/]"));
    }

    // Todo text as markup-safe, single-line text.
    private static string Shown(string text) => Markup.Escape(TodoSidebar.OneLine(text));
}
