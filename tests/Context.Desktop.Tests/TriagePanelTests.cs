using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 179 AC-3, AC-4, AC-11 and the ways in: the inbox triage panel, on its own and in Main.
public sealed class TriagePanelTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private const string Line = "- 2026-10-28 09:15 call Anna";
    private const string InboxText = "# inbox\n" + Line + "\n- second\n";

    private readonly DesktopFixture data = new();
    private readonly List<string> triaged = [];
    private int closed;

    private IRenderedComponent<StatusBarView>? bar;

    // 455: feedback goes to the status bar, which Main renders; tests render the bar next to the panel.
    private IRenderedComponent<StatusBarView> Bar => bar ??= data.Bunit.Render<StatusBarView>();

    public void Dispose() => data.Dispose();

    private string InboxPath => Path.Combine(data.Root, "inbox.md");

    private async Task<IRenderedComponent<TriagePanel>> Render(Func<string?, Task<string?>>? guard = null)
    {
        await data.RefreshAsync();
        IRenderedComponent<TriagePanel> cut = data.Bunit.Render<TriagePanel>(p => p
            .Add(t => t.Guard, guard)
            .Add(t => t.OnTriaged, (string target) => triaged.Add(target))
            .Add(t => t.OnClose, () => closed++));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".triage-loading")), Wait);
        return cut;
    }

    private static Task Key(IRenderedComponent<TriagePanel> cut, string key) =>
        StaleClick.KeyDownAsync(cut, "section.triage-panel", new KeyboardEventArgs { Key = key });

    private string LastFocusId()
    {
        JSRuntimeInvocation last = data.JsCalls().Last(i => i.Identifier == "Blazor._internal.domWrapper.focus");
        return ((ElementReference)last.Arguments[0]!).Id;
    }

    // AC-3: the first item's text, the stamp in grey, the count and the five buttons.
    [Fact]
    public async Task Render_FirstItemCountAndButtons()
    {
        data.WriteNote("inbox", InboxText);

        IRenderedComponent<TriagePanel> cut = await Render();

        Assert.Equal("Inbox triage", cut.Find(".panel-bar h2").TextContent);
        IElement stamp = cut.Find(".triage-item .stamp");
        Assert.Equal("2026-10-28 09:15", stamp.TextContent);
        Assert.True(stamp.ClassList.Contains("grey"));
        Assert.Equal("call Anna", cut.Find(".triage-item .text").TextContent);
        Assert.Equal("2 left", cut.Find(".triage-left").TextContent);
        Assert.Equal(
            ["Note (N)", "Todo (T)", "Delegate (W)", "Add to note (A)", "Drop (D)", "Later (L)"],
            cut.FindAll(".triage-buttons button").Select(b => b.TextContent).ToArray());
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
    }

    // AC-3: outside text is shown as one line.
    [Fact]
    public async Task Render_ControlCharacters_OneLine()
    {
        data.WriteNote("inbox", "# inbox\n- a\u0007b‮c\n");

        IRenderedComponent<TriagePanel> cut = await Render();

        Assert.Equal(DesktopText.OneLine("a\u0007b‮c"), cut.Find(".triage-item .text").TextContent);
        Assert.Empty(cut.FindAll(".triage-item .stamp"));
    }

    // AC-3: an empty inbox, with and without snoozed lines; a missing inbox creates no file.
    [Theory]
    [InlineData(null, "The inbox is empty.")]
    [InlineData("# inbox\n", "The inbox is empty.")]
    [InlineData("# inbox\n- a snooze:2026-11-01\n- b snooze:2026-11-02\n", "The inbox is empty. 2 lines are snoozed.")]
    [InlineData("# inbox\n- a snooze:2026-11-01\n", "The inbox is empty. 1 line is snoozed.")]
    public async Task Render_Empty_GreyText(string? text, string expected)
    {
        if (text is not null)
        {
            data.WriteNote("inbox", text);
        }

        IRenderedComponent<TriagePanel> cut = await Render();

        IElement empty = cut.Find(".triage-empty");
        Assert.Equal(expected, empty.TextContent);
        Assert.True(empty.ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll(".triage-buttons"));
        Assert.Equal(text is not null, File.Exists(InboxPath));
    }

    // AC-3: a sensitive inbox is not opened: red text, no key-store call, nothing written.
    [Fact]
    public async Task Render_SensitiveInbox_RedNoAgree()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("inbox", InboxText);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        IRenderedComponent<TriagePanel> cut = await Render();

        IElement text = cut.Find(".triage-sensitive");
        Assert.Equal("inbox is sensitive. Nothing to triage.", text.TextContent);
        Assert.True(text.ClassList.Contains("red"));
        await Key(cut, "d");
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-4, AC-5: N in any case opens the title form pre-filled and focused; Enter creates the note, the next line
    // shows and the panel has the focus again.
    [Theory]
    [InlineData("n")]
    [InlineData("N")]
    public async Task KeyN_CreatesNote_NextItemShown(string key)
    {
        data.WriteNote("inbox", InboxText);
        IRenderedComponent<TriagePanel> cut = await Render();
        string panelId = LastFocusId();

        await Key(cut, key);

        IElement title = cut.Find("input.triage-title");
        Assert.Equal("call Anna", title.GetAttribute("value"));
        string titleId = ElementRefs.Id<TriagePanel, TriagePanel>(cut, "field");
        Assert.Equal(titleId, LastFocusId());
        await StaleClick.FireAsync(cut, () => cut.Find("input.triage-title"), e => e.InputAsync(new ChangeEventArgs { Value = "Call Anna" }));
        await StaleClick.KeyDownAsync(cut, "input.triage-title", new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Equal("second", cut.Find(".triage-item .text").TextContent), Wait);
        Assert.Equal("Created Call Anna.", data.StatusBar.Text);
        Assert.Equal("accent", data.StatusBar.Css);
        Assert.Empty(cut.FindAll(".triage-form"));
        Assert.Equal("1 left", cut.Find(".triage-left").TextContent);
        Assert.NotEqual(titleId, panelId);
        Assert.Equal(panelId, LastFocusId());
        Assert.Equal("# Call Anna\n\ncall Anna\n", File.ReadAllText(Path.Combine(data.Root, "Call Anna.md")));
        Assert.Equal([string.Empty], triaged);
    }

    // AC-5: the title is cut to 100 characters.
    [Fact]
    public async Task NoteForm_LongText_TitleCut()
    {
        string text = new string('a', 150);
        data.WriteNote("inbox", "# inbox\n- " + text + "\n");
        IRenderedComponent<TriagePanel> cut = await Render();

        await StaleClick.ClickAsync(cut, "button.triage-note");

        Assert.Equal(new string('a', 100), cut.Find("input.triage-title").GetAttribute("value"));
    }

    // AC-4: Esc closes an open form first, then the panel; letters in a field don't trigger actions.
    [Fact]
    public async Task Escape_ClosesFormThenPanel()
    {
        data.WriteNote("inbox", InboxText);
        IRenderedComponent<TriagePanel> cut = await Render();
        await Key(cut, "a");
        await StaleClick.KeyDownAsync(cut, "input.triage-target", new KeyboardEventArgs { Key = "d" });
        Assert.Empty(cut.FindAll("[role=dialog]"));

        await StaleClick.KeyDownAsync(cut, "input.triage-target", new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll(".triage-form"));
        Assert.Equal(0, closed);

        await Key(cut, "l");
        Assert.Single(cut.FindAll(".triage-form"));
        await Key(cut, "Escape");
        Assert.Empty(cut.FindAll(".triage-form"));
        Assert.Equal(0, closed);

        await Key(cut, "Escape");
        Assert.Equal(1, closed);
        Assert.Equal(InboxText, File.ReadAllText(InboxPath));
    }

    // AC-4, AC-6: T opens the todo form; an empty date means today; a bad date is red and writes nothing.
    [Fact]
    public async Task KeyT_AddsTodo_BadDateRefused()
    {
        data.WriteNote("inbox", InboxText);
        IRenderedComponent<TriagePanel> cut = await Render();

        await Key(cut, "T");
        Assert.Equal("call Anna", cut.Find("input.triage-todo-text").GetAttribute("value"));
        await StaleClick.InputAsync(cut, "input.triage-date", new ChangeEventArgs { Value = "someday" });
        await StaleClick.KeyDownAsync(cut, "input.triage-date", new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Type a date: yyyy-MM-dd, today or tomorrow.", data.StatusBar.Text);
        Assert.Equal("red", data.StatusBar.Css);
        Assert.False(File.Exists(Path.Combine(data.Root, TodoStore.FileName)));

        Assert.Empty(cut.FindAll("button.triage-submit"));
        Assert.Equal("Empty: today", cut.Find("input.triage-date").GetAttribute("placeholder"));
        await StaleClick.InputAsync(cut, "input.triage-date", new ChangeEventArgs { Value = string.Empty });
        await StaleClick.KeyDownAsync(cut, "input.triage-date", new KeyboardEventArgs { Key = "Enter" });

        Bar.WaitForAssertion(() => Assert.Equal("Added: call Anna", data.StatusBar.Text), Wait);
        TodoItem todo = Assert.Single(data.State!.Snapshot!.Todos);
        Assert.Equal(DesktopFixture.Today, todo.Due);
        Assert.Equal("# inbox\n- second\n", File.ReadAllText(InboxPath));
    }

    // 243 AC-6: W opens the delegate form after Todo (pressed), Person focused, text prefilled; Esc closes it.
    [Theory]
    [InlineData("w")]
    [InlineData("W")]
    public async Task KeyW_OpensDelegateForm_EscCloses(string key)
    {
        data.WriteNote("inbox", InboxText);
        IRenderedComponent<TriagePanel> cut = await Render();
        Assert.Equal("false", cut.Find("button.triage-delegate").GetAttribute("aria-pressed"));
        Assert.Equal("triage-delegate", cut.Find("button.triage-todo").NextElementSibling!.ClassName);

        await Key(cut, key);

        Assert.Equal("true", cut.Find("button.triage-delegate").GetAttribute("aria-pressed"));
        IElement person = cut.Find("input.triage-person");
        Assert.Equal(("Person", "Name", string.Empty), (person.GetAttribute("aria-label"), person.GetAttribute("placeholder"), person.GetAttribute("value") ?? string.Empty));
        Assert.Equal(ElementRefs.Id<TriagePanel, TriagePanel>(cut, "field"), LastFocusId());
        IElement text = cut.Find("input.triage-delegate-text");
        Assert.Equal(("Todo text", "call Anna"), (text.GetAttribute("aria-label"), text.GetAttribute("value")));
        IElement date = cut.Find("input.triage-follow-up");
        Assert.Equal(("Follow-up date", "Empty: in a week"), (date.GetAttribute("aria-label"), date.GetAttribute("placeholder")));
        Assert.Equal("Cancel", cut.Find(".triage-form button.triage-cancel").TextContent);

        await StaleClick.FireAsync(cut, () => cut.Find("input.triage-person"), e => e.KeyDownAsync(new KeyboardEventArgs { Key = "d" }));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        await StaleClick.KeyDownAsync(cut, "input.triage-person", new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".triage-form"));
        Assert.Equal(0, closed);
        Assert.Equal(InboxText, File.ReadAllText(InboxPath));
    }

    // 243 AC-6: the w key inside another form's text field types, it doesn't switch forms.
    [Fact]
    public async Task KeyW_InTextField_NoDelegateForm()
    {
        data.WriteNote("inbox", InboxText);
        IRenderedComponent<TriagePanel> cut = await Render();
        await Key(cut, "t");

        await StaleClick.KeyDownAsync(cut, "input.triage-todo-text", new KeyboardEventArgs { Key = "w" });

        Assert.Empty(cut.FindAll("input.triage-person"));
        Assert.Single(cut.FindAll("input.triage-todo-text"));
    }

    // 243 AC-7: Enter with an empty follow-up date adds the waiting-for todo for today+7 and moves on.
    [Fact]
    public async Task Delegate_EmptyDate_InAWeek_NextItemShown()
    {
        data.WriteNote("inbox", InboxText);
        IRenderedComponent<TriagePanel> cut = await Render();
        await StaleClick.ClickAsync(cut, "button.triage-delegate");
        await StaleClick.InputAsync(cut, "input.triage-person", new ChangeEventArgs { Value = "Anna" });
        await StaleClick.InputAsync(cut, "input.triage-delegate-text", new ChangeEventArgs { Value = "send the offer" });

        await StaleClick.KeyDownAsync(cut, "input.triage-follow-up", new KeyboardEventArgs { Key = "Enter" });

        Bar.WaitForAssertion(() => Assert.Equal("Added: waiting: @[Anna] send the offer", data.StatusBar.Text), Wait);
        Assert.Equal("accent", data.StatusBar.Css);
        Assert.Equal("second", cut.Find(".triage-item .text").TextContent);
        Assert.Empty(cut.FindAll(".triage-form"));
        TodoItem todo = Assert.Single(data.State!.Snapshot!.Todos);
        Assert.Equal(("waiting: @[Anna] send the offer", new DateOnly(2026, 11, 5)), (todo.Text, todo.Due));
        Assert.Equal("# inbox\n- second\n", File.ReadAllText(InboxPath));
        Assert.Equal([string.Empty], triaged);
    }

    // 243 AC-7: a typed follow-up date is used.
    [Fact]
    public async Task Delegate_TypedDate_DueThen()
    {
        data.WriteNote("inbox", InboxText);
        IRenderedComponent<TriagePanel> cut = await Render();
        await Key(cut, "w");
        await StaleClick.InputAsync(cut, "input.triage-person", new ChangeEventArgs { Value = "Anna" });
        await StaleClick.InputAsync(cut, "input.triage-follow-up", new ChangeEventArgs { Value = "tomorrow" });

        await StaleClick.KeyDownAsync(cut, "input.triage-person", new KeyboardEventArgs { Key = "Enter" });

        Bar.WaitForAssertion(() => Assert.Equal("Added: waiting: @[Anna] call Anna", data.StatusBar.Text), Wait);
        Assert.Equal(DesktopFixture.Today.AddDays(1), Assert.Single(data.State!.Snapshot!.Todos).Due);
    }

    // 243 AC-8: no person and a bad date are red; nothing is written and the line stays.
    [Fact]
    public async Task Delegate_NoPersonOrBadDate_RedNothingWritten()
    {
        data.WriteNote("inbox", InboxText);
        IRenderedComponent<TriagePanel> cut = await Render();
        await Key(cut, "w");

        await StaleClick.KeyDownAsync(cut, "input.triage-person", new KeyboardEventArgs { Key = "Enter" });

        Bar.WaitForAssertion(() => Assert.Equal(DesktopActions.TriagePersonText, data.StatusBar.Text), Wait);
        Assert.Equal("red", data.StatusBar.Css);
        Assert.False(File.Exists(Path.Combine(data.Root, TodoStore.FileName)));

        await StaleClick.InputAsync(cut, "input.triage-person", new ChangeEventArgs { Value = "Anna" });
        await StaleClick.InputAsync(cut, "input.triage-follow-up", new ChangeEventArgs { Value = "someday" });
        await StaleClick.KeyDownAsync(cut, "input.triage-follow-up", new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(DesktopActions.TriageDateText, data.StatusBar.Text);
        Assert.Equal("red", data.StatusBar.Css);
        Assert.False(File.Exists(Path.Combine(data.Root, TodoStore.FileName)));
        Assert.Equal(InboxText, File.ReadAllText(InboxPath));
        Assert.Equal("call Anna", cut.Find(".triage-item .text").TextContent);
    }

    // AC-4, AC-7: A opens the name form; a name matched like /open gets the line; no match is red.
    [Fact]
    public async Task KeyA_AppendsToNote_NoMatchRefused()
    {
        data.WriteNote("inbox", InboxText);
        data.WriteNote("Plan", "# Plan\n");
        IRenderedComponent<TriagePanel> cut = await Render();

        await Key(cut, "a");
        await StaleClick.InputAsync(cut, "input.triage-target", new ChangeEventArgs { Value = "Nope" });
        await StaleClick.KeyDownAsync(cut, "input.triage-target", new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal("No note named Nope.", data.StatusBar.Text);
        Assert.Equal("red", data.StatusBar.Css);

        await StaleClick.InputAsync(cut, "input.triage-target", new ChangeEventArgs { Value = "inbox" });
        await StaleClick.KeyDownAsync(cut, "input.triage-target", new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal("Pick a note other than inbox.", data.StatusBar.Text);

        await StaleClick.InputAsync(cut, "input.triage-target", new ChangeEventArgs { Value = "plan" });
        await StaleClick.KeyDownAsync(cut, "input.triage-target", new KeyboardEventArgs { Key = "Enter" });

        Bar.WaitForAssertion(() => Assert.Equal("Added to Plan.", data.StatusBar.Text), Wait);
        Assert.Equal("# Plan\n- call Anna\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal(["Plan"], triaged);
    }

    // AC-8: a sensitive target is unlocked once (one Agree), re-sealed, and locked again afterwards.
    [Fact]
    public async Task Append_SensitiveTarget_OneAgreeLockedAgain()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteNote("inbox", InboxText);
        data.WriteSensitive("Secret", "# Secret\n");
        IRenderedComponent<TriagePanel> cut = await Render();

        await Key(cut, "a");
        await StaleClick.InputAsync(cut, "input.triage-target", new ChangeEventArgs { Value = "Secret" });
        await StaleClick.KeyDownAsync(cut, "input.triage-target", new KeyboardEventArgs { Key = "Enter" });

        Bar.WaitForAssertion(() => Assert.Equal("Added to Secret.", data.StatusBar.Text), Wait);
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Null(data.State!.Session.Note);
        string path = Path.Combine(data.Root, "Secret" + NoteStore.SensitiveExtension);
        Assert.Equal(
            "# Secret\n- call Anna\n",
            SealedNote.Open("Secret", File.ReadAllBytes(path), data.Keys.MachinePublicKey()!, data.Keys.Agree));
        Assert.False(File.Exists(Path.Combine(data.Root, "Secret.md")));
        Assert.Equal("# inbox\n- second\n", File.ReadAllText(InboxPath));
    }

    // AC-8: an unlock that fails is red and writes nothing.
    [Fact]
    public async Task Append_SensitiveUnlockFails_NothingWritten()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteNote("inbox", InboxText);
        data.WriteSensitive("Secret", "# Secret\n");
        IRenderedComponent<TriagePanel> cut = await Render();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        data.Keys.AgreeThrows = true;

        await Key(cut, "a");
        await StaleClick.InputAsync(cut, "input.triage-target", new ChangeEventArgs { Value = "Secret" });
        await StaleClick.KeyDownAsync(cut, "input.triage-target", new KeyboardEventArgs { Key = "Enter" });

        Bar.WaitForAssertion(() => Assert.Equal("Can't unlock Secret.", data.StatusBar.Text), Wait);
        Assert.Equal("red", data.StatusBar.Css);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-4, AC-10: D asks; Cancel writes nothing; Drop removes the line.
    [Fact]
    public async Task KeyD_AsksThenDrops()
    {
        data.WriteNote("inbox", InboxText);
        IRenderedComponent<TriagePanel> cut = await Render();

        await Key(cut, "D");
        Assert.Equal("Drop this line? call Anna", cut.Find("[role=dialog] .question").TextContent);
        await StaleClick.ClickAsync(cut, "[role=dialog] button.cancel");
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(InboxText, File.ReadAllText(InboxPath));

        await Key(cut, "d");
        Assert.Equal("Drop", cut.Find("[role=dialog] button.confirm").TextContent);
        await StaleClick.ClickAsync(cut, "[role=dialog] button.confirm");

        cut.WaitForAssertion(() => Assert.Equal("second", cut.Find(".triage-item .text").TextContent), Wait);
        Assert.Equal("Dropped.", data.StatusBar.Text);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal("# inbox\n- second\n", File.ReadAllText(InboxPath));
    }

    // AC-4, AC-11: L then 1 snoozes to tomorrow, L then 2 to next week; both lines leave the queue.
    [Fact]
    public async Task KeyL_ThenOneOrTwo_Snoozes()
    {
        data.WriteNote("inbox", InboxText);
        IRenderedComponent<TriagePanel> cut = await Render();

        await Key(cut, "l");
        await Key(cut, "1");
        Bar.WaitForAssertion(() => Assert.Equal("Snoozed until 2026-10-30.", data.StatusBar.Text), Wait);
        cut.WaitForAssertion(() => Assert.Equal("second", cut.Find(".triage-item .text").TextContent), Wait);

        await Key(cut, "L");
        await Key(cut, "2");
        Bar.WaitForAssertion(() => Assert.Equal("Snoozed until 2026-11-05.", data.StatusBar.Text), Wait);
        cut.WaitForAssertion(() => Assert.Equal("The inbox is empty. 2 lines are snoozed.", cut.Find(".triage-empty").TextContent), Wait);
        Assert.Equal(
            "# inbox\n" + Line + " snooze:2026-10-30\n- second snooze:2026-11-05\n",
            File.ReadAllText(InboxPath));
    }

    // AC-11: the Later buttons and a typed day; a day not after today is red and writes nothing.
    [Fact]
    public async Task Later_TypedDay_MustBeAfterToday()
    {
        data.WriteNote("inbox", InboxText);
        IRenderedComponent<TriagePanel> cut = await Render();

        await StaleClick.ClickAsync(cut, "button.triage-later");
        Assert.Equal("Tomorrow (1)", cut.Find("button.triage-tomorrow").TextContent);
        Assert.Equal("Next week (2)", cut.Find("button.triage-week").TextContent);
        await StaleClick.InputAsync(cut, "input.triage-until", new ChangeEventArgs { Value = "today" });
        await StaleClick.KeyDownAsync(cut, "input.triage-until", new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal("Pick a day after today.", data.StatusBar.Text);
        Assert.Equal("red", data.StatusBar.Css);
        Assert.Equal(InboxText, File.ReadAllText(InboxPath));

        await StaleClick.InputAsync(cut, "input.triage-until", new ChangeEventArgs { Value = "2026-12-01" });
        await StaleClick.KeyDownAsync(cut, "input.triage-until", new KeyboardEventArgs { Key = "Enter" });

        Bar.WaitForAssertion(() => Assert.Equal("Snoozed until 2026-12-01.", data.StatusBar.Text), Wait);
        Assert.Contains(Line + " snooze:2026-12-01\n", File.ReadAllText(InboxPath), StringComparison.Ordinal);
    }

    // The guard holds an action back: red text, nothing written.
    [Fact]
    public async Task Guard_HoldsBack_NothingWritten()
    {
        data.WriteNote("inbox", InboxText);
        List<string?> asked = [];
        IRenderedComponent<TriagePanel> cut = await Render(target =>
        {
            asked.Add(target);
            return Task.FromResult<string?>("Save inbox first.");
        });

        await Key(cut, "l");
        await Key(cut, "1");

        Assert.Equal("Save inbox first.", data.StatusBar.Text);
        Assert.Equal("red", data.StatusBar.Css);
        Assert.Equal([null], asked);
        Assert.Equal(InboxText, File.ReadAllText(InboxPath));
    }

    // /triage in the palette shows the panel under the capture box; Close hides it.
    [Fact]
    public async Task Palette_Triage_ShowsPanelUnderCapture()
    {
        data.WriteNote("inbox", InboxText);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.Empty(cut.FindAll(".triage-panel"));

        await StaleClick.KeyDownAsync(cut, ".layout", new KeyboardEventArgs { Key = "k", CtrlKey = true });
        await StaleClick.InputAsync(cut, ".palette-input", new ChangeEventArgs { Value = "/triage" });
        await StaleClick.KeyDownAsync(cut, ".palette-input", new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".triage-panel .triage-item")), Wait);
        Assert.Equal("top-bar", cut.Find("main.current-region").Children[0].ClassName);
        Assert.True(cut.Find("main.current-region").Children[1].ClassList.Contains("triage-panel"));
        Assert.Empty(cut.FindAll(".palette"));

        await StaleClick.ClickAsync(cut, ".triage-panel button.close");
        Assert.Empty(cut.FindAll(".triage-panel"));
    }

    // The Triage button in the notes panel shows it; opening a note hides it.
    [Fact]
    public async Task NotesPanelButton_ShowsPanel_OpeningANoteHidesIt()
    {
        data.WriteNote("inbox", InboxText);
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.Equal("Triage", cut.Find(".notes-bar-buttons button.triage-open").GetAttribute("aria-label"));
        await StaleClick.ClickAsync(cut, "button.triage-open");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".triage-panel .triage-item")), Wait);

        await Open(cut, js, "Plan");
        Assert.Empty(cut.FindAll(".triage-panel"));
    }

    // Nit 2: an open target with unsaved changes is refused with "Save <name> first."; nothing is written.
    [Fact]
    public async Task Main_DirtyOpenTarget_SaveFirst()
    {
        data.WriteNote("inbox", InboxText);
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("# Plan\n\nUnsaved\n");
        await StaleClick.ClickAsync(cut, "button.triage-open");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".triage-panel .triage-item")), Wait);
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        await StaleClick.KeyDownAsync(cut, ".triage-panel", new KeyboardEventArgs { Key = "a" });
        await StaleClick.InputAsync(cut, "input.triage-target", new ChangeEventArgs { Value = "Plan" });
        await StaleClick.KeyDownAsync(cut, "input.triage-target", new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Save Plan first.", data.StatusBar.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
    }

    // 203 AC-8: an open target with unsaved changes is saved first; then the line is added and the editor remounted.
    [Fact]
    public async Task Main_DirtyOpenTarget_SavedFirstThenAdded()
    {
        data.WriteNote("inbox", InboxText);
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("# Plan\n\nTyped\n");
        await StaleClick.ClickAsync(cut, "button.triage-open");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".triage-panel .triage-item")), Wait);
        int mounts = js.Calls("ContextEditor.mount").Count;

        await StaleClick.KeyDownAsync(cut, ".triage-panel", new KeyboardEventArgs { Key = "a" });
        await StaleClick.InputAsync(cut, "input.triage-target", new ChangeEventArgs { Value = "Plan" });
        await StaleClick.KeyDownAsync(cut, "input.triage-target", new KeyboardEventArgs { Key = "Enter" });

        Bar.WaitForAssertion(() => Assert.Equal("Added to Plan.", data.StatusBar.Text), Wait);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        Assert.Equal("# Plan\n\nTyped\n- call Anna\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
    }

    // AC-7: an open target without unsaved changes is remounted with the added line, and saves from there.
    [Fact]
    public async Task Main_CleanOpenTarget_Remounts()
    {
        data.WriteNote("inbox", InboxText);
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await StaleClick.ClickAsync(cut, "button.triage-open");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".triage-panel .triage-item")), Wait);
        int mounts = js.Calls("ContextEditor.mount").Count;

        await StaleClick.KeyDownAsync(cut, ".triage-panel", new KeyboardEventArgs { Key = "a" });
        await StaleClick.InputAsync(cut, "input.triage-target", new ChangeEventArgs { Value = "Plan" });
        await StaleClick.KeyDownAsync(cut, "input.triage-target", new KeyboardEventArgs { Key = "Enter" });

        Bar.WaitForAssertion(() => Assert.Equal("Added to Plan.", data.StatusBar.Text), Wait);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        Assert.Equal("# Plan\n- call Anna\n", js.MountedText());

        js.Edit("# Plan\n- call Anna\nMore\n");
        await StaleClick.KeyDownAsync(cut, ".note-editor", new KeyboardEventArgs { Key = "s", CtrlKey = true });
        cut.WaitForAssertion(() => Assert.Equal("Saved Plan.", cut.Find(".main-status").TextContent), Wait);
        Assert.Equal("# Plan\n- call Anna\nMore\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
    }

    // AC-9 and nit 2: the open inbox is remounted after a drop when clean, and refused with "Save inbox first." when not.
    [Fact]
    public async Task Main_OpenInbox_RemountsOrSaveFirst()
    {
        data.WriteNote("inbox", InboxText);
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "inbox");
        await StaleClick.ClickAsync(cut, "button.triage-open");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".triage-panel .triage-item")), Wait);
        int mounts = js.Calls("ContextEditor.mount").Count;

        await StaleClick.KeyDownAsync(cut, ".triage-panel", new KeyboardEventArgs { Key = "d" });
        await StaleClick.ClickAsync(cut, "[role=dialog] button.confirm");

        Bar.WaitForAssertion(() => Assert.Equal("Dropped.", data.StatusBar.Text), Wait);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        Assert.Equal("# inbox\n- second\n", js.MountedText());

        js.EditUnsavable("# inbox\n- second\nUnsaved\n");
        cut.WaitForAssertion(() => Assert.Equal("second", cut.Find(".triage-item .text").TextContent), Wait);
        await StaleClick.KeyDownAsync(cut, ".triage-panel", new KeyboardEventArgs { Key = "l" });
        await StaleClick.KeyDownAsync(cut, ".triage-panel", new KeyboardEventArgs { Key = "1" });

        Assert.Equal("Save inbox first.", data.StatusBar.Text);
        Assert.Equal("# inbox\n- second\n", File.ReadAllText(InboxPath));
    }

    private static async Task Open(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NoteOpener.OpenAsync(cut, name);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
    }
}
