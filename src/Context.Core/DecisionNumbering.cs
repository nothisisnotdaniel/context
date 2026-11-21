namespace Context;

/// <summary>
/// Numbers a plain note's <c>!d</c> markers against the index (job 349; moved out of the desktop's state so the
/// command-line tool numbers the same way). The caller holds whatever lock guards <paramref name="index"/> and passes
/// only plain notes: a sensitive note is never numbered.
/// </summary>
internal static class DecisionNumbering
{
    /// <summary>
    /// Numbers the markers of <paramref name="note"/>'s new <paramref name="text"/> with <paramref name="folder"/>'s
    /// prefix (<see cref="DecisionNumbers.Apply"/>): syncs the index first (so it holds the note as stored), keeps an
    /// own-prefix token when this note owns that number or the owner's paragraph has exactly this text, and reserves the
    /// new numbers in one <c>BEGIN IMMEDIATE</c> transaction above the counter, stepping over every own-prefix number in
    /// use, in the text's tokens or in its references. <c>null</c> when there is nothing to tell, the numbered text would
    /// be over <see cref="NoteStore.MaxNoteBytes"/>, or anything fails (an unreadable index): the note is then saved as
    /// typed.
    /// </summary>
    public static DecisionEdits? Number(TagIndex index, NoteStore store, DecisionFolder folder, string note, string text)
    {
        if (!text.Contains(DecisionParser.Marker, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            index.Sync(store);
            IReadOnlyDictionary<int, string>? owners = null;
            IReadOnlyDictionary<int, string>? texts = null;
            List<int> taken =
            [
                .. DecisionParser.Markers(text).Where(d => d.Prefix == folder.Prefix).Select(d => d.Number ?? 0),
                .. DecisionParser.References(text).Where(r => r.Prefix is null || r.Prefix == folder.Prefix).Select(r => r.Number),
            ];
            DecisionEdits edits = DecisionNumbers.Apply(
                text,
                folder,
                (number, decision) =>
                {
                    owners ??= index.DecisionOwners(folder.Prefix);
                    texts ??= index.OwnedDecisionTexts(folder.Prefix);
                    return Keeps(note, number, decision, owners, texts);
                },
                count => index.ReserveDecisionNumbers(folder.Prefix, count, taken));
            if (System.Text.Encoding.UTF8.GetByteCount(edits.Text) > NoteStore.MaxNoteBytes)
            {
                return null;
            }

            return edits.Noteworthy ? edits : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// After a write that was not a save (capture, import, the day's end, a merge) of the plain note
    /// <paramref name="name"/>: numbers it in a second write that goes through only while the file is as it was read
    /// (else the next save numbers it). Returns whether it wrote; never throws. A sensitive note is never found here
    /// (<see cref="NoteStore.TryFind"/>), so it is never numbered.
    /// </summary>
    public static bool NumberWritten(TagIndex index, NoteStore store, DecisionFolder folder, string name) =>
        NumberWritten(index, store, folder, name, out _);

    /// <summary>
    /// <see cref="NumberWritten(TagIndex, NoteStore, DecisionFolder, string)"/>, also giving the numbering's result
    /// (<c>null</c> when there was nothing to tell), so the desktop can tell when no numbers were left.
    /// </summary>
    public static bool NumberWritten(TagIndex index, NoteStore store, DecisionFolder folder, string name, out DecisionEdits? numbered) =>
        NumberWritten(index, store, () => folder, name, out numbered);

    /// <summary>
    /// <see cref="NumberWritten(TagIndex, NoteStore, DecisionFolder, string, out DecisionEdits?)"/> that asks
    /// <paramref name="folder"/> for the folder id only when the note holds a <c>!d</c> marker, so a write without one
    /// never creates <c>decision-folder.json</c>; a <c>null</c> folder numbers nothing.
    /// </summary>
    public static bool NumberWritten(TagIndex index, NoteStore store, Func<DecisionFolder?> folder, string name, out DecisionEdits? numbered)
    {
        numbered = null;
        try
        {
            if (!store.TryFind(name, out NoteInfo? found) || found is null)
            {
                return false;
            }

            EditableNote opened = store.ReadForEdit(found);
            if (!opened.Text.Contains(DecisionParser.Marker, StringComparison.Ordinal) || folder() is not { } decisionFolder)
            {
                return false;
            }

            numbered = Number(index, store, decisionFolder, found.Name, opened.Text);
            if (numbered is not { Changed: true } edits)
            {
                return false;
            }

            return store.SaveIfUnchanged(opened, edits.Text, out _) == NoteSaveResult.Saved;
        }
        catch (Exception)
        {
            // The write is done; the markers stay as they are until the next save.
            return false;
        }
    }

    // An own-prefix token stays when this note owns it, or its owner's logged paragraph has exactly this text (a copy).
    private static bool Keeps(
        string note,
        int number,
        string text,
        IReadOnlyDictionary<int, string> owners,
        IReadOnlyDictionary<int, string> texts)
    {
        if (!owners.TryGetValue(number, out string? owner))
        {
            return false;
        }

        return string.Equals(owner, note, StringComparison.Ordinal)
            || (texts.TryGetValue(number, out string? logged) && string.Equals(logged, text, StringComparison.Ordinal));
    }
}
