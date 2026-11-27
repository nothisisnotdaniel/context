using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 490: Main's resizable and collapsible sidebars: the first render, the toggles, the separators' pointer drag and
// keyboard, and the status line when a save fails.
public sealed class SidebarLayoutUiTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();
    private int saves;

    public void Dispose() => data.Dispose();

    private string SettingsPath => Path.Combine(data.Root, SettingsStore.FileName);

    private static string Layout(bool leftCollapsed, bool rightCollapsed) =>
        "{\"sidebars\":{\"left\":{\"width\":300,\"collapsed\":" + (leftCollapsed ? "true" : "false")
        + "},\"right\":{\"width\":360,\"collapsed\":" + (rightCollapsed ? "true" : "false") + "}}}";

    private SidebarLayout Saved() => new SettingsStore(data.Root).ReadSidebars();

    // Renders Main and waits until the saved layout is applied and every load the page starts by itself has rendered;
    // from then on, every DesktopActions write is counted.
    private async Task<IRenderedComponent<Main>> RenderSettledAsync()
    {
        data.WriteNote("Plan", "# Plan\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.True(await data.SidebarsSet.WaitAsync(Wait));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal), Wait);
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        await cut.InvokeAsync(() => { });
        data.Actions!.BeforeWrite = () => Interlocked.Increment(ref saves);
        return cut;
    }

    private static Task PointerAsync(IRenderedComponent<Main> cut, string selector, string eventName, PointerEventArgs e) =>
        cut.Find(selector).TriggerEventAsync(eventName, e);

    private static PointerEventArgs Press(double x) => new() { Button = 0, Buttons = 1, ClientX = x };

    private static PointerEventArgs Move(double x) => new() { Buttons = 1, ClientX = x };

    // 490 AC-3: the first render applies the saved widths and the collapsed classes exactly for the saved sides.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task FirstRender_AppliesSavedLayout(bool leftCollapsed, bool rightCollapsed)
    {
        data.WriteFile(SettingsStore.FileName, Layout(leftCollapsed, rightCollapsed));

        IRenderedComponent<Main> cut = await RenderSettledAsync();

        cut.WaitForAssertion(() => Assert.Equal(leftCollapsed, cut.Find(".layout").ClassList.Contains("left-collapsed")), Wait);
        IElement layout = cut.Find(".layout");
        Assert.Equal(rightCollapsed, layout.ClassList.Contains("right-collapsed"));
        Assert.False(layout.ClassList.Contains("resizing"));
        Assert.Equal([(300, 360)], data.Sidebars());
        Assert.Equal(leftCollapsed, cut.FindAll(".left-resizer").Count == 0);
        Assert.Equal(rightCollapsed, cut.FindAll(".right-resizer").Count == 0);
        Assert.Equal(0, saves);
    }

    // 490 AC-3, AC-8: no file, a broken file or a folder in its place gives the default layout and no error.
    [Theory]
    [InlineData("none")]
    [InlineData("broken")]
    [InlineData("folder")]
    public async Task FirstRender_NoOrBrokenSettings_IsDefault_NoError(string state)
    {
        if (state == "broken")
        {
            data.WriteFile(SettingsStore.FileName, "{\"sidebars\":");
        }
        else if (state == "folder")
        {
            Directory.CreateDirectory(SettingsPath);
        }

        IRenderedComponent<Main> cut = await RenderSettledAsync();

        Assert.Equal([(240, 280)], data.Sidebars());
        Assert.Equal("layout", cut.Find(".layout").ClassName);
        Assert.Equal(string.Empty, cut.Find(".main-status").TextContent);
        Assert.Equal("240", cut.Find(".left-resizer").GetAttribute("aria-valuenow"));
        Assert.Equal("280", cut.Find(".right-resizer").GetAttribute("aria-valuenow"));
    }

    // 490 AC-4: each toggle is an icon-only button with its labels, aria-expanded and aria-controls.
    [Fact]
    public async Task Toggles_HaveLabelsAndControls()
    {
        IRenderedComponent<Main> cut = await RenderSettledAsync();

        foreach ((string side, string region, string regionClass) in new[] { ("left", "left-sidebar", "notes-region"), ("right", "right-sidebar", "todos-region") })
        {
            IElement toggle = cut.Find($"button.sidebar-toggle.{side}-toggle");
            Assert.Equal("button", toggle.GetAttribute("type"));
            Assert.Equal($"Collapse {side} sidebar", toggle.GetAttribute("aria-label"));
            Assert.Equal($"Collapse {side} sidebar", toggle.GetAttribute("title"));
            Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
            Assert.Equal(region, toggle.GetAttribute("aria-controls"));
            Assert.Equal(string.Empty, toggle.TextContent.Trim());
            Assert.NotNull(toggle.QuerySelector("svg[aria-hidden='true']"));
            Assert.Equal(regionClass, cut.Find("#" + region).ClassName);
        }
    }

    // 490 AC-4: a click collapses the side: only the toggle shows, the panels stay mounted under hidden, the separator
    // goes; the layout is saved. A second click expands it again.
    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public async Task Toggle_Click_CollapsesAndSaves_ThenExpands(string side)
    {
        IRenderedComponent<Main> cut = await RenderSettledAsync();
        string region = side == "left" ? ".notes-region" : ".todos-region";
        string panel = side == "left" ? "NotesPanel" : "TodoPanel";
        int panelsBefore = side == "left" ? cut.FindComponents<NotesPanel>().Count : cut.FindComponents<TodoPanel>().Count;

        await cut.Find($".{side}-toggle").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.True(cut.Find(".layout").ClassList.Contains($"{side}-collapsed")), Wait);
        IElement toggle = cut.Find($".{side}-toggle");
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Equal($"Expand {side} sidebar", toggle.GetAttribute("aria-label"));
        Assert.Equal($"Expand {side} sidebar", toggle.GetAttribute("title"));
        Assert.True(cut.Find($"{region} .sidebar-panels").HasAttribute("hidden"));
        Assert.Empty(cut.FindAll($".{side}-resizer"));
        Assert.Equal(panelsBefore, side == "left" ? cut.FindComponents<NotesPanel>().Count : cut.FindComponents<TodoPanel>().Count);
        Assert.True(panelsBefore > 0, panel);
        Assert.True(Saved().Collapsed(side == "left" ? SidebarSide.Left : SidebarSide.Right));
        Assert.Equal(1, saves);

        await cut.Find($".{side}-toggle").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.False(cut.Find(".layout").ClassList.Contains($"{side}-collapsed")), Wait);
        Assert.False(cut.Find($"{region} .sidebar-panels").HasAttribute("hidden"));
        Assert.Single(cut.FindAll($".{side}-resizer"));
        Assert.Equal(SidebarLayout.Default, Saved());
        Assert.Equal(2, saves);
        Assert.Equal([(240, 280)], data.Sidebars());
    }

    // 490 AC-5: each separator has its role, orientation, label, tab stop and values.
    [Fact]
    public async Task Separators_HaveTheirAria()
    {
        data.WriteFile(SettingsStore.FileName, Layout(false, false));
        IRenderedComponent<Main> cut = await RenderSettledAsync();

        foreach ((string side, string now, string min, string max) in new[] { ("left", "300", "160", "480"), ("right", "360", "200", "520") })
        {
            IElement resizer = cut.Find($"div.sidebar-resizer.{side}-resizer");
            Assert.Equal("separator", resizer.GetAttribute("role"));
            Assert.Equal("vertical", resizer.GetAttribute("aria-orientation"));
            Assert.Equal($"Resize {side} sidebar", resizer.GetAttribute("aria-label"));
            Assert.Equal("0", resizer.GetAttribute("tabindex"));
            Assert.Equal((now, min, max), (resizer.GetAttribute("aria-valuenow"), resizer.GetAttribute("aria-valuemin"), resizer.GetAttribute("aria-valuemax")));
        }
    }

    // 490 AC-5: a drag sets the width from the start on each move (clamped; + for left, - for right), marks the
    // layout as resizing and saves once on the release.
    [Theory]
    [InlineData("left", "onpointerup", 240 + 50, 280)]
    [InlineData("right", "onpointerup", 240, 280 + 50)]
    [InlineData("left", "onpointercancel", 240 + 50, 280)]
    public async Task Drag_MovesEdge_SavesOnceOnRelease(string side, string end, int left, int right)
    {
        IRenderedComponent<Main> cut = await RenderSettledAsync();
        double sign = side == "left" ? 1 : -1;

        await PointerAsync(cut, $".{side}-resizer", "onpointerdown", Press(500));
        Assert.True(cut.Find(".layout").ClassList.Contains("resizing"));
        await PointerAsync(cut, ".layout", "onpointermove", Move(500 + (sign * 20)));
        await PointerAsync(cut, ".layout", "onpointermove", Move(500 + (sign * 2000)));
        await PointerAsync(cut, ".layout", "onpointermove", Move(500 + (sign * 50)));
        Assert.Equal(0, saves);
        await PointerAsync(cut, ".layout", end, new PointerEventArgs { ClientX = 500 + (sign * 50) });

        cut.WaitForAssertion(() => Assert.False(cut.Find(".layout").ClassList.Contains("resizing")), Wait);
        int max = side == "left" ? 480 : 520;
        (int, int) clamped = side == "left" ? (max, 280) : (240, max);
        (int, int) first = side == "left" ? (260, 280) : (240, 300);
        Assert.Equal([(240, 280), first, clamped, (left, right)], data.Sidebars());
        Assert.Equal(new SidebarLayout(left, false, right, false), Saved());
        Assert.Equal(1, saves);
        Assert.Equal((side == "left" ? left : right).ToString(System.Globalization.CultureInfo.InvariantCulture), cut.Find($".{side}-resizer").GetAttribute("aria-valuenow"));
    }

    // 490 AC-5: only a primary-button press starts a drag; a drag with no net change saves nothing; moves without a
    // drag do nothing.
    [Fact]
    public async Task Drag_NoNetChangeOrSecondaryButton_SavesNothing()
    {
        IRenderedComponent<Main> cut = await RenderSettledAsync();

        await PointerAsync(cut, ".left-resizer", "onpointerdown", new PointerEventArgs { Button = 2, Buttons = 2, ClientX = 500 });
        Assert.False(cut.Find(".layout").ClassList.Contains("resizing"));
        await PointerAsync(cut, ".left-resizer", "onpointerdown", Press(500));
        await PointerAsync(cut, ".layout", "onpointermove", Move(540));
        await PointerAsync(cut, ".layout", "onpointermove", Move(500));
        await PointerAsync(cut, ".layout", "onpointerup", new PointerEventArgs { ClientX = 500 });

        cut.WaitForAssertion(() => Assert.False(cut.Find(".layout").ClassList.Contains("resizing")), Wait);
        Assert.Equal(0, saves);
        Assert.Equal([(240, 280), (280, 280), (240, 280)], data.Sidebars());
        Assert.False(File.Exists(SettingsPath));
    }

    // 490 AC-5 (reviewer note): a release outside the window doesn't leave the drag stuck: the next move with no button
    // pressed ends it and saves the layout once.
    [Fact]
    public async Task Drag_ReleasedOutside_EndsOnNextMove()
    {
        IRenderedComponent<Main> cut = await RenderSettledAsync();

        await PointerAsync(cut, ".left-resizer", "onpointerdown", Press(500));
        await PointerAsync(cut, ".layout", "onpointermove", Move(532));
        await PointerAsync(cut, ".layout", "onpointermove", new PointerEventArgs { Buttons = 0, ClientX = 700 });

        cut.WaitForAssertion(() => Assert.False(cut.Find(".layout").ClassList.Contains("resizing")), Wait);
        Assert.Equal(new SidebarLayout(272, false, 280, false), Saved());
        Assert.Equal(1, saves);
        Assert.Equal([(240, 280), (272, 280)], data.Sidebars());
    }

    // 490 AC-6: arrows move the edge by Step (ArrowRight widens left and narrows right), Home/End go to the bounds;
    // each change saves once and calls setSidebars; a key at a bound saves nothing.
    [Theory]
    [InlineData("left", "ArrowRight", 256, 280)]
    [InlineData("left", "ArrowLeft", 224, 280)]
    [InlineData("left", "Home", 160, 280)]
    [InlineData("left", "End", 480, 280)]
    [InlineData("right", "ArrowRight", 240, 264)]
    [InlineData("right", "ArrowLeft", 240, 296)]
    [InlineData("right", "Home", 240, 200)]
    [InlineData("right", "End", 240, 520)]
    public async Task Keys_MoveTheEdge_SaveOnce(string side, string key, int left, int right)
    {
        IRenderedComponent<Main> cut = await RenderSettledAsync();

        await cut.Find($".{side}-resizer").KeyDownAsync(new KeyboardEventArgs { Key = key });

        cut.WaitForAssertion(() => Assert.Equal(1, saves), Wait);
        Assert.Equal(new SidebarLayout(left, false, right, false), Saved());
        Assert.Equal([(240, 280), (left, right)], data.Sidebars());
        Assert.Equal((side == "left" ? left : right).ToString(System.Globalization.CultureInfo.InvariantCulture), cut.Find($".{side}-resizer").GetAttribute("aria-valuenow"));

        // The same key again at a bound (Home/End) changes nothing and saves nothing.
        if (key is "Home" or "End")
        {
            await cut.Find($".{side}-resizer").KeyDownAsync(new KeyboardEventArgs { Key = key });
            await cut.InvokeAsync(() => { });
            Assert.Equal(1, saves);
            Assert.Equal(2, data.Sidebars().Count);
        }
    }

    // 490 AC-6: Enter collapses the side and focuses its toggle; other keys do nothing.
    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public async Task Enter_Collapses_FocusesToggle(string side)
    {
        IRenderedComponent<Main> cut = await RenderSettledAsync();
        await cut.Find($".{side}-resizer").KeyDownAsync(new KeyboardEventArgs { Key = "a" });
        Assert.Equal(0, saves);

        await cut.Find($".{side}-resizer").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.True(cut.Find(".layout").ClassList.Contains($"{side}-collapsed")), Wait);
        Assert.Equal(1, saves);
        Assert.True(Saved().Collapsed(side == "left" ? SidebarSide.Left : SidebarSide.Right));
        // The element reference id is only in the markup of the render that created the element, so the toggle's
        // reference is read from Main itself.
        ElementReference toggle = (ElementReference)typeof(Main)
            .GetField(side + "Toggle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        JSRuntimeInvocation focus = data.Bunit.JSInterop.Invocations.Last(i => i.Identifier.EndsWith(".focus", StringComparison.Ordinal));
        Assert.Equal(toggle.Id, ((ElementReference)focus.Arguments[0]!).Id);
    }

    // 490 AC-7: a failed or refused save shows the red status line and keeps the layout for the session; the next
    // Done save shows no status.
    [Theory]
    [InlineData("broken")]
    [InlineData("large")]
    public async Task SaveFails_ShowsRedStatus_KeepsLayout(string state)
    {
        data.WriteFile(
            SettingsStore.FileName,
            state == "broken" ? "{\"dayEnd\":{\"moved\":5}}" : "{\"padding\":\"" + new string('x', 65_516) + "\"}");
        byte[] before = File.ReadAllBytes(SettingsPath);
        IRenderedComponent<Main> cut = await RenderSettledAsync();

        await cut.Find(".left-toggle").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Can't save the sidebar layout.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.True(cut.Find(".layout").ClassList.Contains("left-collapsed"));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));

        File.WriteAllText(SettingsPath, "{}");
        await cut.Find(".left-toggle").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(string.Empty, cut.Find(".main-status").TextContent), Wait);
        Assert.False(cut.Find(".layout").ClassList.Contains("left-collapsed"));
        Assert.Equal(SidebarLayout.Default, Saved());
    }

    // 490 AC-7: a Done save shows no status.
    [Fact]
    public async Task SaveDone_ShowsNoStatus()
    {
        IRenderedComponent<Main> cut = await RenderSettledAsync();

        await cut.Find(".right-toggle").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(1, saves), Wait);
        Assert.Equal(string.Empty, cut.Find(".main-status").TextContent);
    }
}
