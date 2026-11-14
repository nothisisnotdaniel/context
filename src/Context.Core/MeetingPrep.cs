using System.Text;

namespace Context;

/// <summary>A note related to an appointment through shared tags or title words.</summary>
internal sealed record RelatedNote
{
    public RelatedNote(string name, IReadOnlyList<string> tags, IReadOnlyList<string> words)
    {
        Name = name;
        Tags = tags;
        Words = words;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never
    /// print them. Front ends draw it through their <c>Render</c> step.
    /// </summary>
    public override string ToString() => nameof(RelatedNote);

    /// <summary>The full note name (raw: clean it before showing it).</summary>
    public string Name { get; }

    /// <summary>The matching tags, normalised (lower-case, no <c>#</c>), sorted ordinally.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>The title words the note shares with the appointment, lower-case, sorted ordinally.</summary>
    public IReadOnlyList<string> Words { get; }
}

/// <summary>
/// Meeting prep: finds the notes related to an appointment by the words of its title and attendee names. Pure and
/// in memory: the caller reads the notes and the index once and passes them in.
/// </summary>
internal static class MeetingPrep
{
    /// <summary>Shorter words are left out.</summary>
    public const int MinWordLength = 3;

    /// <summary>At most this many related notes per appointment.</summary>
    public const int MaxRelated = 3;

    /// <summary>Common English and German words that say nothing about a meeting.</summary>
    public static readonly IReadOnlySet<string> StopWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "the", "and", "for", "with", "from", "about", "into", "our", "you", "are", "was", "not",
        "meeting", "call", "sync", "weekly", "daily",
        "und", "der", "die", "das", "mit", "für", "von", "den", "dem", "ein", "eine", "zum", "zur",
    };

    /// <summary>
    /// The maximal runs of letters and digits in <paramref name="text"/>, lower-cased (invariant), without runs shorter
    /// than <see cref="MinWordLength"/>, runs of digits only and <see cref="StopWords"/>; each once, in text order.
    /// </summary>
    public static IReadOnlyList<string> Words(string text)
    {
        List<string> words = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        StringBuilder run = new();
        bool letter = false;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsLetterOrDigit(text[i]))
            {
                run.Append(char.ToLowerInvariant(text[i]));
                letter |= !char.IsDigit(text[i]);
                continue;
            }

            if (run.Length >= MinWordLength && letter)
            {
                string word = run.ToString();
                if (!StopWords.Contains(word) && seen.Add(word))
                {
                    words.Add(word);
                }
            }

            run.Clear();
            letter = false;
        }

        return words;
    }

    /// <summary>
    /// The notes related to <paramref name="appointment"/>: a note matches through a tag when every word of the tag
    /// (at least one) is a word of the title or an attendee name, and through its title (the last name segment)
    /// when they share a word. Only notes in <paramref name="notes"/> are used; rows of
    /// <paramref name="noteTags"/> for other notes are ignored. Ranked by the number of matches (descending), then by
    /// name ignoring case, then ordinally; at most <see cref="MaxRelated"/>.
    /// </summary>
    public static IReadOnlyList<RelatedNote> Related(
        Appointment appointment, IReadOnlyList<NoteInfo> notes, IReadOnlyList<(string Note, string Tag)> noteTags)
    {
        HashSet<string> words = new(Words(appointment.Title), StringComparer.Ordinal);
        foreach (string attendee in appointment.Attendees)
        {
            words.UnionWith(Words(attendee));
        }

        if (words.Count == 0)
        {
            return [];
        }

        Dictionary<string, (SortedSet<string> Tags, SortedSet<string> Words)> found = new(StringComparer.Ordinal);
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (NoteInfo note in notes)
        {
            names.Add(note.Name);
            foreach (string word in Words(note.Title))
            {
                if (words.Contains(word))
                {
                    Entry(found, note.Name).Words.Add(word);
                }
            }
        }

        foreach ((string note, string tag) in noteTags)
        {
            if (!names.Contains(note))
            {
                continue;
            }

            IReadOnlyList<string> tagWords = Words(tag);
            if (tagWords.Count > 0 && tagWords.All(words.Contains))
            {
                Entry(found, note).Tags.Add(tag);
            }
        }

        return found
            .OrderByDescending(f => f.Value.Tags.Count + f.Value.Words.Count)
            .ThenBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Key, StringComparer.Ordinal)
            .Take(MaxRelated)
            .Select(f => new RelatedNote(f.Key, f.Value.Tags.ToList(), f.Value.Words.ToList()))
            .ToList();
    }

    private static (SortedSet<string> Tags, SortedSet<string> Words) Entry(
        Dictionary<string, (SortedSet<string> Tags, SortedSet<string> Words)> found, string name)
    {
        if (!found.TryGetValue(name, out (SortedSet<string> Tags, SortedSet<string> Words) entry))
        {
            entry = (new SortedSet<string>(StringComparer.Ordinal), new SortedSet<string>(StringComparer.Ordinal));
            found[name] = entry;
        }

        return entry;
    }
}
