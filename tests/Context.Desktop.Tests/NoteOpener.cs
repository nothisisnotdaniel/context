using Bunit;
using Microsoft.AspNetCore.Components.Web;

using Microsoft.AspNetCore.Components;
namespace Context.Tests;

/// <summary>
/// Opens a note in <see cref="Main"/> by its full name through the palette's <c>/open</c>, the same
/// <c>RequestOpen</c> a sidebar click runs. Since job 240 the sidebar lists only recently opened notes, so a note
/// that was never opened has no row to click.
/// </summary>
internal static class NoteOpener
{
    public static async Task OpenAsync(IRenderedComponent<Main> cut, string name)
    {
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = "/open " + name });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    }
}
