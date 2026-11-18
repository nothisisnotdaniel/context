namespace Context.Tests;

public sealed class MeetingPrepTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 29, 10, 0, 0);

    private readonly string parent;
    private readonly string root;

    public MeetingPrepTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-meetingprep-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private static Appointment Meeting(string title, params string[] attendees) =>
        new(title, null, Start, Start.AddHours(1), false, attendees);

    private static NoteInfo Note(string name) => new(name, Start, "/nowhere/" + name + ".md");

    private static IReadOnlyList<RelatedNote> Related(
        Appointment appointment, string[] notes, params (string Note, string Tag)[] tags) =>
        MeetingPrep.Related(appointment, notes.Select(Note).ToList(), tags);

    // AC-1
    [Fact]
    public void Words_Example()
    {
        Assert.Equal(["planning", "acme", "team"], MeetingPrep.Words("Q3 planning with ACME and the HR team"));
    }

    // AC-1
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("a bc 12 2026 123abc", new[] { "123abc" })]
    [InlineData("ACME-Kickoff, acme/kickoff!", new[] { "acme", "kickoff" })]
    [InlineData("Weekly sync call meeting daily", new string[0])]
    [InlineData("Treffen mit der Firma für das Projekt und die Zukunft", new[] { "treffen", "firma", "projekt", "zukunft" })]
    [InlineData("ÜBERSICHT Café", new[] { "übersicht", "café" })]
    [InlineData("tab\there\nnew_line", new[] { "tab", "here", "new", "line" })]
    public void Words_SplitsLowersAndDrops(string text, string[] expected)
    {
        Assert.Equal(expected, MeetingPrep.Words(text));
    }

    // AC-1
    [Fact]
    public void StopWords_HoldTheListedWords()
    {
        foreach (string word in "the and for with meeting call sync weekly daily und der die das mit für".Split(' '))
        {
            Assert.Contains(word, MeetingPrep.StopWords);
        }

        Assert.Equal(3, MeetingPrep.MinWordLength);
        Assert.Equal(3, MeetingPrep.MaxRelated);
    }

    // AC-2: a tag matches the title, a bracket tag matches an attendee, a subpage title word matches.
    [Fact]
    public void Related_TagAttendeeAndTitleWord()
    {
        Appointment meeting = Meeting("ACME kickoff", "Anna Schmidt");

        IReadOnlyList<RelatedNote> related = Related(
            meeting,
            ["Clients", "people/Anna", "projects", "projects/ACME Rollout"],
            ("Clients", "acme"),
            ("people/Anna", "anna schmidt"));

        // people/Anna matches twice (tag and title word), so it ranks first.
        Assert.Equal(["people/Anna", "Clients", "projects/ACME Rollout"], related.Select(r => r.Name));
        Assert.Equal(["anna schmidt"], related[0].Tags);
        Assert.Equal(["anna"], related[0].Words);
        Assert.Equal(["acme"], related[1].Tags);
        Assert.Empty(related[1].Words);
        Assert.Empty(related[2].Tags);
        Assert.Equal(["acme"], related[2].Words);
    }

    // AC-2: every tag word must be an appointment word; a tag without words never matches; the location is unused.
    [Fact]
    public void Related_PartialTagStopWordTagAndLocation_NoMatch()
    {
        Appointment meeting = new("Anna review", "ACME tower", Start, Start.AddHours(1), false);

        IReadOnlyList<RelatedNote> related = Related(
            meeting,
            ["A", "B", "C"],
            ("A", "anna schmidt"),
            ("B", "the"),
            ("C", "acme"));

        Assert.Empty(related);
    }

    // AC-2: the title word comes from the last segment only, not the parent's name.
    [Fact]
    public void Related_ParentSegment_NotMatched()
    {
        IReadOnlyList<RelatedNote> related = Related(Meeting("ACME kickoff"), ["ACME", "ACME/Notes"]);

        Assert.Equal(["ACME"], related.Select(r => r.Name));
    }

    // AC-3: ranked by matches, then name ignoring case, then ordinal; at most three; sorted tags and words.
    [Fact]
    public void Related_RankCapAndTieOrder()
    {
        Appointment meeting = Meeting("Zeta alpha budget plan");

        IReadOnlyList<RelatedNote> related = Related(
            meeting,
            ["beta", "Budget Plan", "alpha", "Alpha", "zeta"],
            ("zeta", "plan"),
            ("zeta", "budget"),
            ("zeta", "alpha"),
            ("beta", "zeta"));

        Assert.Equal(["zeta", "Budget Plan", "Alpha"], related.Select(r => r.Name));
        Assert.Equal(["alpha", "budget", "plan"], related[0].Tags);
        Assert.Equal(["zeta"], related[0].Words);
        Assert.Equal(["budget", "plan"], related[1].Words);
        Assert.Equal(["alpha"], related[2].Words);
    }

    // AC-3: each note once, Name raw.
    [Fact]
    public void Related_NoteOnce_RawName()
    {
        const string Name = "ACME <i>\u001b";

        RelatedNote note = Assert.Single(Related(Meeting("acme"), [Name], (Name, "acme"), (Name, "acme")));

        Assert.Equal(Name, note.Name);
        Assert.Equal(["acme"], note.Tags);
        Assert.Equal(["acme"], note.Words);
    }

    // AC-3: no words, no related notes.
    [Theory]
    [InlineData("")]
    [InlineData("The weekly sync")]
    [InlineData("Q3 HR")]
    public void Related_AppointmentWithoutWords_Empty(string title)
    {
        Assert.Empty(Related(Meeting(title), ["the", "weekly", "sync", "Q3 HR"], ("the", "the")));
    }

    // AC-3: rows for notes that aren't in the list (stale or unknown) are ignored.
    [Fact]
    public void Related_TagRowOfUnlistedNote_Ignored()
    {
        Assert.Empty(Related(Meeting("acme"), [], ("Gone", "acme")));
    }

    // AC-4: a sealed .ctxs note is in neither NoteStore.List() nor context.db, so it is never related.
    [Fact]
    public void Related_SealedNote_NeverRelated()
    {
        File.WriteAllText(Path.Combine(root, "ACME.ctxs"), "{}");
        File.WriteAllText(Path.Combine(root, "ACME plan.md"), "#acme\n");
        NoteStore store = new NoteStore(root);
        TagIndex index = new TagIndex(root);
        index.Sync(store);

        IReadOnlyList<RelatedNote> related = MeetingPrep.Related(Meeting("ACME"), store.List(), index.NoteTags());

        Assert.Equal(["ACME plan"], related.Select(r => r.Name));
        Assert.DoesNotContain(index.NoteTags(), r => r.Note == "ACME");
    }

    [Fact]
    public void ToString_HidesRawText()
    {
        RelatedNote note = new RelatedNote("SECRET", ["SECRET"], ["SECRET"]);

        Assert.Equal("RelatedNote", note.ToString());
        Assert.Equal("RelatedNote", $"{note}");
    }

    // 214 AC-6: max null keeps every match, ranked as before.
    [Fact]
    public void Related_NoCap_ReturnsEveryMatch()
    {
        string[] notes = ["acme a", "acme b", "acme c", "acme d", "acme e", "other"];

        IReadOnlyList<RelatedNote> related = MeetingPrep.Related(
            Meeting("ACME review"), notes.Select(Note).ToList(), [("acme e", "review")], null);

        Assert.Equal(["acme e", "acme a", "acme b", "acme c", "acme d"], related.Select(r => r.Name).ToArray());
        Assert.Equal(["review"], related[0].Tags);
    }

    // 214: the three-argument form keeps MaxRelated, and an explicit cap of 3 gives the same notes.
    [Fact]
    public void Related_ThreeArguments_KeepsMaxRelated()
    {
        string[] notes = ["acme a", "acme b", "acme c", "acme d", "acme e"];
        List<NoteInfo> listed = notes.Select(Note).ToList();

        IReadOnlyList<RelatedNote> capped = MeetingPrep.Related(Meeting("ACME"), listed, []);
        IReadOnlyList<RelatedNote> three = MeetingPrep.Related(Meeting("ACME"), listed, [], MeetingPrep.MaxRelated);

        Assert.Equal(["acme a", "acme b", "acme c"], capped.Select(r => r.Name).ToArray());
        Assert.Equal(capped.Select(r => r.Name), three.Select(r => r.Name));
    }
}
