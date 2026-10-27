using Spectre.Console;
using Spectre.Console.Rendering;

namespace Context;

/// <summary>
/// The interactive main screen. Each iteration clears the screen, draws the full-terminal layout
/// (main view on the left, the todo sidebar on the right, a one-line hint row and a 3-line input
/// box at the bottom), reads one line and dispatches it through the <see cref="CommandRegistry"/>.
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

    public App(IAnsiConsole console, CommandRegistry? registry = null)
    {
        this.console = console;
        this.registry = registry ?? CommandRegistry.CreateDefault();
    }

    /// <summary>Runs the loop until <c>/quit</c> or the input ends. Returns the process exit code.</summary>
    public int Run()
    {
        LineEditor editor = new LineEditor(console);
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

            string? input = editor.ReadLine(column, line, width - column - 2);
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
            return CommandResult.Error($"{Markup.Escape(command.Name)} failed: {Markup.Escape(ex.Message)}");
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

        Panel sidebar = new Panel(new Markup("[grey]No todos yet[/]"))
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

        console.Clear(home: true);
        console.Write(layout);
    }
}
