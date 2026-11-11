using System.Globalization;
using System.Text;

namespace Context;

/// <summary>What <see cref="Inbox.Capture(NoteStore, string, DateTime)"/> did.</summary>
internal enum CaptureResult
{
    /// <summary>The line was appended (or the inbox note was created with it).</summary>
    Captured,

    /// <summary>The inbox note changed on disk on every attempt; nothing was written.</summary>
    ChangedOnDisk,

    /// <summary>The inbox note would grow past <see cref="NoteStore.MaxEditBytes"/>; nothing was written.</summary>
    TooLarge,
}

/// <summary>
/// The <c>inbox</c> note: quick captures are appended to it as <c>- yyyy-MM-dd HH:mm &lt;text&gt;</c> lines.
/// </summary>
internal static class Inbox
{
    /// <summary>The note captures go to (<c>inbox.md</c>, found in any case).</summary>
    public const string NoteName = "inbox";

    /// <summary>The longest capture text, after <see cref="Clean"/>.</summary>
    public const int MaxTextLength = 1000;

    /// <summary>How many times a capture re-reads and retries when the note changed on disk in between.</summary>
    public const int MaxRetries = 3;

    /// <summary>
    /// The capture text as one line: every control character (line breaks included) becomes a space, then the
    /// text is trimmed. The caller refuses an empty result or one over <see cref="MaxTextLength"/>.
    /// </summary>
    public static string Clean(string text)
    {
        StringBuilder builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Appends <c>- yyyy-MM-dd HH:mm &lt;text&gt;\n</c> (<paramref name="now"/> as given, <paramref name="text"/>
    /// cleaned with <see cref="Clean"/>) to the inbox note, adding a missing final newline first, through
    /// <see cref="NoteStore.ReadForEdit"/> and <see cref="NoteStore.SaveIfUnchanged"/> (temp file
    /// and move). A missing note is created as <c># inbox\n</c> plus the line. When the note changed on
    /// disk between reading and saving it is read again, at most <see cref="MaxRetries"/> more times. Throws
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the note can't be read or written.
    /// </summary>
    public static CaptureResult Capture(NoteStore store, string text, DateTime now) => Capture(store, text, now, null);

    /// <summary>
    /// Like <see cref="Capture(NoteStore, string, DateTime)"/>; <paramref name="beforeSave"/> runs after each read
    /// and before each save, so tests can change the note in between.
    /// </summary>
    internal static CaptureResult Capture(NoteStore store, string text, DateTime now, Action? beforeSave)
    {
        string line = $"- {now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} {Clean(text)}\n";
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (!store.TryFind(NoteName, out NoteInfo? match) || match is null)
            {
                beforeSave?.Invoke();
                try
                {
                    store.CreateWithText(NoteName, $"# {NoteName}\n{line}");
                    return CaptureResult.Captured;
                }
                catch (NoteExistsException)
                {
                    // Created by another writer in between: append to theirs.
                    continue;
                }
            }

            EditableNote opened = store.ReadForEdit(match);
            string body = opened.Text.Length == 0 || opened.Text.EndsWith('\n') ? opened.Text : opened.Text + "\n";
            beforeSave?.Invoke();
            NoteSaveResult result = store.SaveIfUnchanged(opened, body + line, out _);
            switch (result)
            {
                case NoteSaveResult.Saved:
                    return CaptureResult.Captured;
                case NoteSaveResult.TooLarge:
                    return CaptureResult.TooLarge;
                default:
                    // ChangedOnDisk or Missing: read again.
                    continue;
            }
        }

        return CaptureResult.ChangedOnDisk;
    }
}
