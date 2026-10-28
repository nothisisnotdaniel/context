using Spectre.Console;
using Spectre.Console.Rendering;

namespace Context;

/// <summary>
/// The interactive main screen. Each iteration clears the screen, draws the full-terminal layout
/// (main view on the left, the todo sidebar on the right, a one-line hint row and a 3-line input
/// box at the bottom), reads one line and dispatches it through the <see cref="CommandRegistry"/>.
/// The rendered frame is kept line by line so the command popup can restore the rows it covered.
/// All output goes through the injected <see cref="IAnsiConsole"/>, so tests can use a
/// <c>TestConsole</c>.
/// </summary>
internal sealed class App
{
    private const int HintHeight = 1;
    private const int InputHeight = 3;
    private const int SidebarWidth = 32;
    private const string Prompt = "> ";

    private readonly IAnsiConsole console;
    private readonly CommandRegistry registry;
    private readonly TodoSidebar? todos;

    // The last drawn frame, one entry per screen row, so rows the command popup covered can be restored.
    private List<SegmentLine> frame = [];

    /// <param name="console">Where everything is drawn and read from.</param>
    /// <param name="registry">The commands; <see cref="CommandRegistry.CreateDefault"/> if null.</param>
    /// <param name="todos">Fills the <c>Todos</c> sidebar; without one it shows <c>No todos yet</c>.</param>
    public App(IAnsiConsole console, CommandRegistry? registry = null, TodoSidebar? todos = null)
    {
        this.console = console;
        this.registry = registry ?? CommandRegistry.CreateDefault();
        this.todos = todos;
    }

    /// <summary>Runs the loop until <c>/quit</c> or the input ends. Returns the process exit code.</summary>
    public int Run()
    {
        LineEditor editor = new LineEditor(console, registry);
        IRenderable? message = null;
        IRenderable? hint = null;

        while (true)
        {
            Draw(message, hint);

            int width = console.Profile.Width;
            int height = console.Profile.Height;
            // Inside the panel: border + 1 padding on the left, then the prompt.
            int column = 3 + Prompt.Length;
            int line = height - 1;
            console.Cursor.SetPosition(column, line);

            // The popup ends on the hint row (just above the input box) and grows upwards.
            PopupArea popup = new PopupArea(height - InputHeight, width, RestoreRow);
            string? input = editor.ReadLine(column, line, width - column - 2, popup);
            if (input is null)
            {
                return 0;
            }

            CommandResult result = Dispatch(input);
            if (result.Quit)
            {
                console.Clear(home: true);
                return 0;
            }

            message = result.Message;
            hint = result.Hint;
        }
    }

    private CommandResult Dispatch(string input)
    {
        string text = input.Trim();
        if (text.Length == 0)
        {
            return CommandResult.None;
        }

        if (!text.StartsWith('/'))
        {
            return CommandResult.Error("Commands start with [bold]/[/]. Type [bold]/help[/] to see all commands.");
        }

        int split = text.IndexOfAny([' ', '\t']);
        string name = split < 0 ? text : text[..split];
        string argument = split < 0 ? string.Empty : text[(split + 1)..].Trim();

        if (!registry.TryGet(name, out Command? command))
        {
            return CommandResult.Error(
                $"Unknown command [bold]{Markup.Escape(name)}[/]. Type [bold]/help[/] to see all commands.");
        }

        try
        {
            return command.Handler(argument);
        }
        catch (Exception ex)
        {
            return CommandResult.Error($"{Markup.Escape(command.Name)} failed: {Markup.Escape(SafeText.Clean(ex.Message))}");
        }
    }

    private void Draw(IRenderable? message, IRenderable? hint)
    {
        List<IRenderable> mainRows =
        [
            new Markup("[bold steelblue1]Context[/]"),
            new Markup("[grey]Type [bold]/help[/] for commands[/]"),
        ];
        if (message is not null)
        {
            mainRows.Add(Text.Empty);
            mainRows.Add(message);
        }

        // The sidebar panel spans every row above the hint row; its border takes 2 rows and border
        // plus padding take 4 columns.
        int sidebarRows = console.Profile.Height - HintHeight - InputHeight - 2;
        IRenderable todoList = todos?.Render(sidebarRows, SidebarWidth - 4) ?? new Markup("[grey]No todos yet[/]");
        Panel sidebar = new Panel(todoList)
            .Header("[steelblue1]Todos[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey)
            .Expand();

        Panel input = new Panel(new Markup($"[steelblue1]{Markup.Escape(Prompt)}[/]"))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.SteelBlue1)
            .Expand();

        Layout layout = new Layout("Root").SplitRows(
            new Layout("Top").SplitColumns(
                new Layout("Main", new Rows(mainRows)),
                new Layout("Sidebar", sidebar).Size(SidebarWidth)),
            new Layout("Hint", hint ?? Text.Empty).Size(HintHeight),
            new Layout("Input", input).Size(InputHeight));

        frame = Segment.SplitLines(layout.GetSegments(console));
        console.Clear(home: true);
        console.Write(new SegmentsRenderable(frame.SelectMany(l => l.Append(Segment.LineBreak)).SkipLast(1)));
    }

    // Redraws one screen row (1-based) with what the last frame had there.
    private void RestoreRow(int row)
    {
        if (row < 1 || row > frame.Count)
        {
            return;
        }

        console.Cursor.SetPosition(1, row);
        console.Write(new SegmentsRenderable(frame[row - 1]));
    }

    /// <summary>Writes already rendered segments as they are.</summary>
    private sealed class SegmentsRenderable : IRenderable
    {
        private readonly List<Segment> segments;

        public SegmentsRenderable(IEnumerable<Segment> segments)
        {
            this.segments = segments.ToList();
        }

        public Measurement Measure(RenderOptions options, int maxWidth) => new(maxWidth, maxWidth);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) => segments;
    }
}
