namespace Context;

/// <summary>
/// One card of a board: a plain note that has the board's tag (<see cref="Todo"/> <c>null</c>) or an open note todo
/// whose own text has it. Names and texts are raw outside text.
/// </summary>
internal sealed record BoardCard
{
    public BoardCard(string note, string? column, int? statusLine, NoteTodo? todo, IReadOnlyList<string> columns)
    {
        Note = note;
        Column = column;
        StatusLine = statusLine;
        Todo = todo;
        Columns = columns;
    }

    /// <summary>Returns only the type name: the contents are raw outside text.</summary>
    public override string ToString() => nameof(BoardCard);

    /// <summary>The note's full name (<see cref="NoteInfo.Name"/>).</summary>
    public string Note { get; }

    /// <summary>The column of the card's status token, or <c>null</c> for <c>No status</c>.</summary>
    public string? Column { get; }

    /// <summary>For a note card: the 1-based line of its status token, <c>null</c> without one (always <c>null</c> for a todo).</summary>
    public int? StatusLine { get; }

    /// <summary>The note todo for a todo card (its <see cref="NoteTodo.Line"/> and <see cref="NoteTodo.Raw"/> as indexed), else <c>null</c>.</summary>
    public NoteTodo? Todo { get; }

    /// <summary>The board's columns in settings order, so <see cref="BoardView.Move"/> scans with the same ones.</summary>
    public IReadOnlyList<string> Columns { get; }
}

/// <summary>One column of a board: its cards in order.</summary>
internal sealed record BoardColumn
{
    public BoardColumn(string? name, IReadOnlyList<BoardCard> cards)
    {
        Name = name;
        Cards = cards;
    }

    /// <summary>Returns only the type name: the cards are raw outside text.</summary>
    public override string ToString() => nameof(BoardColumn);

    /// <summary>The column (lower-cased), or <c>null</c> for the leading <c>No status</c> column.</summary>
    public string? Name { get; }

    /// <summary>The cards: by note name ignoring case, then ordinal; a note's card before its todos, todos in line order.</summary>
    public IReadOnlyList<BoardCard> Cards { get; }
}

/// <summary>How <see cref="BoardView.Move"/> ended.</summary>
internal enum BoardMoveResult
{
    /// <summary>The status token now names the new column.</summary>
    Done,

    /// <summary>The card is already in that column; nothing was read or written.</summary>
    Unchanged,

    /// <summary>The card's status token (or todo line) is not what the board showed, or the file changed meanwhile; nothing was written.</summary>
    Changed,

    /// <summary>The note no longer exists; nothing was written.</summary>
    Missing,

    /// <summary>The note couldn't be read or written; nothing was changed.</summary>
    Failed,
}

/// <summary>
/// The data of a tag's board (job 242), with no rendering: the board's columns with their cards, each card placed by
/// its <c>#status/&lt;column&gt;</c> token. The notes stay the board's only data; <see cref="Move"/> rewrites the token.
/// Note names and texts are raw: a front end must clean them for its own output.
/// </summary>
internal sealed record BoardView
{
    /// <summary>The longest column name.</summary>
    public const int MaxColumnLength = 40;

    /// <summary>The start of a status token, before its column (matched ASCII-case-insensitively).</summary>
    public const string StatusPrefix = "#status/";

    public BoardView(string tag, IReadOnlyList<BoardColumn> columns, bool unreadable)
    {
        Tag = tag;
        Columns = columns;
        Unreadable = unreadable;
    }

    /// <summary>Returns only the type name: the contents are raw outside text.</summary>
    public override string ToString() => nameof(BoardView);

    /// <summary>The board's tag, normalized, without <c>#</c>.</summary>
    public string Tag { get; }

    /// <summary>
    /// The columns: a leading <c>No status</c> column (<see cref="BoardColumn.Name"/> <c>null</c>) only when it has
    /// cards, then every board column in settings order. Empty when <see cref="Unreadable"/>.
    /// </summary>
    public IReadOnlyList<BoardColumn> Columns { get; }

    /// <summary><c>context.db</c> can't be read; there are no cards.</summary>
    public bool Unreadable { get; }

    /// <summary>The number of cards over all columns.</summary>
    public int CardCount => Columns.Sum(c => c.Cards.Count);

    /// <summary>The refusal for <see cref="BoardMoveResult.Changed"/>; <paramref name="note"/> is the shown note name.</summary>
    public static string ChangedText(string note) => $"{note} changed. Reload the board and try again.";

    /// <summary>The refusal for <see cref="BoardMoveResult.Missing"/>.</summary>
    public static string MissingText(string note) => $"{note} no longer exists. Nothing was moved.";

    /// <summary>The failure for <see cref="BoardMoveResult.Failed"/>.</summary>
    public static string FailedText(string note) => $"Can't write {note}.";

    /// <summary>
    /// The typed tag normalized (<see cref="TagIndex.NormalizeTag"/>); <c>false</c> for a blank one or when the
    /// result is no tag by <see cref="TagParser.NormalizeBracket"/>.
    /// </summary>
    public static bool TryNormalizeTag(string tag, out string normalized)
    {
        normalized = string.Empty;
        string candidate = TagIndex.NormalizeTag(tag);
        if (candidate.Length == 0
            || !string.Equals(TagParser.NormalizeBracket(candidate), candidate, StringComparison.Ordinal))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    /// <summary>
    /// <paramref name="column"/> with ASCII letters lower-cased when it is a column: 1 to <see cref="MaxColumnLength"/>
    /// characters, an ASCII letter first, then ASCII letters, digits, <c>-</c> or <c>_</c>; else <c>null</c>.
    /// </summary>
    public static string? NormalizeColumn(string column)
    {
        if (column.Length == 0 || column.Length > MaxColumnLength || !char.IsAsciiLetter(column[0])
            || !column.All(IsColumnChar))
        {
            return null;
        }

        return column.ToLowerInvariant();
    }

    /// <summary>
    /// Syncs the index and builds the board of <paramref name="setting"/>: one card per plain <c>.md</c> note that
    /// has the tag in the index, in the column of its first status token on a line that is no open note-todo line,
    /// and one card per open note todo whose own text has the tag (<see cref="TagParser.Parse"/>), in the column of
    /// the first status token in its text. A note that can't be read has no card. Sensitive notes are never listed,
    /// so they never appear. Never throws for an unreadable index (<see cref="Unreadable"/>).
    /// </summary>
    public static BoardView Build(NoteStore store, TagIndex index, BoardSetting setting)
    {
        if (!Directory.Exists(store.Root))
        {
            return Arrange(setting, []);
        }

        IReadOnlyList<string> tagged;
        IReadOnlyList<NoteTodo> todos;
        try
        {
            index.Sync(store);
            tagged = index.NotesWithTag(setting.Tag);
            todos = index.NoteTodos();
        }
        catch (TagIndexException)
        {
            return new BoardView(setting.Tag, [], true);
        }

        Dictionary<string, NoteInfo> plain = new(StringComparer.Ordinal);
        foreach (NoteInfo note in store.List())
        {
            plain[note.Name] = note;
        }

        List<BoardCard> cards = [];
        foreach (string name in tagged)
        {
            if (!plain.TryGetValue(name, out NoteInfo? note))
            {
                continue;
            }

            string text;
            try
            {
                text = store.ReadForEdit(note).Text;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            (int Line, StatusToken Token)? status = NoteStatus(text, setting.Columns);
            cards.Add(new BoardCard(name, status?.Token.Column, status?.Line, null, setting.Columns));
        }

        foreach (NoteTodo todo in todos)
        {
            if (!plain.ContainsKey(todo.Note) || !TagParser.Parse(todo.Text).Contains(setting.Tag, StringComparer.Ordinal))
            {
                continue;
            }

            StatusToken? token = Find(todo.Raw, TodoTextStart(todo.Raw), setting.Columns);
            cards.Add(new BoardCard(todo.Note, token?.Column, null, todo, setting.Columns));
        }

        return Arrange(setting, cards);
    }

    /// <summary>
    /// Moves <paramref name="card"/> to <paramref name="column"/> (one of <see cref="BoardCard.Columns"/>, else
    /// <see cref="ArgumentException"/>). Its current column is <see cref="BoardMoveResult.Unchanged"/> with nothing
    /// read. Otherwise reads the note (<see cref="NoteStore.ReadForEdit"/>) and, only when the card's status token is
    /// still where the board found it (for a todo: line <see cref="NoteTodo.Line"/> still parses to the todo with
    /// <see cref="NoteTodo.Raw"/> equal, as in <see cref="NoteTodos.Complete(NoteStore, NoteTodo, NoteHistory?)"/>),
    /// replaces only the token's column, or without a token appends <c> #status/&lt;column&gt;</c> to the todo line or a
    /// final line <c>#status/&lt;column&gt;</c> (and a line ending) to the note, first ending the last line when it has
    /// none. Saves through <see cref="NoteStore.SaveIfUnchanged(EditableNote, string, out EditableNote?, NoteHistory?)"/>
    /// (BOM, CRLF and every other byte kept; a change in between is <see cref="BoardMoveResult.Changed"/>), keeping the
    /// previous bytes as a version with <paramref name="history"/>. Never throws for a read or write failure.
    /// </summary>
    public static BoardMoveResult Move(NoteStore store, BoardCard card, string column, NoteHistory? history = null) =>
        Move(store, card, column, history, null);

    /// <summary>Like <see cref="Move(NoteStore, BoardCard, string, NoteHistory?)"/>; <paramref name="beforeSave"/> runs after the read, before the save.</summary>
    internal static BoardMoveResult Move(NoteStore store, BoardCard card, string column, NoteHistory? history, Action? beforeSave)
    {
        if (!card.Columns.Contains(column, StringComparer.Ordinal))
        {
            throw new ArgumentException("Not a column of the board.", nameof(column));
        }

        if (string.Equals(card.Column, column, StringComparison.Ordinal))
        {
            return BoardMoveResult.Unchanged;
        }

        try
        {
            if (!store.TryFind(card.Note, out NoteInfo? found) || found is null
                || !string.Equals(found.Name, card.Note, StringComparison.Ordinal))
            {
                return BoardMoveResult.Missing;
            }

            EditableNote opened = store.ReadForEdit(found);
            string? moved = card.Todo is { } todo
                ? MoveTodo(opened.Text, card, todo, column)
                : MoveNote(opened.Text, card, column);
            if (moved is null)
            {
                return BoardMoveResult.Changed;
            }

            beforeSave?.Invoke();
            return store.SaveIfUnchanged(opened, moved, out _, history) switch
            {
                NoteSaveResult.Saved => BoardMoveResult.Done,
                NoteSaveResult.Missing => BoardMoveResult.Missing,
                NoteSaveResult.TooLarge => BoardMoveResult.Failed,
                _ => BoardMoveResult.Changed,
            };
        }
        catch (FileNotFoundException)
        {
            return BoardMoveResult.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return BoardMoveResult.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NoteHistoryException)
        {
            return BoardMoveResult.Failed;
        }
    }

    /// <summary>
    /// The first status token in <paramref name="line"/> at or after <paramref name="from"/> whose column is one of
    /// <paramref name="columns"/>: <c>#status/</c> (ASCII-case-insensitive) at the line's start or after whitespace,
    /// outside inline code spans (<see cref="CodeSpans.For"/>), then the longest run of ASCII letters, digits,
    /// <c>-</c> and <c>_</c>, which must be a column (<see cref="NormalizeColumn"/>). A token with an unknown column is
    /// plain text. <c>null</c> when there is none.
    /// </summary>
    internal static StatusToken? Find(string line, int from, IReadOnlyList<string> columns)
    {
        CodeSpans? spans = CodeSpans.For(line);
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '`')
            {
                i = spans!.Skip(i);
                continue;
            }

            if (c == '#' && i >= from && (i == 0 || char.IsWhiteSpace(line[i - 1]))
                && line.Length - i >= StatusPrefix.Length
                && System.Text.Ascii.EqualsIgnoreCase(line.AsSpan(i, StatusPrefix.Length), StatusPrefix))
            {
                int start = i + StatusPrefix.Length;
                int end = start;
                while (end < line.Length && IsColumnChar(line[end]))
                {
                    end++;
                }

                if (NormalizeColumn(line[start..end]) is { } column && columns.Contains(column, StringComparer.Ordinal))
                {
                    return new StatusToken(start, end - start, column);
                }

                i = Math.Max(end, i + 1);
                continue;
            }

            i++;
        }

        return null;
    }

    /// <summary>
    /// A note's status: the first status token on a line outside fences
    /// (<see cref="TagParser.NumberedLinesOutsideFences"/>) that is no open note-todo line, with its 1-based line.
    /// </summary>
    internal static (int Line, StatusToken Token)? NoteStatus(string text, IReadOnlyList<string> columns)
    {
        foreach ((int number, string line) in TagParser.NumberedLinesOutsideFences(text))
        {
            if (NoteTodoParser.ParseLine(number, line) is not null)
            {
                continue;
            }

            if (Find(line, 0, columns) is { } token)
            {
                return (number, token);
            }
        }

        return null;
    }

    private static bool IsColumnChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';

    // Where a todo line's text starts: just after "[ ] " (the line is a todo line, so its first "[ ]" is the box).
    private static int TodoTextStart(string line) =>
        line.IndexOf(NoteTodoParser.OpenBox, StringComparison.Ordinal) + NoteTodoParser.OpenBox.Length + 1;

    // The leading No status column only with cards, then the board's columns; each column's cards by note name
    // ignoring case, then ordinal, a note card before its todos, todos in line order.
    private static BoardView Arrange(BoardSetting setting, List<BoardCard> cards)
    {
        List<BoardCard> sorted = cards
            .OrderBy(c => c.Note, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Note, StringComparer.Ordinal)
            .ThenBy(c => c.Todo is null ? 0 : 1)
            .ThenBy(c => c.Todo?.Line ?? 0)
            .ToList();
        List<BoardColumn> columns = [];
        List<BoardCard> none = sorted.Where(c => c.Column is null).ToList();
        if (none.Count > 0)
        {
            columns.Add(new BoardColumn(null, none));
        }

        foreach (string column in setting.Columns)
        {
            columns.Add(new BoardColumn(column, sorted.Where(c => string.Equals(c.Column, column, StringComparison.Ordinal)).ToList()));
        }

        return new BoardView(setting.Tag, columns, false);
    }

    // The note's text with its status token naming the column, or null when the token is not where the board saw it.
    private static string? MoveNote(string text, BoardCard card, string column)
    {
        (int Line, StatusToken Token)? status = NoteStatus(text, card.Columns);
        if (status?.Line != card.StatusLine || !string.Equals(status?.Token.Column, card.Column, StringComparison.Ordinal))
        {
            return null;
        }

        if (status is not { } found)
        {
            string ended = text.Length == 0 || text.EndsWith('\n') ? text : text + "\n";
            return ended + StatusPrefix + column + "\n";
        }

        int start = LineStart(text, found.Line);
        return Replace(text, start + found.Token.Start, found.Token.Length, column);
    }

    // The note's text with the todo's status token naming the column, or null when the line is not the todo as indexed
    // or its token is not the one the board saw.
    private static string? MoveTodo(string text, BoardCard card, NoteTodo todo, string column)
    {
        ParsedNoteTodo? current = NoteTodoParser.Parse(text).FirstOrDefault(t => t.Line == todo.Line);
        if (current is null || !string.Equals(current.Raw, todo.Raw, StringComparison.Ordinal))
        {
            return null;
        }

        StatusToken? token = Find(todo.Raw, TodoTextStart(todo.Raw), card.Columns);
        if (!string.Equals(token?.Column, card.Column, StringComparison.Ordinal))
        {
            return null;
        }

        int start = LineStart(text, todo.Line);
        if (token is null)
        {
            int end = start + todo.Raw.Length;
            return string.Concat(text.AsSpan(0, end), " " + StatusPrefix + column, text.AsSpan(end));
        }

        return Replace(text, start + token.Start, token.Length, column);
    }

    private static int LineStart(string text, int line)
    {
        int start = 0;
        for (int n = 1; n < line; n++)
        {
            start = text.IndexOf('\n', start) + 1;
        }

        return start;
    }

    private static string Replace(string text, int start, int length, string column) =>
        string.Concat(text.AsSpan(0, start), column, text.AsSpan(start + length));
}

/// <summary>A status token's column inside one line: where the column starts, its length and its lower-cased name.</summary>
internal sealed record StatusToken
{
    public StatusToken(int start, int length, string column)
    {
        Start = start;
        Length = length;
        Column = column;
    }

    /// <summary>The index in the line of the column's first character (after <c>#status/</c>).</summary>
    public int Start { get; }

    /// <summary>The column's length as written.</summary>
    public int Length { get; }

    /// <summary>The column, lower-cased.</summary>
    public string Column { get; }
}
