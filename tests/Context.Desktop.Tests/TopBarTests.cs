using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 200: the top bar's More actions menu, rendered on its own (no files, no waits: every handler here is synchronous).
public sealed class TopBarTests : IDisposable
{
    private readonly BunitContext context = new();
    private readonly List<string> chosen = [];

    public TopBarTests()
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public void Dispose() => context.Dispose();

    private static IReadOnlyList<PageAction> Actions() =>
    [
        new PageAction("rename", "Rename"),
        new PageAction("history", "History", false),
        new PageAction("delete", "Delete"),
    ];

    private IRenderedComponent<TopBar> Render(IReadOnlyList<PageAction> actions, bool disabled = false) =>
        context.Render<TopBar>(p => p
            .Add(c => c.PageActions, actions)
            .Add(c => c.PageActionsDisabled, disabled)
            .Add(c => c.OnPageAction, EventCallback.Factory.Create<string>(this, id => chosen.Add(id))));

    private static void MenuKey(IRenderedComponent<TopBar> cut, string key) =>
        cut.Find(".page-menu").KeyDown(new KeyboardEventArgs { Key = key });

    private static string? Ref(IRenderedComponent<TopBar> cut, string selector)
    {
        string? id = cut.Find(selector).GetAttribute("blazor:elementReference");
        Assert.False(string.IsNullOrEmpty(id));
        return id;
    }

    private string? LastFocusId()
    {
        IReadOnlyList<JSRuntimeInvocation> calls = context.JSInterop.Invocations
            .Where(i => i.Identifier == "Blazor._internal.domWrapper.focus")
            .ToList();
        if (calls.Count == 0)
        {
            return null;
        }

        return Assert.IsType<ElementReference>(calls[^1].Arguments[0]).Id;
    }

    // AC-1: the … button is last, after Pick a day, closed; a click opens the menu.
    [Fact]
    public void Render_MenuButtonLast_ClickOpensMenu()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        IReadOnlyList<IElement> buttons = cut.FindAll(".top-bar-buttons > button");
        Assert.Equal(
            ["capture-open", "today-open", "day-pick-open", "page-menu-open"],
            buttons.Select(b => b.ClassName ?? string.Empty).ToArray());
        IElement open = buttons[^1];
        Assert.Equal("button", open.GetAttribute("type"));
        Assert.Equal("More actions", open.GetAttribute("aria-label"));
        Assert.Equal("More actions", open.GetAttribute("title"));
        Assert.Equal("menu", open.GetAttribute("aria-haspopup"));
        Assert.Equal("false", open.GetAttribute("aria-expanded"));
        Assert.False(open.HasAttribute("disabled"));
        Assert.Empty(cut.FindAll(".page-menu"));

        cut.Find("button.page-menu-open").Click();

        Assert.Equal("true", cut.Find("button.page-menu-open").GetAttribute("aria-expanded"));
        IElement menu = cut.Find("div.page-menu[role=menu]");
        Assert.Equal("More actions", menu.GetAttribute("aria-label"));
        IReadOnlyList<IElement> items = cut.FindAll(".page-menu button");
        Assert.Equal(["Rename", "History", "Delete"], items.Select(b => b.TextContent).ToArray());
        Assert.Equal(["rename", "history", "delete"], items.Select(b => b.ClassName ?? string.Empty).ToArray());
        Assert.Equal(["menuitem", "menuitemcheckbox", "menuitem"], items.Select(b => b.GetAttribute("role") ?? string.Empty).ToArray());
        Assert.Equal("false", items[1].GetAttribute("aria-checked"));
        Assert.All(items, b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.All(items, b => Assert.Equal("-1", b.GetAttribute("tabindex")));

        cut.Find("button.page-menu-open").Click();

        Assert.Empty(cut.FindAll(".page-menu"));
        Assert.Equal("false", cut.Find("button.page-menu-open").GetAttribute("aria-expanded"));
    }

    // AC-7: no actions, no … button.
    [Fact]
    public void Render_NoActions_NoMenuButton()
    {
        IRenderedComponent<TopBar> cut = Render([]);

        Assert.Empty(cut.FindAll("button.page-menu-open"));
        Assert.Equal(3, cut.FindAll(".top-bar-buttons > button").Count);
    }

    // AC-8: disabled while busy, and an open menu closes when the parent disables it.
    [Fact]
    public void Disabled_ButtonDisabled_OpenMenuHidden()
    {
        IRenderedComponent<TopBar> cut = Render(Actions(), disabled: true);
        Assert.True(cut.Find("button.page-menu-open").HasAttribute("disabled"));

        IRenderedComponent<TopBar> other = Render(Actions());
        other.Find("button.page-menu-open").Click();
        Assert.Single(other.FindAll(".page-menu"));
        other.Render(p => p.Add(c => c.PageActionsDisabled, true));

        Assert.Empty(other.FindAll(".page-menu"));
        Assert.True(other.Find("button.page-menu-open").HasAttribute("disabled"));
        Assert.Equal("false", other.Find("button.page-menu-open").GetAttribute("aria-expanded"));
        Assert.Empty(chosen);
    }

    // A menu hidden by an empty list or by disabling stays closed when the actions come back.
    [Fact]
    public void HiddenMenu_StaysClosed_WhenActionsReturn()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.page-menu-open").Click();
        cut.Render(p => p.Add(c => c.PageActions, []));
        Assert.Empty(cut.FindAll("button.page-menu-open"));
        cut.Render(p => p.Add(c => c.PageActions, Actions()));
        Assert.Empty(cut.FindAll(".page-menu"));
        Assert.Equal("false", cut.Find("button.page-menu-open").GetAttribute("aria-expanded"));

        cut.Find("button.page-menu-open").Click();
        cut.Render(p => p.Add(c => c.PageActionsDisabled, true));
        cut.Render(p => p.Add(c => c.PageActionsDisabled, false));
        Assert.Empty(cut.FindAll(".page-menu"));
        Assert.Empty(chosen);
    }

    // AC-9: opening focuses the first item; arrows move and wrap; Return runs the focused item and closes the menu.
    [Fact]
    public void Keys_ArrowsWrap_EnterRunsFocusedItem()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.page-menu-open").Click();
        // bUnit writes an element's reference id only into the render that created it, so read all three now.
        string? first = Ref(cut, ".page-menu button.rename");
        string? second = Ref(cut, ".page-menu button.history");
        string? last = Ref(cut, ".page-menu button.delete");
        Assert.Equal(first, LastFocusId());

        MenuKey(cut, "ArrowDown");
        Assert.Equal(second, LastFocusId());

        MenuKey(cut, "ArrowDown");
        MenuKey(cut, "ArrowDown");
        Assert.Equal(first, LastFocusId());

        MenuKey(cut, "ArrowUp");
        Assert.Equal(last, LastFocusId());
        Assert.Empty(chosen);

        MenuKey(cut, "Enter");

        Assert.Equal(["delete"], chosen);
        Assert.Empty(cut.FindAll(".page-menu"));
        Assert.Equal("false", cut.Find("button.page-menu-open").GetAttribute("aria-expanded"));
    }

    // AC-9: Esc closes the menu, runs nothing and gives the focus back to the … button.
    [Fact]
    public void Keys_Escape_ClosesAndFocusesMenuButton()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());
        string? button = Ref(cut, "button.page-menu-open");

        cut.Find("button.page-menu-open").Click();
        MenuKey(cut, "ArrowDown");
        MenuKey(cut, "Escape");

        Assert.Empty(cut.FindAll(".page-menu"));
        Assert.Empty(chosen);
        Assert.Equal(button, LastFocusId());
    }

    // OnPageAction receives the clicked item's id once; the menu closes.
    [Fact]
    public void Click_Item_RaisesItsIdOnce()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.page-menu-open").Click();
        cut.Find(".page-menu button.history").Click();

        Assert.Equal(["history"], chosen);
        Assert.Empty(cut.FindAll(".page-menu"));
    }

    // AC-10: the backdrop closes the menu and raises nothing.
    [Fact]
    public void Backdrop_ClosesWithoutRunning()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.page-menu-open").Click();
        cut.Find(".page-menu-backdrop").Click();

        Assert.Empty(cut.FindAll(".page-menu"));
        Assert.Empty(cut.FindAll(".page-menu-backdrop"));
        Assert.Empty(chosen);
        Assert.Equal("false", cut.Find("button.page-menu-open").GetAttribute("aria-expanded"));
    }

    // AC-10: at most one popover: the menu closes Capture and Pick a day, and each of those closes the menu.
    [Fact]
    public void Popovers_MutuallyExclusive()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.capture-open").Click();
        Assert.Equal(["capture"], Open(cut));
        cut.Find("button.page-menu-open").Click();
        Assert.Equal(["menu"], Open(cut));
        cut.Find("button.day-pick-open").Click();
        Assert.Equal(["day"], Open(cut));
        cut.Find("button.page-menu-open").Click();
        Assert.Equal(["menu"], Open(cut));
        cut.Find("button.capture-open").Click();
        Assert.Equal(["capture"], Open(cut));
        Assert.Equal("false", cut.Find("button.page-menu-open").GetAttribute("aria-expanded"));
        Assert.Empty(chosen);
    }

    private static string[] Open(IRenderedComponent<TopBar> cut)
    {
        List<string> open = [];
        if (cut.FindAll(".capture-pop").Count > 0)
        {
            open.Add("capture");
        }

        if (cut.FindAll(".day-pick").Count > 0)
        {
            open.Add("day");
        }

        if (cut.FindAll(".page-menu").Count > 0)
        {
            open.Add("menu");
        }

        return open.ToArray();
    }
}
