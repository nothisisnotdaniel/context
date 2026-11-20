namespace Context;

/// <summary>An open topic line of a plain note, as <see cref="TagIndex.Topics"/> stores it.</summary>
internal sealed record NoteTopic
{
    public NoteTopic(string note, int line, string person, string text, string raw)
    {
        Note = note;
        Line = line;
        Person = person;
        Text = text;
        Raw = raw;
    }

    /// <summary>
    /// Returns only the type name: the note name, person, text and line are raw outside text, so formatting this record
    /// must never print them.
    /// </summary>
    public override string ToString() => nameof(NoteTopic);

    /// <summary>The note's full name (<see cref="NoteInfo.Name"/>).</summary>
    public string Note { get; }

    /// <summary>The 1-based number of the line in the note.</summary>
    public int Line { get; }

    /// <summary>The mention's normalized name as written (raw, see <see cref="ParsedTopic.Person"/>).</summary>
    public string Person { get; }

    /// <summary>The topic's text (raw, see <see cref="ParsedTopic.Text"/>).</summary>
    public string Text { get; }

    /// <summary>The whole line as it was when the note was indexed.</summary>
    public string Raw { get; }
}

/// <summary>
/// Topics for the next 1:1 (job 253): picking the open topics of some people and ticking one off in its note. The note
/// stays the only source.
/// </summary>
internal static class MeetingTopics
{
    /// <summary>
    /// The topics of <paramref name="topics"/> whose person resolves (<see cref="PersonAliases.PersonKey"/>) to one of
    /// <paramref name="names"/>, ordered by the first such name's position, then note name ignoring case, then ordinal,
    /// then line.
    /// </summary>
    public static IReadOnlyList<NoteTopic> For(
        IReadOnlyList<NoteTopic> topics, PersonAliases aliases, IReadOnlyList<string> names)
    {
        Dictionary<string, int> positions = new(StringComparer.Ordinal);
        for (int i = 0; i < names.Count; i++)
        {
            positions.TryAdd(aliases.PersonKey(names[i]), i);
        }

        if (positions.Count == 0)
        {
            return [];
        }

        List<(NoteTopic Topic, int Position)> found = [];
        foreach (NoteTopic topic in topics)
        {
            if (positions.TryGetValue(aliases.PersonKey(topic.Person), out int position))
            {
                found.Add((topic, position));
            }
        }

        return found
            .OrderBy(f => f.Position)
            .ThenBy(f => f.Topic.Note, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Topic.Note, StringComparer.Ordinal)
            .ThenBy(f => f.Topic.Line)
            .Select(f => f.Topic)
            .ToList();
    }

    /// <summary>
    /// Ticks <paramref name="topic"/> off in its note: reads the note (<see cref="NoteStore.ReadForEdit"/>) and, only
    /// when line <see cref="NoteTopic.Line"/> still parses to a topic whose line is exactly <see cref="NoteTopic.Raw"/>
    /// (ordinal), rewrites it to its done form (<see cref="TopicParser.DoneForm"/>) and saves through
    /// <see cref="NoteStore.SaveIfUnchanged(EditableNote, string, out EditableNote?, NoteHistory?)"/> (every other byte,
    /// CRLF and BOM kept; a change in between is <see cref="NoteTodoResult.Changed"/>). With <paramref name="history"/>
    /// the previous bytes are kept as a version. Never throws for a read or write failure
    /// (<see cref="NoteTodoResult.Failed"/>). The texts are <see cref="NoteTodos"/>'.
    /// </summary>
    public static NoteTodoResult Complete(NoteStore store, NoteTopic topic, NoteHistory? history = null) =>
        Complete(store, topic, history, null);

    /// <summary>Like <see cref="Complete(NoteStore, NoteTopic, NoteHistory?)"/>; <paramref name="beforeSave"/> runs after the read, before the save.</summary>
    internal static NoteTodoResult Complete(NoteStore store, NoteTopic topic, NoteHistory? history, Action? beforeSave)
    {
        try
        {
            if (!store.TryFind(topic.Note, out NoteInfo? found) || found is null
                || !string.Equals(found.Name, topic.Note, StringComparison.Ordinal))
            {
                return NoteTodoResult.Missing;
            }

            EditableNote opened = store.ReadForEdit(found);
            string? ticked = Tick(opened.Text, topic);
            if (ticked is null)
            {
                return NoteTodoResult.Changed;
            }

            beforeSave?.Invoke();
            return store.SaveIfUnchanged(opened, ticked, out _, history) switch
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

    // The text with the topic's line in its done form, or null when that line is not the topic's line as indexed.
    private static string? Tick(string text, NoteTopic topic)
    {
        ParsedTopic? current = TopicParser.Parse(text).FirstOrDefault(t => t.Line == topic.Line);
        if (current is null || !string.Equals(current.Raw, topic.Raw, StringComparison.Ordinal))
        {
            return null;
        }

        int start = 0;
        for (int n = 1; n < topic.Line; n++)
        {
            start = text.IndexOf('\n', start) + 1;
        }

        // The editable text has "\n" line ends; Raw holds no line end, so the line is exactly Raw's length.
        string? done = TopicParser.DoneForm(current.Raw);
        if (done is null || string.CompareOrdinal(text, start, current.Raw, 0, current.Raw.Length) != 0)
        {
            return null;
        }

        return string.Concat(text.AsSpan(0, start), done, text.AsSpan(start + current.Raw.Length));
    }
}
