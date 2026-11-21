using System.Text;

namespace Context;

/// <summary>What one <see cref="DecisionMigration.Run"/> did.</summary>
internal sealed record DecisionMigrationReport(bool Ran, int Notes, IReadOnlyList<string> Failed)
{
    /// <summary>Only the type name, so note names never leak through formatting.</summary>
    public override string ToString() => nameof(DecisionMigrationReport);
}

/// <summary>
/// The one-time move of job 343's <c>#!d</c> markers to job 349's <c>!d &lt;prefix&gt;-&lt;n&gt;</c> markers, once per
/// data folder (<see cref="DecisionFolder.Migrated"/>), run by the desktop app at start before the first numbering.
/// </summary>
internal static class DecisionMigration
{
    /// <summary>
    /// Unless <paramref name="folder"/> is already migrated, rewrites every readable plain note (<see cref="NoteStore.List"/>;
    /// a sensitive note is never opened) that has a job 343 marker (<see cref="DecisionParser.LegacyMarkers"/>) and no
    /// other folder's <c>decision-folder::</c> GUID: <c>#!d&lt;n&gt;</c> becomes <c>!d &lt;prefix&gt;-&lt;n&gt;</c> with
    /// the same number, a bare <c>#!d</c> becomes <c>!d</c> plus a fresh number (reserved above every number a note still
    /// holds), and the <c>decision-folder::</c> line is added. References and every other byte stay. The note is read with
    /// <see cref="NoteStore.ReadForEdit"/> and saved with
    /// <see cref="NoteStore.SaveIfUnchanged(EditableNote, string, out EditableNote?, NoteHistory?)"/>, so its old bytes are a
    /// version in <paramref name="history"/> first and a note changed in between is not written. Another folder's note,
    /// a note over the size cap or one that can't be read counts as done, unchanged. When no note failed (a write, the
    /// history, a change in between, a note that is not strict UTF-8), the folder file is marked migrated; else the next
    /// start runs again, finding the migrated notes with nothing left to change. Never throws.
    /// </summary>
    public static DecisionMigrationReport Run(
        TagIndex index, NoteStore store, NoteHistory? history, DecisionFolder folder, Action<string>? beforeSave = null)
    {
        if (folder.Migrated)
        {
            return new DecisionMigrationReport(false, 0, []);
        }

        IReadOnlyList<NoteInfo> notes;
        try
        {
            notes = store.List();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DecisionMigrationReport(false, 0, []);
        }

        List<(NoteInfo Note, EditableNote Opened, IReadOnlyList<(int Index, int Length, int? Number)> Markers)> pending = [];
        HashSet<int> taken = [];
        List<string> failed = [];
        foreach (NoteInfo note in notes)
        {
            EditableNote opened;
            try
            {
                opened = store.ReadForEdit(note);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Gone, unreadable or over the cap: skipped, counted as done.
                continue;
            }

            if (DecisionFolder.IsForeign(opened.Text, folder.Guid))
            {
                continue;
            }

            IReadOnlyList<(int Index, int Length, int? Number)> markers = DecisionParser.LegacyMarkers(opened.Text);
            if (markers.Count == 0)
            {
                continue;
            }

            foreach ((_, _, int? number) in markers)
            {
                if (number is int n)
                {
                    taken.Add(n);
                }
            }

            pending.Add((note, opened, markers));
        }

        int written = 0;
        foreach ((NoteInfo note, EditableNote opened, IReadOnlyList<(int Index, int Length, int? Number)> markers) in pending)
        {
            try
            {
                int bare = markers.Count(m => m.Number is null);
                int? first = null;
                if (bare > 0)
                {
                    first = index.ReserveDecisionNumbers(folder.Prefix, bare, taken);
                    if (first is null)
                    {
                        failed.Add(note.Name);
                        continue;
                    }
                }

                string text = Converted(opened.Text, markers, folder, first);
                if (DecisionFolder.NoteGuid(text) is null)
                {
                    text = DecisionFolder.WithFolderLine(text, folder.Guid);
                }

                if (!StrictUtf8Note(note, opened))
                {
                    failed.Add(note.Name);
                    continue;
                }

                beforeSave?.Invoke(note.Name);
                switch (store.SaveIfUnchanged(opened, text, out _, history))
                {
                    case NoteSaveResult.Saved:
                        written++;
                        break;
                    case NoteSaveResult.Missing:
                    case NoteSaveResult.Unchanged:
                        break;
                    default:
                        failed.Add(note.Name);
                        break;
                }
            }
            catch (Exception)
            {
                // The write, the history or the index failed: the note keeps its bytes and the next start tries again.
                failed.Add(note.Name);
            }
        }

        if (failed.Count == 0)
        {
            DecisionFolder.MarkMigrated(store.Root, folder);
        }

        return new DecisionMigrationReport(true, written, failed);
    }

    /// <summary>
    /// <paramref name="text"/> with each legacy marker (<see cref="DecisionParser.LegacyMarkers"/>) rewritten:
    /// <c>#!d&lt;n&gt;</c> to <c>!d &lt;prefix&gt;-&lt;n&gt;</c>, a bare <c>#!d</c> to <c>!d &lt;prefix&gt;-&lt;first + k&gt;</c>
    /// (the k-th bare one), or to <c>!d</c> when <paramref name="first"/> is <c>null</c>.
    /// </summary>
    internal static string Converted(
        string text, IReadOnlyList<(int Index, int Length, int? Number)> markers, DecisionFolder folder, int? first)
    {
        StringBuilder builder = new(text.Length + (markers.Count * 8));
        int at = 0;
        int bare = 0;
        foreach ((int index, int length, int? number) in markers)
        {
            builder.Append(text, at, index - at);
            builder.Append(DecisionParser.Marker);
            if (number is int n)
            {
                builder.Append(' ').Append(DecisionParser.Label(folder.Prefix, n));
            }
            else if (first is int from)
            {
                builder.Append(' ').Append(DecisionParser.Label(folder.Prefix, from + bare));
                bare++;
            }

            at = index + length;
        }

        builder.Append(text, at, text.Length - at);
        return builder.ToString();
    }

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    // True when the note's bytes are strict UTF-8 (an optional UTF-8 BOM, then no invalid byte), so the save changes no
    // other byte than the markers. A note changed since the read is left to SaveIfUnchanged.
    private static bool StrictUtf8Note(NoteInfo note, EditableNote opened)
    {
        byte[] raw = BoundedFile.Read(note.Path, NoteStore.MaxEditBytes);
        if (EditableNote.Hash(raw) != opened.Version)
        {
            return true;
        }

        ReadOnlySpan<byte> body = raw.AsSpan();
        if (body.StartsWith(Utf8Bom))
        {
            body = body[Utf8Bom.Length..];
        }

        try
        {
            StrictUtf8.GetCharCount(body);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
