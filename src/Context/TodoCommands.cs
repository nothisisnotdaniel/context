using System.Globalization;
using Spectre.Console;

namespace Context;

/// <summary>The <c>/todo</c> and <c>/done</c> commands over a <see cref="TodoStore"/>.</summary>
internal static class TodoCommands
{
    /// <summary>The longest todo text <c>/todo</c> accepts.</summary>
    public const int MaxTextLength = 200;

    private const string DateFormat = "yyyy-MM-dd";
    private const string TodoForm = "[bold]/todo <yyyy-MM-dd|today|tomorrow> <text>[/]";
    private const string DoneForm = "[bold]/done <number>[/]";
    private const string Unreadable =
        "Can't read todos.json, so nothing was changed. Fix or move the file and try again.";

    /// <summary>Adds <c>/todo</c> and <c>/done</c> to <paramref name="registry"/>.</summary>
    public static void Register(CommandRegistry registry, TodoStore store, Func<DateOnly> today)
    {
        registry.Register("/todo", "Add a todo: /todo <date> <text>", argument => Add(store, today, argument));
        registry.Register("/done", "Mark a todo done: /done <number>", argument => Done(store, argument));
    }

    /// <summary>
    /// Parses <c>yyyy-MM-dd</c> (invariant culture, exact), <c>today</c> or <c>tomorrow</c> (any case).
    /// Returns false for anything else.
    /// </summary>
    public static bool TryParseDate(string token, DateOnly today, out DateOnly date)
    {
        if (string.Equals(token, "today", StringComparison.OrdinalIgnoreCase))
        {
            date = today;
            return true;
        }

        if (string.Equals(token, "tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            date = today.AddDays(1);
            return true;
        }

        return DateOnly.TryParseExact(token, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
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

        if (!TryParseDate(token, today(), out DateOnly due))
        {
            return CommandResult.Error($"That date isn't valid. Type {TodoForm}.");
        }

        if (text.Length > MaxTextLength)
        {
            return CommandResult.Error($"A todo can be at most {MaxTextLength} characters. Type {TodoForm}.");
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

    private static CommandResult Done(TodoStore store, string argument)
    {
        if (!int.TryParse(argument.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
        {
            return CommandResult.Error($"Type {DoneForm} with a number from the sidebar.");
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

        if (number < 1 || number > todos.Count)
        {
            return CommandResult.Error($"There is no todo {number}. Type {DoneForm} with a number from 1 to {todos.Count}.");
        }

        // The sidebar number only picks the todo; it is removed by its id, so a list that changed in between can't
        // make another todo go away.
        TodoItem? done;
        try
        {
            done = store.Complete(todos[number - 1].Id);
        }
        catch (TodoStoreException)
        {
            return CommandResult.Error(Unreadable);
        }

        if (done is null)
        {
            return CommandResult.Error($"There is no todo {number}. Type {DoneForm} with a number from the sidebar.");
        }

        return new CommandResult(new Markup($"Done: [steelblue1]{Shown(done.Text)}[/]"));
    }

    // Todo text as markup-safe, single-line text.
    private static string Shown(string text) => Markup.Escape(TodoSidebar.OneLine(text));
}
