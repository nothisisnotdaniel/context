using System.Globalization;

namespace Context;

internal sealed class JournalDay
{
    public required DateOnly Date { get; init; }
    public string? Daily { get; init; }
    public string? Body { get; init; }
    public required IReadOnlyList<string> Meetings { get; init; }
    public required IReadOnlyList<Appointment> Appointments { get; init; }
}

internal sealed class Journal
{
    private readonly NoteStore notes;
    private readonly Func<DateOnly, IReadOnlyList<Appointment>> appointments;
    private readonly Dictionary<DateOnly, List<NoteInfo>> candidates = [];
    private readonly HashSet<string> tagged = new(StringComparer.Ordinal);
    private readonly DateOnly? oldest;
    private DateOnly? next;

    public Journal(NoteStore notes, DateOnly today, DateOnly? calendarStart, Func<DateOnly, IReadOnlyList<Appointment>> appointments)
    {
        this.notes = notes;
        this.appointments = appointments;
        oldest = calendarStart;
        foreach (NoteInfo note in notes.List())
        {
            string[] parts = note.Name.Split('/');
            string title = note.Title;
            if (!(parts.Length == 1 || (parts.Length == 3 && parts[0] == MeetingNote.FolderName && title.Length == 10))
                || title.Length < 10 || (title.Length > 10 && title[10] != ' ')
                || !DateOnly.TryParseExact(title[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
                || date > today)
            {
                continue;
            }

            // A legacy name alone is ambiguous. Only its tag extends the note history bound.
            // Untagged matches are already covered by the calendar's bound.
            if (title.Length > 10 && Read(note) is { } legacy && TagParser.Parse(legacy).Contains(MeetingNote.Tag))
            {
                tagged.Add(note.Name);
            }
            if (title.Length == 10 || tagged.Contains(note.Name))
            {
                oldest = oldest is null || date < oldest ? date : oldest;
            }

            if (!candidates.TryGetValue(date, out List<NoteInfo>? group))
            {
                group = [];
                candidates.Add(date, group);
            }
            group.Add(note);
        }
        next = oldest <= today ? today : null;
    }

    public bool Exhausted => next is null;

    public IReadOnlyList<JournalDay> ReadBatch()
    {
        List<JournalDay> result = [];
        for (int i = 0; i < 14 && next is { } date; i++)
        {
            IReadOnlyList<Appointment> items = appointments(date);
            string? daily = null;
            string? body = null;
            List<string> meetings = [];
            foreach (NoteInfo note in candidates.GetValueOrDefault(date) ?? [])
            {
                if (note.Parent is null && note.Title.Length == 10)
                {
                    string? text = Read(note);
                    if (text is not null)
                    {
                        daily = note.Name;
                        string clean = SafeText.Clean(text);
                        int newline = clean.IndexOf('\n');
                        string line = newline < 0 ? clean : clean[..newline];
                        body = line.StartsWith("# ", StringComparison.Ordinal) && line[2..].Trim() == note.Name
                            ? newline < 0 ? string.Empty : clean[(newline + 1)..]
                            : clean;
                    }
                }
                else if (note.Parent is not null || items.Any(a => MeetingNote.IsNoteOf(a, note.Name)) || tagged.Contains(note.Name))
                {
                    meetings.Add(note.Name);
                }
            }
            if (!string.IsNullOrWhiteSpace(body) || meetings.Count > 0 || items.Count > 0)
            {
                result.Add(new JournalDay { Date = date, Daily = daily, Body = body, Meetings = meetings, Appointments = items });
            }
            next = date == oldest ? null : date.AddDays(-1);
        }
        return result;
    }

    private string? Read(NoteInfo note)
    {
        try
        {
            return notes.Read(note);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
