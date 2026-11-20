namespace Context;

/// <summary>Where a command bar line goes (job 282).</summary>
internal enum CommandBarTarget
{
    /// <summary>Nothing runs; <see cref="CommandBarRoute.Error"/> says why.</summary>
    None,

    /// <summary>The Times tool, with <see cref="CommandBarRoute.TimesArguments"/>.</summary>
    Times,

    /// <summary>Context's own commands (<c>todo</c>, <c>note</c>, <c>capture</c>), with <see cref="CommandBarRoute.ContextLine"/>.</summary>
    Context,
}

/// <summary>What <see cref="CommandBarRouter.Route"/> made of a line.</summary>
internal sealed class CommandBarRoute
{
    public CommandBarRoute(CommandBarTarget target, IReadOnlyList<string> timesArguments, string contextLine, string error)
    {
        Target = target;
        TimesArguments = timesArguments;
        ContextLine = contextLine;
        Error = error;
    }

    public CommandBarTarget Target { get; }

    /// <summary>The words passed to <c>tm</c>, each one argument (Times only, else empty).</summary>
    public IReadOnlyList<string> TimesArguments { get; }

    /// <summary>The trimmed line for Context's hook (Context only, else empty).</summary>
    public string ContextLine { get; }

    /// <summary>The fixed text shown when nothing runs (None only, else empty).</summary>
    public string Error { get; }

    public override string ToString() => nameof(CommandBarRoute);
}

/// <summary>One completion row of the command bar: the whole line it puts in the input, and what the row shows.</summary>
internal sealed class CommandBarSuggestion
{
    public CommandBarSuggestion(string line, string label, string detail)
    {
        Line = line;
        Label = label;
        Detail = detail;
    }

    /// <summary>The line the input holds after Tab, ending in a space.</summary>
    public string Line { get; }

    public string Label { get; }

    public string Detail { get; }

    public override string ToString() => nameof(CommandBarSuggestion);
}

/// <summary>A command the bar suggests: its name, an optional alias and a short description.</summary>
internal sealed class CommandBarCommand
{
    public CommandBarCommand(string name, string? alias, string description)
    {
        Name = name;
        Alias = alias;
        Description = description;
    }

    public string Name { get; }

    public string? Alias { get; }

    public string Description { get; }

    public override string ToString() => nameof(CommandBarCommand);
}

/// <summary>
/// The command bar's rules (job 282), pure: which runner a line goes to (<see cref="Route"/>) and what the bar
/// suggests while typing (<see cref="Suggest"/>). It reads no file; the quicklist comes from the caller.
/// </summary>
internal static class CommandBarRouter
{
    /// <summary>The longest line (trimmed) that runs.</summary>
    public const int MaxLineLength = 300;

    /// <summary>The most suggestions shown.</summary>
    public const int MaxSuggestions = 6;

    public const string EmptyText = "Type a command.";

    public const string TooLongText = "The command is too long.";

    /// <summary>The Times commands the bar suggests (others still run).</summary>
    public static readonly IReadOnlyList<CommandBarCommand> TimesCommands =
    [
        new CommandBarCommand("arrive", "a", "Book your arrival"),
        new CommandBarCommand("leave", "l", "Book your leaving"),
        new CommandBarCommand("break", "b", "Book the start of a break"),
        new CommandBarCommand("breakend", "be", "Book the end of a break"),
        new CommandBarCommand("checkpoint", "cp", "Book a checkpoint: [H:mm] <work item> <name>"),
        new CommandBarCommand("summary", "s", "Show the time per work item today"),
        new CommandBarCommand("status", null, "Show today's bookings"),
    ];

    /// <summary>Context's own commands in the bar.</summary>
    public static readonly IReadOnlyList<CommandBarCommand> ContextCommands =
    [
        new CommandBarCommand("todo", null, "Add a todo: <date|today|tomorrow|every …> [HH:mm] <text>"),
        new CommandBarCommand("note", null, "Add a line to today's note"),
        new CommandBarCommand("capture", null, "Capture text to the inbox"),
    ];

    private static readonly char[] Whitespace = [' ', '\t', '\r', '\n', '\v', '\f'];

    /// <summary>
    /// Routes a line: trimmed; over <see cref="MaxLineLength"/> is <see cref="TooLongText"/>; empty, or only one
    /// <c>times</c>/<c>tm</c> word, is <see cref="EmptyText"/>; a first word <c>todo</c>, <c>note</c> or <c>capture</c>
    /// (ignoring case) goes to Context; any other line goes to Times without one leading <c>times</c>/<c>tm</c> word,
    /// its words as separate arguments. In a <c>cp</c>/<c>checkpoint</c> line the first word after an optional time word
    /// that is 1-9 ASCII digits and a quicklist work item becomes <c>#&lt;1-based position&gt;</c>.
    /// </summary>
    public static CommandBarRoute Route(string? line, IReadOnlyList<(int WorkItem, string Title)> projects)
    {
        string trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length > MaxLineLength)
        {
            return Refused(TooLongText);
        }

        List<string> words = Words(trimmed);
        if (words.Count == 0)
        {
            return Refused(EmptyText);
        }

        if (IsContextCommand(words[0]))
        {
            return new CommandBarRoute(CommandBarTarget.Context, [], trimmed, string.Empty);
        }

        if (IsTimesPrefix(words[0]))
        {
            words.RemoveAt(0);
            if (words.Count == 0)
            {
                return Refused(EmptyText);
            }
        }

        if (IsCheckpoint(words[0]))
        {
            int at = words.Count > 1 && TodoInput.LooksLikeTime(words[1]) ? 2 : 1;
            if (at < words.Count && IsWorkItemNumber(words[at]))
            {
                int position = PositionOf(projects, int.Parse(words[at], System.Globalization.CultureInfo.InvariantCulture));
                if (position > 0)
                {
                    words[at] = "#" + position.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }

        return new CommandBarRoute(CommandBarTarget.Times, words, string.Empty, string.Empty);
    }

    /// <summary>
    /// The suggestions for a line as typed, at most <see cref="MaxSuggestions"/>. With no whitespace after the command
    /// word: the command names (Context's and <see cref="TimesCommands"/>) ranked by <see cref="CommandMatcher"/>, an
    /// exact name or alias first. After a <c>cp</c>/<c>checkpoint</c> word (and an optional time word): the quicklist
    /// rows whose work item starts with the typed text or whose title contains it, ignoring case. A leading
    /// <c>times</c>/<c>tm</c> word stays in each suggested line. Anything else suggests nothing.
    /// </summary>
    public static IReadOnlyList<CommandBarSuggestion> Suggest(string? line, IReadOnlyList<(int WorkItem, string Title)> projects)
    {
        string text = (line ?? string.Empty).TrimStart();
        if (text.Length > MaxLineLength)
        {
            return [];
        }

        string prefix = string.Empty;
        string firstWord = FirstWord(text);
        if (IsTimesPrefix(firstWord) && text.Length > firstWord.Length)
        {
            int rest = firstWord.Length;
            while (rest < text.Length && char.IsWhiteSpace(text[rest]))
            {
                rest++;
            }

            prefix = text[..firstWord.Length] + " ";
            text = text[rest..];
        }

        string word = FirstWord(text);
        if (word.Length == text.Length)
        {
            return word.Length == 0 ? [] : CommandNames(prefix, word);
        }

        if (!IsCheckpoint(word))
        {
            return [];
        }

        // "cp " + optional time word, then the partial work item (no space after it yet).
        List<string> words = Words(text);
        bool endsInSpace = char.IsWhiteSpace(text[^1]);
        int at = words.Count > 1 && TodoInput.LooksLikeTime(words[1]) && (words.Count > 2 || endsInSpace) ? 2 : 1;
        string partial;
        if (words.Count == at && endsInSpace)
        {
            partial = string.Empty;
        }
        else if (words.Count == at + 1 && !endsInSpace)
        {
            partial = words[at];
        }
        else
        {
            return [];
        }

        string head = prefix + string.Join(' ', words.Take(at)) + " ";
        return projects
            .Select(p => (Number: p.WorkItem.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Title))
            .Where(p => partial.Length == 0
                || p.Number.StartsWith(partial, StringComparison.Ordinal)
                || p.Title.Contains(partial, StringComparison.OrdinalIgnoreCase))
            .Take(MaxSuggestions)
            .Select(p => new CommandBarSuggestion(head + p.Number + " ", p.Number, p.Title))
            .ToList();
    }

    private static List<CommandBarSuggestion> CommandNames(string prefix, string word)
    {
        List<(CommandBarCommand Command, string Key)> keys = ContextCommands.Concat(TimesCommands)
            .SelectMany(c => c.Alias is null ? new[] { (c, c.Name) } : new[] { (c, c.Name), (c, c.Alias) })
            .ToList();

        // An exact name or alias first, then the matcher's order; each command once.
        IEnumerable<CommandBarCommand> exact = keys
            .Where(k => string.Equals(k.Key, word, StringComparison.OrdinalIgnoreCase))
            .Select(k => k.Command);
        IEnumerable<CommandBarCommand> matched = CommandMatcher.Match(keys, k => k.Key, word).Select(k => k.Command);
        return exact.Concat(matched)
            .Distinct()
            .Take(MaxSuggestions)
            .Select(c => new CommandBarSuggestion(
                prefix + c.Name + " ",
                c.Alias is null ? c.Name : c.Name + " (" + c.Alias + ")",
                c.Description))
            .ToList();
    }

    private static CommandBarRoute Refused(string text) => new CommandBarRoute(CommandBarTarget.None, [], string.Empty, text);

    private static List<string> Words(string text) => text.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string FirstWord(string text)
    {
        int end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        return text[..end];
    }

    private static bool IsContextCommand(string word) =>
        ContextCommands.Any(c => string.Equals(c.Name, word, StringComparison.OrdinalIgnoreCase));

    private static bool IsTimesPrefix(string word) =>
        string.Equals(word, "times", StringComparison.OrdinalIgnoreCase) || string.Equals(word, "tm", StringComparison.OrdinalIgnoreCase);

    private static bool IsCheckpoint(string word) =>
        string.Equals(word, "cp", StringComparison.OrdinalIgnoreCase) || string.Equals(word, "checkpoint", StringComparison.OrdinalIgnoreCase);

    private static bool IsWorkItemNumber(string word) =>
        word.Length is >= 1 and <= 9 && word.All(c => c is >= '0' and <= '9');

    private static int PositionOf(IReadOnlyList<(int WorkItem, string Title)> projects, int workItem)
    {
        for (int i = 0; i < projects.Count; i++)
        {
            if (projects[i].WorkItem == workItem)
            {
                return i + 1;
            }
        }

        return 0;
    }
}
