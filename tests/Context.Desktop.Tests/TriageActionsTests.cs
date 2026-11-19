using System.Text;

namespace Context.Tests;

// 179 AC-5 to AC-10: the DesktopActions triage methods, against real files and the fixture's fixed local clock.
public sealed class TriageActionsTests : IDisposable
{
    private const string Line = "- 2026-10-28 09:15 call Anna";
    private const string InboxText = "# inbox\n- first\n" + Line + "\n- last\n";
    private const string InboxAfter = "# inbox\n- first\n- last\n";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private static InboxItem Item => InboxTriage.Parse(Line)!;

    private string InboxPath => Path.Combine(data.Root, "inbox.md");

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root);

    // AC-5: the note is created with the title heading and the text, then the line leaves the inbox.
    [Fact]
    public async Task TriageToNote_CreatesTheNoteThenRemovesTheLine()
    {
        data.WriteNote("inbox", InboxText);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.TriageToNote(Item, "Call Anna");

        Assert.Equal((ActionKind.Done, "Created Call Anna."), (result.Kind, result.Text));
        Assert.Equal("# Call Anna\n\ncall Anna\n", File.ReadAllText(Path.Combine(data.Root, "Call Anna.md")));
        Assert.Equal(InboxAfter, File.ReadAllText(InboxPath));
        Assert.Contains(data.State!.Snapshot!.Notes, n => n.Name == "Call Anna");
    }

    // AC-5: an invalid or existing title gives the create texts and leaves every file unchanged.
    [Theory]
    [InlineData("", "Invalid")]
    [InlineData("a/b", "Invalid")]
    [InlineData("Plan", "Exists")]
    public async Task TriageToNote_BadTitle_NothingChanges(string title, string reason)
    {
        data.WriteNote("inbox", InboxText);
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.TriageToNote(Item, title);

        Assert.Equal((ActionKind.Refused, reason), (result.Kind, result.Reason.ToString()));
        Assert.Equal(before, Files());
    }

    // AC-5: an existing title gives the CreateNote text.
    [Fact]
    public async Task TriageToNote_Exists_GivesTheCreateText()
    {
        data.WriteNote("inbox", InboxText);
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        ActionResult created = await data.Actions!.CreateNote("Plan");

        ActionResult result = await data.Actions.TriageToNote(Item, "Plan");

        Assert.Equal(created.Text, result.Text);
    }

    // AC-6: the todo is added for the day given, then the line leaves the inbox.
    [Fact]
    public async Task TriageToTodo_AddsTheTodoThenRemovesTheLine()
    {
        data.WriteNote("inbox", InboxText);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.TriageToTodo(Item, "call Anna", DesktopFixture.Today.AddDays(1));

        Assert.Equal((ActionKind.Done, "Added: call Anna"), (result.Kind, result.Text));
        TodoItem todo = Assert.Single(data.State!.Snapshot!.Todos);
        Assert.Equal(("call Anna", DesktopFixture.Today.AddDays(1)), (todo.Text, todo.Due));
        Assert.Equal(InboxAfter, File.ReadAllText(InboxPath));
    }

    // AC-6: a text over 200 characters or an unreadable todos.json gives the AddTodo texts; the inbox is unchanged.
    [Fact]
    public async Task TriageToTodo_TooLongOrBadTodos_InboxUnchanged()
    {
        data.WriteNote("inbox", InboxText);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult tooLong = await data.Actions!.TriageToTodo(Item, new string('x', 201), DesktopFixture.Today);

        Assert.Equal((ActionKind.Refused, DesktopActions.TodoTooLongText), (tooLong.Kind, tooLong.Text));
        Assert.Equal(before, Files());

        data.WriteFile(TodoStore.FileName, "{ not json");
        SortedDictionary<string, string> broken = Files();
        ActionResult unreadable = await data.Actions.TriageToTodo(Item, "call Anna", DesktopFixture.Today);

        Assert.Equal((ActionKind.Failed, DesktopActions.TodosUnreadableText), (unreadable.Kind, unreadable.Text));
        Assert.Equal(broken, Files());
    }

    // 243 AC-7: the waiting-for todo is added for the follow-up day, then the line leaves the inbox.
    [Fact]
    public async Task TriageDelegate_AddsTheWaitingTodoThenRemovesTheLine()
    {
        data.WriteNote("inbox", InboxText);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.TriageDelegate(Item, " anna  berg ", " call Anna ", DesktopFixture.Today.AddDays(3));

        Assert.Equal((ActionKind.Done, "Added: waiting: @[anna berg] call Anna"), (result.Kind, result.Text));
        TodoItem todo = Assert.Single(data.State!.Snapshot!.Todos);
        Assert.Equal(("waiting: @[anna berg] call Anna", DesktopFixture.Today.AddDays(3)), (todo.Text, todo.Due));
        Assert.Equal("anna berg", WaitingFor.TryParse(todo.Text)!.Person);
        Assert.Equal(InboxAfter, File.ReadAllText(InboxPath));
    }

    // 243 AC-8: an empty or invalid person, an empty text or a too long todo is refused; nothing is written.
    [Theory]
    [InlineData("", "call Anna", DesktopActions.TriagePersonText)]
    [InlineData("   ", "call Anna", DesktopActions.TriagePersonText)]
    [InlineData("a/b", "call Anna", DesktopActions.TriagePersonText)]
    [InlineData("a]b", "call Anna", DesktopActions.TriagePersonText)]
    [InlineData("Anna", "", DesktopActions.EmptyTodoText)]
    [InlineData("Anna", "   ", DesktopActions.EmptyTodoText)]
    public async Task TriageDelegate_Refused_NothingWritten(string person, string text, string expected)
    {
        data.WriteNote("inbox", InboxText);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.TriageDelegate(Item, person, text, DesktopFixture.Today);

        Assert.Equal((ActionKind.Refused, expected), (result.Kind, result.Text));
        Assert.Equal(before, Files());
    }

    // 243 AC-8: the composed text counts against the todo length; just over it is refused, at it is added.
    [Fact]
    public async Task TriageDelegate_ComposedTooLong_Refused()
    {
        data.WriteNote("inbox", InboxText);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();
        int room = DesktopActions.MaxTodoLength - WaitingFor.Compose("Anna", string.Empty).Length;

        ActionResult tooLong = await data.Actions!.TriageDelegate(Item, "Anna", new string('x', room + 1), DesktopFixture.Today);

        Assert.Equal((ActionKind.Refused, DesktopActions.TodoTooLongText), (tooLong.Kind, tooLong.Text));
        Assert.Equal(before, Files());

        ActionResult fits = await data.Actions.TriageDelegate(Item, "Anna", new string('x', room), DesktopFixture.Today);

        Assert.Equal(ActionKind.Done, fits.Kind);
        Assert.Equal(DesktopActions.MaxTodoLength, Assert.Single(data.State!.Snapshot!.Todos).Text.Length);
    }

    // 243 AC-7: the line is gone: refused, nothing written; the removal fails: LineKept, the todo stays.
    [Fact]
    public async Task TriageDelegate_LineGoneOrKept()
    {
        data.WriteNote("inbox", "# inbox\n- first\n" + Line + " edited\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult gone = await data.Actions!.TriageDelegate(Item, "Anna", "call Anna", DesktopFixture.Today);

        Assert.Equal((ActionKind.Refused, DesktopActions.TriageLineGoneText), (gone.Kind, gone.Text));
        Assert.Equal(before, Files());

        data.WriteNote("inbox", InboxText);
        await data.RefreshAsync();
        data.Actions.BeforeTriageRemove = () => File.WriteAllText(InboxPath, "# inbox\n- someone else\n");

        ActionResult kept = await data.Actions.TriageDelegate(Item, "Anna", "call Anna", DesktopFixture.Today);

        Assert.Equal((ActionKind.Failed, ActionReason.LineKept), (kept.Kind, kept.Reason));
        Assert.Equal("waiting: @[Anna] call Anna", Assert.Single(data.State!.Snapshot!.Todos).Text);
    }

    // AC-7: "- <text>" is appended after a final newline is added; the other bytes and CRLF stay; the line leaves.
    [Theory]
    [InlineData("# Plan\n- old\n", "# Plan\n- old\n- call Anna\n")]
    [InlineData("# Plan\n- old", "# Plan\n- old\n- call Anna\n")]
    [InlineData("# Plan\r\n- old\r\n", "# Plan\r\n- old\r\n- call Anna\r\n")]
    [InlineData("", "- call Anna\n")]
    public async Task TriageAppend_PlainNote_AppendsTheLine(string text, string expected)
    {
        data.WriteNote("inbox", InboxText);
        data.WriteNote("Plan", text);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.TriageAppend(Item, "Plan", null);

        Assert.Equal((ActionKind.Done, "Added to Plan."), (result.Kind, result.Text));
        Assert.Equal(expected, File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal(InboxAfter, File.ReadAllText(InboxPath));
    }

    // AC-7: no such note, or the inbox itself as target, is refused and nothing changes.
    [Theory]
    [InlineData("Nope", "No note named Nope.")]
    [InlineData("inbox", "Pick a note other than inbox.")]
    [InlineData("INBOX", "Pick a note other than inbox.")]
    public async Task TriageAppend_MissingOrInboxTarget_Refused(string name, string text)
    {
        data.WriteNote("inbox", InboxText);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.TriageAppend(Item, name, null);

        Assert.Equal((ActionKind.Refused, text), (result.Kind, result.Text));
        Assert.Equal(before, Files());
    }

    // AC-8: a sensitive target read once (one Agree) is re-sealed with the line; no .md and no temp file appear.
    [Fact]
    public async Task TriageAppend_SensitiveTarget_ReSealed()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteNote("inbox", InboxText);
        data.WriteSensitive("Secret", "# Secret\n");
        await data.RefreshAsync();
        EditableNote? unlocked = await data.State!.ReadSensitive("Secret");
        Assert.NotNull(unlocked);
        Assert.Equal(1, data.Keys.Agrees);

        ActionResult result = await data.Actions!.TriageAppend(Item, "Secret", unlocked);

        Assert.Equal((ActionKind.Done, "Added to Secret."), (result.Kind, result.Text));
        string path = Path.Combine(data.Root, "Secret" + NoteStore.SensitiveExtension);
        Assert.Equal(
            "# Secret\n- call Anna\n",
            SealedNote.Open("Secret", File.ReadAllBytes(path), data.Keys.MachinePublicKey()!, data.Keys.Agree));
        Assert.False(File.Exists(Path.Combine(data.Root, "Secret.md")));
        Assert.Empty(Directory.GetFiles(data.Root, "*.tmp", SearchOption.AllDirectories));
        Assert.Equal(InboxAfter, File.ReadAllText(InboxPath));
    }

    // AC-8: a sensitive target without an unlocked note is refused with no key-store call and nothing written.
    [Fact]
    public async Task TriageAppend_SensitiveNotUnlocked_CantUnlock()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteNote("inbox", InboxText);
        data.WriteSensitive("Secret", "# Secret\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.TriageAppend(Item, "Secret", null);

        Assert.Equal((ActionKind.Refused, "Can't unlock Secret."), (result.Kind, result.Text));
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(before, Files());
    }

    // AC-8: an unlocked note the session doesn't hold (here: closed again) is refused and nothing is written.
    [Fact]
    public async Task TriageAppend_NoteNotInSession_Refused()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteNote("inbox", InboxText);
        data.WriteSensitive("Secret", "# Secret\n");
        await data.RefreshAsync();
        EditableNote? unlocked = await data.State!.ReadSensitive("Secret");
        Assert.NotNull(unlocked);
        data.State.Session.Close("Secret");
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.TriageAppend(Item, "Secret", unlocked);

        Assert.Equal((ActionKind.Refused, "Can't unlock Secret."), (result.Kind, result.Text));
        Assert.Equal(before, Files());
    }

    // AC-9: when the inbox no longer holds the exact line, nothing is written to any target.
    [Fact]
    public async Task Triage_LineGone_NothingWritten()
    {
        data.WriteNote("inbox", "# inbox\n- first\n" + Line + " edited\n");
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult note = await data.Actions!.TriageToNote(Item, "Call Anna");
        ActionResult todo = await data.Actions.TriageToTodo(Item, "call Anna", DesktopFixture.Today);
        ActionResult append = await data.Actions.TriageAppend(Item, "Plan", null);
        ActionResult drop = await data.Actions.TriageDrop(Item, new Confirmation());
        ActionResult later = await data.Actions.TriageSnooze(Item, DesktopFixture.Today.AddDays(1));

        Assert.All(
            [note, todo, append, drop, later],
            r => Assert.Equal((ActionKind.Refused, DesktopActions.TriageLineGoneText), (r.Kind, r.Text)));
        Assert.Equal(before, Files());
    }

    // AC-9: the target was saved but the line couldn't be removed: Failed with the kept text; the target stays.
    [Fact]
    public async Task TriageAppend_RemoveFails_TargetKept()
    {
        data.WriteNote("inbox", InboxText);
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        data.Actions!.BeforeTriageRemove = () => File.WriteAllText(InboxPath, "# inbox\n- someone else\n");

        ActionResult result = await data.Actions.TriageAppend(Item, "Plan", null);

        Assert.Equal(
            (ActionKind.Failed, ActionReason.LineKept, "Saved to Plan, but the line is still in the inbox."),
            (result.Kind, result.Reason, result.Text));
        Assert.Equal("# Plan\n- call Anna\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
    }

    // AC-9: the removal throws after the note was created: Failed with the kept text; the note stays, the line too.
    [Fact]
    public async Task TriageToNote_RemoveThrows_NoteKept()
    {
        data.WriteNote("inbox", InboxText);
        await data.RefreshAsync();
        data.Actions!.BeforeTriageRemove = () => throw new IOException("disk");

        ActionResult result = await data.Actions.TriageToNote(Item, "Call Anna");

        Assert.Equal((ActionKind.Failed, "Saved to Call Anna, but the line is still in the inbox."), (result.Kind, result.Text));
        Assert.True(File.Exists(Path.Combine(data.Root, "Call Anna.md")));
        Assert.Equal(InboxText, File.ReadAllText(InboxPath));
    }

    // A throwing write hook before any write: Failed, and nothing is written.
    [Fact]
    public async Task TriageToNote_BeforeWriteThrows_NothingWritten()
    {
        data.WriteNote("inbox", InboxText);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();
        data.Actions!.BeforeWrite = () => throw new IOException("disk");

        ActionResult result = await data.Actions.TriageToNote(Item, "Call Anna");

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal(before, Files());
    }

    // AC-10: Drop with a confirmation removes the first equal line; the rest stays byte-identical.
    [Fact]
    public async Task TriageDrop_Confirmed_RemovesTheFirstEqualLine()
    {
        data.WriteNote("inbox", "# inbox\r\n" + Line + "\r\n- b\r\n" + Line + "\r\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.TriageDrop(Item, new Confirmation());

        Assert.Equal((ActionKind.Done, "Dropped."), (result.Kind, result.Text));
        Assert.Equal(Encoding.UTF8.GetBytes("# inbox\r\n- b\r\n" + Line + "\r\n"), File.ReadAllBytes(InboxPath));
    }

    // AC-10: without a confirmation nothing is written.
    [Fact]
    public async Task TriageDrop_NotConfirmed_Refused()
    {
        data.WriteNote("inbox", InboxText);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.TriageDrop(Item, null);

        Assert.Equal((ActionKind.Refused, ActionReason.NotConfirmed), (result.Kind, result.Reason));
        Assert.Equal(before, Files());
    }

    // AC-11: Later rewrites the line in place with the day.
    [Fact]
    public async Task TriageSnooze_RewritesTheLine()
    {
        data.WriteNote("inbox", InboxText);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.TriageSnooze(Item, new DateOnly(2026, 11, 5));

        Assert.Equal((ActionKind.Done, "Snoozed until 2026-11-05."), (result.Kind, result.Text));
        Assert.Equal("# inbox\n- first\n" + Line + " snooze:2026-11-05\n- last\n", File.ReadAllText(InboxPath));
    }

    // AC-11: today or an earlier day is refused.
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task TriageSnooze_NotAfterToday_Refused(int days)
    {
        data.WriteNote("inbox", InboxText);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.TriageSnooze(Item, DesktopFixture.Today.AddDays(days));

        Assert.Equal((ActionKind.Refused, "Pick a day after today."), (result.Kind, result.Text));
        Assert.Equal(before, Files());
    }

    // AC-3: a sensitive inbox is never opened (no key-store call) and nothing is written.
    [Fact]
    public async Task Triage_SensitiveInbox_RefusedWithoutAgree()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("inbox", "# inbox\n" + Line + "\n");
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult note = await data.Actions!.TriageToNote(Item, "Call Anna");
        ActionResult append = await data.Actions.TriageAppend(Item, "Plan", null);
        ActionResult drop = await data.Actions.TriageDrop(Item, new Confirmation());
        ActionResult later = await data.Actions.TriageSnooze(Item, DesktopFixture.Today.AddDays(1));
        TriageView view = await data.State!.Triage();

        Assert.All(
            [note, append, drop, later],
            r => Assert.Equal((ActionKind.Refused, DesktopActions.TriageSensitiveText), (r.Kind, r.Text)));
        Assert.Equal(TriageKind.Sensitive, view.Kind);
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(before, Files());
    }

    // AC-12: a missing inbox gives the empty view and creates no file.
    [Fact]
    public async Task Triage_MissingInbox_EmptyNoFile()
    {
        await data.RefreshAsync();

        TriageView view = await data.State!.Triage();

        Assert.Equal(TriageKind.Shown, view.Kind);
        Assert.Empty(view.Queue.Items);
        Assert.False(File.Exists(InboxPath));
    }
}
