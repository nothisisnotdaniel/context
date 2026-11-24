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

internal sealed class Journal : IDisposable
{
    private readonly Func<NoteInfo, FileStream> openPlain;
    private readonly Func<DateOnly, IReadOnlyList<Appointment>> appointments;
    private readonly Dictionary<DateOnly, List<NoteInfo>> candidates = [];
    private readonly DateOnly? oldestLocal;
    private readonly Func<DateOnly?>? outlookStart;
    private readonly IDisposable? sources;
    private DateOnly? next;

    public Journal(Func<IReadOnlyList<NoteInfo>> listNotes, Func<NoteInfo, FileStream> openPlain,
        DateOnly today, DateOnly? calendarStart, Func<DateOnly, IReadOnlyList<Appointment>> appointments,
        Func<DateOnly?>? outlookStart = null, IDisposable? sources = null)
    {
        this.openPlain = openPlain;
        this.appointments = appointments;
        this.outlookStart = outlookStart;
        this.sources = sources;
        oldestLocal = calendarStart;
        IReadOnlyList<NoteInfo> notes;
        try
        {
            notes = listNotes();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            notes = [];
        }

        // Like the editor's exact-name lookup, select the first listed file for each name.
        foreach (NoteInfo note in notes.DistinctBy(n => n.Name, StringComparer.Ordinal))
        {
            string[] parts = note.Name.Split('/');
            string title = note.Title;
            if (!(parts.Length == 1 || (parts.Length == 3 && parts[0].Equals(MeetingNote.FolderName, StringComparison.OrdinalIgnoreCase) && title.Length == 10))
                || title.Length < 10 || (title.Length > 10 && title[10] != ' ')
                || !DateOnly.TryParseExact(title[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
                || date > today)
            {
                continue;
            }

            // A legacy name alone is ambiguous. Only its tag extends the note history bound.
            // Untagged matches are already covered by the calendar's bound.
            if (title.Length == 10 || HasMeetingTag(note))
            {
                oldestLocal = oldestLocal is null || date < oldestLocal ? date : oldestLocal;
            }

            if (!candidates.TryGetValue(date, out List<NoteInfo>? group))
            {
                group = [];
                candidates.Add(date, group);
            }
            group.Add(note);
        }
        next = Oldest <= today ? today : null;
    }

    public bool Exhausted => next is null;

    public void Dispose() => sources?.Dispose();

    // A failed Outlook day read withdraws its bound. Notes and readable calendars keep their own bound.
    private DateOnly? Oldest => outlookStart?.Invoke() is { } date && (oldestLocal is null || date < oldestLocal)
        ? date : oldestLocal;

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
                        int newline = text.IndexOf('\n');
                        string line = newline < 0 ? text : text[..newline];
                        if (line.StartsWith("# ", StringComparison.Ordinal) && line[2..].Trim() == note.Name)
                        {
                            text = newline < 0 ? string.Empty : text[(newline + 1)..];
                        }
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            body = SafeText.Clean(text);
                        }
                    }
                }
                else if (note.Parent is not null || items.Any(a => MeetingNote.IsNoteOf(a, note.Name)))
                {
                    if (CanOpenForEdit(note))
                    {
                        meetings.Add(note.Name);
                    }
                }
                else if (HasMeetingTag(note))
                {
                    meetings.Add(note.Name);
                }
            }
            if (body is not null || meetings.Count > 0 || items.Count > 0)
            {
                result.Add(new JournalDay { Date = date, Daily = daily, Body = body, Meetings = meetings, Appointments = items });
            }
            next = Oldest is { } oldest && date > oldest ? date.AddDays(-1) : null;
        }
        return result;
    }

    private bool HasMeetingTag(NoteInfo note) =>
        Read(note) is { } text && TagParser.Parse(text).Contains(MeetingNote.Tag);

    private bool CanOpenForEdit(NoteInfo note)
    {
        try
        {
            using FileStream plain = openPlain(note);
            return plain.CanSeek && plain.Length <= NoteStore.MaxEditBytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string? Read(NoteInfo note)
    {
        try
        {
            using FileStream plain = openPlain(note);
            return NoteStore.DecodeText(BoundedFile.Read(plain, NoteStore.MaxNoteBytes));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
