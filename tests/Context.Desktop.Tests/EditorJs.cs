using Bunit;
using Microsoft.JSInterop;

namespace Context.Tests;

/// <summary>
/// Plans bunit's strict JS interop for the bundled editor (<c>ContextEditor.*</c>): mount, focus, markClean,
/// insertAttachment and destroy do nothing; headings returns <c>[]</c> (or what <see cref="SetHeadings"/> sets) and
/// scrollToHeading true; isDirty and getMarkdown return what the test sets; isLossless (no longer called by
/// the desktop since job 210) returns true, so a test can check it is never asked. markClean
/// makes the editor clean; markCleanIf does so only when its Markdown is the editor's current one, and returns that.
/// attributes returns <c>[]</c> (or what <see cref="SetAttributeRows"/> sets); setAttributes makes the editor dirty and
/// returns true. setLinkTitle (job 244) returns true and is recorded (<see cref="LinkTitles"/>).
/// </summary>
internal sealed class EditorJs
{
    private readonly BunitJSInterop js;

    public EditorJs(BunitContext context, bool dirty = false, string markdown = "")
    {
        js = context.JSInterop;
        js.SetupVoid("ContextEditor.mount", _ => true).SetVoidResult();
        js.SetupVoid("ContextEditor.focus", _ => true).SetVoidResult();
        js.SetupVoid("ContextEditor.toggleInlineCode", _ => true).SetVoidResult();
        js.SetupVoid("ContextEditor.toggleCodeBlock", _ => true).SetVoidResult();
        js.SetupVoid("ContextEditor.markClean", _ => MarkClean()).SetVoidResult();
        js.Setup<bool>("ContextEditor.markCleanIf", i => CleanIf(i)).SetResult(true);
        js.Setup<bool>("ContextEditor.markCleanIf", i => !Same(i)).SetResult(false);
        js.SetupVoid("ContextEditor.destroy", _ => true).SetVoidResult();
        js.SetupVoid("ContextEditor.insertAttachment", _ => true).SetVoidResult();
        Dirty = dirty;
        Markdown = markdown;
        js.Setup<bool>("ContextEditor.isLossless", _ => true).SetResult(true);
        js.Setup<bool>("ContextEditor.isDirty", _ => Dirty).SetResult(true);
        js.Setup<bool>("ContextEditor.isDirty", _ => !Dirty).SetResult(false);
        js.Setup<string>("ContextEditor.getMarkdown", _ => true).SetResult(markdown);
        js.Setup<string>("ContextEditor.headings", _ => true).SetResult("[]");
        js.Setup<bool>("ContextEditor.scrollToHeading", _ => true).SetResult(true);
        js.Setup<string>("ContextEditor.attributes", _ => true).SetResult("[]");
        js.Setup<bool>("ContextEditor.setAttributes", _ => SetAttributes()).SetResult(true);
        js.Setup<bool>("ContextEditor.setLinkTitle", _ => true).SetResult(true);
        js.Setup<bool>("ContextEditor.applyDecisionEdits", i => ApplyDecisionEdits(i)).SetResult(true);
        js.Setup<bool>("ContextEditor.applyDecisionEdits", _ => TypedDuringSave).SetResult(false);
        js.Setup<string>("ContextEditor.decisionReferencesIn", _ => true).SetResult("[]");
        js.Setup<bool>("ContextEditor.setDecisionTitles", _ => true).SetResult(true);
        js.Setup<bool>("ContextEditor.scrollToDecision", _ => true).SetResult(false);
    }

    /// <summary>
    /// Job 343: when true, <c>ContextEditor.applyDecisionEdits</c> answers false (text was typed during the save, so
    /// the page stays dirty); else it takes the saved text as the editor's, makes the editor clean and answers true.
    /// </summary>
    public bool TypedDuringSave { get; set; }

    private bool ApplyDecisionEdits(JSRuntimeInvocation call)
    {
        if (TypedDuringSave)
        {
            return false;
        }

        Markdown = (string)call.Arguments[2]!;
        js.Setup<string>("ContextEditor.getMarkdown", _ => true).SetResult(Markdown);
        Dirty = false;
        return true;
    }

    /// <summary>Makes every later <c>ContextEditor.decisionReferencesIn</c> call return <paramref name="json"/>.</summary>
    public void SetDecisionReferences(string json) =>
        js.Setup<string>("ContextEditor.decisionReferencesIn", _ => true).SetResult(json);

    /// <summary>Makes every later <c>ContextEditor.scrollToDecision</c> call for <paramref name="number"/> answer true.</summary>
    public void HasDecision(int number) =>
        js.Setup<bool>("ContextEditor.scrollToDecision", i => (int)i.Arguments[1]! == number).SetResult(true);

    /// <summary>The (edits JSON, saved text) pairs passed to <c>ContextEditor.applyDecisionEdits</c> so far.</summary>
    public IReadOnlyList<(string Edits, string Saved)> DecisionEdits() =>
        Calls("ContextEditor.applyDecisionEdits").Select(c => ((string)c.Arguments[1]!, (string)c.Arguments[2]!)).ToList();

    /// <summary>The titles JSON passed to <c>ContextEditor.setDecisionTitles</c> so far.</summary>
    public IReadOnlyList<string> DecisionTitles() =>
        Calls("ContextEditor.setDecisionTitles").Select(c => (string)c.Arguments[1]!).ToList();

    /// <summary>The numbers passed to <c>ContextEditor.scrollToDecision</c> so far.</summary>
    public IReadOnlyList<int> DecisionJumps() =>
        Calls("ContextEditor.scrollToDecision").Select(c => (int)c.Arguments[1]!).ToList();

    /// <summary>The (url, title) pairs passed to <c>ContextEditor.setLinkTitle</c> so far, in order.</summary>
    public IReadOnlyList<(string Url, string Title)> LinkTitles() =>
        Calls("ContextEditor.setLinkTitle").Select(c => ((string)c.Arguments[1]!, (string)c.Arguments[2]!)).ToList();

    /// <summary>Makes every later <c>ContextEditor.attributes</c> call return <paramref name="json"/>.</summary>
    public void SetAttributeRows(string json) => js.Setup<string>("ContextEditor.attributes", _ => true).SetResult(json);

    /// <summary>The edits JSON passed to <c>ContextEditor.setAttributes</c> so far, in order.</summary>
    public IReadOnlyList<string> AttributeEdits() =>
        Calls("ContextEditor.setAttributes").Select(c => (string)c.Arguments[1]!).ToList();

    // An applied attribute edit makes the editor dirty, like the page's setAttributes.
    private bool SetAttributes()
    {
        Dirty = true;
        return true;
    }

    /// <summary>Makes every later <c>ContextEditor.headings</c> call return <paramref name="json"/>.</summary>
    public void SetHeadings(string json) => js.Setup<string>("ContextEditor.headings", _ => true).SetResult(json);

    /// <summary>
    /// Holds every later <c>ContextEditor.headings</c> call until the returned plan's <c>SetResult</c> (a newer
    /// <see cref="SetHeadings"/> answers the calls after it); <c>Asked</c> completes once such a call came in.
    /// </summary>
    public (JSRuntimeInvocationHandler<string> Plan, Task Asked) HoldHeadings()
    {
        TaskCompletionSource asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        JSRuntimeInvocationHandler<string> plan = js.Setup<string>("ContextEditor.headings", _ =>
        {
            asked.TrySetResult();
            return true;
        });
        return (plan, asked.Task);
    }

    /// <summary>The heading indexes passed to <c>ContextEditor.scrollToHeading</c> so far, in order.</summary>
    public IReadOnlyList<int> Jumps() => Calls("ContextEditor.scrollToHeading").Select(c => (int)c.Arguments[1]!).ToList();

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

    /// <summary>Makes every later <paramref name="identifier"/> call (<c>ContextEditor.mount</c>, <c>focus</c>,
    /// <c>headings</c> or <c>scrollToHeading</c>) fail with <paramref name="exception"/>.</summary>
    public void Fail(string identifier, Exception exception)
    {
        if (identifier == "ContextEditor.scrollToHeading")
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

    /// <summary>The .NET object the latest mount handed to the page (what the editor calls back).</summary>
    public EditorInterop Page() =>
        ((DotNetObjectReference<EditorInterop>)Calls("ContextEditor.mount")[^1].Arguments[2]!).Value;
}
