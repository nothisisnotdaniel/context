namespace Context;

/// <summary>The review date of a plain note, as <see cref="TagIndex.Reviews"/> stores it (job 256).</summary>
internal sealed record NoteReview
{
    public NoteReview(string note, int line, DateOnly due, string raw)
    {
        Note = note;
        Line = line;
        Due = due;
        Raw = raw;
    }

    /// <summary>
    /// Returns only the type name: the note name and line are raw outside text, so formatting this record must never
    /// print them.
    /// </summary>
    public override string ToString() => nameof(NoteReview);

    /// <summary>The note's full name (<see cref="NoteInfo.Name"/>).</summary>
    public string Note { get; }

    /// <summary>The 1-based number of the review line in the note.</summary>
    public int Line { get; }

    /// <summary>The day the note should be checked again.</summary>
    public DateOnly Due { get; }

    /// <summary>The whole line as it was when the note was indexed.</summary>
    public string Raw { get; }
}

/// <summary>
/// Review dates in notes (job 256): which notes are due for a check, moving a date on and removing the line. The note
/// stays the only source.
/// </summary>
internal static class NoteReviews
{
    /// <summary>
    /// The rows of <paramref name="reviews"/> with <see cref="NoteReview.Due"/> on or before <paramref name="today"/>,
    /// sorted by due date, then note name ignoring case, then ordinal.
    /// </summary>
    public static IReadOnlyList<NoteReview> Due(IEnumerable<NoteReview> reviews, DateOnly today) =>
        reviews
            .Where(r => r.Due <= today)
            .OrderBy(r => r.Due)
            .ThenBy(r => r.Note, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Note, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Moves <paramref name="review"/>'s date to <paramref name="day"/>: reads the note
    /// (<see cref="NoteStore.ReadForEdit"/>) and, only when line <see cref="NoteReview.Line"/> is still the note's
    /// review line and exactly <see cref="NoteReview.Raw"/> (ordinal), rewrites it to
    /// <see cref="ReviewDateParser.SetDate"/> and saves through
    /// <see cref="NoteStore.SaveIfUnchanged(EditableNote, string, out EditableNote?, NoteHistory?)"/> (every other byte,
    /// CRLF and BOM kept; a change in between is <see cref="NoteTodoResult.Changed"/>). With <paramref name="history"/>
    /// the previous bytes are kept as a version. Never throws for a read or write failure
    /// (<see cref="NoteTodoResult.Failed"/>). The texts are <see cref="NoteTodos"/>'.
    /// </summary>
    public static NoteTodoResult Reschedule(NoteStore store, NoteReview review, DateOnly day, NoteHistory? history = null) =>
        Reschedule(store, review, day, history, null);

    /// <summary>Like <see cref="Reschedule(NoteStore, NoteReview, DateOnly, NoteHistory?)"/>; <paramref name="beforeSave"/> runs after the read, before the save.</summary>
    internal static NoteTodoResult Reschedule(
        NoteStore store, NoteReview review, DateOnly day, NoteHistory? history, Action? beforeSave) =>
        Edit(store, review, history, beforeSave, (text, start) =>
        {
            string? moved = ReviewDateParser.SetDate(review.Raw, day);
            return moved is null
                ? null
                : string.Concat(text.AsSpan(0, start), moved, text.AsSpan(start + review.Raw.Length));
        });

    /// <summary>
    /// Removes <paramref name="review"/>'s line from its note together with its line end (a last line without one goes
    /// with the line end before it), by the rules of <see cref="Reschedule(NoteStore, NoteReview, DateOnly, NoteHistory?)"/>.
    /// </summary>
    public static NoteTodoResult Remove(NoteStore store, NoteReview review, NoteHistory? history = null) =>
        Remove(store, review, history, null);

    /// <summary>Like <see cref="Remove(NoteStore, NoteReview, NoteHistory?)"/>; <paramref name="beforeSave"/> runs after the read, before the save.</summary>
    internal static NoteTodoResult Remove(NoteStore store, NoteReview review, NoteHistory? history, Action? beforeSave) =>
        Edit(store, review, history, beforeSave, (text, start) =>
        {
            int end = start + review.Raw.Length;
            if (end < text.Length)
            {
                // The line's own "\n" goes with it.
                return string.Concat(text.AsSpan(0, start), text.AsSpan(end + 1));
            }

            // The last line has no line end: the one before it goes instead (none for a note of one line).
            return text[..Math.Max(0, start - 1)];
        });

    // Reads, checks that the review line is still as indexed, applies change (text and the line's start index; null:
    // changed) and saves only when the file is unchanged.
    private static NoteTodoResult Edit(
        NoteStore store, NoteReview review, NoteHistory? history, Action? beforeSave, Func<string, int, string?> change)
    {
        try
        {
            if (!store.TryFind(review.Note, out NoteInfo? found) || found is null
                || !string.Equals(found.Name, review.Note, StringComparison.Ordinal))
            {
                return NoteTodoResult.Missing;
            }

            EditableNote opened = store.ReadForEdit(found);
            int start = LineStart(opened.Text, review);
            string? changed = start < 0 ? null : change(opened.Text, start);
            if (changed is null)
            {
                return NoteTodoResult.Changed;
            }

            beforeSave?.Invoke();
            return store.SaveIfUnchanged(opened, changed, out _, history) switch
            {
                NoteSaveResult.Saved => NoteTodoResult.Done,
                NoteSaveResult.Missing => NoteTodoResult.Missing,
                NoteSaveResult.TooLarge => NoteTodoResult.Failed,
                _ => NoteTodoResult.Changed,
            };
        }
        catch (FileNotFoundException)
        {
            return NoteTodoResult.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return NoteTodoResult.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NoteHistoryException)
        {
            return NoteTodoResult.Failed;
        }
    }

    // The index of the review line's first character in text ("\n" line ends), or -1 when the note's review line is
    // no longer the indexed one (another line, another text).
    private static int LineStart(string text, NoteReview review)
    {
        ParsedReview? current = ReviewDateParser.Parse(text);
        if (current is null || current.Line != review.Line
            || !string.Equals(current.Raw, review.Raw, StringComparison.Ordinal))
        {
            return -1;
        }

        int start = 0;
        for (int n = 1; n < review.Line; n++)
        {
            start = text.IndexOf('\n', start) + 1;
        }

        // The editable text has "\n" line ends; Raw holds no line end, so the line is exactly Raw's length (a stray
        // "\r" the parser dropped makes it longer: no edit then).
        int end = start + review.Raw.Length;
        return end <= text.Length
            && string.CompareOrdinal(text, start, review.Raw, 0, review.Raw.Length) == 0
            && (end == text.Length || text[end] == '\n')
            ? start
            : -1;
    }
}
