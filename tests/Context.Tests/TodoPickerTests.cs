namespace Context.Tests;

// 144 AC-1: a todo is picked by its text; the first equal text, else the only one containing the query.
public sealed class TodoPickerTests
{
    private static readonly DateTime Created = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static TodoItem[] Todos(params string[] texts) =>
        texts.Select((text, i) => new TodoItem(Guid.NewGuid(), text, new DateOnly(2026, 11, 1 + i), Created)).ToArray();

    [Fact]
    public void Pick_EqualText_WinsOverTodosContainingIt()
    {
        TodoItem[] todos = Todos("Buy milk and bread", "Buy milk", "Buy milkshake");

        TodoPick pick = TodoPicker.Pick(todos, "Buy milk");

        Assert.Same(todos[1], pick.Todo);
        Assert.Equal(1, pick.Matches);
    }

    [Theory]
    [InlineData("buy MILK")]
    [InlineData("  Buy milk \t")]
    [InlineData("BUY MILK")]
    public void Pick_IgnoresCase_AndTrimsTheQuery(string query)
    {
        TodoItem[] todos = Todos("Call Anna", "Buy milk");

        Assert.Same(todos[1], TodoPicker.Pick(todos, query).Todo);
    }

    [Fact]
    public void Pick_EqualTexts_ReturnsTheFirstInListOrder()
    {
        TodoItem[] todos = Todos("Other", "Water plants", "water PLANTS", "Water plants");

        TodoPick pick = TodoPicker.Pick(todos, "water plants");

        Assert.Same(todos[1], pick.Todo);
        Assert.Equal(1, pick.Matches);
    }

    [Fact]
    public void Pick_NoEqualText_ReturnsTheOnlyTodoContainingTheQuery()
    {
        TodoItem[] todos = Todos("Call Anna", "Buy milk", "Pay rent");

        TodoPick pick = TodoPicker.Pick(todos, "ILK");

        Assert.Same(todos[1], pick.Todo);
        Assert.Equal(1, pick.Matches);
    }

    [Theory]
    [InlineData("xyz", 0)]
    [InlineData("call", 2)]
    [InlineData("a", 4)]
    public void Pick_NoneOrSeveralContaining_ReturnsNoTodoAndTheCount(string query, int matches)
    {
        TodoItem[] todos = Todos("Call Anna", "Call Ben", "Pay rent", "Read a book");

        TodoPick pick = TodoPicker.Pick(todos, query);

        Assert.Null(pick.Todo);
        Assert.Equal(matches, pick.Matches);
    }

    [Fact]
    public void Pick_EmptyList_ReturnsNoTodoAndZero()
    {
        TodoPick pick = TodoPicker.Pick([], "x");

        Assert.Null(pick.Todo);
        Assert.Equal(0, pick.Matches);
    }

    [Fact]
    public void Pick_Digit_IsTextNotAPosition()
    {
        Assert.Null(TodoPicker.Pick(Todos("Buy milk", "Call Anna"), "1").Todo);

        TodoItem[] todos = Todos("Buy milk", "Call Anna", "Read chapter 1");
        Assert.Same(todos[2], TodoPicker.Pick(todos, "1").Todo);
    }

    // The text is compared as shown: line breaks are spaces, a tab is four spaces, other controls are U+FFFD.
    [Theory]
    [InlineData("first second")]
    [InlineData("first second third    end")]
    [InlineData("st sec")]
    public void Pick_MultiLineText_IsComparedOnOneLine(string query)
    {
        TodoItem[] todos = Todos("unrelated", "first\nsecond\r\nthird\tend");

        Assert.Same(todos[1], TodoPicker.Pick(todos, query).Todo);
    }

    [Fact]
    public void Pick_ControlCharacters_AreComparedAsShown()
    {
        TodoItem[] todos = Todos("a\u001b[2Jb\u0007c", "other");

        Assert.Same(todos[0], TodoPicker.Pick(todos, "a�[2Jb�c").Todo);
        Assert.Same(todos[0], TodoPicker.Pick(todos, "[2Jb").Todo);
        Assert.Null(TodoPicker.Pick(todos, "a\u001b[2Jb").Todo);
        Assert.Equal("a�[2Jb c", TodoPicker.OneLine("a\u001b[2Jb\nc"));
    }

    [Fact]
    public void ToString_HidesTheTodoText()
    {
        TodoPick pick = TodoPicker.Pick(Todos("SECRET-144"), "secret");

        Assert.NotNull(pick.Todo);
        Assert.Equal("TodoPick", pick.ToString());
        Assert.Equal("TodoPick", $"{pick}");
    }
}
