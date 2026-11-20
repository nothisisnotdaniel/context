using System.Globalization;
using System.Text;

namespace Context;

/// <summary>What one <see cref="TodoMarkerConversion.Run(NoteStore, NoteHistory, Func{DateTime})"/> did.</summary>
internal sealed record TodoMarkerConversionResult
{
    public TodoMarkerConversionResult(bool ran, int lines, int notes, IReadOnlyList<string> skipped)
    {
        Ran = ran;
        Lines = lines;
        Notes = notes;
        Skipped = skipped;
    }

    /// <summary>False when the record already existed or there is no data folder: nothing was read or written.</summary>
    public bool Ran { get; }

    /// <summary>The todo lines that became <c>- { }</c> / <c>- {x}</c>.</summary>
    public int Lines { get; }

    /// <summary>The notes those lines are in.</summary>
    public int Notes { get; }

    /// <summary>
    /// The names (raw) of the notes that had todo lines but were not converted: too large, unreadable, failed to write
    /// or changed in between. Each one keeps its bytes.
    /// </summary>
    public IReadOnlyList<string> Skipped { get; }

    /// <summary>Only the type name, so note names never leak through formatting.</summary>
    public override string ToString() => nameof(TodoMarkerConversionResult);
}

/// <summary>
/// The one-off conversion of job 329: note todos became <c>- { } text</c> and <c>- [ ] text</c> a checkbox that stays in
/// its note, so every old todo line <c>- [ ] text</c> / <c>- [x] text</c> is rewritten once per data folder to
/// <c>- { } text</c> / <c>- {x} text</c>. Called by the command-line tool before every todo read and by the desktop
/// actions once at start; the record <c>&lt;root&gt;/.migrations/todo-braces</c> stops every later run.
/// </summary>
internal static class TodoMarkerConversion
{
    /// <summary>The folder of the one-off migration records, in the data folder.</summary>
    public const string FolderName = ".migrations";

    /// <summary>The record's file name inside <see cref="FolderName"/>.</summary>
    public const string RecordName = "todo-braces";

    /// <summary>The record's path in the data folder <paramref name="root"/>.</summary>
    public static string RecordPath(string root) => Path.Combine(root, FolderName, RecordName);

    /// <summary>The report when lines were changed.</summary>
    public static string ChangedText(int lines, int notes) =>
        $"Changed {lines.ToString(CultureInfo.InvariantCulture)} todo line(s) in "
        + $"{notes.ToString(CultureInfo.InvariantCulture)} note(s) to - {{ }}.";

    /// <summary>The report line for one skipped note; <paramref name="note"/> is the shown (cleaned) note name.</summary>
    public static string SkippedText(string note) => $"Can't convert the todos in {note}.";

    /// <summary>
    /// The report lines of <paramref name="result"/>: <see cref="ChangedText"/> when a line changed, then one
    /// <see cref="SkippedText"/> per skipped note (its name through <paramref name="clean"/>). Empty when nothing
    /// changed and nothing was skipped.
    /// </summary>
    public static IReadOnlyList<string> Report(TodoMarkerConversionResult result, Func<string, string> clean)
    {
        List<string> lines = [];
        if (result.Lines > 0)
        {
            lines.Add(ChangedText(result.Lines, result.Notes));
        }

        lines.AddRange(result.Skipped.Select(name => SkippedText(clean(name))));
        return lines;
    }

    /// <summary>
    /// Converts every plain note of <paramref name="store"/> once, unless the record exists or the data folder doesn't
    /// (then nothing is read or written and <see cref="TodoMarkerConversionResult.Ran"/> is false). In each note
    /// (<see cref="NoteStore.List"/>: <c>.md</c> only, a sensitive <c>.ctxs</c> note is never opened) every line outside
    /// fences that is <see cref="IsOldTodo"/> gets its box replaced (<c>[ ]</c> by <c>{ }</c>, <c>[x]</c>/<c>[X]</c> by
    /// <c>{x}</c>); every other byte stays (CRLF, BOM, indent). The note is read with
    /// <see cref="NoteStore.ReadForEdit"/> and saved through
    /// <see cref="NoteStore.SaveIfUnchanged(EditableNote, string, out EditableNote?, NoteHistory?)"/>, so a note that
    /// changed in between is not written and the old bytes become a version in <paramref name="history"/>. A note that
    /// is too large, can't be read or written, or changed in between keeps its bytes and is listed as skipped. When the
    /// notes are done the record is written through <see cref="PrivateFile"/> with the time from
    /// <paramref name="utcNow"/> (also when notes were skipped). Never throws for a read or write failure.
    /// </summary>
    public static TodoMarkerConversionResult Run(NoteStore store, NoteHistory history, Func<DateTime> utcNow) =>
        Run(store, history, utcNow, null);

    /// <summary>Like <see cref="Run(NoteStore, NoteHistory, Func{DateTime})"/>; <paramref name="beforeSave"/> runs with the note's name after its read, before its save.</summary>
    internal static TodoMarkerConversionResult Run(
        NoteStore store, NoteHistory history, Func<DateTime> utcNow, Action<string>? beforeSave)
    {
        string record = RecordPath(store.Root);
        if (!Directory.Exists(store.Root) || File.Exists(record) || Directory.Exists(record))
        {
            return new TodoMarkerConversionResult(false, 0, 0, []);
        }

        IReadOnlyList<NoteInfo> notes;
        try
        {
            notes = store.List();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing could be listed: no record, so the next start tries again.
            return new TodoMarkerConversionResult(false, 0, 0, []);
        }

        int lines = 0;
        int changedNotes = 0;
        List<string> skipped = [];
        foreach (NoteInfo note in notes)
        {
            int changed = ConvertNote(store, history, note, beforeSave);
            if (changed > 0)
            {
                lines += changed;
                changedNotes++;
            }
            else if (changed < 0)
            {
                skipped.Add(note.Name);
            }
        }

        WriteRecord(store.Root, record, utcNow(), lines, changedNotes, skipped.Count);
        return new TodoMarkerConversionResult(true, lines, changedNotes, skipped);
    }

    /// <summary>
    /// True when <paramref name="line"/> (one line, no line end) is an old note todo: optional spaces or tabs,
    /// <c>-</c>, <c>*</c> or <c>+</c>, one space, <c>[ ]</c>, <c>[x]</c> or <c>[X]</c>, one space, then text, and its
    /// open form (<c>[ ]</c>) is a line <see cref="NoteTodoParser.ParseCheckboxes"/> takes (non-empty text, no topic).
    /// <paramref name="box"/> is the index of the <c>[</c>.
    /// </summary>
    internal static bool IsOldTodo(int number, string line, out int box)
    {
        box = -1;
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
        {
            i++;
        }

        if (line.Length - i < 7
            || line[i] is not ('-' or '*' or '+')
            || line[i + 1] != ' '
            || line[i + 2] != '['
            || line[i + 3] is not (' ' or 'x' or 'X')
            || line[i + 4] != ']'
            || line[i + 5] != ' ')
        {
            return false;
        }

        string open = line[i + 3] == ' '
            ? line
            : string.Concat(line.AsSpan(0, i + 3), " ", line.AsSpan(i + 4));
        if (NoteTodoParser.ParseCheckboxLine(number, open) is null)
        {
            return false;
        }

        box = i + 2;
        return true;
    }

    /// <summary>
    /// <paramref name="text"/> (<c>\n</c> line ends) with every old todo line's box replaced, and how many lines
    /// changed.
    /// </summary>
    internal static string Convert(string text, out int changed)
    {
        changed = 0;
        StringBuilder? converted = null;
        foreach ((int number, int start, string line) in TagParser.IndexedLinesOutsideFences(text))
        {
            if (!IsOldTodo(number, line, out int box))
            {
                continue;
            }

            converted ??= new StringBuilder(text);
            int at = start + box;
            bool done = text[at + 1] != ' ';
            converted[at] = '{';
            converted[at + 1] = done ? 'x' : ' ';
            converted[at + 2] = '}';
            changed++;
        }

        return converted?.ToString() ?? text;
    }

    // The number of lines changed in the note; 0 when it has none (or is gone); -1 when it had some but was skipped.
    private static int ConvertNote(NoteStore store, NoteHistory history, NoteInfo note, Action<string>? beforeSave)
    {
        try
        {
            EditableNote opened = store.ReadForEdit(note);
            string converted = Convert(opened.Text, out int changed);
            if (changed == 0)
            {
                return 0;
            }

            beforeSave?.Invoke(note.Name);
            return store.SaveIfUnchanged(opened, converted, out _, history) switch
            {
                NoteSaveResult.Saved => changed,
                NoteSaveResult.Missing => 0,
                _ => -1,
            };
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Too large (FileTooLargeException), unreadable, or the write or the history failed.
            return -1;
        }
    }

    // The record of the run; a failed write leaves no record, so the next start runs again (and finds nothing left).
    private static void WriteRecord(string root, string record, DateTime utc, int lines, int notes, int skipped)
    {
        string body = string.Create(
            CultureInfo.InvariantCulture,
            $"todo-braces\nconverted: {utc.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}\nlines: {lines}\nnotes: {notes}\nskipped: {skipped}\n");
        try
        {
            PrivateFile.CreateFolder(Path.Combine(root, FolderName));
            PrivateFile.Write(record, Encoding.UTF8.GetBytes(body));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another run wrote it first, or the folder can't be written.
        }
    }
}
