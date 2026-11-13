using Bunit;
using Microsoft.JSInterop;

namespace Context.Tests;

/// <summary>
/// Plans bunit's strict JS interop for the bundled editor (<c>ContextEditor.*</c>): mount, focus, markClean and
/// destroy do nothing; isLossless, isDirty and getMarkdown return what the test sets.
/// </summary>
internal sealed class EditorJs
{
    private readonly BunitJSInterop js;

    public EditorJs(BunitContext context, bool lossless = true, bool dirty = false, string markdown = "")
    {
        js = context.JSInterop;
        js.SetupVoid("ContextEditor.mount", _ => true).SetVoidResult();
        js.SetupVoid("ContextEditor.focus", _ => true).SetVoidResult();
        js.SetupVoid("ContextEditor.markClean", _ => true).SetVoidResult();
        js.SetupVoid("ContextEditor.destroy", _ => true).SetVoidResult();
        Lossless = lossless;
        Dirty = dirty;
        Markdown = markdown;
        js.Setup<bool>("ContextEditor.isLossless", _ => true).SetResult(lossless);
        js.Setup<bool>("ContextEditor.isDirty", _ => true).SetResult(dirty);
        js.Setup<string>("ContextEditor.getMarkdown", _ => true).SetResult(markdown);
    }

    public bool Lossless { get; }

    public bool Dirty { get; private set; }

    public string Markdown { get; private set; }

    /// <summary>Makes the editor report <paramref name="markdown"/> as an edit (isDirty true).</summary>
    public void Edit(string markdown)
    {
        Dirty = true;
        Markdown = markdown;
        js.Setup<bool>("ContextEditor.isDirty", _ => true).SetResult(true);
        js.Setup<string>("ContextEditor.getMarkdown", _ => true).SetResult(markdown);
    }

    /// <summary>Makes every later <paramref name="identifier"/> call (<c>ContextEditor.mount</c>, <c>focus</c> or
    /// <c>isLossless</c>) fail with <paramref name="exception"/>.</summary>
    public void Fail(string identifier, Exception exception)
    {
        if (identifier == "ContextEditor.isLossless")
        {
            js.Setup<bool>(identifier, _ => true).SetException(exception);
        }
        else
        {
            js.SetupVoid(identifier, _ => true).SetException(exception);
        }
    }

    /// <summary>The calls of <paramref name="identifier"/> so far.</summary>
    public IReadOnlyList<JSRuntimeInvocation> Calls(string identifier) =>
        js.Invocations.Where(i => i.Identifier == identifier).ToList();

    /// <summary>The markdown passed to the latest mount.</summary>
    public string MountedText() => (string)Calls("ContextEditor.mount")[^1].Arguments[1]!;

    /// <summary>The .NET object the latest mount handed to the page (what the editor calls back).</summary>
    public EditorInterop Page() =>
        ((DotNetObjectReference<EditorInterop>)Calls("ContextEditor.mount")[^1].Arguments[2]!).Value;
}
