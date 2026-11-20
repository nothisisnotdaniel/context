namespace Context;

/// <summary>
/// Asked by the window's closing handler (<see cref="Closing"/>): with unsaved changes in the open note it cancels the
/// close and runs the save asynchronously (never blocking the window thread on the Blazor dispatcher). When the save
/// leaves nothing unsaved it calls the close action once; otherwise <c>Main</c> asks <c>Discard changes to
/// &lt;name&gt;?</c> and calls <see cref="Discard"/> on Discard. One per app (a singleton).
/// </summary>
internal sealed class CloseGuard
{
    private readonly Action close;
    private readonly object gate = new();
    private Func<bool>? hasUnsaved;
    private Func<Task<bool>>? saveFirst;
    private Func<Task>? askDiscard;
    private bool allowed;
    private bool saving;

    /// <param name="close">Closes the window for good; it marshals the close onto the window thread itself.</param>
    public CloseGuard(Action close)
    {
        this.close = close;
    }

    /// <summary>
    /// Wires the guard to the page: <paramref name="unsaved"/> tells (without waiting) whether the open note has
    /// unsaved changes or a save pending, <paramref name="save"/> saves first (true when nothing unsaved is left) and
    /// <paramref name="ask"/> shows the discard question.
    /// </summary>
    public void Attach(Func<bool> unsaved, Func<Task<bool>> save, Func<Task> ask)
    {
        lock (gate)
        {
            hasUnsaved = unsaved;
            saveFirst = save;
            askDiscard = ask;
        }
    }

    /// <summary>Unwires the page (it is gone); a later close proceeds.</summary>
    public void Detach()
    {
        lock (gate)
        {
            hasUnsaved = null;
            saveFirst = null;
            askDiscard = null;
        }
    }

    /// <summary>
    /// The window is closing: true cancels the close. Nothing unsaved (or a close already allowed) lets it proceed;
    /// otherwise the save starts in the background and the close is cancelled.
    /// </summary>
    public bool Closing()
    {
        Func<Task<bool>> save;
        Func<Task> ask;
        lock (gate)
        {
            if (allowed || hasUnsaved is not { } unsaved || saveFirst is null || askDiscard is null || !unsaved())
            {
                return false;
            }

            if (saving)
            {
                return true;
            }

            saving = true;
            save = saveFirst;
            ask = askDiscard;
        }

        _ = SaveThenCloseAsync(save, ask);
        return true;
    }

    /// <summary>The user chose Discard in the close question: the window closes without saving.</summary>
    public void Discard() => CloseNow();

    private async Task SaveThenCloseAsync(Func<Task<bool>> save, Func<Task> ask)
    {
        bool saved = false;
        try
        {
            saved = await save();
            if (!saved)
            {
                await ask();
            }
        }
        catch (Exception)
        {
            // The page is gone or the save threw; the window stays open and nothing is lost by closing it later.
        }
        finally
        {
            lock (gate)
            {
                saving = false;
            }
        }

        if (saved)
        {
            CloseNow();
        }
    }

    private void CloseNow()
    {
        lock (gate)
        {
            if (allowed)
            {
                return;
            }

            allowed = true;
        }

        close();
    }
}
