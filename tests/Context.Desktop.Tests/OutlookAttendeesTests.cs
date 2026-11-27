namespace Context.Tests;

// Job 483 AC-5, AC-6: Outlook's display-name lists become names; addresses never pass.
public sealed class OutlookAttendeesTests
{
    [Fact]
    public void Names_SplitTrimNormalize_DropsEmptyInvalidAndAddresses()
    {
        Assert.Equal(["Ann Lee", "Bob Ray"], OutlookAttendees.Names("Ann Lee; Bob  Ray ;;bob@x.org"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ; ;")]
    [InlineData("a/b")]
    [InlineData("Ann <ann@x.org>")]
    [InlineData("ann@x.org")]
    public void Names_NothingValid_Empty(string? list)
    {
        Assert.Empty(OutlookAttendees.Names(list));
    }

    [Fact]
    public void Names_KeepsOrder_DropsOnlyBadEntries()
    {
        Assert.Equal(
            ["Lee, Ann", "Cy", "Dee"],
            OutlookAttendees.Names("  Lee, Ann ;x@x.org; Cy\t;\"quoted\";Dee"));
    }

    [Theory]
    [InlineData("Ann  Lee ", "Ann Lee")]
    [InlineData("ann@x.org", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("a|b", null)]
    public void Name_FilteredLikeAnEntry(string? organizer, string? expected)
    {
        Assert.Equal(expected, OutlookAttendees.Name(organizer));
    }

    [Fact]
    public void People_RequiredThenOptional_OptionalSubset()
    {
        OutlookPeople people = OutlookAttendees.People(" Olga ", "Ann Lee; ann@x.org; Bob", "Cy;cy@x.org");

        Assert.Equal("Olga", people.Organizer);
        Assert.Equal(["Ann Lee", "Bob", "Cy"], people.Attendees);
        Assert.Equal(["Cy"], people.OptionalAttendees);
        Assert.Equal("OutlookPeople", people.ToString());
    }

    [Fact]
    public void People_AddressOrganizer_NullAndNothingLeaks()
    {
        OutlookPeople people = OutlookAttendees.People("olga@x.org", null, "dee@x.org");

        Assert.Null(people.Organizer);
        Assert.Empty(people.Attendees);
        Assert.Empty(people.OptionalAttendees);
    }
}
