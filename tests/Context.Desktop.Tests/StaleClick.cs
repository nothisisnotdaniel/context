using AngleSharp.Dom;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 363: a page that renders again by itself (a save, then OnThemeChanged, Load() or a DesktopState reload) gives its
// @onclick lambdas new handler IDs on every render. When such a render lands between Find and the dispatch, the click
// names a handler ID that no longer exists. bUnit throws UnknownEventHandlerIdException before any handler runs, so
// finding the element again and clicking once more can never apply a click twice.
// 491: the same holds for every event a test fires on an element it found from the test thread (input, change, key
// down, key up, drag and drop), so each goes through FireAsync, or Fire in a test that does not await (bUnit's
// synchronous Click(), Change() and KeyDown() drop the event's task, so a stale handler can make them do nothing). A test that finds and fires inside cut.InvokeAsync needs none of this:
// no render lands between the two on the dispatcher.
internal static class StaleClick
{
    private const int Attempts = 5;

    /// <summary>Finds <paramref name="selector"/> and clicks it, finding it again (at most 5 attempts in total) only
    /// while the click names a handler a newer render dropped.</summary>
    public static Task ClickAsync<T>(IRenderedComponent<T> cut, string selector)
        where T : IComponent =>
        FireAsync(cut, selector, e => e.ClickAsync(new MouseEventArgs()));

    /// <summary><see cref="ClickAsync{T}(IRenderedComponent{T}, string)"/> with the given mouse event.</summary>
    public static Task ClickAsync<T>(IRenderedComponent<T> cut, string selector, MouseEventArgs args)
        where T : IComponent =>
        FireAsync(cut, selector, e => e.ClickAsync(args));

    /// <summary>Fires <c>oninput</c> on <paramref name="selector"/>, as <see cref="FireAsync{T}"/>.</summary>
    public static Task InputAsync<T>(IRenderedComponent<T> cut, string selector, ChangeEventArgs args)
        where T : IComponent =>
        FireAsync(cut, selector, e => e.InputAsync(args));

    /// <summary>Fires <c>onchange</c> on <paramref name="selector"/>, as <see cref="FireAsync{T}"/>.</summary>
    public static Task ChangeAsync<T>(IRenderedComponent<T> cut, string selector, ChangeEventArgs args)
        where T : IComponent =>
        FireAsync(cut, selector, e => e.ChangeAsync(args));

    /// <summary>Fires <c>onkeydown</c> on <paramref name="selector"/>, as <see cref="FireAsync{T}"/>.</summary>
    public static Task KeyDownAsync<T>(IRenderedComponent<T> cut, string selector, KeyboardEventArgs args)
        where T : IComponent =>
        FireAsync(cut, selector, e => e.KeyDownAsync(args));

    /// <summary>Fires <c>onkeyup</c> on <paramref name="selector"/>, as <see cref="FireAsync{T}"/>.</summary>
    public static Task KeyUpAsync<T>(IRenderedComponent<T> cut, string selector, KeyboardEventArgs args)
        where T : IComponent =>
        FireAsync(cut, selector, e => e.KeyUpAsync(args));

    /// <summary>Finds <paramref name="selector"/> and fires the event on it, finding it again (at most 5 attempts in
    /// total) only while the event names a handler a newer render dropped.</summary>
    public static Task FireAsync<T>(IRenderedComponent<T> cut, string selector, Func<IElement, Task> fire)
        where T : IComponent =>
        FireAsync(cut, () => cut.Find(selector), fire);

    /// <summary>As <see cref="FireAsync{T}(IRenderedComponent{T}, string, Func{IElement, Task})"/>, for an element
    /// that <paramref name="find"/> picks from the current markup (a row of a FindAll, a helper's query).</summary>
    public static async Task FireAsync<T>(IRenderedComponent<T> cut, Func<IElement> find, Func<IElement, Task> fire)
        where T : IComponent
    {
        _ = cut;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await fire(find());
                return;
            }
            catch (UnknownEventHandlerIdException) when (attempt < Attempts)
            {
                // A render replaced the element's handler; find the element again.
            }
        }
    }

    /// <summary><see cref="FireAsync{T}(IRenderedComponent{T}, Func{IElement}, Func{IElement, Task})"/> for a test that
    /// does not await: it blocks until the event's handler finished, at most <see cref="BoundedWait.Dispatcher"/>. Use it
    /// instead of bUnit's synchronous <c>Click()</c>, <c>Change()</c> or <c>KeyDown()</c>: those drop the event's task, so
    /// when a load the page started holds the dispatcher, a stale handler's exception is lost and the event silently does
    /// nothing.</summary>
    public static void Fire(Func<IElement> find, Func<IElement, Task> fire) =>
        BoundedWait.Result(FireAndTell(find, fire), BoundedWait.Dispatcher, "an event fired through StaleClick.Fire");

    private static async Task<bool> FireAndTell(Func<IElement> find, Func<IElement, Task> fire)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await fire(find());
                return true;
            }
            catch (UnknownEventHandlerIdException) when (attempt < Attempts)
            {
                // A render replaced the element's handler; find the element again.
            }
        }
    }
}
