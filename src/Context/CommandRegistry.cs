using Spectre.Console;
using Spectre.Console.Rendering;

namespace Context;

/// <summary>What a command handler hands back to the loop.</summary>
internal sealed record CommandResult
{
    /// <param name="message">Shown in the main view under the title until the next command, or <c>null</c> for nothing.</param>
    /// <param name="quit">True to end the loop with exit code 0.</param>
    /// <param name="hint">Shown on the hint row just above the input box until the next command, or <c>null</c> for nothing.</param>
    public CommandResult(IRenderable? message = null, bool quit = false, IRenderable? hint = null)
    {
        Message = message;
        Quit = quit;
        Hint = hint;
    }

    public static CommandResult None { get; } = new();

    /// <summary>Shown in the main view under the title until the next command, or <c>null</c> for nothing.</summary>
    public IRenderable? Message { get; }

    /// <summary>Shown on the hint row just above the input box until the next command, or <c>null</c> for nothing.</summary>
    public IRenderable? Hint { get; }

    /// <summary>True to end the loop with exit code 0.</summary>
    public bool Quit { get; }

    /// <summary>
    /// A red hint for errors. <paramref name="markup"/> is wrapped in <c>[red]…[/]</c>; any user
    /// text in it must already be escaped with <see cref="Markup.Escape"/>.
    /// </summary>
    public static CommandResult Error(string markup) => new(hint: new Markup($"[red]{markup}[/]"));
}

/// <summary>A registered command. <see cref="Handler"/> receives the text after the name, trimmed.</summary>
internal sealed record Command
{
    public Command(string name, string description, Func<string, CommandResult> handler)
    {
        Name = name;
        Description = description;
        Handler = handler;
    }

    public string Name { get; }

    public string Description { get; }

    public Func<string, CommandResult> Handler { get; }
}

/// <summary>
/// The single registry of <c>/</c> commands. Every feature adds its commands here, and
/// <c>/help</c> lists whatever is registered. Names are matched case-insensitively.
/// </summary>
internal sealed class CommandRegistry
{
    private readonly Dictionary<string, Command> commands = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every registered command, ordered by name.</summary>
    public IEnumerable<Command> Commands => commands.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a registry with the built-in <c>/help</c> and <c>/quit</c> commands.</summary>
    public static CommandRegistry CreateDefault()
    {
        CommandRegistry registry = new CommandRegistry();
        registry.Register("/help", "List all commands", _ => new CommandResult(registry.RenderHelp()));
        registry.Register("/quit", "Exit Context", _ => new CommandResult(quit: true));
        return registry;
    }

    /// <summary>
    /// Adds a command. Throws <see cref="ArgumentException"/> if the name does not start with
    /// <c>/</c>, contains whitespace, or is already registered (ignoring case).
    /// </summary>
    public void Register(string name, string description, Func<string, CommandResult> handler)
    {
        if (!name.StartsWith('/') || name.Length < 2 || name.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"Command name must start with '/' and contain no spaces: '{name}'.", nameof(name));
        }

        if (!commands.TryAdd(name, new Command(name, description, handler)))
        {
            throw new ArgumentException($"Command '{name}' is already registered.", nameof(name));
        }
    }

    /// <summary>Looks up a command by name, ignoring case. Returns <c>false</c> if it is not registered.</summary>
    public bool TryGet(string name, out Command command) => commands.TryGetValue(name, out command!);

    /// <summary>
    /// The commands that match the command token of <paramref name="line"/> (the text up to the
    /// first whitespace), best first. An empty token (just <c>/</c>) returns every command in name
    /// order. Otherwise each name scores the smaller Levenshtein distance to the whole name or to
    /// its prefix of the token's length, so a prefix scores 0 and typos still match. A command is
    /// kept if its score is 0 for a 1-letter token, at most 1 for 2–3 letters and at most 2 for
    /// longer tokens. Results are sorted by score, then by name.
    /// </summary>
    public IReadOnlyList<Command> Match(string line)
    {
        int end = 0;
        while (end < line.Length && !char.IsWhiteSpace(line[end]))
        {
            end++;
        }

        string token = line[..end];
        if (token.StartsWith('/'))
        {
            token = token[1..];
        }

        token = token.ToLowerInvariant();
        if (token.Length == 0)
        {
            return Commands.ToList();
        }

        int limit = token.Length switch
        {
            1 => 0,
            <= 3 => 1,
            _ => 2,
        };

        List<(Command Command, int Score)> scored = [];
        foreach (Command command in commands.Values)
        {
            string name = command.Name[1..].ToLowerInvariant();
            string prefix = name[..Math.Min(token.Length, name.Length)];
            int score = Math.Min(Levenshtein(token, name), Levenshtein(token, prefix));
            if (score <= limit)
            {
                scored.Add((command, score));
            }
        }

        return scored
            .OrderBy(s => s.Score)
            .ThenBy(s => s.Command.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.Command)
            .ToList();
    }

    /// <summary>The Levenshtein (edit) distance between two strings: inserts, deletes and substitutions.</summary>
    internal static int Levenshtein(string a, string b)
    {
        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>A table of every registered command and its description.</summary>
    public IRenderable RenderHelp()
    {
        Table table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("[grey]Command[/]")
            .AddColumn("[grey]Description[/]");

        foreach (Command command in Commands)
        {
            table.AddRow(
                new Markup($"[steelblue1]{Markup.Escape(command.Name)}[/]"),
                new Text(command.Description));
        }

        return table;
    }
}
