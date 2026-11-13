namespace Context;

/// <summary>
/// The one sensitive note that is open, and when it was last used. The note's text lives only here and in the
/// editor; no key is kept (key bytes are zeroed right after each use). <see cref="Tick"/> locks after
/// <see cref="IdleAfter"/> without an open, a save or editor input.
/// </summary>
internal sealed class SensitiveSession
{
    /// <summary>How long an open sensitive note may sit unused.</summary>
    public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5);

    private readonly object gate = new();
    private readonly Func<DateTime> utcNow;
    private EditableNote? note;
    private DateTime lastUsedUtc;

    public SensitiveSession(Func<DateTime> utcNow)
    {
        this.utcNow = utcNow;
    }

    /// <summary>Raised after <see cref="Lock"/> dropped the note (on the caller's thread).</summary>
    public event Action? Locked;

    /// <summary>
    /// Raised by <see cref="Tick"/> when the open note sat unused for <see cref="IdleAfter"/>. The listener saves
    /// unsaved changes and then calls <see cref="Lock"/>. With no listener <see cref="Tick"/> locks itself.
    /// </summary>
    public event Action? IdleReached;

    /// <summary>
    /// The note to edit from a sealed note's text. The sealed text is the note as a plain file would hold it: a
    /// leading U+FEFF stands for a byte order mark and line endings are kept, so the editor gets the text with
    /// <c>\n</c> and without the mark, like a plain note. <paramref name="version"/> is the hash of the sealed file.
    /// </summary>
    public static EditableNote ToEditable(NoteInfo found, string sealedText, string version)
    {
        bool hadBom = sealedText.StartsWith('\uFEFF');
        string text = hadBom ? sealedText[1..] : sealedText;
        bool hadCrlf = text.Contains("\r\n", StringComparison.Ordinal);
        return new EditableNote(
            found.Name, found.Path, text.Replace("\r\n", "\n", StringComparison.Ordinal), version, hadCrlf, hadBom);
    }

    /// <summary>The text to seal for <paramref name="text"/> (with <c>\n</c>): the line endings and mark <paramref name="opened"/> had.</summary>
    public static string ToSealedText(EditableNote opened, string text)
    {
        string lines = opened.HadCrlf ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text;
        return opened.HadBom ? "\uFEFF" + lines : lines;
    }

    /// <summary>The open sensitive note, or <c>null</c>.</summary>
    public EditableNote? Note
    {
        get
        {
            lock (gate)
            {
                return note;
            }
        }
    }

    /// <summary>Makes <paramref name="opened"/> the open note (replacing any other) and counts as use.</summary>
    public void Open(EditableNote opened)
    {
        lock (gate)
        {
            note = opened;
            lastUsedUtc = Now();
        }
    }

    /// <summary>Counts as use of the open note: the idle time starts again.</summary>
    public void Touch()
    {
        lock (gate)
        {
            lastUsedUtc = Now();
        }
    }

    /// <summary>Drops the open note when it is called <paramref name="name"/>, without raising <see cref="Locked"/>.</summary>
    public void Close(string name)
    {
        lock (gate)
        {
            if (note is not null && string.Equals(note.Name, name, StringComparison.Ordinal))
            {
                note = null;
            }
        }
    }

    /// <summary>Drops the open note and raises <see cref="Locked"/>, so the editor is taken down.</summary>
    public void Lock()
    {
        lock (gate)
        {
            note = null;
        }

        Raise(Locked);
    }

    /// <summary>
    /// When a note is open and was not used for <see cref="IdleAfter"/>: raises <see cref="IdleReached"/> (and
    /// starts the idle time again, so a listener whose save was refused is asked again later), or locks when nobody
    /// listens. Returns true when it did either.
    /// </summary>
    public bool Tick()
    {
        Action? listeners;
        lock (gate)
        {
            if (note is null)
            {
                return false;
            }

            DateTime now = Now();
            if (now < lastUsedUtc)
            {
                lastUsedUtc = now;
                return false;
            }

            if (now - lastUsedUtc < IdleAfter)
            {
                return false;
            }

            lastUsedUtc = now;
            listeners = IdleReached;
        }

        if (listeners is null)
        {
            Lock();
        }
        else
        {
            Raise(listeners);
        }

        return true;
    }

    // A failing clock counts as "long ago", so the note locks instead of staying open.
    private DateTime Now()
    {
        try
        {
            return utcNow();
        }
        catch (Exception)
        {
            return DateTime.MaxValue;
        }
    }

    private static void Raise(Action? handlers)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception)
            {
                // A failing listener must not stop the others.
            }
        }
    }
}
