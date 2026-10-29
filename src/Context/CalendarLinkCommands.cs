using System.Globalization;
using Spectre.Console;

namespace Context;

/// <summary>
/// <c>/calendar-link &lt;url&gt;</c> saves the published calendar link, <c>/calendar-link</c> shows it (masked)
/// and <c>/calendar-link off</c> removes it and its cached copy. The link is a secret: it is only ever shown as
/// <see cref="CalendarLink.Mask"/>, and what the user typed is never echoed back.
/// </summary>
internal static class CalendarLinkCommands
{
    public const string Invalid = "The calendar link must be an https:// or webcal:// address.";
    public const string NotSet = "No calendar link set.";
    public const string Removed = "Calendar link removed.";
    public const string Unreadable = "Can't read calendar-link.txt. Set the link again or type /calendar-link off.";
    public const string NotSaved = "Can't save the calendar link. Check that the Context folder is writable.";
    public const string NotRemoved = "Can't remove the calendar link. Check that the Context folder is writable.";

    private const string Off = "off";
    private const string TimeFormat = "yyyy-MM-dd HH:mm";

    /// <summary>Adds <c>/calendar-link</c> to <paramref name="registry"/>.</summary>
    /// <param name="zone">The zone the last download time is shown in.</param>
    public static void Register(CommandRegistry registry, CalendarLink link, CalendarFeed feed, TimeZoneInfo zone)
    {
        registry.Register(
            "/calendar-link",
            "Exchange calendar: /calendar-link <https or webcal url>, or off",
            argument => Run(link, feed, zone, argument));
    }

    private static CommandResult Run(CalendarLink link, CalendarFeed feed, TimeZoneInfo zone, string argument)
    {
        string value = argument.Trim();
        if (value.Length == 0)
        {
            return Show(link, feed, zone);
        }

        if (value.Equals(Off, StringComparison.OrdinalIgnoreCase))
        {
            return Clear(link, feed);
        }

        if (CalendarLink.Validate(value) is null)
        {
            return CommandResult.Error(Invalid);
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
            return CommandResult.Error(NotSaved);
        }

        return Hint($"Calendar link saved ([steelblue1]{Masked(saved)}[/]).");
    }

    private static CommandResult Show(CalendarLink link, CalendarFeed feed, TimeZoneInfo zone)
    {
        Uri? uri;
        try
        {
            uri = link.Get();
        }
        catch (CalendarLinkException)
        {
            return CommandResult.Error(Unreadable);
        }

        if (uri is null)
        {
            return Hint($"[grey]{NotSet}[/]");
        }

        DateTime? cachedAt = feed.CachedAtUtc();
        string downloaded = cachedAt is null
            ? "not downloaded yet"
            : "last download " + TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(cachedAt.Value, DateTimeKind.Utc), zone)
                .ToString(TimeFormat, CultureInfo.InvariantCulture);
        return Hint($"Calendar link: [steelblue1]{Masked(uri)}[/] [grey]({downloaded})[/]");
    }

    private static CommandResult Clear(CalendarLink link, CalendarFeed feed)
    {
        bool had;
        try
        {
            had = link.Clear();
            feed.ClearCache();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Error(NotRemoved);
        }

        return Hint(had ? Removed : $"[grey]{NotSet}[/]");
    }

    private static CommandResult Hint(string markup) => new(hint: new Markup(markup));

    private static string Masked(Uri uri) => Markup.Escape(SafeText.Clean(CalendarLink.Mask(uri)));
}
