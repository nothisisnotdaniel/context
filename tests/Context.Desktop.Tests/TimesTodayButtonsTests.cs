using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 383: the Times panel's buttons and checkpoint form, rendered alone over the fixture's state and actions. The Times
// folder is the fixture's own; the actions' local clock (10:05) gives the booked time, FakeTimesRunner the runs, and
// nothing here waits on time.
public sealed class TimesTodayButtonsTests : IDisposable
{
    // The panel loads on Task.Run and each booking runs through DesktopActions on Task.Run; every wait passes this.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private const string Working =
        """
        {"entries":[
          {"kind":"arrival","time":"08:00"},
          {"kind":"checkpoint","time":"08:05","name":"Review","workItem":1234}
        ]}
        """;

    private const string OnBreak =
        """
        {"entries":[
          {"kind":"arrival","time":"08:00"},
          {"kind":"checkpoint","time":"08:05","name":"Review","workItem":1234},
          {"kind":"breakStart","time":"09:00"}
        ]}
        """;

    private const string Left =
        """
        {"entries":[
          {"kind":"arrival","time":"08:00"},
          {"kind":"checkpoint","time":"08:05","name":"Review","workItem":1234},
          {"kind":"leaving","time":"09:00"}
        ]}
        """;

    private const string Projects =
        """{"projects":[{"workItem":4711,"title":"Billing"},{"workItem":42,"title":"Plan\nnext <b>"}]}""";

    private readonly DesktopFixture data = new();

    public TimesTodayButtonsTests()
    {
        data.UtcNow = new DateTime(2026, 10, 29, 9, 30, 0, DateTimeKind.Utc);
        Directory.CreateDirectory(data.TimesFolder);
    }

    private IRenderedComponent<StatusBarView>? bar;

    private IRenderedComponent<StatusBarView> Bar => bar ??= data.Bunit.Render<StatusBarView>();

    public void Dispose() => data.Dispose();

    private void WriteDay(string json) =>
        File.WriteAllText(Path.Combine(data.TimesFolder, "2026-10-29.json"), json);

    private async Task<IRenderedComponent<TimesTodayPanel>> Render()
    {
        await data.RefreshAsync();
        return data.Bunit.Render<TimesTodayPanel>();
    }

    private static string[] Buttons(IRenderedComponent<TimesTodayPanel> cut) =>
        cut.FindAll("section.times-today div.times-actions button").Select(b => b.TextContent.Trim()).ToArray();

    private static void WaitForButtons(IRenderedComponent<TimesTodayPanel> cut, params string[] buttons) =>
        cut.WaitForAssertion(() => Assert.Equal(buttons, Buttons(cut)), Wait);

    // 455: the booking's result is in the status bar; the panel has no result line of its own.
    private IElement WaitForResult(IRenderedComponent<TimesTodayPanel> cut)
    {
        Bar.WaitForAssertion(() => Assert.NotEqual(string.Empty, data.StatusBar.Text), Wait);
        Assert.Empty(cut.FindAll("p.times-result"));
        return Bar.Find(".main-status");
    }

    private static async Task OpenForm(IRenderedComponent<TimesTodayPanel> cut)
    {
        await StaleClick.ClickAsync(cut, "button.times-checkpoint");
        cut.WaitForElement("div.times-form", Wait);
    }

    private string? LastFocusId()
    {
        JSRuntimeInvocation? last = data.JsCalls()
            .LastOrDefault(i => i.Identifier == "Blazor._internal.domWrapper.focus");
        return last is { } call ? Assert.IsType<ElementReference>(call.Arguments[0]).Id : null;
    }

    // AC-1: a null Times folder renders no section once the panel has loaded.
    [Fact]
    public async Task NullFolder_RendersNothing()
    {
        data.NoTimesFolder = true;
        IRenderedComponent<TimesTodayPanel> cut = await Render();

        cut.WaitForState(() => cut.RenderCount >= 2, Wait);
        Assert.Empty(cut.FindAll("section"));
        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    // AC-1: a Times folder that does not exist renders no section either.
    [Fact]
    public async Task MissingFolder_RendersNothing()
    {
        Directory.Delete(data.TimesFolder, recursive: true);
        IRenderedComponent<TimesTodayPanel> cut = await Render();

        cut.WaitForState(() => cut.RenderCount >= 2, Wait);
        Assert.Empty(cut.FindAll("section"));
        Assert.False(Directory.Exists(data.TimesFolder));
    }

    // AC-2: the folder exists and today has no arrival: only Arrive, no line.
    [Fact]
    public async Task Out_ShowsOnlyArrive()
    {
        IRenderedComponent<TimesTodayPanel> cut = await Render();

        WaitForButtons(cut, "Arrive");
        Assert.Single(cut.FindAll("button.times-arrive"));
        Assert.Empty(cut.FindAll("p.times-line"));
        Assert.Empty(cut.FindAll("p.times-result"));
        Assert.Equal(string.Empty, data.StatusBar.Text);
        Assert.Equal("button", cut.Find("button.times-arrive").GetAttribute("type"));
    }

    // AC-2: an absence day counts as no arrival: only Arrive, no line.
    [Fact]
    public async Task AbsenceDay_ShowsOnlyArrive()
    {
        WriteDay("""{"absence":"vacation"}""");
        IRenderedComponent<TimesTodayPanel> cut = await Render();

        WaitForButtons(cut, "Arrive");
        Assert.Empty(cut.FindAll("p.times-line"));
    }

    // AC-3: each state's buttons after the unchanged job 277 line.
    [Theory]
    [InlineData("working", "Arrived 08:00 · Time left 5:30 · #1234 Review", new[] { "times-break", "times-checkpoint", "times-leave" }, new[] { "Break", "Checkpoint…", "Leave" })]
    [InlineData("onbreak", "Arrived 08:00 · Time left 7:00", new[] { "times-breakend" }, new[] { "End break" })]
    [InlineData("left", "Arrived 08:00 · Left 09:00 · Time left 7:00", new[] { "times-arrive" }, new[] { "Arrive" })]
    public async Task State_ShowsItsButtons(string state, string line, string[] classes, string[] texts)
    {
        WriteDay(state switch { "working" => Working, "onbreak" => OnBreak, _ => Left });
        IRenderedComponent<TimesTodayPanel> cut = await Render();

        WaitForButtons(cut, texts);
        Assert.Equal(line, cut.Find("p.times-line").TextContent);
        Assert.Equal(classes, cut.FindAll("div.times-actions button").Select(b => b.ClassList.Single(c => c.StartsWith("times-", StringComparison.Ordinal))).ToArray());
        Assert.Null(cut.Find("section.times-today").GetAttribute("aria-busy"));
    }

    // AC-3: unreadable: the grey line and no buttons.
    [Fact]
    public async Task Unreadable_NoButtons()
    {
        WriteDay("{");
        IRenderedComponent<TimesTodayPanel> cut = await Render();

        cut.WaitForAssertion(() => Assert.Equal("Can't read Times' day file.", cut.Find("p.times-line").TextContent), Wait);
        Assert.Empty(cut.FindAll("button"));
    }

    // AC-4, AC-7, AC-10: each button runs one call with its arguments, shows the accent result, and the panel reloads
    // the day file (rewritten before the click, as Times would), so the new state's buttons show; Context writes nothing.
    [Theory]
    [InlineData("", "times-arrive", "arrive", "Booked arrival at 10:05 in Times.", "working")]
    [InlineData("working", "times-break", "break", "Booked break at 10:05 in Times.", "onbreak")]
    [InlineData("onbreak", "times-breakend", "breakend", "Booked break end at 10:05 in Times.", "working")]
    [InlineData("working", "times-leave", "leave", "Booked leaving at 10:05 in Times.", "left")]
    public async Task Button_BooksAndReloads(string before, string button, string command, string booked, string after)
    {
        if (before.Length > 0)
        {
            WriteDay(before == "working" ? Working : OnBreak);
        }

        IRenderedComponent<TimesTodayPanel> cut = await Render();
        cut.WaitForElement("button." + button, Wait);

        WriteDay(after switch { "working" => Working, "onbreak" => OnBreak, _ => Left });
        SortedDictionary<string, string> files = FileSnapshot.Take(data.TimesFolder);
        await StaleClick.ClickAsync(cut, "button." + button);

        IElement result = WaitForResult(cut);
        Assert.Equal(booked, result.TextContent);
        Assert.Contains("accent", result.ClassList);
        Assert.Equal("status", result.GetAttribute("role"));
        WaitForButtons(cut, after switch { "working" => ["Break", "Checkpoint…", "Leave"], "onbreak" => ["End break"], _ => ["Arrive"] });
        IReadOnlyList<string> call = Assert.Single(data.TimesRunner.Calls);
        Assert.Equal([command, "10:05"], call);
        Assert.Equal(files, FileSnapshot.Take(data.TimesFolder));
    }

    // AC-5: Checkpoint… opens the form: projects from projects.json, a 200-character name field that gets focus.
    [Fact]
    public async Task Checkpoint_OpensForm_WithProjects_FocusOnName()
    {
        WriteDay(Working);
        File.WriteAllText(Path.Combine(data.TimesFolder, TimesProjects.FileName), Projects);
        IRenderedComponent<TimesTodayPanel> cut = await Render();
        WaitForButtons(cut, "Break", "Checkpoint…", "Leave");
        Assert.Empty(cut.FindAll("div.times-form"));
        Assert.Equal("false", cut.Find("button.times-checkpoint").GetAttribute("aria-expanded"));

        await OpenForm(cut);

        IElement form = cut.Find("section.times-today div.times-form");
        cut.WaitForAssertion(
            () => Assert.Equal(
                ["No project", "#4711 Billing", DesktopText.OneLine("#42 Plan\nnext <b>")],
                cut.FindAll("select.times-project option").Select(o => o.TextContent).ToArray()),
            Wait);
        Assert.Equal(["", "1", "2"], cut.FindAll("select.times-project option").Select(o => o.GetAttribute("value") ?? "-").ToArray());
        Assert.Empty(form.QuerySelectorAll("b"));
        IElement name = cut.Find("input.times-name");
        Assert.Equal("200", name.GetAttribute("maxlength"));
        Assert.Equal("text", name.GetAttribute("type"));
        Assert.Equal("Book", cut.Find("button.times-book").TextContent);
        Assert.Equal("Cancel", cut.Find("button.times-cancel").TextContent);
        Assert.Equal("true", cut.Find("button.times-checkpoint").GetAttribute("aria-expanded"));
        string nameId = cut.Instance.NameInputReference.Id;
        Assert.False(string.IsNullOrEmpty(nameId));
        cut.WaitForAssertion(() => Assert.Equal(nameId, LastFocusId()), Wait);
    }

    // AC-5: Esc in the name field, the select or on Book, and Cancel, close the form, run nothing and focus Checkpoint….
    [Theory]
    [InlineData("name")]
    [InlineData("select")]
    [InlineData("cancel")]
    [InlineData("book")]
    public async Task Cancel_RunsNothing_FocusBack(string how)
    {
        WriteDay(Working);
        IRenderedComponent<TimesTodayPanel> cut = await Render();
        WaitForButtons(cut, "Break", "Checkpoint…", "Leave");
        await OpenForm(cut);
        string nameId = cut.Instance.NameInputReference.Id;
        cut.WaitForAssertion(() => Assert.Equal(nameId, LastFocusId()), Wait);
        await StaleClick.InputAsync(cut, "input.times-name", new ChangeEventArgs { Value = "Docs" });

        switch (how)
        {
            case "name":
                await StaleClick.KeyDownAsync(cut, "input.times-name", new KeyboardEventArgs { Key = "Escape" });
                break;
            case "select":
                await StaleClick.KeyDownAsync(cut, "select.times-project", new KeyboardEventArgs { Key = "Escape" });
                break;
            case "book":
                await StaleClick.KeyDownAsync(cut, "button.times-book", new KeyboardEventArgs { Key = "Escape" });
                break;
            default:
                await StaleClick.ClickAsync(cut, "button.times-cancel");
                break;
        }

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("div.times-form")), Wait);
        string checkpointId = cut.Instance.CheckpointButtonReference.Id;
        Assert.False(string.IsNullOrEmpty(checkpointId));
        cut.WaitForAssertion(() => Assert.Equal(checkpointId, LastFocusId()), Wait);
        Assert.Empty(data.TimesRunner.Calls);
        Assert.Empty(cut.FindAll("p.times-result"));
        Assert.Equal(string.Empty, data.StatusBar.Text);
    }

    // AC-6, AC-7, AC-10: a chosen project and a name typed with option-like and project-like starts; Enter books one
    // call, the accent result names the cleaned checkpoint, the form closes and the reloaded state shows.
    [Theory]
    [InlineData("2", " -x\nnext ", "#2", "x next")]
    [InlineData("", "#3 y", null, "3 y")]
    [InlineData("1", "--version", "#1", "version")]
    public async Task Checkpoint_EnterBooks(string project, string typed, string? position, string name)
    {
        WriteDay(Working);
        File.WriteAllText(Path.Combine(data.TimesFolder, TimesProjects.FileName), Projects);
        IRenderedComponent<TimesTodayPanel> cut = await Render();
        WaitForButtons(cut, "Break", "Checkpoint…", "Leave");
        await OpenForm(cut);
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("select.times-project option").Count), Wait);
        SortedDictionary<string, string> files = FileSnapshot.Take(data.TimesFolder);

        await StaleClick.ChangeAsync(cut, "select.times-project", new ChangeEventArgs { Value = project });
        await StaleClick.InputAsync(cut, "input.times-name", new ChangeEventArgs { Value = typed });
        await StaleClick.KeyDownAsync(cut, "input.times-name", new KeyboardEventArgs { Key = "Enter" });

        IElement result = WaitForResult(cut);
        Assert.Equal($"Booked checkpoint {name} at 10:05 in Times.", result.TextContent);
        Assert.Contains("accent", result.ClassList);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("div.times-form")), Wait);
        WaitForButtons(cut, "Break", "Checkpoint…", "Leave");
        IReadOnlyList<string> call = Assert.Single(data.TimesRunner.Calls);
        string[] expected = position is null ? ["checkpoint", "10:05", name] : ["checkpoint", "10:05", position, name];
        Assert.Equal(expected, call);
        Assert.Equal(files, FileSnapshot.Take(data.TimesFolder));
    }

    // AC-6: Enter while an input method is composing books nothing and keeps the form.
    [Fact]
    public async Task Checkpoint_EnterWhileComposing_RunsNothing()
    {
        WriteDay(Working);
        IRenderedComponent<TimesTodayPanel> cut = await Render();
        WaitForButtons(cut, "Break", "Checkpoint…", "Leave");
        await OpenForm(cut);

        await StaleClick.InputAsync(cut, "input.times-name", new ChangeEventArgs { Value = "Docs" });
        await StaleClick.KeyDownAsync(cut, "input.times-name", new KeyboardEventArgs { Key = "Enter", IsComposing = true });

        Assert.Empty(data.TimesRunner.Calls);
        Assert.Single(cut.FindAll("div.times-form"));
        Assert.Empty(cut.FindAll("p.times-result"));
        Assert.Equal(string.Empty, data.StatusBar.Text);
    }

    // AC-6: the Book button books too.
    [Fact]
    public async Task Checkpoint_BookButtonBooks()
    {
        WriteDay(Working);
        IRenderedComponent<TimesTodayPanel> cut = await Render();
        WaitForButtons(cut, "Break", "Checkpoint…", "Leave");
        await OpenForm(cut);

        await StaleClick.InputAsync(cut, "input.times-name", new ChangeEventArgs { Value = "Docs" });
        await StaleClick.ClickAsync(cut, "button.times-book");

        Assert.Equal("Booked checkpoint Docs at 10:05 in Times.", WaitForResult(cut).TextContent);
        Assert.Equal(["checkpoint", "10:05", "Docs"], Assert.Single(data.TimesRunner.Calls));
    }

    // AC-6: a name blank after the clean-up shows the red hint, runs nothing and keeps the form.
    [Theory]
    [InlineData("")]
    [InlineData("  #-- \n ")]
    public async Task Checkpoint_BlankName_RunsNothing(string typed)
    {
        WriteDay(Working);
        IRenderedComponent<TimesTodayPanel> cut = await Render();
        WaitForButtons(cut, "Break", "Checkpoint…", "Leave");
        await OpenForm(cut);

        await StaleClick.InputAsync(cut, "input.times-name", new ChangeEventArgs { Value = typed });
        await StaleClick.KeyDownAsync(cut, "input.times-name", new KeyboardEventArgs { Key = "Enter" });

        IElement result = WaitForResult(cut);
        Assert.Equal("Name the checkpoint.", result.TextContent);
        Assert.Contains("red", result.ClassList);
        Assert.Empty(data.TimesRunner.Calls);
        Assert.Single(cut.FindAll("div.times-form"));
    }

    // AC-8: failures show the red text, keep the form and its input; a throwing runner gives the not-found text.
    [Theory]
    [InlineData("notfound", "Times isn't installed, so nothing was booked.")]
    [InlineData("timedout", "Times didn't answer in time.")]
    [InlineData("refused", "Times refused the booking: �[1mAlready�[0m booked")]
    [InlineData("stdout", "Times refused the booking: from stdout")]
    [InlineData("empty", "Times refused the booking: exit code 3")]
    [InlineData("throws", "Times isn't installed, so nothing was booked.")]
    public async Task Checkpoint_Failure_KeepsInput(string how, string text)
    {
        data.TimesRunner.Result = how switch
        {
            "notfound" => new TimesRunResult(TimesRunKind.NotFound, -1, string.Empty, string.Empty),
            "timedout" => new TimesRunResult(TimesRunKind.TimedOut, -1, string.Empty, string.Empty),
            "refused" => new TimesRunResult(TimesRunKind.Done, 1, "ignored\n", "\n  \n\u001b[1mAlready\u001b[0m booked\nsecond\n"),
            "stdout" => new TimesRunResult(TimesRunKind.Done, 1, "\nfrom stdout\n", " \n"),
            _ => new TimesRunResult(TimesRunKind.Done, 3, string.Empty, string.Empty),
        };
        if (how == "throws")
        {
            data.TimesRunner.Throws = new InvalidOperationException("boom");
        }

        WriteDay(Working);
        IRenderedComponent<TimesTodayPanel> cut = await Render();
        WaitForButtons(cut, "Break", "Checkpoint…", "Leave");
        await OpenForm(cut);
        await StaleClick.InputAsync(cut, "input.times-name", new ChangeEventArgs { Value = "Docs" });
        await StaleClick.ClickAsync(cut, "button.times-book");

        IElement result = WaitForResult(cut);
        Assert.Equal(text, result.TextContent);
        Assert.Contains("red", result.ClassList);
        Assert.Single(cut.FindAll("div.times-form"));
        Assert.Equal("Docs", cut.Find("input.times-name").GetAttribute("value"));
        Assert.Single(data.TimesRunner.Calls);
        Assert.False(cut.Find("button.times-book").HasAttribute("disabled"));
    }

    // AC-8: a plain button's failure shows the red text and keeps the state's buttons.
    [Fact]
    public async Task Button_Failure_ShowsRed()
    {
        data.TimesRunner.Result = new TimesRunResult(TimesRunKind.TimedOut, -1, string.Empty, string.Empty);
        IRenderedComponent<TimesTodayPanel> cut = await Render();
        WaitForButtons(cut, "Arrive");

        await StaleClick.ClickAsync(cut, "button.times-arrive");

        IElement result = WaitForResult(cut);
        Assert.Equal("Times didn't answer in time.", result.TextContent);
        Assert.Contains("red", result.ClassList);
        WaitForButtons(cut, "Arrive");
    }

    // AC-9: while a run is held, every button is disabled and the section is busy; a second click starts no run.
    [Fact]
    public async Task Pending_DisablesButtons_SecondClickRunsNothing()
    {
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        data.TimesRunner.Hold = hold.Task;
        WriteDay(Working);
        IRenderedComponent<TimesTodayPanel> cut = await Render();
        WaitForButtons(cut, "Break", "Checkpoint…", "Leave");
        await OpenForm(cut);
        await StaleClick.InputAsync(cut, "input.times-name", new ChangeEventArgs { Value = "Docs" });

        Task first = StaleClick.ClickAsync(cut, "button.times-book");
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("section.times-today").GetAttribute("aria-busy")), Wait);
        cut.WaitForAssertion(() => Assert.Single(data.TimesRunner.Calls), Wait);
        Assert.All(cut.FindAll("section.times-today button"), b => Assert.True(b.HasAttribute("disabled")));
        Assert.True(cut.Find("input.times-name").HasAttribute("disabled"));
        Assert.True(cut.Find("select.times-project").HasAttribute("disabled"));

        await StaleClick.KeyDownAsync(cut, "input.times-name", new KeyboardEventArgs { Key = "Enter" });
        Assert.Single(data.TimesRunner.Calls);

        hold.SetResult();
        await first;
        Assert.Equal("Booked checkpoint Docs at 10:05 in Times.", WaitForResult(cut).TextContent);
        cut.WaitForAssertion(() => Assert.Null(cut.Find("section.times-today").GetAttribute("aria-busy")), Wait);
        Assert.Single(data.TimesRunner.Calls);
    }
}
