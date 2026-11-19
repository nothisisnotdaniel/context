namespace Context.Tests;

/// <summary>The command bar's routing and completion rules (job 282, AC-1, AC-3, AC-6).</summary>
public sealed class CommandBarRouterTests
{
    private static readonly IReadOnlyList<(int WorkItem, string Title)> Quicklist =
    [
        (4711, "Billing export"),
        (1234, "Review queue"),
        (9, "Meetings"),
    ];

    // AC-1: Context's three words go to Context with the trimmed line, ignoring case.
    [Theory]
    [InlineData("todo today x", "todo today x")]
    [InlineData("  NOTE  hello  ", "NOTE  hello")]
    [InlineData("Capture some text", "Capture some text")]
    [InlineData("todo", "todo")]
    public void Route_ContextWords_GoToContext(string line, string expected)
    {
        CommandBarRoute route = CommandBarRouter.Route(line, Quicklist);

        Assert.Equal(CommandBarTarget.Context, route.Target);
        Assert.Equal(expected, route.ContextLine);
        Assert.Empty(route.TimesArguments);
    }

    // AC-1: every other line goes to Times, one leading times/tm word stripped, words as separate arguments.
    [Theory]
    [InlineData("a", "a")]
    [InlineData("times a", "a")]
    [InlineData("tm a", "a")]
    [InlineData("TM   l", "l")]
    [InlineData("Times  status", "status")]
    [InlineData("tm tm a", "tm|a")]
    [InlineData("  b  ", "b")]
    [InlineData("summary\tyesterday", "summary|yesterday")]
    [InlineData("tm todo x", "todo|x")]
    public void Route_OtherLines_GoToTimes(string line, string expected)
    {
        CommandBarRoute route = CommandBarRouter.Route(line, Quicklist);

        Assert.Equal(CommandBarTarget.Times, route.Target);
        Assert.Equal(expected, string.Join('|', route.TimesArguments));
    }

    // AC-1 + AC-2: shell characters stay inside their one word.
    [Fact]
    public void Route_ShellCharacters_StayOneArgumentEach()
    {
        CommandBarRoute route = CommandBarRouter.Route("cp a;b $(x) |y \"q'", Quicklist);

        Assert.Equal(["cp", "a;b", "$(x)", "|y", "\"q'"], route.TimesArguments);
    }

    // AC-1: empty, only a prefix, or too long runs nothing.
    [Theory]
    [InlineData("", CommandBarRouter.EmptyText)]
    [InlineData("   ", CommandBarRouter.EmptyText)]
    [InlineData("times", CommandBarRouter.EmptyText)]
    [InlineData(" TM ", CommandBarRouter.EmptyText)]
    public void Route_EmptyLines_RunNothing(string line, string text)
    {
        CommandBarRoute route = CommandBarRouter.Route(line, Quicklist);

        Assert.Equal(CommandBarTarget.None, route.Target);
        Assert.Equal(text, route.Error);
    }

    [Fact]
    public void Route_NullLine_RunsNothing()
    {
        Assert.Equal(CommandBarRouter.EmptyText, CommandBarRouter.Route(null, Quicklist).Error);
    }

    [Fact]
    public void Route_OverMaxLength_IsTooLong()
    {
        string atMax = "a " + new string('x', CommandBarRouter.MaxLineLength - 2);
        string over = atMax + "x";

        Assert.Equal(CommandBarTarget.Times, CommandBarRouter.Route("  " + atMax + "  ", Quicklist).Target);
        CommandBarRoute route = CommandBarRouter.Route(over, Quicklist);
        Assert.Equal(CommandBarTarget.None, route.Target);
        Assert.Equal(CommandBarRouter.TooLongText, route.Error);
    }

    // AC-3: a quicklist work item becomes its 1-based position.
    [Theory]
    [InlineData("cp 1234 review", "cp|#2|review")]
    [InlineData("checkpoint 4711", "checkpoint|#1")]
    [InlineData("CP 9:30 9 standup", "CP|9:30|#3|standup")]
    [InlineData("tm cp 10:15 1234 x", "cp|10:15|#2|x")]
    [InlineData("cp 5555 review", "cp|5555|review")]
    [InlineData("cp 01234 review", "cp|#2|review")]
    [InlineData("cp review 1234", "cp|review|1234")]
    [InlineData("cp 1234567890 x", "cp|1234567890|x")]
    [InlineData("a 1234", "a|1234")]
    [InlineData("cp #2 x", "cp|#2|x")]
    public void Route_Checkpoint_RewritesQuicklistNumbers(string line, string expected)
    {
        Assert.Equal(expected, string.Join('|', CommandBarRouter.Route(line, Quicklist).TimesArguments));
    }

    [Fact]
    public void Route_Checkpoint_WithoutQuicklist_PassesThrough()
    {
        Assert.Equal("cp|1234|x", string.Join('|', CommandBarRouter.Route("cp 1234 x", []).TimesArguments));
    }

    // AC-6: names while there is no space; an exact alias first.
    [Fact]
    public void Complete_ExactAlias_ComesFirst()
    {
        IReadOnlyList<CommandBarSuggestion> rows = CommandBarRouter.Suggest("cp", Quicklist);

        Assert.Equal("checkpoint ", rows[0].Line);
        Assert.Equal("checkpoint (cp)", rows[0].Label);
        Assert.Contains("work item", rows[0].Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a", "arrive ")]
    [InlineData("be", "breakend ")]
    [InlineData("TO", "todo ")]
    [InlineData("not", "note ")]
    [InlineData("stat", "status ")]
    [InlineData("tm a", "tm arrive ")]
    [InlineData("times  l", "times leave ")]
    public void Complete_Names_FirstRow(string line, string first)
    {
        Assert.Equal(first, CommandBarRouter.Suggest(line, Quicklist)[0].Line);
    }

    [Fact]
    public void Complete_Names_RankedByMatcher_OnceEach()
    {
        IReadOnlyList<CommandBarSuggestion> rows = CommandBarRouter.Suggest("br", Quicklist);

        Assert.Equal(["break ", "breakend ", "arrive "], rows.Select(r => r.Line));
        Assert.Equal(["break (b)", "breakend (be)", "arrive (a)"], rows.Select(r => r.Label));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzzz")]
    [InlineData("a ")]
    [InlineData("todo x")]
    [InlineData("summary ")]
    public void Complete_NothingToSuggest(string line)
    {
        Assert.Empty(CommandBarRouter.Suggest(line, Quicklist));
    }

    // AC-6: quicklist rows after cp / checkpoint (and an optional time word).
    [Fact]
    public void Complete_AfterCheckpoint_ListsAllRows()
    {
        IReadOnlyList<CommandBarSuggestion> rows = CommandBarRouter.Suggest("cp ", Quicklist);

        Assert.Equal(["cp 4711 ", "cp 1234 ", "cp 9 "], rows.Select(r => r.Line));
        Assert.Equal(["4711", "1234", "9"], rows.Select(r => r.Label));
        Assert.Equal(["Billing export", "Review queue", "Meetings"], rows.Select(r => r.Detail));
    }

    [Theory]
    [InlineData("cp 12", "cp 1234 ")]
    [InlineData("checkpoint 47", "checkpoint 4711 ")]
    [InlineData("cp REV", "cp 1234 ")]
    [InlineData("cp 9:30 meet", "cp 9:30 9 ")]
    [InlineData("tm cp 9:30 ", "tm cp 9:30 4711 |tm cp 9:30 1234 |tm cp 9:30 9 ")]
    [InlineData("cp 9", "cp 9 ")]
    public void Complete_AfterCheckpoint_Filters(string line, string expected)
    {
        Assert.Equal(expected, string.Join('|', CommandBarRouter.Suggest(line, Quicklist).Select(r => r.Line)));
    }

    [Theory]
    [InlineData("cp 1234 ")]
    [InlineData("cp 1234 rev")]
    [InlineData("cp 9:30 1234 ")]
    public void Complete_AfterTheWorkItem_SuggestsNothing(string line)
    {
        Assert.Empty(CommandBarRouter.Suggest(line, Quicklist));
    }

    [Fact]
    public void Complete_AfterCheckpoint_AtMostSix()
    {
        List<(int WorkItem, string Title)> many = Enumerable.Range(1, 10).Select(i => (100 + i, "Project " + i)).ToList();

        IReadOnlyList<CommandBarSuggestion> rows = CommandBarRouter.Suggest("cp ", many);

        Assert.Equal(CommandBarRouter.MaxSuggestions, rows.Count);
        Assert.Equal("cp 101 ", rows[0].Line);
    }

    [Fact]
    public void Complete_OverMaxLength_SuggestsNothing()
    {
        Assert.Empty(CommandBarRouter.Suggest(new string('a', CommandBarRouter.MaxLineLength + 1), Quicklist));
    }
}
