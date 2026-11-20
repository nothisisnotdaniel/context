namespace Context;

/// <summary>What <see cref="CalendarLinkView.Run"/> did or found.</summary>
internal enum CalendarLinkViewKind
{
    /// <summary>
    /// A link is set: <see cref="CalendarLinkView.Masked"/> and <see cref="CalendarLinkView.CachedAtUtc"/>.
    /// </summary>
    Shown,

    /// <summary>No link is set (shown, or <c>off</c> with nothing to remove).</summary>
    NotSet,

    /// <summary>The link was saved and the old cache dropped: <see cref="CalendarLinkView.Masked"/>.</summary>
    Saved,

    /// <summary><c>off</c> deleted the link and the cache.</summary>
    Removed,

    /// <summary>The typed value isn't an <c>https://</c> or <c>webcal://</c> link; nothing was written.</summary>
    Invalid,

    /// <summary><c>calendar-link.txt</c> can't be read or holds no valid link.</summary>
    Unreadable,

    /// <summary>The link couldn't be written.</summary>
    NotSaved,

    /// <summary>The link or the cache couldn't be deleted.</summary>
    NotRemoved,
}

/// <summary>
/// The outcome of <c>/calendar-link [url|off]</c>, with no rendering. The link is a bearer secret, so this only
/// ever holds <see cref="CalendarLink.Mask"/> of it: never the <see cref="Uri"/>, the stored link, the typed
/// argument or an exception message. Front ends should still clean <see cref="Masked"/> before showing it.
/// </summary>
internal sealed record CalendarLinkView
{
    private const string Off = "off";

    public CalendarLinkView(CalendarLinkViewKind kind, string? masked = null, DateTime? cachedAtUtc = null)
    {
        Kind = kind;
        Masked = masked;
        CachedAtUtc = cachedAtUtc;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never
    /// print them. Front ends draw it through their <c>Render</c> step.
    /// </summary>
    public override string ToString() => nameof(CalendarLinkView);

    public CalendarLinkViewKind Kind { get; }

    /// <summary>The link as <see cref="CalendarLink.Mask"/> shows it (<c>Shown</c>, <c>Saved</c>), else null.</summary>
    public string? Masked { get; }

    /// <summary>When the cached copy was downloaded (UTC; <c>Shown</c> only), <c>null</c> if never.</summary>
    public DateTime? CachedAtUtc { get; }

    /// <summary>
    /// Runs the command: a blank <paramref name="argument"/> shows the link, <c>off</c> (any case) deletes the
    /// link and the cache, anything else is validated and saved (and the old cache deleted).
    /// </summary>
    public static CalendarLinkView Run(CalendarLink link, CalendarFeed feed, string argument)
    {
        string value = argument.Trim();
        if (value.Length == 0)
        {
            return Show(link, feed);
        }

        if (value.Equals(Off, StringComparison.OrdinalIgnoreCase))
        {
            return Clear(link, feed);
        }

        if (CalendarLink.Validate(value) is null)
        {
            return new CalendarLinkView(CalendarLinkViewKind.Invalid);
        }

        Uri saved;
        try
        {
            saved = link.Set(value);
            // The cache belongs to the old link.
            feed.ClearCache();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new CalendarLinkView(CalendarLinkViewKind.NotSaved);
        }

        return new CalendarLinkView(CalendarLinkViewKind.Saved, CalendarLink.Mask(saved));
    }

    /// <summary>
    /// What <c>/calendar-link</c> shows without an argument: the masked link and the time of the cached copy,
    /// <c>NotSet</c> or <c>Unreadable</c>. Reads only; nothing is written or downloaded.
    /// </summary>
    internal static CalendarLinkView Show(CalendarLink link, CalendarFeed feed)
    {
        Uri? uri;
        try
        {
            uri = link.Get();
        }
        catch (CalendarLinkException)
        {
            return new CalendarLinkView(CalendarLinkViewKind.Unreadable);
        }

        if (uri is null)
        {
            return new CalendarLinkView(CalendarLinkViewKind.NotSet);
        }

        return new CalendarLinkView(CalendarLinkViewKind.Shown, CalendarLink.Mask(uri), feed.CachedAtUtc());
    }

    private static CalendarLinkView Clear(CalendarLink link, CalendarFeed feed)
    {
        bool had;
        try
        {
            had = link.Clear();
            feed.ClearCache();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new CalendarLinkView(CalendarLinkViewKind.NotRemoved);
        }

        return new CalendarLinkView(had ? CalendarLinkViewKind.Removed : CalendarLinkViewKind.NotSet);
    }
}
