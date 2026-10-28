using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Context;

/// <summary>
/// Builds the body of the <c>Todos</c> sidebar: one line per todo, <c>&lt;n&gt; &lt;MM-dd&gt; &lt;text&gt;</c>,
/// in <see cref="TodoStore.List"/> order. The date is red when overdue, steelblue1 when due today and
/// grey when later. Text is cleaned, escaped and cut to one line.
/// </summary>
internal sealed class TodoSidebar
{
    private const string Ellipsis = "…";

    private readonly TodoStore store;
    private readonly Func<DateOnly> today;

    public TodoSidebar(TodoStore store, Func<DateOnly> today)
    {
        this.store = store;
        this.today = today;
    }

    /// <summary>
    /// The sidebar body for a panel with <paramref name="rows"/> text rows, each <paramref name="width"/>
    /// cells wide. With more todos than rows, the last row is <c>… N more</c>.
    /// </summary>
    public IRenderable Render(int rows, int width)
    {
        rows = Math.Max(rows, 1);
        width = Math.Max(width, 1);

        IReadOnlyList<TodoItem> todos;
        try
        {
            todos = store.List();
        }
        catch (TodoStoreException)
        {
            return new Markup($"[red]{Markup.Escape(Fit("Can't read todos.json", width))}[/]");
        }

        if (todos.Count == 0)
        {
            return new Markup("[grey]No todos yet[/]");
        }

        DateOnly now = today();
        int shown = todos.Count <= rows ? todos.Count : rows - 1;
        List<IRenderable> lines = [];
        for (int i = 0; i < shown; i++)
        {
            lines.Add(Line(i + 1, todos[i], now, width));
        }

        if (shown < todos.Count)
        {
            string more = $"{Ellipsis} {todos.Count - shown} more";
            lines.Add(new Markup($"[grey]{Markup.Escape(Fit(more, width))}[/]"));
        }

        return new Rows(lines);
    }

    /// <summary>Outside text made safe for one line: <see cref="SafeText.Clean"/>, then line breaks become spaces.</summary>
    internal static string OneLine(string text) => SafeText.Clean(text).Replace('\n', ' ');

    /// <summary>Cuts <paramref name="text"/> to at most <paramref name="width"/> cells, ending in <c>…</c> if cut.</summary>
    internal static string Fit(string text, int width)
    {
        if (CellWidth(text) <= width)
        {
            return text;
        }

        StringBuilder builder = new StringBuilder();
        int used = 0;
        int room = width - 1;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int cells = Math.Max(0, rune.ToString().GetCellWidth());
            if (used + cells > room)
            {
                break;
            }

            builder.Append(rune.ToString());
            used += cells;
        }

        return builder.Append(Ellipsis).ToString();
    }

    private static int CellWidth(string text) => text.GetCellWidth();

    private static Markup Line(int number, TodoItem todo, DateOnly now, int width)
    {
        string colour = todo.Due < now ? "red" : todo.Due == now ? "steelblue1" : "grey";
        string date = todo.Due.ToString("MM-dd", CultureInfo.InvariantCulture);
        string prefix = $"{number} {date} ";
        string text = Fit(OneLine(todo.Text), Math.Max(width - CellWidth(prefix), 1));
        return new Markup($"{number} [{colour}]{date}[/] {Markup.Escape(text)}");
    }
}
