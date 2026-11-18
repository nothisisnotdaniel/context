namespace Context.Tests;

// 217 AC-2, AC-6: turning Outlook on and off in settings.json through DesktopActions.
public sealed class OutlookSettingsActionsTests : IDisposable
{
    private readonly DesktopFixture data = new();

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
        await state.RecentMail();
        Assert.Equal((1, 1), (data.Outlook.AppointmentCalls, data.Outlook.MailCalls));

        ActionResult result = await data.Actions!.SetOutlookEnabled(true);
        Assert.Equal(ActionKind.Done, result.Kind);
        int appointmentCalls = data.Outlook.AppointmentCalls;
        await state.DayAppointments(DesktopFixture.Today);
        await state.RecentMail();

        // The refresh after the action has already read today's appointments anew; the mail is read anew too.
        Assert.True(appointmentCalls >= 2);
        Assert.Equal(appointmentCalls, data.Outlook.AppointmentCalls);
        Assert.Equal(2, data.Outlook.MailCalls);
    }
}
