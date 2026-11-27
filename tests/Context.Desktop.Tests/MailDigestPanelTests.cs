using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 286 AC-5 to AC-10: the Mail digest page through Main (/digest), with FakeOutlookSource and the fixture's clock.
public sealed class MailDigestPanelTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    // 08:30 UTC is 09:30 in the fixture's UTC+1 zone.
    private static readonly DateTime Received = new(2026, 10, 28, 8, 30, 0, DateTimeKind.Utc);

    private readonly DesktopFixture data = new();

    private IRenderedComponent<StatusBarView>? bar;

    // 455: feedback goes to the status bar, which Main renders; tests render the bar next to the panel.
    private IRenderedComponent<StatusBarView> Bar => bar ??= data.Bunit.Render<StatusBarView>();

    public void Dispose() => data.Dispose();

    private static MailMessage Mail(
        string body, string subject = "Status", string sender = "Anna", DateTime? received = null, string messageClass = "IPM.Note") =>
        new(subject, sender, received ?? Received, messageClass, body);

    // Turns the digest and Outlook on, as the settings page would.
    private void TurnOn()
    {
        SettingsStore store = new SettingsStore(data.Root);
        store.SaveOutlook(true);
        store.SaveMailDigest(true);
    }

    // Renders Main, waits until every load it starts by itself has rendered, then opens the page with /digest.
    private async Task<IRenderedComponent<Main>> Open()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal), Wait);
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "k", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = "/digest" });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".mail-digest-panel"));
            Assert.Empty(cut.FindAll(".mail-digest-loading"));
        }, Wait);
        return cut;
    }

    private static async Task ReadMail(IRenderedComponent<Main> cut)
    {
        await cut.Find("button.read-mail").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".mail-digest-reading")), Wait);
    }

    private static string[] Texts(IRenderedComponent<Main> cut) =>
        [.. cut.FindAll(".digest-row .digest-text").Select(e => e.TextContent)];

    // The ids of the Accept buttons, read in the render that created them.
    private static string[] AcceptIds(IRenderedComponent<Main> cut)
    {
        string[] ids = [.. cut.FindAll(".digest-row button.digest-accept").Select(b => b.GetAttribute("blazor:elementReference") ?? string.Empty)];
        Assert.All(ids, id => Assert.False(string.IsNullOrEmpty(id)));
        return ids;
    }

    private int FocusCalls() =>
        data.JsCalls().Count(i => i.Identifier == "Blazor._internal.domWrapper.focus");

    private string LastFocusId()
    {
        JSRuntimeInvocation last = data.JsCalls().Last(i => i.Identifier == "Blazor._internal.domWrapper.focus");
        return ((ElementReference)last.Arguments[0]!).Id;
    }

    // Every file under the data folder with its bytes.
    private SortedDictionary<string, byte[]> Files()
    {
        SortedDictionary<string, byte[]> files = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(data.Root, "*", SearchOption.AllDirectories))
        {
            files[Path.GetRelativePath(data.Root, path).Replace('\\', '/')] = File.ReadAllBytes(path);
        }

        return files;
    }

    // AC-5: with the digest setting off, Outlook off or Outlook not Available the page says so in grey and never reads.
    [Theory]
    [InlineData(false, true, "Available")]
    [InlineData(true, false, "Available")]
    [InlineData(true, true, "NotInstalled")]
    [InlineData(true, true, "Unsupported")]
    public async Task Off_GreyLine_NeverReads(bool digest, bool outlook, string availability)
    {
        SettingsStore store = new SettingsStore(data.Root);
        store.SaveOutlook(outlook);
        store.SaveMailDigest(digest);
        data.Outlook.Available = Enum.Parse<OutlookAvailability>(availability);
        data.Outlook.Mails.Add(Mail("Please send it."));

        IRenderedComponent<Main> cut = await Open();

        IElement line = cut.Find(".mail-digest-panel .mail-digest-off");
        Assert.Equal("Turn on mail suggestions in Settings.", line.TextContent);
        Assert.Contains("grey", line.ClassList);
        Assert.Empty(cut.FindAll("button.read-mail"));
        Assert.Equal("Mail digest", cut.Find(".mail-digest-panel .panel-bar h2").TextContent);

        await data.State!.Refresh();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".mail-digest-off")), Wait);
        Assert.Equal(0, data.Outlook.MailCalls);
    }

    // AC-6: opening the page and a state change read nothing; Read mail reads 7 days, at most 200, once.
    [Fact]
    public async Task ReadMail_OnlyOnClick_SevenDaysAtMost200()
    {
        TurnOn();
        data.Outlook.Mails.Add(Mail("Please send it."));

        IRenderedComponent<Main> cut = await Open();
        await data.State!.Refresh();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button.read-mail")), Wait);
        Assert.Equal("Read mail", cut.Find("button.read-mail").TextContent);
        Assert.Equal(0, data.Outlook.MailCalls);
        Assert.Empty(cut.FindAll(".digest-row"));

        await ReadMail(cut);

        (DateTime since, int max) = Assert.Single(data.Outlook.MailArguments);
        Assert.Equal(data.UtcNow.AddDays(-7), since);
        Assert.Equal(DateTimeKind.Utc, since.Kind);
        Assert.Equal(200, max);
        Assert.Equal(["Please send it."], Texts(cut));
    }

    // AC-6: while the read runs the page shows Reading mail… and the button is disabled.
    [Fact]
    public async Task ReadMail_WhileRunning_ShowsReading()
    {
        TurnOn();
        data.Outlook.Mails.Add(Mail("Please send it."));
        IRenderedComponent<Main> cut = await Open();
        data.Outlook.Block();

        Task click = cut.Find("button.read-mail").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Reading mail…", cut.Find(".mail-digest-reading").TextContent), Wait);
        Assert.Contains("grey", cut.Find(".mail-digest-reading").ClassList);
        Assert.True(cut.Find("button.read-mail").HasAttribute("disabled"));

        data.Outlook.Release();
        await click;
        cut.WaitForAssertion(() => Assert.Equal(["Please send it."], Texts(cut)), Wait);
        Assert.Empty(cut.FindAll(".mail-digest-reading"));
        Assert.False(cut.Find("button.read-mail").HasAttribute("disabled"));
    }

    // AC-6: the status lines are OutlookStatusLine's, except that a stopped Outlook names its mail.
    [Theory]
    [InlineData("NotRunning", "Outlook isn't running. Start it to see its mail.", "grey")]
    [InlineData("Denied", "Can't read Outlook (access denied).", "red")]
    [InlineData("Failed", "Can't read Outlook (error).", "red")]
    public async Task ReadMail_Kinds_StatusLine(string kind, string text, string css)
    {
        TurnOn();
        data.Outlook.MailKind = Enum.Parse<OutlookReadKind>(kind);
        IRenderedComponent<Main> cut = await Open();

        await ReadMail(cut);

        cut.WaitForAssertion(() => Assert.Equal(text, cut.Find(".mail-digest-status").TextContent), Wait);
        Assert.Contains(css, cut.Find(".mail-digest-status").ClassList);
        Assert.Empty(cut.FindAll(".digest-row"));
    }

    // AC-6: a source that throws reads as (error).
    [Fact]
    public async Task ReadMail_Throws_Error()
    {
        TurnOn();
        IRenderedComponent<Main> cut = await Open();
        data.Outlook.Throws = new InvalidOperationException("secret detail");

        await ReadMail(cut);

        cut.WaitForAssertion(() => Assert.Equal("Can't read Outlook (error).", cut.Find(".mail-digest-status").TextContent), Wait);
        Assert.DoesNotContain("secret detail", cut.Markup, StringComparison.Ordinal);
    }

    // AC-6: a read that outlasts OutlookTimeout shows (timed out) in red.
    [Fact]
    public async Task ReadMail_Blocked_TimedOut()
    {
        TurnOn();
        data.OutlookTimeout = TimeSpan.FromMilliseconds(100);
        data.Outlook.Mails.Add(Mail("Please send it."));
        IRenderedComponent<Main> cut = await Open();
        data.Outlook.Block();

        await ReadMail(cut);

        cut.WaitForAssertion(() => Assert.Equal("Can't read Outlook (timed out).", cut.Find(".mail-digest-status").TextContent), Wait);
        Assert.Contains("red", cut.Find(".mail-digest-status").ClassList);
        Assert.Empty(cut.FindAll(".digest-row"));
    }

    // AC-6: a read with nothing to suggest says so.
    [Fact]
    public async Task ReadMail_Nothing_NoSuggestions()
    {
        TurnOn();
        data.Outlook.Mails.Add(Mail("Thanks."));
        IRenderedComponent<Main> cut = await Open();

        await ReadMail(cut);

        cut.WaitForAssertion(() => Assert.Equal("No suggestions.", cut.Find(".mail-digest-empty").TextContent), Wait);
        Assert.Empty(cut.FindAll(".mail-digest-status"));
    }

    // AC-7: each row shows its kind, its text as text, grey "Sender · Received" and Accept and Dismiss.
    [Fact]
    public async Task Rows_KindTextSourceAndButtons()
    {
        TurnOn();
        data.Outlook.Mails.Add(Mail("Please <b>send</b> it.\nOk?", "Budget", "Bob\nSmith"));
        data.Outlook.Mails.Add(Mail(string.Empty, "Planning", "Eve", Received.AddHours(-1), "IPM.Schedule.Meeting.Request"));
        IRenderedComponent<Main> cut = await Open();

        await ReadMail(cut);

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".digest-row").Count), Wait);
        Assert.Equal(["Todo", "Follow-up", "Meeting note"], cut.FindAll(".digest-row .digest-kind").Select(e => e.TextContent).ToArray());
        Assert.Equal(["Please <b>send</b> it.", "Reply to BobSmith: Budget", "Planning"], Texts(cut));
        Assert.Empty(cut.FindAll(".digest-text b"));
        IElement source = cut.Find(".digest-row .digest-source");
        Assert.Equal("Bob Smith · 2026-10-28 09:30", source.TextContent);
        Assert.Contains("grey", source.ClassList);
        Assert.Equal("Eve · 2026-10-28 08:30", cut.FindAll(".digest-row .digest-source")[2].TextContent);
        Assert.Equal(["Accept", "Accept", "Accept"], cut.FindAll(".digest-row button.digest-accept").Select(b => b.TextContent).ToArray());
        Assert.Equal(["Dismiss", "Dismiss", "Dismiss"], cut.FindAll(".digest-row button.digest-dismiss").Select(b => b.TextContent).ToArray());
        Assert.All(cut.FindAll(".mail-digest-panel button"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Equal("Accept: Planning", cut.FindAll(".digest-row button.digest-accept")[2].GetAttribute("aria-label"));
        Assert.Equal("Dismiss: Planning", cut.FindAll(".digest-row button.digest-dismiss")[2].GetAttribute("aria-label"));
    }

    // AC-7, AC-10: reading and dismissing write nothing: the data folder's files and bytes are unchanged.
    [Fact]
    public async Task ReadAndDismiss_WriteNothing()
    {
        TurnOn();
        data.WriteNote("Plan", "# Plan\n");
        data.Outlook.Mails.Add(Mail("Please send it.\nOk?", "Budget"));
        data.Outlook.Mails.Add(Mail(string.Empty, "Planning", messageClass: "IPM.Schedule.Meeting.Request"));
        IRenderedComponent<Main> cut = await Open();
        SortedDictionary<string, byte[]> before = Files();

        await ReadMail(cut);
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".digest-row").Count), Wait);
        for (int i = 0; i < 3; i++)
        {
            await cut.Find(".digest-row button.digest-dismiss").ClickAsync(new MouseEventArgs());
        }

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".digest-row")), Wait);
        Assert.Equal(before.Keys, Files().Keys);
        Assert.All(before, f => Assert.Equal(f.Value, Files()[f.Key]));
        Assert.Empty(data.Todos!.List());
    }

    // AC-8: Accept on a todo adds it due today, removes the row and moves the focus to the next row's Accept.
    [Fact]
    public async Task Accept_Todo_AddsIt_RemovesRow_FocusesNext()
    {
        TurnOn();
        data.Outlook.Mails.Add(Mail("Please send it. Please call Bob."));
        IRenderedComponent<Main> cut = await Open();
        await ReadMail(cut);
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".digest-row").Count), Wait);
        string[] ids = AcceptIds(cut);

        await cut.Find(".digest-row button.digest-accept").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(["Please call Bob."], Texts(cut)), Wait);
        TodoItem added = Assert.Single(data.Todos!.List());
        Assert.Equal(("Please send it.", DesktopFixture.Today), (added.Text, added.Due));
        Assert.Equal("accent", data.StatusBar.Css);
        cut.WaitForAssertion(() => Assert.Equal(ids[1], LastFocusId()), Wait);
    }

    // AC-8: a refused Accept (a meeting note whose name is taken) keeps the row with the red result line.
    [Fact]
    public async Task Accept_Refused_KeepsRowWithResult()
    {
        TurnOn();
        data.WriteNote("Planning", "# Planning\nmine\n");
        data.Outlook.Mails.Add(Mail(string.Empty, "Planning", messageClass: "IPM.Schedule.Meeting.Request"));
        IRenderedComponent<Main> cut = await Open();
        await ReadMail(cut);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".digest-row")), Wait);

        await cut.Find(".digest-row button.digest-accept").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Note Planning already exists.", cut.Find(".main-status").TextContent), Wait);
        Assert.Contains("red", cut.Find(".main-status").ClassList);
        Assert.Empty(cut.FindAll(".digest-row .digest-result"));
        Assert.Equal(["Planning"], Texts(cut));
        Assert.Equal("# Planning\nmine\n", File.ReadAllText(Path.Combine(data.Root, "Planning.md")));
    }

    // AC-9: Dismiss removes the row for the session and moves the focus on; a later Read mail doesn't show it again.
    [Fact]
    public async Task Dismiss_HiddenForTheSession()
    {
        TurnOn();
        data.Outlook.Mails.Add(Mail("Please send it. Please call Bob."));
        IRenderedComponent<Main> cut = await Open();
        await ReadMail(cut);
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".digest-row").Count), Wait);
        string[] ids = AcceptIds(cut);

        await cut.Find(".digest-row button.digest-dismiss").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(["Please call Bob."], Texts(cut)), Wait);
        cut.WaitForAssertion(() => Assert.Equal(ids[1], LastFocusId()), Wait);
        int focused = FocusCalls();

        // With no row left, the focus goes to Read mail (a new focus call on another element).
        await cut.Find(".digest-row button.digest-dismiss").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".digest-row")), Wait);
        cut.WaitForAssertion(() => Assert.True(FocusCalls() > focused), Wait);
        Assert.DoesNotContain(LastFocusId(), ids);

        await ReadMail(cut);

        Assert.Equal(2, data.Outlook.MailCalls);
        cut.WaitForAssertion(() => Assert.Equal("No suggestions.", cut.Find(".mail-digest-empty").TextContent), Wait);
        Assert.Empty(data.Todos!.List());
    }

    // The page's close button and Esc close it.
    [Fact]
    public async Task Close_AndEscape_ClosePage()
    {
        TurnOn();
        IRenderedComponent<Main> cut = await Open();

        await cut.Find(".mail-digest-panel button.close").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll(".mail-digest-panel"));

        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "k", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = "/digest" });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".mail-digest-panel")), Wait);

        await cut.Find("section.mail-digest-panel").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll(".mail-digest-panel"));
        Assert.Equal(0, data.Outlook.MailCalls);
    }
}
