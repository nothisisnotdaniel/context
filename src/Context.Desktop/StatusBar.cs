namespace Context;

/// <summary>
/// The one feedback message of the desktop window (job 455), shown by <see cref="StatusBarView"/> in the status bar
/// along the bottom: the result of a save, share, move, copy, setting change or failed action. Each
/// <see cref="Show(string, string)"/> replaces what is shown and returns the message's id; <see cref="Clear"/> with
/// that id empties the bar only while the message is still the one shown, so one panel never clears another's message.
/// Thread-safe; <see cref="Changed"/> is raised after each change, on the thread that made it, and a throwing handler
/// is ignored.
/// </summary>
internal sealed class StatusBar
{
    private readonly object gate = new();
    private long next;
    private Message current = Message.Empty;

    /// <summary>Raised after the shown message changed.</summary>
    public event Action? Changed;

    /// <summary>The text shown on one line; empty while nothing or a multi-line message is shown.</summary>
    public string Text
    {
        get
        {
            lock (gate)
            {
                return current.Text;
            }
        }
    }

    /// <summary>The colour class of <see cref="Text"/> (<c>grey</c>, <c>accent</c>, <c>red</c> or empty).</summary>
    public string Css
    {
        get
        {
            lock (gate)
            {
                return current.Css;
            }
        }
    }

    /// <summary>The lines of a multi-line message, each with its colour class; empty for a one-line message.</summary>
    public IReadOnlyList<(string Text, string Css)> Lines
    {
        get
        {
            lock (gate)
            {
                return current.Lines;
            }
        }
    }

    /// <summary>The message shown now, with its id (0 while the bar is empty); for <see cref="Restore"/>.</summary>
    public Message Current
    {
        get
        {
            lock (gate)
            {
                return current;
            }
        }
    }

    /// <summary>Shows <paramref name="text"/> (through <see cref="DesktopText.OneLine"/>) in <paramref name="css"/>.</summary>
    /// <returns>The message's id, for <see cref="Clear"/>.</returns>
    public long Show(string text, string css) => Set(DesktopText.OneLine(text), css, []);

    /// <summary>Shows one line per item (each through <see cref="DesktopText.OneLine"/>) with its colour class.</summary>
    /// <returns>The message's id, for <see cref="Clear"/>.</returns>
    public long Show(IReadOnlyList<(string Text, string Css)> lines)
    {
        List<(string Text, string Css)> shown = [];
        foreach ((string text, string css) in lines)
        {
            shown.Insert(shown.Count, (DesktopText.OneLine(text), css));
        }

        return Set(string.Empty, string.Empty, shown);
    }

    /// <summary>
    /// A component's one status line: shows <paramref name="text"/> in <paramref name="css"/> and returns its id, or,
    /// for an empty text, clears the component's own message <paramref name="id"/> and returns 0.
    /// </summary>
    public long Update(long id, string text, string css)
    {
        if (text.Length == 0)
        {
            Clear(id);
            return 0;
        }

        return Show(text, css);
    }

    /// <summary>Empties the bar if message <paramref name="id"/> is the one shown; otherwise leaves it.</summary>
    public void Clear(long id)
    {
        lock (gate)
        {
            if (id == 0 || current.Id != id)
            {
                return;
            }

            current = Message.Empty;
        }

        Raise();
    }

    /// <summary>
    /// Shows <paramref name="before"/> again if message <paramref name="id"/> is still the one shown (the editor's
    /// <c>Saving…</c> after a save refused as busy); otherwise leaves the bar.
    /// </summary>
    public void Restore(long id, Message before)
    {
        lock (gate)
        {
            if (id == 0 || current.Id != id)
            {
                return;
            }

            current = before;
        }

        Raise();
    }

    private long Set(string text, string css, IReadOnlyList<(string Text, string Css)> lines)
    {
        long id;
        lock (gate)
        {
            next++;
            id = next;
            current = new Message(id, text, css, lines);
        }

        Raise();
        return id;
    }

    private void Raise()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception)
        {
            // A broken listener must not break the action that showed the message.
        }
    }

    /// <summary>One shown message: its id (0 for none), one-line text and class, or its lines.</summary>
    internal sealed record Message
    {
        public Message(long id, string text, string css, IReadOnlyList<(string Text, string Css)> lines)
        {
            Id = id;
            Text = text;
            Css = css;
            Lines = lines;
        }

        /// <summary>The empty bar.</summary>
        public static Message Empty { get; } = new(0, string.Empty, string.Empty, []);

        public long Id { get; }

        public string Text { get; }

        public string Css { get; }

        public IReadOnlyList<(string Text, string Css)> Lines { get; }
    }
}
