using System.Text;

namespace Context.Tests;

// 242: the board of a tag (AC-2 to AC-5): status tokens, cards, columns, order and moving a card.
public sealed class BoardViewTests : IDisposable
{
    private const string NotADatabase = "this is not a database, just some text that fills the header....";
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly string[] Columns = ["todo", "doing", "done"];
    private static readonly BoardSetting Launch = new("launch", Columns);
    private static readonly DateTime HistoryNow = new(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc);

    private readonly string root = Path.Combine(Path.GetTempPath(), "context-board-" + Guid.NewGuid().ToString("N"));
    private readonly NoteStore store;
    private readonly TagIndex index;

    public BoardViewTests()
    {
        Directory.CreateDirectory(root);
        store = new NoteStore(root);
        index = new TagIndex(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string PathOf(string name) => Path.Combine(root, name + ".md");

    private void Write(string name, string text, bool bom = false) => File.WriteAllBytes(PathOf(name), Utf8(text, bom));

    private static byte[] Utf8(string text, bool bom = false) =>
        bom ? [.. Bom, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text);

    private BoardView Build() => BoardView.Build(store, index, Launch);

    private BoardCard Card(string note, bool todo = false) =>
        Build().Columns.SelectMany(c => c.Cards).Single(c => c.Note == note && (c.Todo is not null) == todo);

    private static string? ColumnOf(string line) => BoardView.Find(line, 0, Columns)?.Column;

    // AC-2: a token at line start or after whitespace, any ASCII case, only for a known column.
    [Theory]
    [InlineData("#status/doing", "doing")]
    [InlineData("text #STATUS/Doing more", "doing")]
    [InlineData("a\t#Status/done", "done")]
    [InlineData("#status/doing,", "doing")]
    [InlineData("x#status/doing", null)]
    [InlineData("#status/unknown #status/todo", "todo")]
    [InlineData("#status/doing-later", null)]
    [InlineData("#status/", null)]
    [InlineData("#status", null)]
    [InlineData("#statusdoing", null)]
    [InlineData("`#status/doing`", null)]
    [InlineData("`#status/doing` #status/done", "done")]
    [InlineData("``a ` #status/doing``", null)]
    [InlineData("` #status/doing", "doing")]
    [InlineData("#ſtatus/doing", null)]
    public void Find_MatchesOnlyValidTokens(string line, string? expected)
    {
        Assert.Equal(expected, ColumnOf(line));
    }

    // AC-2: the found token's position covers the column as written.
    [Fact]
    public void Find_GivesTheColumnsPosition()
    {
        StatusToken token = BoardView.Find("go #status/DOING now", 0, Columns)!;

        Assert.Equal(11, token.Start);
        Assert.Equal(5, token.Length);
        Assert.Equal("doing", token.Column);
    }

    // AC-2: the column rule.
    [Theory]
    [InlineData("Todo", "todo")]
    [InlineData("a1-_b", "a1-_b")]
    [InlineData("", null)]
    [InlineData("1a", null)]
    [InlineData("-a", null)]
    [InlineData("a b", null)]
    [InlineData("ä", null)]
    public void NormalizeColumn_FollowsTheRule(string column, string? expected)
    {
        Assert.Equal(expected, BoardView.NormalizeColumn(column));
        Assert.Equal("a" + new string('b', 39), BoardView.NormalizeColumn("a" + new string('b', 39)));
        Assert.Null(BoardView.NormalizeColumn("a" + new string('b', 40)));
    }

    // AC-2: TagParser is unchanged: the token still indexes as #status, and the parser version stays 10.
    [Fact]
    public void TagParser_IsUnchanged()
    {
        Assert.Equal(["status"], TagParser.Parse("#status/doing"));
        Assert.Equal(18, TagIndex.ParserVersion);
    }

    // AC-3: a note card's column is its first token outside fences, code spans and open todo lines.
    [Fact]
    public void Build_NoteCard_TakesTheFirstTokenOutsideFencesSpansAndTodoLines()
    {
        Write("Plan", "# Plan #launch\n```\n#status/done\n```\n`#status/done`\n- { } item #status/todo\n- {x} closed #status/doing\nend #status/done\n");

        BoardCard card = Card("Plan");

        Assert.Equal("doing", card.Column);
        Assert.Equal(7, card.StatusLine);
        Assert.Null(card.Todo);
        Assert.Equal(Columns, card.Columns);
        Assert.Equal(nameof(BoardCard), card.ToString());
    }

    // AC-3: an open todo whose text has the tag is a card, its column from its own text; a todo without the tag is not.
    [Fact]
    public void Build_TodoCards_HaveTheTagInTheirOwnText()
    {
        Write("Work", "intro\n- { } ship it #launch #status/doing\n- { } other #status/done\n  * { } later #Launch\n");

        BoardView view = Build();

        List<BoardCard> cards = view.Columns.SelectMany(c => c.Cards).ToList();
        Assert.Equal(3, cards.Count);
        BoardCard note = cards.Single(c => c.Todo is null);
        Assert.Equal("Work", note.Note);
        Assert.Null(note.Column);
        BoardCard ship = cards.Single(c => c.Todo?.Line == 2);
        Assert.Equal("doing", ship.Column);
        BoardCard later = cards.Single(c => c.Todo?.Line == 4);
        Assert.Null(later.Column);
        Assert.Equal("later #Launch", later.Todo!.Text);
    }

    // 329 AC-2, AC-3: a "- [ ]" checkbox line is no todo card; a "{ }" todo's token right after its box is found and
    // moved in place.
    [Fact]
    public void Build_CheckboxLine_NoTodoCard_BraceTokenAfterBoxMoves()
    {
        Write("Work", "#launch\n- [ ] box #launch #status/doing\n  - { } #status/todo ship #launch\n");

        BoardView view = Build();

        List<BoardCard> cards = view.Columns.SelectMany(c => c.Cards).ToList();
        BoardCard todo = cards.Single(c => c.Todo is not null);
        Assert.Equal((3, "todo"), (todo.Todo!.Line, todo.Column));
        Assert.Equal(2, cards.Count);

        Assert.Equal(BoardMoveResult.Done, BoardView.Move(store, todo, "done"));
        Assert.Equal(
            Utf8("#launch\n- [ ] box #launch #status/doing\n  - { } #status/done ship #launch\n"),
            File.ReadAllBytes(PathOf("Work")));
    }

    // AC-3: a note whose only tag is on a todo line still gets a note card; an untagged note gets none.
    [Fact]
    public void Build_TagOnlyOnATodoLine_StillGivesANoteCard()
    {
        Write("Only", "- { } do #launch\n");
        Write("Untagged", "#other #status/doing\n");

        BoardView view = Build();

        Assert.Equal(["Only", "Only"], view.Columns.SelectMany(c => c.Cards).Select(c => c.Note));
    }

    // AC-3: sensitive notes never appear, and nothing else is read from them.
    [Fact]
    public void Build_SensitiveNotes_NeverAppear()
    {
        Write("Plain", "#launch\n");
        File.WriteAllText(Path.Combine(root, "Secret.ctxs"), "#launch\n- { } hidden #launch\n");

        BoardView view = Build();

        Assert.Equal(["Plain"], view.Columns.SelectMany(c => c.Cards).Select(c => c.Note));
    }

    // AC-4: settings order, a leading No status only with cards; cards by name ignoring case, then ordinal, the
    // note's card before its todos in line order.
    [Fact]
    public void Build_OrdersColumnsAndCards()
    {
        Write("beta", "#launch #status/todo\n- { } b2 #launch #status/todo\n- { } b1 #launch #status/todo\n");
        Write("Alpha", "#launch #status/todo\n");
        Write("gamma", "#launch\n");
        Write("delta", "#launch #status/done\n");

        BoardView view = Build();

        Assert.Equal([null, "todo", "doing", "done"], view.Columns.Select(c => c.Name));
        Assert.Equal(["gamma"], view.Columns[0].Cards.Select(c => c.Note));
        Assert.Equal(["Alpha", "beta", "beta", "beta"], view.Columns[1].Cards.Select(c => c.Note));
        Assert.Equal([null, 2, 3], view.Columns[1].Cards.Skip(1).Select(c => c.Todo?.Line));
        Assert.Empty(view.Columns[2].Cards);
        Assert.Equal(["delta"], view.Columns[3].Cards.Select(c => c.Note));
        Assert.Equal(6, view.CardCount);
        Assert.False(view.Unreadable);
        Assert.Equal("launch", view.Tag);
        Assert.Equal(nameof(BoardView), view.ToString());
    }

    // AC-4: no No status column without such cards; no cards at all keeps every board column.
    [Fact]
    public void Build_NoCards_HasOnlyTheBoardColumns()
    {
        Write("Other", "#other\n");

        BoardView view = Build();

        Assert.Equal(["todo", "doing", "done"], view.Columns.Select(c => c.Name));
        Assert.Equal(0, view.CardCount);
    }

    // AC-4: a corrupt context.db is Unreadable with no cards, and the file is left as it was.
    [Fact]
    public void Build_CorruptIndex_IsUnreadable()
    {
        Write("Plan", "#launch\n");
        string db = Path.Combine(root, TagIndex.FileName);
        File.WriteAllText(db, NotADatabase);

        BoardView view = Build();

        Assert.True(view.Unreadable);
        Assert.Empty(view.Columns);
        Assert.Equal(NotADatabase, File.ReadAllText(db));
    }

    // AC-5: moving to the current column is Unchanged and writes nothing.
    [Fact]
    public void Move_ToTheCurrentColumn_IsUnchanged_WritingNothing()
    {
        Write("Plan", "#launch #status/doing\n");
        BoardCard card = Card("Plan");
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        Assert.Equal(BoardMoveResult.Unchanged, BoardView.Move(store, card, "doing"));

        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // AC-5: only the token's column changes; CRLF, BOM and every other byte stay; a version is kept.
    [Fact]
    public void Move_NoteCard_ReplacesOnlyTheColumn_KeepingCrlfAndBom()
    {
        Write("Plan", "# Plan #launch\r\n`#status/todo` x #STATUS/Todo y #status/done\r\nend", bom: true);
        BoardCard card = Card("Plan");
        NoteHistory history = new(store, () => HistoryNow);

        Assert.Equal(BoardMoveResult.Done, BoardView.Move(store, card, "done", history));

        Assert.Equal(
            Utf8("# Plan #launch\r\n`#status/todo` x #STATUS/done y #status/done\r\nend", bom: true),
            File.ReadAllBytes(PathOf("Plan")));
        Assert.Single(Directory.GetFiles(Path.Combine(root, ".history", "Plan")));
        Assert.Equal("done", Card("Plan").Column);
    }

    // AC-5: a note without a token gets a final line, after ending its last line, in the file's line ending.
    [Theory]
    [InlineData("#launch\n", "#launch\n#status/doing\n")]
    [InlineData("#launch", "#launch\n#status/doing\n")]
    [InlineData("#launch\r\nend", "#launch\r\nend\r\n#status/doing\r\n")]
    [InlineData("#launch\r\n", "#launch\r\n#status/doing\r\n")]
    public void Move_NoteWithoutToken_AppendsAFinalLine(string text, string expected)
    {
        Write("Plan", text);
        BoardCard card = Card("Plan");
        Assert.Null(card.Column);

        Assert.Equal(BoardMoveResult.Done, BoardView.Move(store, card, "doing"));

        Assert.Equal(Utf8(expected), File.ReadAllBytes(PathOf("Plan")));
    }

    // AC-5: a todo card's token is replaced in its own line only; the note's own token stays.
    [Fact]
    public void Move_TodoCard_ReplacesItsOwnToken()
    {
        Write("Work", "#status/todo\r\n- { } ship #launch #status/todo\r\n", bom: true);
        BoardCard card = Card("Work", todo: true);

        Assert.Equal(BoardMoveResult.Done, BoardView.Move(store, card, "doing"));

        Assert.Equal(Utf8("#status/todo\r\n- { } ship #launch #status/doing\r\n", bom: true), File.ReadAllBytes(PathOf("Work")));
    }

    // AC-5: a todo without a token gets " #status/<col>" at its line's end.
    [Fact]
    public void Move_TodoWithoutToken_AppendsToItsLine()
    {
        Write("Work", "a\r\n- { } ship #launch\r\nb");
        BoardCard card = Card("Work", todo: true);

        Assert.Equal(BoardMoveResult.Done, BoardView.Move(store, card, "done"));

        Assert.Equal(Utf8("a\r\n- { } ship #launch #status/done\r\nb"), File.ReadAllBytes(PathOf("Work")));
    }

    // AC-5: an edit between Build and Move that changes the card's token or todo line is Changed and writes nothing.
    [Theory]
    [InlineData(false, "#launch #status/done\n")]
    [InlineData(false, "#launch\n")]
    [InlineData(false, "intro\n#launch #status/todo\n")]
    [InlineData(false, "#launch #status/todo2\n")]
    [InlineData(true, "#launch\n- { } ship #launch #status/todo later\n")]
    [InlineData(true, "#launch\n- {x} ship #launch #status/todo\n")]
    [InlineData(true, "- { } ship #launch #status/todo\n")]
    public void Move_EditedAfterBuild_IsChanged(bool todo, string now)
    {
        Write("Plan", "#launch #status/todo\n- { } ship #launch #status/todo\n");
        BoardCard card = Card("Plan", todo);
        Write("Plan", now);

        Assert.Equal(BoardMoveResult.Changed, BoardView.Move(store, card, "doing"));

        Assert.Equal(Utf8(now), File.ReadAllBytes(PathOf("Plan")));
    }

    // AC-5: a change between the read and the save is Changed, and the other writer's bytes stay.
    [Fact]
    public void Move_FileChangedBeforeSave_IsChanged()
    {
        Write("Plan", "#launch #status/todo\n");
        BoardCard card = Card("Plan");
        byte[] other = Utf8("#launch #status/todo\nmore\n");

        BoardMoveResult result = BoardView.Move(store, card, "doing", null, () => File.WriteAllBytes(PathOf("Plan"), other));

        Assert.Equal(BoardMoveResult.Changed, result);
        Assert.Equal(other, File.ReadAllBytes(PathOf("Plan")));
    }

    // AC-5: a note that is gone is Missing.
    [Fact]
    public void Move_MissingNote_IsMissing()
    {
        Write("Plan", "#launch\n");
        BoardCard card = Card("Plan");
        File.Delete(PathOf("Plan"));

        Assert.Equal(BoardMoveResult.Missing, BoardView.Move(store, card, "doing"));
        Assert.False(File.Exists(PathOf("Plan")));
    }

    // AC-5: a note that can't be read is Failed, never an exception.
    [Fact]
    public void Move_UnreadableNote_IsFailed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Write("Plan", "#launch\n");
        BoardCard card = Card("Plan");
        File.SetUnixFileMode(PathOf("Plan"), UnixFileMode.None);
        try
        {
            Assert.Equal(BoardMoveResult.Failed, BoardView.Move(store, card, "doing"));
        }
        finally
        {
            File.SetUnixFileMode(PathOf("Plan"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    // AC-5: a column that is not on the board is a programming error.
    [Fact]
    public void Move_UnknownColumn_Throws()
    {
        Write("Plan", "#launch\n");
        BoardCard card = Card("Plan");

        Assert.Throws<ArgumentException>(() => BoardView.Move(store, card, "later"));
    }
}
