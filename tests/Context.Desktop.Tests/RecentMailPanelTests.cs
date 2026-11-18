using System.Reflection;
using AngleSharp.Dom;
using Bunit;

namespace Context.Tests;

// 217 AC-1, AC-4, AC-5, AC-7: the last day's Inbox mail in the right sidebar, below the appointments.
public sealed class RecentMailPanelTests : IDisposable
{
    // The mail loads on Task.Run (and the timeout test waits for a blocked source); every wait passes this bound.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // The fixture's clock is 2026-10-29 09:00 UTC, 10:00 in its zone (UTC+1).
    private static MailSummary Mail(DateTime receivedUtc, string sender, string subject) =>
        new(DateTime.SpecifyKind(receivedUtc, DateTimeKind.Utc), sender, subject);

    private void OutlookOn() => new SettingsStore(data.Root).SaveOutlook(true);

    // The panel's loaded view (null until its first load finished).
    private static RecentMailView? Loaded(IRenderedComponent<RecentMailPanel> cut) =>
        (RecentMailView?)typeof(RecentMailPanel)
            .GetField("view", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cut.Instance);

    private IRenderedComponent<RecentMailPanel> Show()
    {
        data.CreateState();
        IRenderedComponent<RecentMailPanel> cut = data.Bunit.Render<RecentMailPanel>();
        cut.WaitForAssertion(() =>
        {
            RecentMailView? view = Loaded(cut);
            Assert.NotNull(view);
            Assert.Equal(view.Status == OutlookStatus.Off ? 0 : 1, cut.FindAll("section.recent-mail").Count);
        }, Wait);
        return cut;
    }

    private static string[] Rows(IRenderedComponent<RecentMailPanel> cut) =>
        cut.FindAll(".recent-mail .mail-item")
            .Select(r => r.QuerySelector(".time")!.TextContent + " | " + r.QuerySelector(".mail-sender")!.TextContent
                + " | " + r.QuerySelector(".mail-subject")!.TextContent)
            .ToArray();

    // AC-1: with Outlook off the panel renders nothing and the source is never asked.
    [Theory]
    [InlineData(null)]
    [InlineData("{\"outlook\":{\"enabled\":false}}")]
    public void Off_RendersNothing(string? settings)
    {
        if (settings is not null)
        {
            data.WriteFile(SettingsStore.FileName, settings);
        }

        data.Outlook.Mails = [Mail(new DateTime(2026, 10, 29, 8, 0, 0), "Ann", "Hello")];

        IRenderedComponent<RecentMailPanel> cut = Show();

        Assert.Equal(OutlookStatus.Off, Loaded(cut)!.Status);
        Assert.Equal(string.Empty, cut.Markup.Trim());
        Assert.Equal(0, data.Outlook.MailCalls);
    }

    // AC-7: section.recent-mail right after the appointments in the sidebar, heading Mail, rows newest first with the
    // time (HH:mm today, ddd HH:mm before, in the calendar's zone), sender and subject; no click action.
    [Fact]
    public async Task On_RowsInTheSidebar_AfterTheAppointments()
    {
        OutlookOn();
        data.Outlook.Mails =
        [
            Mail(new DateTime(2026, 10, 28, 20, 0, 0), "Bob", "Yesterday"),
            Mail(new DateTime(2026, 10, 29, 8, 15, 0), "Ann", "Today"),
            Mail(new DateTime(2026, 10, 28, 23, 30, 0), "Cy", "After midnight here"),
        ];
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading…", cut.Find(".day-appointments").TextContent, StringComparison.Ordinal);
            Assert.Equal(3, cut.FindAll(".recent-mail .mail-item").Count);
        }, Wait);

        IElement aside = cut.Find("aside.todos-region");
        Assert.Contains("day-appointments", aside.Children[0].ClassList);
        IElement section = aside.Children[1];
        Assert.Equal("SECTION", section.TagName);
        Assert.Contains("recent-mail", section.ClassList);
        Assert.Equal("Mail", section.GetAttribute("aria-label"));
        Assert.Equal("Mail", section.QuerySelector("h2")!.TextContent);
        Assert.Equal(
            ["09:15 | Ann | Today", "00:30 | Cy | After midnight here", "Wed 21:00 | Bob | Yesterday"],
            cut.FindAll(".recent-mail .mail-item")
                .Select(r => r.QuerySelector(".time")!.TextContent + " | " + r.QuerySelector(".mail-sender")!.TextContent
                    + " | " + r.QuerySelector(".mail-subject")!.TextContent)
                .ToArray());
        Assert.All(cut.FindAll(".recent-mail .mail-item"), r => Assert.Equal("DIV", r.TagName));
        Assert.Empty(section.QuerySelectorAll("button, a, input"));
        Assert.DoesNotContain("blazor:on", section.OuterHtml, StringComparison.Ordinal);
    }

    // AC-7: at most ten, received in the last 24 hours; blank sender and subject read (no sender) / (no subject);
    // outside text is shown on one line.
    [Fact]
    public void On_TenAtMost_Placeholders_OneLine()
    {
        OutlookOn();
        data.Outlook.Mails =
        [
            .. Enumerable.Range(0, 11).Select(i => Mail(new DateTime(2026, 10, 29, 8, 0, 0).AddMinutes(-i), "S", "m" + i)),
            Mail(new DateTime(2026, 10, 28, 8, 59, 0), "Old", "Too old"),
            Mail(new DateTime(2026, 10, 29, 8, 30, 0), "  ", "<b>Two\nlines</b>"),
            Mail(new DateTime(2026, 10, 29, 8, 20, 0), "Eve", string.Empty),
        ];

        IRenderedComponent<RecentMailPanel> cut = Show();

        string[] rows = Rows(cut);
        Assert.Equal(10, rows.Length);
        Assert.Equal("09:30 | (no sender) | " + DesktopText.OneLine("<b>Two\nlines</b>"), rows[0]);
        Assert.Equal("09:20 | Eve | (no subject)", rows[1]);
        Assert.Equal("09:00 | S | m0", rows[2]);
        Assert.DoesNotContain(rows, r => r.Contains("Too old", StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".recent-mail b"));
    }

    // AC-7: with no mail the panel says so in grey.
    [Fact]
    public void On_NoMail_GreyLine()
    {
        OutlookOn();

        IRenderedComponent<RecentMailPanel> cut = Show();

        IElement line = cut.Find(".recent-mail p");
        Assert.Equal("No mail in the last day.", line.TextContent);
        Assert.Contains("grey", line.ClassList);
        Assert.Empty(cut.FindAll(".mail-item"));
    }

    // AC-4: each failure shows its status line in the mail panel, and nothing throws.
    [Theory]
    [InlineData("NotRunning", false, "grey", "Outlook isn't running. Start it to see its appointments and mail.")]
    [InlineData("Denied", false, "red", "Can't read Outlook (access denied).")]
    [InlineData("Failed", false, "red", "Can't read Outlook (error).")]
    [InlineData("Ok", true, "red", "Can't read Outlook (error).")]
    public void Failure_StatusLine(string kind, bool throws, string css, string text)
    {
        OutlookOn();
        data.Outlook.MailKind = Enum.Parse<OutlookReadKind>(kind);
        data.Outlook.Throws = throws ? new InvalidOperationException("MARKER-217") : null;

        IRenderedComponent<RecentMailPanel> cut = Show();

        IElement line = cut.Find(".recent-mail p.outlook-status");
        Assert.Equal(text, line.TextContent);
        Assert.Contains(css, line.ClassList);
        Assert.Equal("status", line.GetAttribute("role"));
        Assert.Empty(cut.FindAll(".mail-item"));
        Assert.DoesNotContain("MARKER-217", cut.Markup, StringComparison.Ordinal);
    }

    // AC-5: a blocked source call shows "timed out" after OutlookTimeout.
    [Fact]
    public void Blocked_TimedOut()
    {
        OutlookOn();
        data.OutlookTimeout = TimeSpan.FromMilliseconds(100);
        data.Outlook.Block();
        data.CreateState();

        IRenderedComponent<RecentMailPanel> cut = data.Bunit.Render<RecentMailPanel>();

        cut.WaitForAssertion(
            () => Assert.Equal("Can't read Outlook (timed out).", cut.Find(".recent-mail p.outlook-status").TextContent),
            Wait);
        Assert.Contains("red", cut.Find(".recent-mail p.outlook-status").ClassList);
        Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal);
    }

    // AC-7: a state change reloads the mail and keeps the shown rows until the new ones are there.
    [Fact]
    public async Task StateChanged_Reloads_KeepsTheRows()
    {
        // The source is blocked while the rows are checked: a timeout far beyond the test keeps them from timing out.
        data.OutlookTimeout = TimeSpan.FromMinutes(5);
        OutlookOn();
        DateTime start = data.UtcNow;
        data.Outlook.Mails = [Mail(new DateTime(2026, 10, 29, 8, 0, 0), "Ann", "First")];
        DesktopState state = data.CreateState();

        // The mail is cached at the start, today's appointments three minutes later.
        await state.RecentMail();
        data.UtcNow = start.AddMinutes(3);
        await state.Refresh();
        IRenderedComponent<RecentMailPanel> cut = Show();
        Assert.Equal(["09:00 | Ann | First"], Rows(cut));
        Assert.Equal(1, data.Outlook.MailCalls);

        // Five minutes on the mail is stale (the appointments are not), and the source waits.
        data.UtcNow = start.AddMinutes(5);
        data.Outlook.Block();
        await state.Refresh();

        // The panel's reload is blocked in the source, so no render follows the call: wait on the fake itself.
        Assert.True(data.Outlook.WaitForMailCalls(2, Wait));
        Assert.Equal(["09:00 | Ann | First"], Rows(cut));

        data.Outlook.Mails = [Mail(new DateTime(2026, 10, 29, 8, 50, 0), "Bob", "Second")];
        data.Outlook.Release();

        cut.WaitForAssertion(() => Assert.Equal(["09:50 | Bob | Second"], Rows(cut)), Wait);
    }
}
