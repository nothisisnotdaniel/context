using System.Globalization;

namespace Context;

/// <summary>How <see cref="ScreenshotInbox.Add(NoteStore, NoteAttachments, string, byte[], DateTime)"/> ended.</summary>
internal enum ScreenshotAddKind
{
    /// <summary>The image is stored as an attachment of <c>inbox</c> and a dated line links it.</summary>
    Added,

    /// <summary>Over <see cref="NoteAttachments.MaxBytes"/>; nothing was written.</summary>
    TooLarge,

    /// <summary>Not a PNG, JPEG, GIF or WebP image whose extension matches its bytes; nothing was written.</summary>
    Unsupported,

    /// <summary><c>inbox</c> is a sensitive note; nothing was read or written.</summary>
    Sensitive,

    /// <summary>The inbox would grow past <see cref="NoteStore.MaxEditBytes"/>; the attachment was deleted again.</summary>
    InboxTooLarge,

    /// <summary>The inbox changed on disk on every attempt; the attachment was deleted again.</summary>
    ChangedOnDisk,

    /// <summary>The attachment could not be stored (its folder is a link, or the inbox is gone); nothing was linked.</summary>
    Failed,
}

/// <summary>The answer of <see cref="ScreenshotInbox.Add(NoteStore, NoteAttachments, string, byte[], DateTime)"/>.</summary>
internal sealed record ScreenshotAdd
{
    public ScreenshotAdd(ScreenshotAddKind kind, string? name)
    {
        Kind = kind;
        Name = name;
    }

    public ScreenshotAddKind Kind { get; }

    /// <summary>The stored attachment name, only when added.</summary>
    public string? Name { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(ScreenshotAdd);
}

/// <summary>
/// Screenshots into the inbox (job 260): the image is stored as an attachment of <c>inbox</c>
/// (<see cref="NoteAttachments.Add"/>) and linked by one <see cref="Inbox.Capture(NoteStore, string, DateTime)"/> line,
/// <c>- yyyy-MM-dd HH:mm Screenshot ![&lt;name&gt;](.attachments/&lt;name&gt;)</c>.
/// </summary>
internal static class ScreenshotInbox
{
    /// <summary>The word before the image link in the inbox line.</summary>
    public const string LinePrefix = "Screenshot ";

    /// <summary>The name of a hotkey capture taken at <paramref name="now"/>: <c>screenshot-yyyyMMdd-HHmmss.png</c>.</summary>
    public static string HotkeyName(DateTime now) =>
        "screenshot-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png";

    /// <summary>
    /// Stores <paramref name="bytes"/> as an image attachment of <c>inbox</c> under
    /// <see cref="NoteAttachments.CleanName"/> of <paramref name="fileName"/> (a clash gets <c>-2</c> and so on) and
    /// appends <c>Screenshot ![&lt;name&gt;](.attachments/&lt;name&gt;)</c> through
    /// <see cref="Inbox.Capture(NoteStore, string, DateTime)"/> at <paramref name="now"/>. Bytes over
    /// <see cref="NoteAttachments.MaxBytes"/>, or that are no PNG, JPEG, GIF or WebP image matching the name's extension,
    /// and a sensitive inbox are refused before anything is written. A missing inbox is created as <c># inbox\n</c>
    /// first. When the capture fails (the inbox keeps changing, would be too large, or throws) the stored attachment is
    /// deleted again; an exception is then rethrown. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when it can't write.
    /// </summary>
    public static ScreenshotAdd Add(NoteStore store, NoteAttachments attachments, string fileName, byte[] bytes, DateTime now) =>
        Add(store, attachments, fileName, bytes, now, null);

    /// <summary>
    /// Like <see cref="Add(NoteStore, NoteAttachments, string, byte[], DateTime)"/>; <paramref name="beforeSave"/> runs
    /// before every save attempt of the capture, so tests can change the inbox in between.
    /// </summary>
    internal static ScreenshotAdd Add(
        NoteStore store, NoteAttachments attachments, string fileName, byte[] bytes, DateTime now, Action? beforeSave)
    {
        if (bytes.LongLength > NoteAttachments.MaxBytes)
        {
            return new ScreenshotAdd(ScreenshotAddKind.TooLarge, null);
        }

        string clean = NoteAttachments.CleanName(fileName);
        if (!NoteAttachments.IsImage(NoteAttachments.Sniff(clean, bytes)) || !NoteAttachments.IsStoredName(clean))
        {
            return new ScreenshotAdd(ScreenshotAddKind.Unsupported, null);
        }

        // A sealed inbox is matched by name only: it is never opened.
        if (store.TryFindSensitive(Inbox.NoteName, out _))
        {
            return new ScreenshotAdd(ScreenshotAddKind.Sensitive, null);
        }

        if (!store.TryFind(Inbox.NoteName, out NoteInfo? inbox) || inbox is null)
        {
            try
            {
                store.CreateWithText(Inbox.NoteName, $"# {Inbox.NoteName}\n");
            }
            catch (NoteExistsException)
            {
                // Created by another writer in between: use theirs.
            }

            if (!store.TryFind(Inbox.NoteName, out inbox) || inbox is null)
            {
                return new ScreenshotAdd(ScreenshotAddKind.Failed, null);
            }
        }

        AttachmentAdd added = attachments.Add(Inbox.NoteName, clean, bytes);
        if (added.Kind != AttachmentAddKind.Added || added.Name is null || added.Markdown is null)
        {
            return new ScreenshotAdd(
                added.Kind switch
                {
                    AttachmentAddKind.TooLarge => ScreenshotAddKind.TooLarge,
                    AttachmentAddKind.Unsupported => ScreenshotAddKind.Unsupported,
                    _ => ScreenshotAddKind.Failed,
                },
                null);
        }

        string stored = Path.Combine(NoteAttachments.Folder(inbox), added.Name);
        CaptureResult result;
        try
        {
            result = Inbox.Capture(store, LinePrefix + added.Markdown, now, beforeSave);
        }
        catch (Exception)
        {
            DeleteQuietly(stored);
            throw;
        }

        switch (result)
        {
            case CaptureResult.Captured:
                return new ScreenshotAdd(ScreenshotAddKind.Added, added.Name);
            case CaptureResult.TooLarge:
                DeleteQuietly(stored);
                return new ScreenshotAdd(ScreenshotAddKind.InboxTooLarge, null);
            default:
                DeleteQuietly(stored);
                return new ScreenshotAdd(ScreenshotAddKind.ChangedOnDisk, null);
        }
    }

    // The attachment this call stored, never through a link; a failure leaves an unlinked file behind.
    private static void DeleteQuietly(string path)
    {
        try
        {
            FileInfo info = new FileInfo(path);
            if (info.Exists && info.LinkTarget is null)
            {
                info.Delete();
            }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            // Left behind: an attachment no line links.
        }
    }
}
