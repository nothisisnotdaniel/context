namespace Context;

/// <summary>The configured sources for one journal. A missing source leaves the others available.</summary>
internal sealed class JournalFiles : IDisposable
{
    private readonly Dictionary<string, JournalDirectory?> roots = new(StringComparer.Ordinal);
    private readonly JournalDirectory? notes;
    private readonly JournalDirectory? calendars;
    private readonly JournalDirectory? cache;

    public JournalFiles(NoteStore store, CalendarStore calendar, CalendarFeed? feed)
    {
        notes = Root(store.Root);
        calendars = Root(Path.GetDirectoryName(Path.GetFullPath(calendar.Folder))!)?.BindCalendar();
        cache = feed is null ? null : Root(Path.GetDirectoryName(feed.CachePath)!);
    }

    private JournalDirectory? Root(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!roots.TryGetValue(path, out JournalDirectory? directory))
        {
            roots[path] = directory = JournalDirectory.Bind(path);
        }
        return directory;
    }

    public IReadOnlyList<NoteInfo> ListNotes() => notes?.ListNotes() ?? [];
    public JournalNoteLink Link(NoteInfo note) => new(note, notes);
    public FileStream OpenNote(NoteInfo note) => notes?.OpenNote(note) ?? throw new IOException("Note folder unavailable.");
    public bool ReadOutlook()
    {
        try
        {
            return notes?.ReadOutlook() == true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
    public string? ReadCache() => cache?.ReadCalendar(CalendarFeed.CacheFileName);

    public IEnumerable<(string Name, string? Text)> ReadCalendars() => calendars?.ReadCalendars() ?? [];

    public void Dispose()
    {
        calendars?.Dispose();
        foreach (JournalDirectory? root in roots.Values)
        {
            root?.Dispose();
        }
    }
}
