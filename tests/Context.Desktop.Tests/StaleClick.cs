using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 363: a page that renders again by itself (a save, then OnThemeChanged, Load() or a DesktopState reload) gives its
// @onclick lambdas new handler IDs on every render. When such a render lands between Find and the dispatch, the click
// names a handler ID that no longer exists. bUnit throws UnknownEventHandlerIdException before any handler runs, so
// finding the element again and clicking once more can never apply a click twice.
internal static class StaleClick
{
    private const int Attempts = 5;

    /// <summary>Finds <paramref name="selector"/> and clicks it, finding it again (at most 5 attempts in total) only
    /// while the click names a handler a newer render dropped.</summary>
    public static async Task ClickAsync<T>(IRenderedComponent<T> cut, string selector)
        where T : IComponent
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await cut.Find(selector).ClickAsync(new MouseEventArgs());
                return;
            }
            catch (UnknownEventHandlerIdException) when (attempt < Attempts)
            {
                // A render replaced the element's handler; find the element again.
            }
        }
    }
}
