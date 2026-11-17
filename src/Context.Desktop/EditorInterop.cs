using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Context;

/// <summary>
/// One file dropped onto the page's editor: its name as the OS gives it (raw, untrusted), its size and its bytes
/// (<c>null</c> for a file over <see cref="NoteAttachments.MaxBytes"/>, which the page never reads).
/// </summary>
public sealed record DroppedFile
{
    public DroppedFile(string name, long size, byte[]? bytes)
    {
        Name = name;
        Size = size;
        Bytes = bytes;
    }

    public string Name { get; }

    public long Size { get; }

    public byte[]? Bytes { get; }

    /// <summary>Only the type name, so the raw name and bytes never leak through formatting.</summary>
    public override string ToString() => nameof(DroppedFile);
}

/// <summary>
/// One drop: how many files were dropped and the files themselves (empty when <see cref="Count"/> is over
/// <see cref="NoteAttachments.MaxFilesPerDrop"/>).
/// </summary>
public sealed record DroppedFiles
{
    public DroppedFiles(int count, IReadOnlyList<DroppedFile> files)
    {
        Count = count;
        Files = files;
    }

    public int Count { get; }

    public IReadOnlyList<DroppedFile> Files { get; }

    /// <summary>Only the type name, so the raw names never leak through formatting.</summary>
    public override string ToString() => nameof(DroppedFiles);
}

/// <summary>An image the page's editor shows inline: its link source, MIME type and base64 bytes (checked by .NET).</summary>
public sealed record InlineImage
{
    public InlineImage(string src, string mime, string base64)
    {
        Src = src;
        Mime = mime;
        Base64 = base64;
    }

    public string Src { get; }

    public string Mime { get; }

    public string Base64 { get; }

    /// <summary>Only the type name, so the bytes never leak through formatting.</summary>
    public override string ToString() => nameof(InlineImage);
}

/// <summary>
/// The only bridge to JavaScript: calls the bundled editor (<c>wwwroot/editor/editor.js</c>, global
/// <c>ContextEditor</c>) for one host element and passes its tag, link and mention clicks and its dirty flips back as
/// events. It never changes data: its <see cref="JSInvokableAttribute"/> methods only raise <see cref="TagClicked"/>,
/// <see cref="LinkClicked"/>, <see cref="PersonClicked"/>, <see cref="DirtyChanged"/>, <see cref="FilesDropped"/>,
/// <see cref="AttachmentClicked"/> and <see cref="Changed"/>, and the component decides what to open, show or store.
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

    /// <summary>
    /// Raised on every change of the editor's text (once per changing transaction); the component restarts its
    /// auto-save pause. Nothing is read or written here.
    /// </summary>
    public event Func<Task>? Changed;

    /// <summary>Raised once per drop of files onto the editor.</summary>
    public event Func<DroppedFiles, Task>? FilesDropped;

    /// <summary>Raised with the href of a clicked attachment PDF link, as written (<c>.attachments/x.pdf</c>).</summary>
    public event Func<string, Task>? AttachmentClicked;

    /// <summary>
    /// Opens <paramref name="markdown"/> in a new editor inside <paramref name="element"/>, showing the
    /// <paramref name="images"/> (already checked by <see cref="NoteAttachments.TryResolve"/>) inline. A first line
    /// <c># &lt;title&gt;</c> matching <paramref name="name"/> is not shown and is saved back unchanged.
    /// </summary>
    public async Task MountAsync(ElementReference element, string markdown, IReadOnlyList<InlineImage> images, string name)
    {
        self ??= DotNetObjectReference.Create(this);
        host = element;
        await js.InvokeVoidAsync("ContextEditor.mount", element, markdown, self, images, name);
    }

    /// <summary>
    /// Inserts the link to a stored attachment at the editor's drop position (which then moves past it):
    /// an image node (alt = <paramref name="name"/>, shown from <paramref name="base64"/>) or a PDF link.
    /// </summary>
    public ValueTask InsertAttachmentAsync(
        ElementReference element, string src, string name, bool isImage, string mime, string? base64) =>
        js.InvokeVoidAsync("ContextEditor.insertAttachment", element, src, name, isImage, mime, base64);

    /// <summary>
    /// True when the editor's Markdown round-trips <paramref name="markdown"/> byte for byte, with the title line of
    /// the note <paramref name="name"/> kept as it is.
    /// </summary>
    public ValueTask<bool> IsLosslessAsync(string markdown, string name) =>
        js.InvokeAsync<bool>("ContextEditor.isLossless", markdown, name);

    /// <summary>The editor's text as Markdown.</summary>
    public ValueTask<string> GetMarkdownAsync(ElementReference element) =>
        js.InvokeAsync<string>("ContextEditor.getMarkdown", element);

    /// <summary>True when the text changed since it was opened or last saved.</summary>
    public ValueTask<bool> IsDirtyAsync(ElementReference element) => js.InvokeAsync<bool>("ContextEditor.isDirty", element);

    /// <summary>Marks the editor's current text as saved.</summary>
    public ValueTask MarkCleanAsync(ElementReference element) => js.InvokeVoidAsync("ContextEditor.markClean", element);

    /// <summary>
    /// Marks the editor's text as saved only when its Markdown still equals <paramref name="markdown"/> (the text a
    /// save just wrote), compared and marked in one JavaScript turn. False when it was edited since, so it stays dirty.
    /// </summary>
    public ValueTask<bool> MarkCleanIfAsync(ElementReference element, string markdown) =>
        js.InvokeAsync<bool>("ContextEditor.markCleanIf", element, markdown);

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

    /// <summary>Called by the editor once per drop of files: the number dropped and the files (none over the limit).</summary>
    [JSInvokable]
    public Task OnDrop(int count, DroppedFile[] files) =>
        FilesDropped?.Invoke(new DroppedFiles(count, files)) ?? Task.CompletedTask;

    /// <summary>Called by the editor when an attachment PDF link is clicked.</summary>
    [JSInvokable]
    public Task OnAttachment(string href) => AttachmentClicked?.Invoke(href) ?? Task.CompletedTask;

    /// <summary>Called by the editor on every change of its text.</summary>
    [JSInvokable]
    public Task OnChanged() => Changed?.Invoke() ?? Task.CompletedTask;

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
