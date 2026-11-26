using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Context.Tests;

// 217 AC-2, AC-6: turning Outlook on and off in settings.json through DesktopActions. 286: the same for the Mail
// digest's setting, its SettingsStore pair and its checkbox.
public sealed class OutlookSettingsActionsTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    private IRenderedComponent<StatusBarView>? bar;

    // 455: feedback goes to the status bar, which Main renders; tests render the bar next to the panel.
    private IRenderedComponent<StatusBarView> Bar => bar ??= data.Bunit.Render<StatusBarView>();

    public void Dispose() => data.Dispose();

    private string SettingsPath => Path.Combine(data.Root, SettingsStore.FileName);

    private bool Stored() => new SettingsStore(data.Root).ReadOutlook();

    // AC-2: on and off answer Done with their texts, store the value, keep the other properties and refresh.
    [Fact]
    public async Task SetOutlookEnabled_OnThenOff_StoresIt_KeepsOthers_Refreshes()
    {
        data.WriteFile(SettingsStore.FileName, "{\"theme\":\"dark\",\"savedSearches\":[{\"name\":\"Work\",\"query\":\"#work\"}]}");
        await data.RefreshAsync();
        int builds = data.State!.Builds;

        ActionResult on = await data.Actions!.SetOutlookEnabled(true);

        Assert.Equal((ActionKind.Done, ActionReason.None, "Outlook on."), (on.Kind, on.Reason, on.Text));
        Assert.True(Stored());
        Assert.True(data.State.Builds > builds);
        Assert.Contains("\"theme\": \"dark\"", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
        Assert.Equal(["Work"], new SettingsStore(data.Root).ReadSavedSearches().Select(s => s.Name).ToArray());

        ActionResult off = await data.Actions.SetOutlookEnabled(false);

        Assert.Equal((ActionKind.Done, "Outlook off."), (off.Kind, off.Text));
        Assert.False(Stored());
    }

    // AC-2: turning it on is refused, writing nothing, when the source is not Available; turning it off still works.
    [Theory]
    [InlineData("Unsupported")]
    [InlineData("NotInstalled")]
    public async Task SetOutlookEnabled_On_NotAvailable_Refused(string availability)
    {
        data.Outlook.Available = Enum.Parse<OutlookAvailability>(availability);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.SetOutlookEnabled(true);

        Assert.Equal((ActionKind.Refused, ActionReason.Invalid), (result.Kind, result.Reason));
        Assert.Equal(DesktopActions.OutlookUnavailableText, result.Text);
        Assert.False(File.Exists(SettingsPath));

        ActionResult off = await data.Actions.SetOutlookEnabled(false);
        Assert.Equal(ActionKind.Done, off.Kind);
        Assert.False(Stored());
    }

    // AC-2: a broken settings.json is Failed with the fixed text and left as it is.
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"dayEnd\":{\"moved\":5}}")]
    public async Task SetOutlookEnabled_BrokenSettings_Failed_Unchanged(string text)
    {
        data.WriteFile(SettingsStore.FileName, text);
        await data.RefreshAsync();
        byte[] before = File.ReadAllBytes(SettingsPath);

        ActionResult result = await data.Actions!.SetOutlookEnabled(true);

        Assert.Equal((ActionKind.Failed, ActionReason.Unreadable), (result.Kind, result.Reason));
        Assert.Equal("Can't read settings.json. Nothing was saved.", result.Text);
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    // AC-6: SetOutlookEnabled clears the cache, so the next read asks the source again within the five minutes.
    [Fact]
    public async Task SetOutlookEnabled_ClearsTheCache()
    {
        new SettingsStore(data.Root).SaveOutlook(true);
        DesktopState state = data.CreateState();
        await state.DayAppointments(DesktopFixture.Today);
        Assert.Equal(1, data.Outlook.AppointmentCalls);

        ActionResult result = await data.Actions!.SetOutlookEnabled(true);
        Assert.Equal(ActionKind.Done, result.Kind);
        int appointmentCalls = data.Outlook.AppointmentCalls;
        await state.DayAppointments(DesktopFixture.Today);

        // The refresh after the action has already read today's appointments anew, so this read is served from it.
        Assert.True(appointmentCalls >= 2);
        Assert.Equal(appointmentCalls, data.Outlook.AppointmentCalls);
    }

    // 286: SaveMailDigest stores exactly "mailDigest":{"enabled":…}, keeps the other properties; anything else is off.
    [Fact]
    public void SaveMailDigest_RoundTrips_KeepsOthers()
    {
        data.WriteFile(SettingsStore.FileName, "{\"theme\":\"dark\",\"outlook\":{\"enabled\":true}}");
        SettingsStore store = new SettingsStore(data.Root);
        Assert.False(store.ReadMailDigest());

        store.SaveMailDigest(true);

        Assert.True(store.ReadMailDigest());
        Assert.True(store.ReadOutlook());
        Assert.Contains("\"theme\": \"dark\"", File.ReadAllText(SettingsPath), StringComparison.Ordinal);

        store.SaveMailDigest(false);
        Assert.False(store.ReadMailDigest());
        Assert.True(store.ReadOutlook());
    }

    // 286: a value other than true reads as off.
    [Theory]
    [InlineData("{\"mailDigest\":{\"enabled\":\"true\"}}")]
    [InlineData("{\"mailDigest\":true}")]
    [InlineData("{\"mailDigest\":{}}")]
    public void ReadMailDigest_OtherValues_Off(string text)
    {
        data.WriteFile(SettingsStore.FileName, text);

        Assert.False(new SettingsStore(data.Root).ReadMailDigest());
    }

    // 286: on and off answer Done with their texts, store the value and keep the Outlook setting.
    [Fact]
    public async Task SetMailDigestEnabled_OnThenOff_StoresIt()
    {
        new SettingsStore(data.Root).SaveOutlook(true);
        await data.RefreshAsync();

        ActionResult on = await data.Actions!.SetMailDigestEnabled(true);

        Assert.Equal((ActionKind.Done, ActionReason.None, "Mail suggestions on."), (on.Kind, on.Reason, on.Text));
        Assert.True(new SettingsStore(data.Root).ReadMailDigest());
        Assert.True(Stored());

        ActionResult off = await data.Actions.SetMailDigestEnabled(false);

        Assert.Equal((ActionKind.Done, "Mail suggestions off."), (off.Kind, off.Text));
        Assert.False(new SettingsStore(data.Root).ReadMailDigest());
        Assert.Equal(0, data.Outlook.MailCalls);
    }

    // 286: turning it on without classic Outlook is refused and writes nothing; a broken file is Failed, unchanged.
    [Fact]
    public async Task SetMailDigestEnabled_NotAvailableOrBroken_Refused()
    {
        data.Outlook.Available = OutlookAvailability.Unsupported;
        await data.RefreshAsync();

        ActionResult refused = await data.Actions!.SetMailDigestEnabled(true);

        Assert.Equal((ActionKind.Refused, ActionReason.Invalid, DesktopActions.OutlookUnavailableText), (refused.Kind, refused.Reason, refused.Text));
        Assert.False(File.Exists(SettingsPath));

        data.Outlook.Available = OutlookAvailability.Available;
        data.WriteFile(SettingsStore.FileName, "not json");
        byte[] before = File.ReadAllBytes(SettingsPath);

        ActionResult failed = await data.Actions.SetMailDigestEnabled(true);

        Assert.Equal((ActionKind.Failed, ActionReason.Unreadable), (failed.Kind, failed.Reason));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    // 286: the settings page's checkbox under the Outlook one shows the stored value and toggles it.
    [Fact]
    public async Task SettingsPanel_MailDigestCheckbox_ShowsAndToggles()
    {
        new SettingsStore(data.Root).SaveOutlook(true);
        data.CreateState();
        IRenderedComponent<SettingsPanel> cut = data.Bunit.Render<SettingsPanel>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal), Wait);

        IElement box = cut.Find("label.mail-digest-line input.mail-digest-enabled");
        Assert.Equal("checkbox", box.GetAttribute("type"));
        Assert.False(box.HasAttribute("checked"));
        Assert.Equal("Suggest todos from Outlook mail", cut.Find("label.mail-digest-line").TextContent.Trim());
        Assert.Equal("outlook-hint grey", box.ParentElement!.PreviousElementSibling!.ClassName);

        await cut.Find("input.mail-digest-enabled").ChangeAsync(new ChangeEventArgs { Value = true });

        Bar.WaitForAssertion(() =>
        {
            Assert.Equal("Mail suggestions on.", data.StatusBar.Text);
        }, Wait);
        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("input.mail-digest-enabled").HasAttribute("checked"));
        }, Wait);
        Assert.True(new SettingsStore(data.Root).ReadMailDigest());
        Assert.Equal(0, data.Outlook.MailCalls);
    }

    // 286: without classic Outlook there is no Mail digest checkbox either.
    [Fact]
    public void SettingsPanel_NotAvailable_NoMailDigestCheckbox()
    {
        data.Outlook.Available = OutlookAvailability.NotInstalled;
        data.CreateState();
        IRenderedComponent<SettingsPanel> cut = data.Bunit.Render<SettingsPanel>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal), Wait);

        Assert.Empty(cut.FindAll("input.mail-digest-enabled"));
    }
}
