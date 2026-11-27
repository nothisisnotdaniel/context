namespace Context.Tests;

public sealed class PersonViewTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Zone = IcsParserTests.Zone;

    private readonly string parent;
    private readonly string root;
    private readonly FakeHttpHandler http = FakeHttpHandler.Ok("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n");

    public PersonViewTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-personview-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        http.Dispose();
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private string DbPath => Path.Combine(root, TagIndex.FileName);

    // A feed with a link set, so a fetch would make a request: the view must never make one.
    private CalendarFeed Feed()
    {
        CalendarLink link = new CalendarLink(root);
        link.Set(FakeHttpHandler.Link);
        return new CalendarFeed(root, link, http, () => Now);
    }

    private PersonView Build(string argument, CalendarFeed? feed = null) =>
        PersonView.Build(new TagIndex(root), new NoteStore(root), new CalendarStore(root, Zone), feed, argument);

    private PersonView BuildWithTodos(string argument) =>
        PersonView.Build(
            new TagIndex(root), new NoteStore(root), new CalendarStore(root, Zone), null, argument, new TodoStore(root));

    // 159: adds a todo with a fixed created time; done ones are marked done at Now plus doneHours.
    private TodoItem AddTodo(string text, int dueDay, int? doneHours = null)
    {
        TodoItem todo = new TodoStore(root, () => Now).Add(text, new DateOnly(2026, 11, dueDay));
        return doneHours is { } hours ? new TodoStore(root, () => Now.AddHours(hours)).Complete(todo.Id)! : todo;
    }

    private void WriteNote(string name, string text)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private void WriteCalendar(string name, string text)
    {
        string folder = Path.Combine(root, CalendarStore.FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), text);
    }

    private void PlantCache(string text)
    {
        string cache = Path.Combine(root, CalendarFeed.CacheFileName);
        File.WriteAllText(cache, text);
        // Older than the cache lifetime: a fetch would download again.
        File.SetLastWriteTimeUtc(cache, Now.AddDays(-3));
    }

    private static string Calendar(params string[] events) =>
        "BEGIN:VCALENDAR\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";

    private static string Event(string title, string start, string end, params string[] more) =>
        $"BEGIN:VEVENT\r\nSUMMARY:{title}\r\nDTSTART{start}\r\nDTEND{end}\r\n"
        + string.Concat(more.Select(m => m + "\r\n")) + "END:VEVENT\r\n";

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0);

    // 156 AC-6: every field, with notes, file and cached appointments, latest first, and no request made.
    [Fact]
    public void Build_NotesAndAppointments_EveryField_NoRequest()
    {
        WriteNote("people", "# people");
        WriteNote("people/Anna Schmidt", "# Anna Schmidt\n@[Anna Schmidt]");
        WriteNote("b notes", "met @[anna  SCHMIDT]");
        WriteNote("A plan", "ask @[Anna Schmidt] #work");
        WriteNote("Other", "@[Bob]");
        WriteCalendar("work.ics", Calendar(
            Event("Sync with @[Anna Schmidt]", ":20261027T090000Z", ":20261027T100000Z"),
            Event("Lunch", ":20261028T110000Z", ":20261028T120000Z", "LOCATION:Cafe with @[anna schmidt]"),
            Event("Review", ":20261026T080000Z", ":20261026T090000Z", "ATTENDEE;CN=\"Anna  Schmidt\":mailto:anna@example.com"),
            Event("Not her", ":20261029T080000Z", ":20261029T090000Z", "ATTENDEE;CN=Bob:mailto:anna.schmidt@example.com"),
            Event("Mail only", ":20261029T100000Z", ":20261029T110000Z", "ATTENDEE:mailto:anna@example.com"),
            Event("Anna Schmidt plain", ":20261029T120000Z", ":20261029T130000Z")));
        PlantCache(Calendar(
            Event("Cached @[Anna Schmidt]", ":20261030T080000Z", ":20261030T090000Z"),
            "BEGIN:VEVENT\r\nSUMMARY:Day off\r\nDTSTART;VALUE=DATE:20261025\r\nATTENDEE;CN=anna schmidt:mailto:a@b\r\nEND:VEVENT\r\n"));

        PersonView view = Build("  anna   schmidt ", Feed());

        Assert.Equal(PersonViewKind.Mentions, view.Kind);
        Assert.Equal("Anna Schmidt", view.Person);
        Assert.Equal(["A plan", "b notes"], view.Notes);
        Assert.Equal(
            ["Cached @[Anna Schmidt]", "Lunch", "Sync with @[Anna Schmidt]", "Review", "Day off"],
            view.Appointments.Select(a => a.Title));
        Assert.Equal(At(30, 9), view.Appointments[0].Start);
        Assert.Equal("09:00-10:00", PersonView.TimeText(view.Appointments[0]));
        Assert.Equal("all day", PersonView.TimeText(view.Appointments[4]));
        Assert.Empty(view.UnreadableCalendarFiles);
        Assert.False(view.IndexUnreadable);
        Assert.Same(Zone, view.Zone);
        Assert.Equal(0, http.Requests);
        Assert.Equal(nameof(PersonView), view.ToString());
    }

    // 156 AC-6 / AC-7: a typed mention is the same person; with no page, the ordinal-first spelling is shown.
    [Fact]
    public void Build_TypedMention_NoPage_FirstSpelling()
    {
        WriteNote("A", "@[bob]");
        WriteNote("B", "@[Bob]");

        PersonView view = Build("@[ BOB ]");

        Assert.Equal(PersonViewKind.Mentions, view.Kind);
        Assert.Equal("Bob", view.Person);
        Assert.Equal(["A", "B"], view.Notes);
        Assert.Empty(view.Appointments);
    }

    // 156 AC-6: nobody mentions the person: NoMentions with the typed name, collapsed.
    [Fact]
    public void Build_NoMentions_TypedName()
    {
        WriteNote("A", "@[Bob]");

        PersonView view = Build(" Carl   Lee ");

        Assert.Equal(PersonViewKind.NoMentions, view.Kind);
        Assert.Equal("Carl Lee", view.Person);
        Assert.Empty(view.Notes);
        Assert.Empty(view.Appointments);
        Assert.False(view.IndexUnreadable);
    }

    // 156 AC-6: no name is MissingArgument, with nothing read.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@[ ]")]
    public void Build_NoName_MissingArgument(string argument)
    {
        PersonView view = Build(argument);

        Assert.Equal(PersonViewKind.MissingArgument, view.Kind);
        Assert.Equal(string.Empty, view.Person);
        Assert.False(File.Exists(DbPath));
    }

    // 156 AC-6: a bad context.db sets IndexUnreadable and still returns the appointments.
    [Fact]
    public void Build_BadDb_IndexUnreadable_StillAppointments()
    {
        WriteNote("A", "@[Anna]");
        WriteCalendar("a.ics", Calendar(Event("With @[Anna]", ":20261027T090000Z", ":20261027T100000Z")));
        byte[] garbage = new byte[2048];
        new Random(9).NextBytes(garbage);
        File.WriteAllBytes(DbPath, garbage);

        PersonView view = Build("Anna");

        Assert.True(view.IndexUnreadable);
        Assert.Equal(PersonViewKind.Mentions, view.Kind);
        Assert.Equal("Anna", view.Person);
        Assert.Empty(view.Notes);
        Assert.Equal("With @[Anna]", Assert.Single(view.Appointments).Title);
    }

    // 156 AC-6: unreadable calendar files are named, as in the current view; the rest still count.
    [Fact]
    public void Build_UnreadableCalendarFile_Named()
    {
        WriteCalendar("bad.ics", "not a calendar");
        WriteCalendar("good.ics", Calendar(Event("With @[Anna]", ":20261027T090000Z", ":20261027T100000Z")));

        PersonView view = Build("anna");

        Assert.Equal(["bad.ics"], view.UnreadableCalendarFiles);
        Assert.Equal("With @[Anna]", Assert.Single(view.Appointments).Title);
    }

    // 156 AC-6: without a cached copy nothing is downloaded either, and the files are only read.
    [Fact]
    public void Build_NoCache_NoRequest_OnlyContextDbWritten()
    {
        WriteNote("A", "@[Anna]");
        CalendarFeed feed = Feed();
        string[] before = Directory.GetFiles(root, "*", SearchOption.AllDirectories);

        PersonView view = Build("Anna", feed);

        Assert.Equal(0, http.Requests);
        Assert.Equal(["A"], view.Notes);
        Assert.Equal(
            before.Append(DbPath).Order(StringComparer.Ordinal),
            Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith("-journal", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }
    // 158 AC-5: the alias and the full name give the same person: every note, alias-only ones included, and every
    // appointment whose title, location or attendee uses either.
    [Theory]
    [InlineData("Anna")]
    [InlineData("@[as]")]
    [InlineData("anna schmidt")]
    public void Build_Alias_ResolvesToThePerson_NotesAndAppointments(string typed)
    {
        WriteNote("people", "# people");
        WriteNote("people/Anna Schmidt", "# Anna Schmidt\naliases:: Anna, AS\n@[AS]");
        WriteNote("A", "@[Anna]");
        WriteNote("B", "@[Anna Schmidt]");
        WriteNote("C", "@[as] @[Bob]");
        WriteCalendar("work.ics", Calendar(
            Event("Call @[AS]", ":20261027T090000Z", ":20261027T100000Z"),
            Event("Lunch", ":20261028T110000Z", ":20261028T120000Z", "LOCATION:Cafe with @[anna]"),
            Event("Review", ":20261026T080000Z", ":20261026T090000Z", "ATTENDEE;CN=AS:mailto:x@example.com"),
            Event("Full", ":20261025T080000Z", ":20261025T090000Z", "ATTENDEE;CN=Anna Schmidt:mailto:y@example.com"),
            Event("Bob only", ":20261029T080000Z", ":20261029T090000Z", "ATTENDEE;CN=Bob:mailto:z@example.com")));

        PersonView view = Build(typed);

        Assert.Equal(PersonViewKind.Mentions, view.Kind);
        Assert.Equal("Anna Schmidt", view.Person);
        Assert.Equal(["A", "B", "C"], view.Notes);
        Assert.Equal(["Lunch", "Call @[AS]", "Review", "Full"], view.Appointments.Select(a => a.Title));
        Assert.Empty(view.AmbiguousWith);
        Assert.Empty(view.AliasClashes);
    }

    // 158 AC-6: an alias of two people stays unresolved: its own mentions only, with the sorted titles.
    [Fact]
    public void Build_AmbiguousAlias_Unresolved_WithTitles()
    {
        WriteNote("people", "# people");
        WriteNote("people/Anna Schmidt", "# Anna Schmidt\naliases:: Anna");
        WriteNote("people/anna Berg", "# anna Berg\naliases:: ANNA");
        WriteNote("A", "@[Anna]");
        WriteNote("B", "@[Anna Schmidt]");
        WriteCalendar("work.ics", Calendar(
            Event("With @[anna]", ":20261027T090000Z", ":20261027T100000Z"),
            Event("With @[Anna Schmidt]", ":20261028T090000Z", ":20261028T100000Z")));

        PersonView view = Build("anna");

        Assert.Equal(PersonViewKind.Mentions, view.Kind);
        Assert.Equal("Anna", view.Person);
        Assert.Equal(["A"], view.Notes);
        Assert.Equal("With @[anna]", Assert.Single(view.Appointments).Title);
        Assert.Equal(["anna Berg", "Anna Schmidt"], view.AmbiguousWith);
        Assert.Empty(view.AliasClashes);
    }

    // 158 AC-9: the person's clashes, even with no mentions, and also when the index can't be read.
    [Fact]
    public void Build_AliasClashes_NoMentions_AndBadDb()
    {
        WriteNote("people", "# people");
        WriteNote("people/Anna Schmidt", "# Anna Schmidt\naliases:: Anna, AS, Anna Berg");
        WriteNote("people/Anna Berg", "# Anna Berg\naliases:: Anna");
        string[] expected =
        [
            "Alias Anna is also used by Anna Berg, so @[Anna] links to no one.",
            "Alias Anna Berg is another person's name, so @[Anna Berg] links to them.",
        ];

        PersonView view = Build("Anna Schmidt");

        Assert.Equal(PersonViewKind.NoMentions, view.Kind);
        Assert.Equal(expected, view.AliasClashes.Select(c => c.Text));

        File.WriteAllText(DbPath, "not a database at all, not at all, really not");
        PersonView unreadable = Build("as");

        Assert.True(unreadable.IndexUnreadable);
        Assert.Equal("Anna Schmidt", unreadable.Person);
        Assert.Equal(expected, unreadable.AliasClashes.Select(c => c.Text));
    }

    // 329 AC-2: a person page's topics stay "[ ]" lines; a "- { } next:" line is a note todo, never a topic, and a
    // "- [ ]" checkbox naming the person is neither.
    [Fact]
    public void Build_Topics_OnlyBracketTopicLines()
    {
        File.WriteAllText(
            Path.Combine(root, "Plan.md"),
            "- [ ] next: @[Anna] budget\n- { } next: @[Anna] hiring\n- [ ] call @[Anna]\n");

        PersonView view = BuildWithTodos("Anna");

        NoteTopic topic = Assert.Single(view.Topics);
        Assert.Equal(("Plan", 1, "budget"), (topic.Note, topic.Line, topic.Text));
        Assert.Empty(view.OpenTodos);
    }

    // 159 AC-5: open and done todos that mention the person by full name, alias or any case/space variant.
    [Fact]
    public void Build_Todos_ByNameAliasAndVariant_OpenThenDone()
    {
        WriteNote("people", "# people");
        WriteNote("people/Anna Schmidt", "# Anna Schmidt\naliases:: Anna, AS");
        WriteNote("people/Bob", "# Bob");
        TodoItem late = AddTodo("Call @[Anna Schmidt]", 9);
        TodoItem soon = AddTodo("Ask @[as] and @[Bob]", 3);
        TodoItem spaced = AddTodo("Mail @[ anna   SCHMIDT ]", 5);
        AddTodo("Nobody here, just Anna", 4);
        AddTodo("Only @[Bob]", 6);
        TodoItem doneFirst = AddTodo("Book room for @[Anna]", 1, doneHours: 1);
        TodoItem doneLater = AddTodo("Lunch with @[Anna Schmidt]", 2, doneHours: 5);

        PersonView view = BuildWithTodos("anna");

        Assert.Equal(PersonViewKind.Mentions, view.Kind);
        Assert.Equal("Anna Schmidt", view.Person);
        Assert.Equal([soon.Id, spaced.Id, late.Id], view.OpenTodos.Select(t => t.Id));
        Assert.Equal([doneLater.Id, doneFirst.Id], view.DoneTodos.Select(t => t.Id));
        Assert.False(view.TodosUnreadable);
        Assert.Equal(new DateOnly(2026, 10, 29), view.DoneDate(view.DoneTodos[0]));

        // The same todo is on Bob's page too.
        PersonView bob = BuildWithTodos("Bob");
        Assert.Equal(["Ask @[as] and @[Bob]", "Only @[Bob]"], bob.OpenTodos.Select(t => t.Text));
        Assert.Empty(bob.DoneTodos);
    }

    // 243 AC-4: the open waiting-for todos on the person (by name or alias) are in Waiting, in List() order, and not
    // in OpenTodos; a done one is not waiting; a todo waiting on Anna that mentions Bob is still under Bob's open todos.
    [Fact]
    public void Build_Waiting_ByNameAndAlias_LeftOutOfOpenTodos()
    {
        WriteNote("people", "# people");
        WriteNote("people/Anna Schmidt", "# Anna Schmidt\naliases:: AS");
        TodoItem late = AddTodo("waiting: @[Anna Schmidt] slides for @[Bob]", 9);
        TodoItem soon = AddTodo("Waiting:@[as] the contract", 3);
        TodoItem plain = AddTodo("Call @[Anna Schmidt]", 5);
        TodoItem done = AddTodo("waiting: @[Anna Schmidt] old answer", 2, doneHours: 1);
        AddTodo("waiting: @[Bob] budget", 4);

        PersonView anna = BuildWithTodos("Anna Schmidt");

        Assert.Equal(PersonViewKind.Mentions, anna.Kind);
        Assert.Equal([soon.Id, late.Id], anna.Waiting.Select(t => t.Id));
        Assert.Equal([plain.Id], anna.OpenTodos.Select(t => t.Id));
        Assert.Equal([done.Id], anna.DoneTodos.Select(t => t.Id));

        PersonView bob = BuildWithTodos("Bob");
        Assert.Equal(["waiting: @[Bob] budget"], bob.Waiting.Select(t => t.Text));
        Assert.Equal([late.Id], bob.OpenTodos.Select(t => t.Id));
    }

    // 243 AC-4: a person with only a waiting-for todo has kind Mentions; a bad todos.json empties Waiting too.
    [Fact]
    public void Build_Waiting_OnlyWaiting_AndUnreadable()
    {
        AddTodo("waiting: @[Carl] the keys", 3);
        PersonView carl = BuildWithTodos("Carl");
        Assert.Equal(PersonViewKind.Mentions, carl.Kind);
        Assert.Single(carl.Waiting);
        Assert.Empty(carl.OpenTodos);

        File.WriteAllText(Path.Combine(root, TodoStore.FileName), "{ broken");
        PersonView broken = BuildWithTodos("Carl");
        Assert.True(broken.TodosUnreadable);
        Assert.Empty(broken.Waiting);
    }

    // 159 AC-5: an ambiguous alias links to no page; a todo without a mention is on no page.
    [Fact]
    public void Build_Todos_AmbiguousAlias_NoPage_AndNoMention_NoPage()
    {
        WriteNote("people", "# people");
        WriteNote("people/Anna Schmidt", "# Anna Schmidt\naliases:: Anna");
        WriteNote("people/Anna Berg", "# Anna Berg\naliases:: Anna");
        AddTodo("Call @[Anna]", 3);
        AddTodo("Call Anna Schmidt", 4);

        Assert.Equal(PersonViewKind.NoMentions, BuildWithTodos("Anna Schmidt").Kind);
        Assert.Empty(BuildWithTodos("Anna Berg").OpenTodos);
        PersonView unresolved = BuildWithTodos("Anna");
        Assert.Equal(["Call @[Anna]"], unresolved.OpenTodos.Select(t => t.Text));
        Assert.Equal(["Anna Berg", "Anna Schmidt"], unresolved.AmbiguousWith);
    }

    // 159 AC-5: a person mentioned only in todos has kind Mentions; only a done todo counts too.
    [Fact]
    public void Build_OnlyTodoMentions_KindMentions()
    {
        AddTodo("Call @[Carl]", 3);
        AddTodo("Thank @[Dora]", 3, doneHours: 1);

        PersonView carl = BuildWithTodos("Carl");
        PersonView dora = BuildWithTodos("@[dora]");

        Assert.Equal(PersonViewKind.Mentions, carl.Kind);
        Assert.Empty(carl.Notes);
        Assert.Empty(carl.Appointments);
        Assert.Single(carl.OpenTodos);
        Assert.Equal(PersonViewKind.Mentions, dora.Kind);
        Assert.Empty(dora.OpenTodos);
        Assert.Single(dora.DoneTodos);
    }

    // 159 AC-6: an unreadable todos.json sets TodosUnreadable; notes and appointments are still listed.
    [Fact]
    public void Build_BadTodos_TodosUnreadable_NotesAndAppointmentsStill()
    {
        WriteNote("A", "@[Anna]");
        WriteCalendar("a.ics", Calendar(Event("With @[Anna]", ":20261027T090000Z", ":20261027T100000Z")));
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), "[{\"text\":\"@[Anna]\",\"due\":\"2026-11-03\",\"done\":42}]");

        PersonView view = BuildWithTodos("Anna");

        Assert.True(view.TodosUnreadable);
        Assert.Equal(PersonViewKind.Mentions, view.Kind);
        Assert.Equal(["A"], view.Notes);
        Assert.Single(view.Appointments);
        Assert.Empty(view.OpenTodos);
        Assert.Empty(view.DoneTodos);
    }

    // 159 AC-6: an unreadable context.db still lists the matching todos, with the aliases read from the person pages.
    [Fact]
    public void Build_BadDb_StillTodos_AliasesFromPages()
    {
        WriteNote("people", "# people");
        WriteNote("people/Anna Schmidt", "# Anna Schmidt\naliases:: Anna");
        AddTodo("Call @[Anna]", 3);
        AddTodo("Thank @[anna schmidt]", 3, doneHours: 1);
        File.WriteAllText(DbPath, "not a database at all, not at all, really not");

        PersonView view = BuildWithTodos("Anna Schmidt");

        Assert.True(view.IndexUnreadable);
        Assert.False(view.TodosUnreadable);
        Assert.Equal(PersonViewKind.Mentions, view.Kind);
        Assert.Equal(["Call @[Anna]"], view.OpenTodos.Select(t => t.Text));
        Assert.Equal(["Thank @[anna schmidt]"], view.DoneTodos.Select(t => t.Text));
    }

    // 159 AC-6: with no todo store passed, the view is the same as before this job.
    [Fact]
    public void Build_NoTodoStore_SameAsBefore()
    {
        WriteNote("A", "@[Anna]");
        AddTodo("Call @[Anna]", 3);
        AddTodo("Ask @[Carl]", 3);

        PersonView view = Build("Anna");
        PersonView carl = Build("Carl");

        Assert.Equal(["A"], view.Notes);
        Assert.Empty(view.OpenTodos);
        Assert.Empty(view.DoneTodos);
        Assert.False(view.TodosUnreadable);
        Assert.Equal(PersonViewKind.NoMentions, carl.Kind);
    }

    // Job 227 AC-4: a private appointment never shows on a person page, whether the title, the location or an
    // attendee matches, from a file or the feed's cache.
    [Fact]
    public void Build_PrivateAppointments_NotListed()
    {
        WriteNote("people", "# people");
        WriteNote("people/Anna Schmidt", "# Anna Schmidt\n@[Anna Schmidt]");
        WriteCalendar("work.ics", Calendar(
            Event("Sync with @[Anna Schmidt]", ":20261029T080000Z", ":20261029T090000Z"),
            Event("Secret with @[Anna Schmidt]", ":20261029T100000Z", ":20261029T110000Z", "CLASS:PRIVATE"),
            Event("Lunch", ":20261029T120000Z", ":20261029T130000Z", "LOCATION:@[Anna Schmidt]", "CLASS:CONFIDENTIAL"),
            Event("Call", ":20261029T140000Z", ":20261029T150000Z", "ATTENDEE;CN=Anna Schmidt:mailto:a@b", "class:private")));
        PlantCache(Calendar(
            Event("Cached secret @[Anna Schmidt]", ":20261030T080000Z", ":20261030T090000Z", "CLASS:PRIVATE")));

        PersonView view = Build("Anna Schmidt", Feed());

        Assert.Equal(["Sync with @[Anna Schmidt]"], view.Appointments.Select(a => a.Title));
        Assert.Equal(0, http.Requests);
    }

    // 361 AC-7: an archived done todo that mentions the person is in DoneTodos, in ListAllDone order; a bad archive
    // sets ArchiveUnreadable and DoneTodos still lists the todos.json matches.
    [Fact]
    public void Build_ArchivedTodos_InDoneTodos_BadArchiveFlagged()
    {
        WriteNote("people", "# people");
        WriteNote("people/Anna Schmidt", "# Anna Schmidt");
        TodoItem open = AddTodo("Call @[Anna Schmidt]", 9);
        TodoItem done = AddTodo("Lunch with @[Anna Schmidt]", 2, doneHours: 1);
        Guid archivedId = Guid.NewGuid();
        string archive = Path.Combine(root, TodoStore.ArchiveFileName);
        TodoArchiveFile.Write(
            archive,
            TodoArchiveFile.Entry(
                archivedId, "Old talk with @[Anna Schmidt]", new DateOnly(2026, 8, 1), Now.AddDays(-90), Now.AddDays(-60)),
            TodoArchiveFile.Entry(Guid.NewGuid(), "Old talk with @[Bob]", new DateOnly(2026, 8, 1), Now.AddDays(-90), Now.AddDays(-60)));

        PersonView view = BuildWithTodos("Anna Schmidt");

        Assert.Equal(PersonViewKind.Mentions, view.Kind);
        Assert.Equal([open.Id], view.OpenTodos.Select(t => t.Id));
        Assert.Equal([done.Id, archivedId], view.DoneTodos.Select(t => t.Id));
        Assert.Equal(new DateOnly(2026, 8, 30), view.DoneDate(view.DoneTodos[1]));
        Assert.False(view.ArchiveUnreadable);
        Assert.False(view.TodosUnreadable);

        File.WriteAllText(archive, "not json");

        PersonView bad = BuildWithTodos("Anna Schmidt");

        Assert.True(bad.ArchiveUnreadable);
        Assert.False(bad.TodosUnreadable);
        Assert.Equal([open.Id], bad.OpenTodos.Select(t => t.Id));
        Assert.Equal([done.Id], bad.DoneTodos.Select(t => t.Id));
    }

    // 361 AC-7: a person only an archived todo mentions still has a page with that todo.
    [Fact]
    public void Build_OnlyArchivedTodo_Mentions()
    {
        TodoArchiveFile.Archive(root, Guid.NewGuid(), "Ask @[Carl]", Now.AddDays(-45));

        PersonView view = BuildWithTodos("Carl");

        Assert.Equal(PersonViewKind.Mentions, view.Kind);
        Assert.Equal(["Ask @[Carl]"], view.DoneTodos.Select(t => t.Text));
    }

    // 487 AC-1: an open note todo mentioning the person is listed with its indexed fields; one mentioning only someone
    // else, a done note todo and a note-local checkbox are not.
    [Fact]
    public void Build_NoteTodos_OpenMentioningOnly()
    {
        WriteNote("Plan", "# Plan\n- { } call @[Anna] due:2026-11-03\n- { } ask @[Bob]\n- {x} sent @[Anna]\n- [ ] book @[Anna]");

        PersonView view = Build("Anna");

        NoteTodo todo = Assert.Single(view.NoteTodos);
        Assert.Equal("Plan", todo.Note);
        Assert.Equal(2, todo.Line);
        Assert.Equal("call @[Anna]", todo.Text);
        Assert.Equal(new DateOnly(2026, 11, 3), todo.Due);
        Assert.Equal(PersonViewKind.Mentions, view.Kind);
    }

    // 487 AC-2: a note todo's mention resolves through the aliases (any case and whitespace); an ambiguous alias
    // shows on neither person.
    [Fact]
    public void Build_NoteTodos_ThroughAliases_AmbiguousOnNeither()
    {
        WriteNote("people", "# people");
        WriteNote("people/Anna Schmidt", "# Anna Schmidt\naliases:: Anna");
        WriteNote("Plan", "- { } call @[ anna ]\n- { } mail @[ANNA   schmidt]");

        Assert.Equal(["call @[ anna ]", "mail @[ANNA schmidt]"], Build("Anna Schmidt").NoteTodos.Select(t => t.Text));

        WriteNote("people/Anna Berg", "# Anna Berg\naliases:: anna");
        WriteNote("Plan", "- { } call @[anna]");
        File.SetLastWriteTimeUtc(Path.Combine(root, "Plan.md"), Now.AddHours(1));
        File.SetLastWriteTimeUtc(Path.Combine(root, "people", "Anna Berg.md"), Now.AddHours(1));

        Assert.Empty(Build("Anna Schmidt").NoteTodos);
        Assert.Empty(Build("Anna Berg").NoteTodos);
    }

    // 487 AC-3: by due date (undated last), then note name ignoring case, then line. (The ordinal tie-break between
    // names equal ignoring case can't be two files on a case-insensitive file system.)
    [Fact]
    public void Build_NoteTodos_Order()
    {
        WriteNote("b", "- { } b1 @[Anna]\n- { } b2 @[Anna] due:2026-11-02");
        WriteNote("A", "- { } A1 @[Anna]\n- { } A2 @[Anna]");
        WriteNote("C", "- { } C1 @[Anna] due:2026-11-01\n- { } C2 @[Anna] due:2026-11-02\n- { } C3 @[Anna]");

        PersonView view = Build("Anna");

        Assert.Equal(["C1 @[Anna]", "b2 @[Anna]", "C2 @[Anna]", "A1 @[Anna]", "A2 @[Anna]", "b1 @[Anna]", "C3 @[Anna]"],
            view.NoteTodos.Select(t => t.Text));
    }

    // 487 AC-4: a ticked line is gone on the next build, and the build writes nothing but context.db.
    [Fact]
    public void Build_NoteTodos_TickedGone_OnlyContextDbWritten()
    {
        WriteNote("Plan", "- { } call @[Anna]");
        Assert.Single(Build("Anna").NoteTodos);

        WriteNote("Plan", "- {x} call @[Anna]");
        File.SetLastWriteTimeUtc(Path.Combine(root, "Plan.md"), Now.AddHours(1));
        SortedDictionary<string, string> before = FileSnapshot.Take(root, TagIndex.FileName);

        PersonView view = Build("Anna");

        Assert.Empty(view.NoteTodos);
        Assert.Equal(before, FileSnapshot.Take(root, TagIndex.FileName));
    }

    // 487 AC-5: a note todo on the person's own page is listed (Notes still leaves the page out) and makes Kind Mentions.
    [Fact]
    public void Build_NoteTodos_OwnPage_Listed_KindMentions()
    {
        WriteNote("people", "# people");
        WriteNote("people/Anna", "# Anna\n- { } call @[Anna]");

        PersonView view = Build("Anna");

        Assert.Empty(view.Notes);
        Assert.Equal("people/Anna", Assert.Single(view.NoteTodos).Note);
        Assert.Equal(PersonViewKind.Mentions, view.Kind);
    }

    // 487 AC-6: an unreadable context.db gives no note todos and does not throw.
    [Fact]
    public void Build_NoteTodos_BadDb_Empty()
    {
        WriteNote("Plan", "- { } call @[Anna]");
        File.WriteAllText(DbPath, "this is not a database, just some text that fills the header....");

        PersonView view = Build("Anna");

        Assert.True(view.IndexUnreadable);
        Assert.Empty(view.NoteTodos);
        Assert.Equal(PersonViewKind.NoMentions, view.Kind);
    }
}
