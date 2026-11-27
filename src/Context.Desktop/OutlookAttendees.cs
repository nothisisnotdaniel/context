namespace Context;

/// <summary>
/// Turns the display-name lists an Outlook appointment gives (<c>Organizer</c>, <c>RequiredAttendees</c>,
/// <c>OptionalAttendees</c>) into the names an <see cref="Appointment"/> keeps. Each name is normalized like a mention
/// (<see cref="PersonParser.Normalize"/>); an empty or invalid entry, and any entry holding <c>@</c> (an address), is
/// dropped, so no address reaches the app. Pure, so it is tested without Outlook.
/// </summary>
internal static class OutlookAttendees
{
    /// <summary>
    /// The names in Outlook's <c>;</c>-separated <paramref name="list"/>, in order, each trimmed and normalized.
    /// Empty, invalid and <c>@</c> entries are dropped; <c>null</c> gives an empty list.
    /// </summary>
    internal static IReadOnlyList<string> Names(string? list)
    {
        if (list is null)
        {
            return [];
        }

        return [.. list.Split(';').Select(Name).OfType<string>()];
    }

    /// <summary>
    /// One display name (Outlook's <c>Organizer</c>), trimmed and normalized; <c>null</c> when it is missing, invalid
    /// or holds <c>@</c>.
    /// </summary>
    internal static string? Name(string? entry)
    {
        if (entry is null || entry.Contains('@', StringComparison.Ordinal))
        {
            return null;
        }

        return PersonParser.Normalize(entry);
    }

    /// <summary>
    /// The appointment's people from Outlook's three fields: the organizer (<see cref="Name"/>), the attendees (the
    /// required names followed by the optional names) and the optional names.
    /// </summary>
    internal static OutlookPeople People(string? organizer, string? required, string? optional)
    {
        IReadOnlyList<string> optionalNames = Names(optional);
        List<string> attendees = [.. Names(required), .. optionalNames];
        return new OutlookPeople(Name(organizer), attendees, optionalNames);
    }
}

/// <summary>The people of one Outlook appointment, as <see cref="OutlookAttendees.People"/> reads them.</summary>
internal sealed class OutlookPeople
{
    public OutlookPeople(string? organizer, IReadOnlyList<string> attendees, IReadOnlyList<string> optionalAttendees)
    {
        Organizer = organizer;
        Attendees = attendees;
        OptionalAttendees = optionalAttendees;
    }

    /// <summary>The organizer's name, or <c>null</c>.</summary>
    public string? Organizer { get; }

    /// <summary>The required names followed by the optional names.</summary>
    public IReadOnlyList<string> Attendees { get; }

    /// <summary>The optional names.</summary>
    public IReadOnlyList<string> OptionalAttendees { get; }

    /// <summary>Only the type name, so no name leaks through formatting.</summary>
    public override string ToString() => nameof(OutlookPeople);
}
