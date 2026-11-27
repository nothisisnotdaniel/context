namespace Context.Tests;

// 490: the sidebar layout setting: DesktopActions.SetSidebars and DesktopState.SidebarSetting.
public sealed class SidebarActionsTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string SettingsPath => Path.Combine(data.Root, SettingsStore.FileName);

    // 490 AC-2, AC-7: a save answers Done with no text, stores the layout and keeps the other properties.
    [Fact]
    public async Task SetSidebars_Done_StoresIt_KeepsOthers()
    {
        data.WriteFile(SettingsStore.FileName, "{\"outlook\":{\"enabled\":true},\"appearance\":{\"theme\":\"dark\"}}");
        await data.RefreshAsync();
        SidebarLayout layout = new(320, false, 200, true);

        ActionResult result = await data.Actions!.SetSidebars(layout);

        Assert.Equal((ActionKind.Done, ActionReason.None, string.Empty), (result.Kind, result.Reason, result.Text));
        Assert.Equal(layout, new SettingsStore(data.Root).ReadSidebars());
        Assert.True(new SettingsStore(data.Root).ReadOutlook());
        Assert.Equal(AppTheme.Dark, new SettingsStore(data.Root).ReadTheme());
    }

    // 490 AC-7: a broken file is Failed, an over-64-KiB result and an out-of-range width are Refused, all with
    // SidebarsNotSavedText and nothing written.
    [Theory]
    [InlineData("broken", nameof(ActionKind.Failed))]
    [InlineData("large", nameof(ActionKind.Refused))]
    [InlineData("range", nameof(ActionKind.Refused))]
    public async Task SetSidebars_NotSaved_RefusedOrFailed_Unchanged(string state, string kind)
    {
        data.WriteFile(
            SettingsStore.FileName,
            state switch
            {
                "broken" => "{\"dayEnd\":{\"moved\":5}}",
                "large" => "{\"padding\":\"" + new string('x', 65_516) + "\"}",
                _ => "{}",
            });
        byte[] before = File.ReadAllBytes(SettingsPath);
        await data.RefreshAsync();
        SidebarLayout layout = state == "range" ? new SidebarLayout(100, false, 280, false) : SidebarLayout.Default;

        ActionResult result = await data.Actions!.SetSidebars(layout);

        Assert.Equal((kind, DesktopActions.SidebarsNotSavedText), (result.Kind.ToString(), result.Text));
        Assert.Equal("Can't save the sidebar layout.", DesktopActions.SidebarsNotSavedText);
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    // 490 AC-1, AC-8: SidebarSetting never throws; a missing, broken or unreadable file gives the default.
    [Theory]
    [InlineData(null, 240, false, 280, false)]
    [InlineData("{\"sidebars\":{\"left\":{\"width\":400,\"collapsed\":true},\"right\":{\"width\":200,\"collapsed\":false}}}", 400, true, 200, false)]
    [InlineData("not json", 240, false, 280, false)]
    [InlineData("[1]", 240, false, 280, false)]
    [InlineData("{\"sidebars\":", 240, false, 280, false)]
    public async Task SidebarSetting_SavedOrDefault(string? settings, int left, bool leftCollapsed, int right, bool rightCollapsed)
    {
        if (settings is not null)
        {
            data.WriteFile(SettingsStore.FileName, settings);
        }

        data.CreateState();

        Assert.Equal(new SidebarLayout(left, leftCollapsed, right, rightCollapsed), await data.State!.SidebarSetting());
    }

    // 490 AC-8: a folder in place of settings.json gives the default.
    [Fact]
    public async Task SidebarSetting_Folder_IsDefault()
    {
        Directory.CreateDirectory(SettingsPath);
        data.CreateState();

        Assert.Equal(SidebarLayout.Default, await data.State!.SidebarSetting());
    }
}
