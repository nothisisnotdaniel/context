using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Context;

/// <summary>
/// The only bridge to JavaScript: calls the bundled editor (<c>wwwroot/editor/editor.js</c>, global
/// <c>ContextEditor</c>) for one host element and passes its tag, link and mention clicks and its dirty flips back as
/// events. It never changes data: its <see cref="JSInvokableAttribute"/> methods only raise <see cref="TagClicked"/>,
/// <see cref="LinkClicked"/>, <see cref="PersonClicked"/> and <see cref="DirtyChanged"/>, and the component decides what to open or show.
/// </summary>
internal sealed class EditorInterop
{
    private readonly IJSRuntime js;
    private DotNetObjectReference<EditorInterop>? self;
    private ElementReference? host;

    public EditorInterop(IJSRuntime js)
    {
        this.js = js;
    }

    /// <summary>Raised with the raw text of a clicked tag, as written (<c>#tag</c> or <c>#[tag with spaces]</c>).</summary>
    public event Func<string, Task>? TagClicked;

    /// <summary>Raised with the raw text of a clicked note link, as written (<c>[[Note]]</c>).</summary>
    public event Func<string, Task>? LinkClicked;

    /// <summary>Raised with the raw text of a clicked person mention, as written (<c>@[Anna Schmidt]</c>).</summary>
    public event Func<string, Task>? PersonClicked;

    /// <summary>
    /// Raised when the page's editor went from saved to changed (<c>true</c>) or back (<c>false</c>). For display
    /// only: decisions that protect data ask <see cref="IsDirtyAsync"/>.
    /// </summary>
    public event Func<bool, Task>? DirtyChanged;

    /// <summary>Opens <paramref name="markdown"/> in a new editor inside <paramref name="element"/>.</summary>
    public async Task MountAsync(ElementReference element, string markdown)
    {
        self ??= DotNetObjectReference.Create(this);
        host = element;
        await js.InvokeVoidAsync("ContextEditor.mount", element, markdown, self);
    }

    /// <summary>True when the editor's Markdown round-trips <paramref name="markdown"/> byte for byte.</summary>
    public ValueTask<bool> IsLosslessAsync(string markdown) => js.InvokeAsync<bool>("ContextEditor.isLossless", markdown);

    /// <summary>The editor's text as Markdown.</summary>
    public ValueTask<string> GetMarkdownAsync(ElementReference element) =>
        js.InvokeAsync<string>("ContextEditor.getMarkdown", element);

    /// <summary>True when the text changed since it was opened or last saved.</summary>
    public ValueTask<bool> IsDirtyAsync(ElementReference element) => js.InvokeAsync<bool>("ContextEditor.isDirty", element);

    /// <summary>Marks the editor's current text as saved.</summary>
    public ValueTask MarkCleanAsync(ElementReference element) => js.InvokeVoidAsync("ContextEditor.markClean", element);

    /// <summary>Puts the cursor in the editor.</summary>
    public ValueTask FocusAsync(ElementReference element) => js.InvokeVoidAsync("ContextEditor.focus", element);

    /// <summary>Called by the editor when a tag is clicked.</summary>
    [JSInvokable]
    public Task OnTag(string raw) => TagClicked?.Invoke(raw) ?? Task.CompletedTask;

    /// <summary>Called by the editor when a note link is clicked.</summary>
    [JSInvokable]
    public Task OnLink(string raw) => LinkClicked?.Invoke(raw) ?? Task.CompletedTask;

    /// <summary>Called by the editor when a person mention is clicked.</summary>
    [JSInvokable]
    public Task OnPerson(string raw) => PersonClicked?.Invoke(raw) ?? Task.CompletedTask;

    /// <summary>Called by the editor when its text became changed or unchanged against the saved text.</summary>
    [JSInvokable]
    public Task OnDirty(bool dirty) => DirtyChanged?.Invoke(dirty) ?? Task.CompletedTask;

    /// <summary>Removes the editor from the page and drops the reference the page holds.</summary>
    public async Task DestroyAsync()
    {
        try
        {
            if (host is { } element)
            {
                await js.InvokeVoidAsync("ContextEditor.destroy", element);
            }
        }
        catch (Exception e) when (e is JSDisconnectedException or JSException or TaskCanceledException or InvalidOperationException)
        {
            // The page is gone or never had the editor; nothing to remove.
        }

        host = null;
        self?.Dispose();
        self = null;
    }
}
