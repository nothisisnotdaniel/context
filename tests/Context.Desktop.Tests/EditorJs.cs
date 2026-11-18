using Bunit;
using Microsoft.JSInterop;

namespace Context.Tests;

/// <summary>
/// Plans bunit's strict JS interop for the bundled editor (<c>ContextEditor.*</c>): mount, focus, markClean,
/// insertAttachment and destroy do nothing; headings returns <c>[]</c> (or what <see cref="SetHeadings"/> sets) and
/// scrollToHeading true; isLossless, isDirty and getMarkdown return what the test sets. markClean
/// makes the editor clean; markCleanIf does so only when its Markdown is the editor's current one, and returns that.
/// </summary>
internal sealed class EditorJs
{
    private readonly BunitJSInterop js;

    public EditorJs(BunitContext context, bool lossless = true, bool dirty = false, string markdown = "")
    {
        js = context.JSInterop;
        js.SetupVoid("ContextEditor.mount", _ => true).SetVoidResult();
        js.SetupVoid("ContextEditor.focus", _ => true).SetVoidResult();
        js.SetupVoid("ContextEditor.markClean", _ => MarkClean()).SetVoidResult();
        js.Setup<bool>("ContextEditor.markCleanIf", i => CleanIf(i)).SetResult(true);
        js.Setup<bool>("ContextEditor.markCleanIf", i => !Same(i)).SetResult(false);
        js.SetupVoid("ContextEditor.destroy", _ => true).SetVoidResult();
        js.SetupVoid("ContextEditor.insertAttachment", _ => true).SetVoidResult();
        Lossless = lossless;
        Dirty = dirty;
        Markdown = markdown;
        js.Setup<bool>("ContextEditor.isLossless", _ => true).SetResult(lossless);
        js.Setup<bool>("ContextEditor.isDirty", _ => Dirty).SetResult(true);
        js.Setup<bool>("ContextEditor.isDirty", _ => !Dirty).SetResult(false);
        js.Setup<string>("ContextEditor.getMarkdown", _ => true).SetResult(markdown);
        js.Setup<string>("ContextEditor.headings", _ => true).SetResult("[]");
        js.Setup<bool>("ContextEditor.scrollToHeading", _ => true).SetResult(true);
    }

    /// <summary>Makes every later <c>ContextEditor.headings</c> call return <paramref name="json"/>.</summary>
    public void SetHeadings(string json) => js.Setup<string>("ContextEditor.headings", _ => true).SetResult(json);

    /// <summary>The heading indexes passed to <c>ContextEditor.scrollToHeading</c> so far, in order.</summary>
    public IReadOnlyList<int> Jumps() => Calls("ContextEditor.scrollToHeading").Select(c => (int)c.Arguments[1]!).ToList();

    public bool Lossless { get; }

    public bool Dirty { get; private set; }

    public string Markdown { get; private set; }

    /// <summary>Makes the editor report <paramref name="markdown"/> as an edit (isDirty true).</summary>
    public void Edit(string markdown)
    {
        Markdown = markdown;
        js.Setup<string>("ContextEditor.getMarkdown", _ => true).SetResult(markdown);
        Dirty = true;
    }

    /// <summary>
    /// Makes the editor report an edit that no save can write: <paramref name="markdown"/> padded past
    /// <see cref="DesktopActions.MaxNoteBytes"/>, so the save-first before leaving the note (job 203) is refused as
    /// too large and the <c>Discard changes</c> and <c>Save &lt;name&gt; first.</c> paths show as before.
    /// </summary>
    public void EditUnsavable(string markdown) => Edit(markdown + new string('x', (int)DesktopActions.MaxNoteBytes + 1));

    /// <summary>
    /// Types <paramref name="markdown"/> into the editor of <paramref name="cut"/>: the edit (<see cref="Edit"/>), the
    /// page's dirty report and the <c>OnChanged</c> call that every change of the text makes.
    /// </summary>
    public async Task Change<T>(IRenderedComponent<T> cut, string markdown)
        where T : Microsoft.AspNetCore.Components.IComponent
    {
        Edit(markdown);
        EditorInterop page = Page();
        await cut.InvokeAsync(async () =>
        {
            await page.OnDirty(true);
            await page.OnChanged();
        });
    }

    /// <summary>Makes every later <paramref name="identifier"/> call (<c>ContextEditor.mount</c>, <c>focus</c> or
    /// <c>isLossless</c>) fail with <paramref name="exception"/>.</summary>
    public void Fail(string identifier, Exception exception)
    {
        if (identifier == "ContextEditor.isLossless" || identifier == "ContextEditor.scrollToHeading")
        {
            js.Setup<bool>(identifier, _ => true).SetException(exception);
        }
        else if (identifier == "ContextEditor.headings")
        {
            js.Setup<string>(identifier, _ => true).SetException(exception);
        }
        else
        {
            js.SetupVoid(identifier, _ => true).SetException(exception);
        }
    }

    private bool MarkClean()
    {
        Dirty = false;
        return true;
    }

    private bool Same(JSRuntimeInvocation call) => string.Equals((string?)call.Arguments[1], Markdown, StringComparison.Ordinal);

    private bool CleanIf(JSRuntimeInvocation call)
    {
        if (!Same(call))
        {
            return false;
        }

        Dirty = false;
        return true;
    }

    /// <summary>The calls of <paramref name="identifier"/> so far.</summary>
    public IReadOnlyList<JSRuntimeInvocation> Calls(string identifier) =>
        js.Invocations.Where(i => i.Identifier == identifier).ToList();

    /// <summary>The markdown passed to the latest mount.</summary>
    public string MountedText() => (string)Calls("ContextEditor.mount")[^1].Arguments[1]!;

    /// <summary>The inline images passed to the latest mount (its 4th argument).</summary>
    public IReadOnlyList<InlineImage> MountedImages() =>
        (IReadOnlyList<InlineImage>)Calls("ContextEditor.mount")[^1].Arguments[3]!;

    /// <summary>The note name passed to the latest mount (its 5th argument).</summary>
    public string MountedName() => (string)Calls("ContextEditor.mount")[^1].Arguments[4]!;

    /// <summary>The arguments of the latest isLossless call.</summary>
    public IReadOnlyList<object?> LosslessArguments() => Calls("ContextEditor.isLossless")[^1].Arguments;

    /// <summary>The .NET object the latest mount handed to the page (what the editor calls back).</summary>
    public EditorInterop Page() =>
        ((DotNetObjectReference<EditorInterop>)Calls("ContextEditor.mount")[^1].Arguments[2]!).Value;
}
