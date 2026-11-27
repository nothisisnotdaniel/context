namespace Context.Tests;

// Job 483 AC-4: the people of a meeting by role.
public sealed class MeetingPeopleTests
{
    private static readonly DateTime At = new DateTime(2026, 10, 29, 9, 0, 0);

    private static Appointment Meeting(string? organizer, string[] attendees, string[]? optional = null) =>
        new Appointment("Sync", null, At, At.AddHours(1), false, attendees, organizer, optional);

    private static (string, MeetingRole)[] Rows(IReadOnlyList<MeetingPerson> people) =>
        [.. people.Select(p => (p.Name, p.Role))];

    [Fact]
    public void For_OrganizerThenRequiredThenOptional_FirstSeenOrder()
    {
        IReadOnlyList<MeetingPerson> people = MeetingPeople.For(
        [
            Meeting(null, ["Opt One", "Bob", "Cy", "Opt Two"], ["Opt One", "Opt Two"]),
            Meeting("Ann", ["Dee"]),
        ]);

        Assert.Equal(
            [
                ("Ann", MeetingRole.Organizer), ("Bob", MeetingRole.Required), ("Cy", MeetingRole.Required),
                ("Dee", MeetingRole.Required), ("Opt One", MeetingRole.Optional), ("Opt Two", MeetingRole.Optional),
            ],
            Rows(people));
    }

    [Fact]
    public void For_DuplicatesByCase_OnceWithFirstSpelling()
    {
        IReadOnlyList<MeetingPerson> people = MeetingPeople.For(
        [
            Meeting(null, ["Anna Schmidt", "bob"]),
            Meeting(null, ["ANNA SCHMIDT", "Bob"]),
        ]);

        Assert.Equal([("Anna Schmidt", MeetingRole.Required), ("bob", MeetingRole.Required)], Rows(people));
    }

    [Fact]
    public void For_OrganizerAlsoAttendee_NotRepeated()
    {
        IReadOnlyList<MeetingPerson> people = MeetingPeople.For(
        [
            Meeting("Ann Lee", ["ann lee", "Bob"], ["ann lee"]),
            Meeting("Other", ["Ann Lee"]),
        ]);

        Assert.Equal([("Ann Lee", MeetingRole.Organizer), ("Bob", MeetingRole.Required)], Rows(people));
    }

    [Fact]
    public void For_RequiredInAny_IsRequired()
    {
        IReadOnlyList<MeetingPerson> people = MeetingPeople.For(
        [
            Meeting(null, ["Cy", "Bob"], ["Cy", "Bob"]),
            Meeting(null, ["cy"]),
        ]);

        Assert.Equal([("Cy", MeetingRole.Required), ("Bob", MeetingRole.Optional)], Rows(people));
    }

    [Fact]
    public void For_FirstNonNullOrganizer()
    {
        IReadOnlyList<MeetingPerson> people = MeetingPeople.For([Meeting(null, []), Meeting("Dee", []), Meeting("Eve", [])]);

        Assert.Equal([("Dee", MeetingRole.Organizer)], Rows(people));
    }

    [Fact]
    public void For_NoOrganizerNoAttendees_Empty()
    {
        Assert.Empty(MeetingPeople.For([]));
        Assert.Empty(MeetingPeople.For([Meeting(null, [])]));
    }

    [Fact]
    public void MeetingPerson_ToString_OnlyTypeName()
    {
        Assert.Equal("MeetingPerson", new MeetingPerson("Ann", MeetingRole.Required).ToString());
    }
}
