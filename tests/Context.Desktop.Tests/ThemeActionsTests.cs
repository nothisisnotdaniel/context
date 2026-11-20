using Bunit;

namespace Context.Tests;

// 341: the theme setting: DesktopActions.SetTheme, DesktopState.ThemeSetting and Main applying the saved theme.
public sealed class ThemeActionsTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string SettingsPath => Path.Combine(data.Root, SettingsStore.FileName);

    // 341 AC-4: each theme answers Done with its text, stores it, keeps the other properties and refreshes.
    [Theory]
    [InlineData(nameof(AppTheme.Light), "Theme: Light.", "light")]
    [InlineData(nameof(AppTheme.Dark), "Theme: Dark.", "dark")]
    [InlineData(nameof(AppTheme.System), "Theme: System.", "system")]
    public async Task SetTheme_Done_StoresIt_KeepsOthers_Refreshes(string themeName, string text, string stored)
    {
        AppTheme theme = Enum.Parse<AppTheme>(themeName);
        data.WriteFile(SettingsStore.FileName, "{\"outlook\":{\"enabled\":true},\"savedSearches\":[{\"name\":\"Work\",\"query\":\"#work\"}]}");
        await data.RefreshAsync();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.SetTheme(theme);

        Assert.Equal((ActionKind.Done, ActionReason.None, text), (result.Kind, result.Reason, result.Text));
        Assert.Equal(text, DesktopActions.ThemeText(theme));
        Assert.Equal(theme, new SettingsStore(data.Root).ReadTheme());
        Assert.Contains("\"theme\": \"" + stored + "\"", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
        Assert.True(new SettingsStore(data.Root).ReadOutlook());
        Assert.Single(new SettingsStore(data.Root).ReadSavedSearches());
        Assert.True(data.State.Builds > builds);
    }

    // 341 AC-4: a broken file is Failed, an over-64-KiB result is Refused, both with SettingsNotSavedText and nothing
    // written.
    [Theory]
    [InlineData("broken", nameof(ActionKind.Failed))]
    [InlineData("large", nameof(ActionKind.Refused))]
    public async Task SetTheme_NotSaved_RefusedOrFailed_Unchanged(string state, string kind)
    {
        data.WriteFile(
            SettingsStore.FileName,
            state == "broken" ? "{\"dayEnd\":{\"moved\":5}}" : "{\"padding\":\"" + new string('x', 65_516) + "\"}");
        byte[] before = File.ReadAllBytes(SettingsPath);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.SetTheme(AppTheme.Dark);

        Assert.Equal((kind, DesktopActions.SettingsNotSavedText), (result.Kind.ToString(), result.Text));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    // 341 AC-5: ThemeSetting never throws; a broken or unreadable file gives System.
    [Theory]
    [InlineData(null, nameof(AppTheme.System))]
    [InlineData("{\"appearance\":{\"theme\":\"dark\"}}", nameof(AppTheme.Dark))]
    [InlineData("{\"appearance\":{\"theme\":\"light\"}}", nameof(AppTheme.Light))]
    [InlineData("not json", nameof(AppTheme.System))]
    [InlineData("[1]", nameof(AppTheme.System))]
    public async Task ThemeSetting_SavedOrSystem(string? settings, string expected)
    {
        if (settings is not null)
        {
            data.WriteFile(SettingsStore.FileName, settings);
        }

        data.CreateState();

        Assert.Equal(Enum.Parse<AppTheme>(expected), await data.State!.ThemeSetting());
    }

    // 341 AC-5: a folder in place of settings.json can't be read and gives System.
    [Fact]
    public async Task ThemeSetting_Unreadable_IsSystem()
    {
        Directory.CreateDirectory(SettingsPath);
        data.CreateState();

        Assert.Equal(AppTheme.System, await data.State!.ThemeSetting());
    }

    // 341 AC-5: Main's first render calls setTheme once with the saved theme; a broken or unreadable file gives
    // "system".
    [Theory]
    [InlineData("dark", "dark")]
    [InlineData("light", "light")]
    [InlineData("broken", "system")]
    [InlineData("folder", "system")]
    [InlineData("none", "system")]
    public async Task Main_FirstRender_AppliesSavedTheme(string state, string expected)
    {
        data.WriteNote("Plan", "# Plan\n");
        if (state == "folder")
        {
            Directory.CreateDirectory(SettingsPath);
        }
        else if (state == "broken")
        {
            data.WriteFile(SettingsStore.FileName, "{\"appearance\":");
        }
        else if (state != "none")
        {
            data.WriteFile(SettingsStore.FileName, "{\"appearance\":{\"theme\":\"" + state + "\"}}");
        }

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.True(await data.ThemeSet.WaitAsync(Wait));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal), Wait);
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        await cut.InvokeAsync(() => { });
        Assert.Equal([expected], data.Themes());
    }
}
