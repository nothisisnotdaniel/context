namespace Context;

/// <summary>What <see cref="DesktopState.Board"/> found.</summary>
internal enum BoardPageKind
{
    /// <summary><see cref="BoardPage.View"/> holds the board (maybe with no cards, or <see cref="BoardView.Unreadable"/>).</summary>
    Shown,

    /// <summary>The typed text is no tag; nothing was read.</summary>
    InvalidTag,

    /// <summary><c>settings.json</c> has no board for the tag.</summary>
    NoBoard,

    /// <summary><c>settings.json</c> can't be read or holds a bad value.</summary>
    SettingsUnreadable,

    /// <summary>The board can't be built (<c>context.db</c> or anything else failed).</summary>
    Unreadable,
}

/// <summary>The board page's data (job 242): a kind, the normalized tag and, when shown, the board.</summary>
internal sealed record BoardPage
{
    public BoardPage(BoardPageKind kind, string tag, BoardView? view)
    {
        Kind = kind;
        Tag = tag;
        View = view;
    }

    public BoardPageKind Kind { get; }

    /// <summary>The normalized tag without <c>#</c> (empty for <see cref="BoardPageKind.InvalidTag"/>).</summary>
    public string Tag { get; }

    public BoardView? View { get; }

    /// <summary>Returns only the type name, so formatting the page never prints note text.</summary>
    public override string ToString() => nameof(BoardPage);
}
