using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

public sealed class ConfirmDialogTests : IDisposable
{
    private readonly BunitContext context = new();
    private readonly List<Confirmation> confirmed = [];
    private int cancelled;

    public void Dispose() => context.Dispose();

    // AC-6: an in-window modal dialog with the one-line question, a confirm button and Cancel; focus on Cancel.
    [Fact]
    public void Render_DialogWithQuestionAndButtons_FocusOnCancel()
    {
        IRenderedComponent<ConfirmDialog> cut = Render("Mark this todo done? a\nb <b>c</b>\u0001");

        IElement dialog = cut.Find("[role=dialog]");
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Equal("Mark this todo done? a b <b>c</b>�", cut.Find(".question").TextContent);
        Assert.Empty(cut.FindAll(".question b"));
        IReadOnlyList<IElement> buttons = cut.FindAll("button");
        Assert.Equal(["Mark done", "Cancel"], buttons.Select(b => b.TextContent).ToArray());
        Assert.All(buttons, b => Assert.Equal("button", b.GetAttribute("type")));
        JSRuntimeInvocation focus = context.JSInterop.VerifyFocusAsyncInvoke();
        ElementReference target = Assert.IsType<ElementReference>(focus.Arguments[0]);
        Assert.Equal(ElementRefs.Id<ConfirmDialog, ConfirmDialog>(cut, "cancelButton"), target.Id);
    }

    // AC-6: confirm hands a confirmation to the parent; Cancel and Esc only cancel.
    [Fact]
    public void Buttons_ConfirmCancelEsc()
    {
        IRenderedComponent<ConfirmDialog> cut = Render("Sure?");

        StaleClick.Fire(() => cut.Find("button.cancel"), e => e.ClickAsync(new MouseEventArgs()));
        StaleClick.Fire(() => cut.Find("[role=dialog]"), e => e.KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" }));
        StaleClick.Fire(() => cut.Find("[role=dialog]"), e => e.KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" }));
        Assert.Equal(2, cancelled);
        Assert.Empty(confirmed);

        StaleClick.Fire(() => cut.Find("button.confirm"), e => e.ClickAsync(new MouseEventArgs()));
        Assert.Single(confirmed);
        Assert.Equal(2, cancelled);
    }

    // AC-6: while busy both buttons are disabled and Esc does nothing.
    [Fact]
    public void Busy_ButtonsDisabled_EscIgnored()
    {
        IRenderedComponent<ConfirmDialog> cut = Render("Sure?", busy: true);

        Assert.All(cut.FindAll("button"), b => Assert.True(b.HasAttribute("disabled")));
        StaleClick.Fire(() => cut.Find("[role=dialog]"), e => e.KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" }));
        Assert.Equal(0, cancelled);
        Assert.Empty(confirmed);
    }

    private IRenderedComponent<ConfirmDialog> Render(string question, bool busy = false) =>
        context.Render<ConfirmDialog>(p => p
            .Add(d => d.Question, question)
            .Add(d => d.ConfirmLabel, "Mark done")
            .Add(d => d.Busy, busy)
            .Add(d => d.OnConfirm, (Confirmation c) => confirmed.Add(c))
            .Add(d => d.OnCancel, () => cancelled++));
}
