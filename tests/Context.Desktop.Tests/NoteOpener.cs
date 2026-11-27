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
        await StaleClick.KeyDownAsync(cut, ".layout", new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await StaleClick.InputAsync(cut, ".palette-input", new ChangeEventArgs { Value = "/open " + name });
        await StaleClick.KeyDownAsync(cut, ".palette-input", new KeyboardEventArgs { Key = "Enter" });
    }
}
