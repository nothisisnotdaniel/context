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

    private const string TimeFormat = "yyyy-MM-dd HH:mm";

    /// <summary>Adds <c>/calendar-link</c> to <paramref name="registry"/>.</summary>
    /// <param name="zone">The zone the last download time is shown in.</param>
    public static void Register(CommandRegistry registry, CalendarLink link, CalendarFeed feed, TimeZoneInfo zone)
    {
        registry.Register(
            "/calendar-link",
            "Exchange calendar: /calendar-link <https or webcal url>, or off",
            argument => Render(CalendarLinkView.Run(link, feed, argument), zone));
    }

    /// <summary>
    /// Draws a <c>/calendar-link</c> view as a hint, with the download time in <paramref name="zone"/>. Only the
    /// masked link is ever shown, cleaned and escaped.
    /// </summary>
    internal static CommandResult Render(CalendarLinkView view, TimeZoneInfo zone)
    {
        switch (view.Kind)
        {
            case CalendarLinkViewKind.Invalid:
                return CommandResult.Error(Invalid);
            case CalendarLinkViewKind.Unreadable:
                return CommandResult.Error(Unreadable);
            case CalendarLinkViewKind.NotSaved:
                return CommandResult.Error(NotSaved);
            case CalendarLinkViewKind.NotRemoved:
                return CommandResult.Error(NotRemoved);
            case CalendarLinkViewKind.NotSet:
                return Hint($"[grey]{NotSet}[/]");
            case CalendarLinkViewKind.Removed:
                return Hint(Removed);
            case CalendarLinkViewKind.Saved:
                return Hint($"Calendar link saved ([steelblue1]{Masked(view)}[/]).");
            default:
                string downloaded = view.CachedAtUtc is null
                    ? "not downloaded yet"
                    : "last download " + TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(view.CachedAtUtc.Value, DateTimeKind.Utc), zone)
                        .ToString(TimeFormat, CultureInfo.InvariantCulture);
                return Hint($"Calendar link: [steelblue1]{Masked(view)}[/] [grey]({downloaded})[/]");
        }
    }

    private static CommandResult Hint(string markup) => new(hint: new Markup(markup));

    private static string Masked(CalendarLinkView view) => Markup.Escape(SafeText.Clean(view.Masked ?? string.Empty));
}
