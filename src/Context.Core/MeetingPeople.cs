namespace Context;

/// <summary>The part a person plays in a meeting (job 483).</summary>
internal enum MeetingRole
{
    /// <summary>The <c>ORGANIZER</c> (Outlook: <c>Organizer</c>).</summary>
    Organizer,

    /// <summary>An attendee who isn't optional (<c>CHAIR</c>, <c>REQ-PARTICIPANT</c>, no or any other <c>ROLE</c>).</summary>
    Required,

    /// <summary>An attendee in <see cref="Appointment.OptionalAttendees"/>.</summary>
    Optional,
}

/// <summary>One person of a meeting and the part they play. The name is raw: clean it before showing it.</summary>
internal sealed record MeetingPerson
{
    public MeetingPerson(string name, MeetingRole role)
    {
        Name = name;
        Role = role;
    }

    public string Name { get; }

    public MeetingRole Role { get; }

    /// <summary>Only the type name, so a name never leaks through formatting.</summary>
    public override string ToString() => nameof(MeetingPerson);
}

/// <summary>The people of a meeting by role, for the desktop's appointment page (job 483).</summary>
internal static class MeetingPeople
{
    /// <summary>
    /// The organizer (the first non-null <see cref="Appointment.Organizer"/>), then the required attendees, then the
    /// optional ones, over every appointment in <paramref name="appointments"/>. Within a group names keep first-seen
    /// order; each name appears once (compared by <see cref="PersonParser.Key"/>) with its first spelling. The
    /// organizer is never repeated as an attendee, and a name required in any appointment is required. Empty when there
    /// is no organizer and no attendee.
    /// </summary>
    public static IReadOnlyList<MeetingPerson> For(IEnumerable<Appointment> appointments)
    {
        List<Appointment> all = [.. appointments];
        string? organizer = all.Select(a => a.Organizer).FirstOrDefault(o => o is not null);

        // Keys that are required in at least one appointment.
        HashSet<string> required = new HashSet<string>(StringComparer.Ordinal);
        foreach (Appointment appointment in all)
        {
            HashSet<string> optional = new HashSet<string>(
                appointment.OptionalAttendees.Select(PersonParser.Key), StringComparer.Ordinal);
            foreach (string name in appointment.Attendees)
            {
                string key = PersonParser.Key(name);
                if (!optional.Contains(key))
                {
                    required.Add(key);
                }
            }
        }

        List<MeetingPerson> people = [];
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        if (organizer is not null)
        {
            seen.Add(PersonParser.Key(organizer));
            people.Add(new MeetingPerson(organizer, MeetingRole.Organizer));
        }

        // First spelling and first-seen order over every attendee, then split into the two groups.
        List<string> attendees = [];
        foreach (Appointment appointment in all)
        {
            foreach (string name in appointment.Attendees)
            {
                if (seen.Add(PersonParser.Key(name)))
                {
                    attendees.Add(name);
                }
            }
        }

        foreach (string name in attendees)
        {
            if (required.Contains(PersonParser.Key(name)))
            {
                people.Add(new MeetingPerson(name, MeetingRole.Required));
            }
        }

        foreach (string name in attendees)
        {
            if (!required.Contains(PersonParser.Key(name)))
            {
                people.Add(new MeetingPerson(name, MeetingRole.Optional));
            }
        }

        return people;
    }
}
