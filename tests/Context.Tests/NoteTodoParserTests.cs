namespace Context.Tests;

// 201 AC-1, AC-2: NoteTodoParser finds open "- { } text" lines outside fences, with an optional due:yyyy-MM-dd token.
// 329 AC-1: the todo box is "{ }"; "- [ ] text" is a note checkbox (ParseCheckboxes), never a todo.
public sealed class NoteTodoParserTests
{
    private static string Day(DateOnly? due) =>
        due?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "-";

    [Theory]
    [InlineData("- { } call Anna", "call Anna")]
    [InlineData("* { } call Anna", "call Anna")]
    [InlineData("+ { } call Anna", "call Anna")]
    [InlineData("    - { } call Anna", "call Anna")]
    [InlineData("\t- { } call Anna", "call Anna")]
    [InlineData("- { }   call    Anna   ", "call Anna")]
    [InlineData("- { } call\tAnna", "call Anna")]
    public void Parse_OpenTodoLine_GivesItsText(string line, string text)
    {
        ParsedNoteTodo todo = Assert.Single(NoteTodoParser.Parse(line));

        Assert.Equal(1, todo.Line);
        Assert.Equal(text, todo.Text);
        Assert.Equal(line, todo.Raw);
        Assert.Null(todo.Due);
    }

    [Theory]
    [InlineData("- {x} done")]
    [InlineData("- {X} done")]
    [InlineData("- [ ] x")]
    [InlineData("- [x] x")]
    [InlineData("- [X] x")]
    [InlineData("* [ ] call Anna due:2026-11-20")]
    [InlineData("- [] call")]
    [InlineData("- { }x call")]
    [InlineData("- { }")]
    [InlineData("- { } ")]
    [InlineData("- { }      ")]
    [InlineData("- { } due:2026-11-20")]
    [InlineData("- { }  due:2026-11-20  ")]
    [InlineData("1. { } call")]
    [InlineData("-  { } call")]
    [InlineData("-\t{ } call")]
    [InlineData("- { }\tcall")]
    [InlineData("-{ } call")]
    [InlineData("> - { } call")]
    [InlineData("call - { } Anna")]
    [InlineData("")]
    public void Parse_NotAnOpenTodo_GivesNothing(string line)
    {
        Assert.Empty(NoteTodoParser.Parse(line));
    }

    [Fact]
    public void Parse_LinesInAFence_GiveNothing_AndLinesAreNumberedFromOne()
    {
        string markdown = "# Plan\n```\n- { } in fence\n```\n- { } after\n  ```cs\n- { } second fence\n";

        ParsedNoteTodo todo = Assert.Single(NoteTodoParser.Parse(markdown));

        Assert.Equal(5, todo.Line);
        Assert.Equal("after", todo.Text);
    }

    [Fact]
    public void Parse_CrlfInput_RawHasNoLineEnd()
    {
        IReadOnlyList<ParsedNoteTodo> todos = NoteTodoParser.Parse("intro\r\n- { } one\r\n- {x} two\r\n- { } three\r\n");

        Assert.Equal([(2, "one", "- { } one"), (4, "three", "- { } three")], todos.Select(t => (t.Line, t.Text, t.Raw)));
    }

    [Theory]
    [InlineData("- { } call Anna due:2026-11-20", "call Anna", "2026-11-20")]
    [InlineData("- { } due:2026-11-20 call Anna", "call Anna", "2026-11-20")]
    [InlineData("- { } call due:2026-11-20 Anna", "call Anna", "2026-11-20")]
    [InlineData("- { } call due:2026-11-20 due:2026-12-01", "call due:2026-12-01", "2026-11-20")]
    [InlineData("- { } call due:2026-13-01 due:2026-12-01", "call due:2026-13-01", "2026-12-01")]
    [InlineData("- { } a `x` due:2026-02-28", "a `x`", "2026-02-28")]
    [InlineData("- { } call due:2026-13-01", "call due:2026-13-01", "-")]
    [InlineData("- { } call due:2026-02-30", "call due:2026-02-30", "-")]
    [InlineData("- { } call due:tomorrow", "call due:tomorrow", "-")]
    [InlineData("- { } call Due:2026-11-20", "call Due:2026-11-20", "-")]
    [InlineData("- { } call due:26-11-20", "call due:26-11-20", "-")]
    [InlineData("- { } call due:2026-11-20x", "call due:2026-11-20x", "-")]
    [InlineData("- { } call xdue:2026-11-20", "call xdue:2026-11-20", "-")]
    [InlineData("- { } call due:２026-11-20", "call due:２026-11-20", "-")]
    [InlineData("- { } see `due:2026-11-20` later", "see `due:2026-11-20` later", "-")]
    [InlineData("- { } see ` due:2026-11-20 ` later", "see ` due:2026-11-20 ` later", "-")]
    public void Parse_DueToken(string line, string text, string due)
    {
        ParsedNoteTodo todo = Assert.Single(NoteTodoParser.Parse(line));

        Assert.Equal(text, todo.Text);
        Assert.Equal(due, Day(todo.Due));
        Assert.Equal(line, todo.Raw);
    }

    [Fact]
    public void Parse_LongText_IsCutTo1000()
    {
        string line = "- { } " + new string('a', 1200);

        ParsedNoteTodo todo = Assert.Single(NoteTodoParser.Parse(line));

        Assert.Equal(NoteTodoParser.MaxTextLength, todo.Text.Length);
        Assert.Equal(line, todo.Raw);
    }

    [Fact]
    public void Parse_LongText_NeverSplitsASurrogatePair()
    {
        string line = "- { } " + new string('a', 999) + "\U0001F600" + "tail";

        ParsedNoteTodo todo = Assert.Single(NoteTodoParser.Parse(line));

        Assert.Equal(new string('a', 999), todo.Text);
    }

    [Fact]
    public void Parse_LongText_KeepsAWholePairAtTheEnd()
    {
        string line = "- { } " + new string('a', 998) + "\U0001F600" + "tail";

        ParsedNoteTodo todo = Assert.Single(NoteTodoParser.Parse(line));

        Assert.Equal(new string('a', 998) + "\U0001F600", todo.Text);
    }

    [Fact]
    public void ToString_IsTheTypeNameOnly()
    {
        ParsedNoteTodo todo = Assert.Single(NoteTodoParser.Parse("- { } secret"));

        Assert.Equal(nameof(ParsedNoteTodo), todo.ToString());
        Assert.Equal(nameof(NoteTodo), new NoteTodo("Plan", 1, "secret", null, "- { } secret").ToString());
        Assert.Equal(nameof(TodoEntry), new TodoEntry(new NoteTodo("Plan", 1, "secret", null, "- { } secret")).ToString());
    }

    // 253 AC-2: a topic line is no note todo (nor a checkbox); an ordinary todo mentioning the person still is.
    // 329: a topic is written with "[ ]", so "- { } next: @[X] t" is a todo.
    [Fact]
    public void Parse_TopicLine_IsNoTodo()
    {
        Assert.Empty(NoteTodoParser.Parse("- [ ] next: @[Ann Lee] budget"));
        Assert.Empty(NoteTodoParser.ParseCheckboxes("- [ ] next: @[Ann Lee] budget"));
        Assert.Empty(NoteTodoParser.ParseCheckboxes("  * [ ] NEXT:@[Ann Lee] budget"));
        Assert.Equal("next: @[Ann Lee] budget", Assert.Single(NoteTodoParser.Parse("- { } next: @[Ann Lee] budget")).Text);

        ParsedNoteTodo todo = Assert.Single(NoteTodoParser.Parse("- { } call @[Ann Lee]"));
        Assert.Equal("call @[Ann Lee]", todo.Text);

        // Not a topic (no text after the mention), so it stays a todo.
        Assert.Single(NoteTodoParser.Parse("- { } next: @[Ann Lee]"));
    }

    // 329 AC-1: the "{ }" todo keeps the due rule; a "[ ]" line on its own is a checkbox, with the same shape rules.
    [Fact]
    public void Parse_BothKinds_TodoOnlyBraces()
    {
        string markdown = "- [ ] a\n- { } call Anna due:2026-11-20\n```\n- { } in fence\n```\n1. { } x\n";

        ParsedNoteTodo todo = Assert.Single(NoteTodoParser.Parse(markdown));
        Assert.Equal((2, "call Anna", "2026-11-20"), (todo.Line, todo.Text, Day(todo.Due)));
        ParsedNoteTodo box = Assert.Single(NoteTodoParser.ParseCheckboxes(markdown));
        Assert.Equal((1, "a", "- [ ] a"), (box.Line, box.Text, box.Raw));
        Assert.Empty(NoteTodoParser.ParseCheckboxes("- [x] a\n- [ ]x a\n- [ ] \n- { } b\n"));
    }
}
