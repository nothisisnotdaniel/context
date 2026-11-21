using Microsoft.Data.Sqlite;

namespace Context;

/// <summary>A tag and how many notes have it.</summary>
internal sealed record TagCount
{
    public TagCount(string tag, int count)
    {
        Tag = tag;
        Count = count;
    }

    public string Tag { get; }

    public int Count { get; }
}

/// <summary>A person mentioned with <c>@[Name]</c> and how many notes mention them.</summary>
internal sealed record PersonCount
{
    public PersonCount(string person, int count)
    {
        Person = person;
        Count = count;
    }

    /// <summary>
    /// Returns only the type name: the name is raw outside text, so formatting this record must never print it.
    /// </summary>
    public override string ToString() => nameof(PersonCount);

    /// <summary>
    /// The name shown for the person (raw): the title of their page under <c>people</c> when it exists, else the
    /// ordinal-first spelling among the mentions.
    /// </summary>
    public string Person { get; }

    /// <summary>The number of notes that mention the person, not counting the person's own page.</summary>
    public int Count { get; }
}

/// <summary>Which way the <c>[[note links]]</c> between two notes go, seen from the note asked about.</summary>
internal enum NoteLinkDirection
{
    /// <summary>No <c>[[link]]</c> either way: the notes only share tags.</summary>
    None,

    /// <summary>The note asked about links to <see cref="NoteLink.Note"/>.</summary>
    To,

    /// <summary><see cref="NoteLink.Note"/> links to the note asked about.</summary>
    From,

    /// <summary>Both notes link to each other.</summary>
    Both,
}

/// <summary>
/// A note linked to another note: by the tags the two share (sorted), by <c>[[note links]]</c>
/// (<see cref="Direction"/>), or both.
/// </summary>
internal sealed record NoteLink
{
    /// <summary>A link by shared tags only: <see cref="Direction"/> is <c>None</c>, <see cref="Missing"/> false.</summary>
    public NoteLink(string note, IReadOnlyList<string> sharedTags)
        : this(note, sharedTags, NoteLinkDirection.None, missing: false)
    {
    }

    public NoteLink(string note, IReadOnlyList<string> sharedTags, NoteLinkDirection direction, bool missing)
    {
        Note = note;
        SharedTags = sharedTags;
        Direction = direction;
        Missing = missing;
    }

    /// <summary>
    /// The other note's name (raw), or for a <see cref="Missing"/> note the link target as written.
    /// </summary>
    public string Note { get; }

    /// <summary>The tags the two notes share, sorted (ordinal); empty when they share none.</summary>
    public IReadOnlyList<string> SharedTags { get; }

    /// <summary>Which way the <c>[[note links]]</c> go; <c>None</c> when the notes only share tags.</summary>
    public NoteLinkDirection Direction { get; }

    /// <summary>
    /// True when the note asked about links to <see cref="Note"/> but no note has that name yet. Then
    /// <see cref="Direction"/> is <c>To</c> and <see cref="SharedTags"/> is empty. Once the note exists, the
    /// link resolves without the linking note being edited.
    /// </summary>
    public bool Missing { get; }
}

/// <summary>The indexed note names, for matching typed names and <c>[[link]]</c> targets in constant time.</summary>
internal sealed class NameLookup
{
    private readonly HashSet<string> exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> folded = new(StringComparer.OrdinalIgnoreCase);

    public NameLookup(IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            exact.Add(name);
            folded.TryAdd(name, name);
        }
    }

    /// <summary>The note called <paramref name="wanted"/>: an exact match first, then one ignoring case.</summary>
    public string? Match(string wanted)
    {
        if (exact.Contains(wanted))
        {
            return wanted;
        }

        return folded.TryGetValue(wanted, out string? name) ? name : null;
    }

    /// <summary>
    /// The note a <c>[[link]]</c> target names, like <c>/open</c>: the target as written, then with one trailing
    /// <c>.md</c> removed (ignoring case); each step is <see cref="Match"/>. <c>null</c> if no note matches.
    /// </summary>
    public string? Resolve(string target)
    {
        string? found = Match(target);
        if (found is null && target.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && target.Length > 3)
        {
            found = Match(target[..^3]);
        }

        return found;
    }

    /// <summary>
    /// The note and heading a <c>[[link]]</c> target names: the whole target through <see cref="Resolve"/> first (so a
    /// title with a <c>#</c>, like <c>C# notes</c>, still works, with no heading), then the note part of
    /// <see cref="NoteLinkParser.Split"/> with its heading. <see cref="NoteLinkTarget.Note"/> is the resolved name.
    /// <c>null</c> if no note matches (an empty note part, as in <c>[[#Intro]]</c>, names none).
    /// </summary>
    public NoteLinkTarget? ResolveLink(string target)
    {
        if (Resolve(target) is { } whole)
        {
            return new NoteLinkTarget(whole, null);
        }

        NoteLinkTarget split = NoteLinkParser.Split(target);
        if (split.Note.Length == 0)
        {
            return null;
        }

        return Resolve(split.Note) is { } name ? new NoteLinkTarget(name, split.Heading) : null;
    }
}

/// <summary>
/// A decision row of the index (job 343): the note, the marker's 1-based line, its number (<c>null</c> when bare),
/// the paragraph text (raw) and the note's stored last-write time (UTC).
/// </summary>
internal sealed record IndexedDecision(string Note, int Line, int? Number, string Text, DateTime Modified)
{
    /// <summary>The first line of <see cref="Text"/>.</summary>
    public string Headline => Text.IndexOf('\n', StringComparison.Ordinal) is int end and >= 0 ? Text[..end] : Text;

    /// <summary>Returns only the type name: the text is raw note text.</summary>
    public override string ToString() => nameof(IndexedDecision);
}

/// <summary>
/// Thrown when <c>context.db</c> can't be opened or isn't a valid database. The file is never
/// deleted or overwritten then.
/// </summary>
internal sealed class TagIndexException : IOException
{
    public TagIndexException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// The index of which note has which tag, kept in SQLite at <c>&lt;root&gt;/context.db</c>. The Markdown
/// files are the source of truth: <see cref="Sync"/> rebuilds the index from them, never the other way
/// round. Every value reaches SQL as a parameter.
/// </summary>
internal sealed class TagIndex
{
    /// <summary>The file name of the database inside the root.</summary>
    public const string FileName = "context.db";

    private const string DueFormat = "yyyy-MM-dd";

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS notes(name TEXT NOT NULL PRIMARY KEY, modified INTEGER);
        CREATE TABLE IF NOT EXISTS note_tags(
            note TEXT NOT NULL,
            tag TEXT NOT NULL,
            PRIMARY KEY(note, tag));
        CREATE INDEX IF NOT EXISTS note_tags_tag ON note_tags(tag);
        CREATE TABLE IF NOT EXISTS note_links(
            note TEXT NOT NULL,
            target TEXT NOT NULL,
            heading TEXT,
            whole_key TEXT,
            part_key TEXT,
            PRIMARY KEY(note, target));
        CREATE TABLE IF NOT EXISTS note_people(
            note TEXT NOT NULL,
            person TEXT NOT NULL,
            spelling TEXT NOT NULL,
            PRIMARY KEY(note, person));
        CREATE INDEX IF NOT EXISTS note_people_person ON note_people(person);
        CREATE TABLE IF NOT EXISTS note_attributes(
            note TEXT NOT NULL,
            key TEXT NOT NULL,
            value_key TEXT NOT NULL,
            value TEXT NOT NULL,
            PRIMARY KEY(note, key, value_key));
        CREATE INDEX IF NOT EXISTS note_attributes_key_value ON note_attributes(key, value_key);
        CREATE TABLE IF NOT EXISTS note_decisions(
            note TEXT NOT NULL,
            line INTEGER NOT NULL,
            text TEXT NOT NULL,
            number INTEGER,
            PRIMARY KEY(note, line));
        CREATE TABLE IF NOT EXISTS decision_owners(number INTEGER PRIMARY KEY, note TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS decision_counter(high INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS note_todos(
            note TEXT NOT NULL,
            line INTEGER NOT NULL,
            due TEXT,
            text TEXT NOT NULL,
            raw TEXT NOT NULL,
            PRIMARY KEY(note, line));
        CREATE TABLE IF NOT EXISTS note_topics(
            note TEXT NOT NULL,
            line INTEGER NOT NULL,
            person TEXT NOT NULL,
            text TEXT NOT NULL,
            raw TEXT NOT NULL,
            PRIMARY KEY(note, line));
        CREATE TABLE IF NOT EXISTS note_reviews(
            note TEXT NOT NULL,
            line INTEGER NOT NULL,
            due TEXT NOT NULL,
            raw TEXT NOT NULL,
            PRIMARY KEY(note));
        CREATE TABLE IF NOT EXISTS note_items(
            note TEXT NOT NULL,
            item INTEGER NOT NULL,
            PRIMARY KEY(note, item));
        CREATE INDEX IF NOT EXISTS note_items_item ON note_items(item);
        CREATE VIRTUAL TABLE IF NOT EXISTS note_text USING fts5(note UNINDEXED, body, tokenize='trigram');
        CREATE TABLE IF NOT EXISTS note_text_rows(note TEXT PRIMARY KEY, row INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS note_skipped(note TEXT PRIMARY KEY);
        """;

    /// <summary>
    /// The one read of <c>note_links</c> in <see cref="Links"/>: only the note's own rows and the rows whose whole target
    /// or note part could name it (<c>$k</c> = the note name upper-cased invariant, <c>$kmd</c> = <c>$k</c> + <c>.MD</c>),
    /// through the primary key and the <c>whole_key</c> and <c>part_key</c> indexes, never a scan of the table.
    /// </summary>
    internal const string LinksSql =
        "SELECT note, target FROM note_links "
            + "WHERE note = $note OR whole_key IN ($k, $kmd) OR part_key IN ($k, $kmd) ORDER BY note, target";

    /// <summary>
    /// The parser version the stored tags were made with, kept in <c>PRAGMA user_version</c>. When the
    /// database has an older one, <see cref="Sync"/> re-parses every note once. 1: <c>#[bracket tags]</c>.
    /// 2: <c>[[note links]]</c>. 3: subpages (full names, link targets up to <see cref="NoteLinkParser.MaxTargetLength"/>).
    /// 4: <c>@[person]</c> mentions (and <c>@[[x]]</c> is no link). 5: <c>key:: value</c> attributes. 6: a link target
    /// that runs into a code span is no link. 7: note text (the full-text table <c>note_text</c>). 8: <c>Decision:</c> lines
    /// (<c>note_decisions</c>). 9: open <c>- [ ]</c> todos (<c>note_todos</c>). 10: the heading of a
    /// <c>[[Note#Heading]]</c> link (<c>note_links.heading</c>). 11: <c>next: @[Name]</c> topics (<c>note_topics</c>),
    /// which are no longer note todos. 12: <c>review:yyyy-MM-dd</c> lines (<c>note_reviews</c>). 13: <c>#&lt;n&gt;</c> work item references
    /// (<c>note_items</c>). 14: <c>note_text_rows</c> (note -> <c>note_text</c> rowid), <c>note_skipped</c> (notes over
    /// <see cref="NoteStore.MaxNoteBytes"/>) and the link keys <c>note_links.whole_key</c>/<c>part_key</c>. 15: note
    /// todos are <c>- { }</c> lines (job 329); a <c>- [ ]</c> line is a note-local checkbox and no longer in <c>note_todos</c>.
    /// 16: an <c>affiliation::</c> value is a comma-separated list (job 332), one <c>note_attributes</c> row per part.
    /// 17: decisions are <c>#!d</c> paragraphs (job 343), one <c>note_decisions</c> row per paragraph with its
    /// <c>number</c> (NULL for a bare marker); <c>Decision:</c> lines are no longer read.
    /// </summary>
    public const int ParserVersion = 17;

    /// <summary>
    /// How long opening <c>context.db</c> and starting <see cref="Sync"/>'s write transaction wait while another
    /// process (the desktop app, another command line run) holds a lock on it, before giving up with a
    /// <see cref="TagIndexException"/>.
    /// </summary>
    public static readonly TimeSpan BusyTimeout = TimeSpan.FromSeconds(5);

    private readonly string root;
    private readonly TimeSpan busyTimeout;
    private int reads;

    /// <param name="root">The folder <c>context.db</c> lives in.</param>
    public TagIndex(string root)
        : this(root, BusyTimeout)
    {
    }

    /// <summary>Like the public constructor with another busy timeout (tests use a short one).</summary>
    internal TagIndex(string root, TimeSpan busyTimeout)
    {
        this.root = Path.GetFullPath(root);
        this.busyTimeout = busyTimeout;
    }

    /// <summary>How many times this instance opened the database (one per sync or query); for tests.</summary>
    internal int Reads => Volatile.Read(ref reads);

    /// <summary>The full path of <c>context.db</c>.</summary>
    public string FilePath => Path.Combine(root, FileName);

    /// <summary>
    /// Brings the index up to date with the notes in <paramref name="store"/>, in one transaction. Creates
    /// the root folder and the database if missing. A note is re-parsed (tags and <c>[[note links]]</c>) only
    /// when its last-write time (UTC ticks) differs from the stored one. Notes that are gone lose their rows.
    /// A note that can't be read or is over <see cref="NoteStore.MaxNoteBytes"/> is kept with no tags, no links,
    /// no text and no stored time, so it is retried on the next sync. The cap counts the bytes actually read from the
    /// file (<see cref="BoundedFile.Read"/> stops in its read loop), a UTF-8 BOM and invalid UTF-8 bytes included, never
    /// the length of the decoded text: a note of exactly <see cref="NoteStore.MaxNoteBytes"/> bytes is indexed, one byte
    /// more is not. A note over the cap gets a <c>note_skipped</c> row (<see cref="Skipped"/>), which goes once it is
    /// back under the cap, unreadable for another reason, or gone. Link targets are stored as written and resolved
    /// against note names only when queried. Each <c>note_text</c> row's rowid is kept in <c>note_text_rows</c> and the
    /// row is deleted by that rowid; a sync first drops every <c>note_text</c> row that is not its note's mapped row and
    /// every mapping whose row is gone or names another note (written by an older version, for example), and re-parses
    /// those notes. A re-parsed
    /// note's text is written to <c>note_text</c> again (no row for a note that can't be read or is over
    /// <see cref="NoteStore.MaxNoteBytes"/>); sensitive notes are never listed, so never indexed.
    /// Throws <see cref="TagIndexException"/> if the database can't be opened or isn't valid.
    /// </summary>
    public void Sync(NoteStore store)
    {
        PrivateFile.CreateFolder(root);
        IReadOnlyList<NoteInfo> notes = store.List();
        Run(connection =>
        {
            // BEGIN IMMEDIATE: take the write lock up front (waiting up to the busy timeout), so a sync never fails
            // half way when another process writes at the same time.
            using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            long version;
            using (SqliteCommand pragma = Command(connection, transaction, "PRAGMA user_version"))
            {
                version = Convert.ToInt64(pragma.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            }

            EnsureSecureDelete(connection, transaction);
            bool upgrade = version < ParserVersion;
            if (upgrade)
            {
                // Tags were parsed by an older parser: forget the stored times so every note is re-parsed. Every note
                // text is written again then, so the old rows go in one statement instead of one scan per note.
                Execute(connection, transaction, "UPDATE notes SET modified = NULL");
                Execute(connection, transaction, "DELETE FROM note_text");
                Execute(connection, transaction, "DELETE FROM note_text_rows");
            }
            else
            {
                RepairText(connection, transaction);
            }

            Dictionary<string, long?> stored = new(StringComparer.Ordinal);
            using (SqliteCommand select = Command(connection, transaction, "SELECT name, modified FROM notes"))
            using (SqliteDataReader reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    stored[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetInt64(1);
                }
            }

            HashSet<string> current = new(StringComparer.Ordinal);
            foreach (NoteInfo note in notes)
            {
                current.Add(note.Name);
                long modified = File.GetLastWriteTimeUtc(note.Path).Ticks;
                if (stored.TryGetValue(note.Name, out long? known) && known == modified)
                {
                    continue;
                }

                IReadOnlyList<string> tags;
                IReadOnlyList<string> targets;
                IReadOnlyList<string> people;
                IReadOnlyList<PageAttribute> attributes;
                IReadOnlyList<ParsedDecision> decisions;
                IReadOnlyList<ParsedNoteTodo> noteTodos;
                IReadOnlyList<ParsedTopic> topics;
                ParsedReview? review;
                IReadOnlyList<int> items;
                string? body;
                bool tooLarge = false;
                long? newModified = modified;
                try
                {
                    // A note over NoteStore.MaxNoteBytes throws FileTooLargeException and counts as unreadable.
                    string text = store.Read(note);
                    body = text;
                    tags = TagParser.Parse(text);
                    targets = NoteLinkParser.Parse(text);
                    people = PersonParser.Parse(text);
                    attributes = AttributeParser.Parse(text);
                    decisions = DecisionParser.Parse(text);
                    noteTodos = NoteTodoParser.Parse(text);
                    topics = TopicParser.Parse(text);
                    review = ReviewDateParser.Parse(text);
                    items = WorkItemParser.Parse(text);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    tooLarge = ex is FileTooLargeException;
                    tags = [];
                    targets = [];
                    people = [];
                    attributes = [];
                    decisions = [];
                    noteTodos = [];
                    topics = [];
                    review = null;
                    items = [];
                    body = null;
                    newModified = null;
                }

                Execute(connection, transaction, "DELETE FROM note_tags WHERE note = $note", ("$note", note.Name));
                foreach (string tag in tags)
                {
                    Execute(
                        connection,
                        transaction,
                        "INSERT INTO note_tags(note, tag) VALUES($note, $tag)",
                        ("$note", note.Name),
                        ("$tag", tag));
                }

                Execute(connection, transaction, "DELETE FROM note_links WHERE note = $note", ("$note", note.Name));
                foreach (string target in targets)
                {
                    Execute(
                        connection,
                        transaction,
                        "INSERT INTO note_links(note, target, heading, whole_key, part_key) "
                            + "VALUES($note, $target, $heading, $whole, $part)",
                        ("$note", note.Name),
                        ("$target", target),
                        ("$heading", NoteLinkParser.Split(target).Heading),
                        ("$whole", LinkKey(target)),
                        ("$part", LinkKey(NoteLinkParser.Split(target).Note)));
                }

                Execute(connection, transaction, "DELETE FROM note_people WHERE note = $note", ("$note", note.Name));
                foreach (string person in people)
                {
                    Execute(
                        connection,
                        transaction,
                        "INSERT INTO note_people(note, person, spelling) VALUES($note, $person, $spelling)",
                        ("$note", note.Name),
                        ("$person", PersonParser.Key(person)),
                        ("$spelling", person));
                }

                Execute(connection, transaction, "DELETE FROM note_attributes WHERE note = $note", ("$note", note.Name));
                HashSet<string> affiliations = new(StringComparer.Ordinal);
                foreach (PageAttribute attribute in attributes)
                {
                    if (attribute.Key == PeopleAttributes.AffiliationKey)
                    {
                        // An affiliation is a list: one row per part, each value_key once per note (first spelling).
                        foreach (string part in AttributeParser.ListParts(attribute.Value))
                        {
                            string partKey = AttributeParser.ValueKey(part);
                            if (affiliations.Add(partKey))
                            {
                                InsertAttribute(connection, transaction, note.Name, attribute.Key, partKey, part);
                            }
                        }

                        continue;
                    }

                    InsertAttribute(
                        connection, transaction, note.Name, attribute.Key, AttributeParser.ValueKey(attribute.Value), attribute.Value);
                }

                Execute(connection, transaction, "DELETE FROM note_decisions WHERE note = $note", ("$note", note.Name));
                foreach (ParsedDecision decision in decisions)
                {
                    Execute(
                        connection,
                        transaction,
                        "INSERT INTO note_decisions(note, line, text, number) VALUES($note, $line, $text, $number)",
                        ("$note", note.Name),
                        ("$line", decision.Line),
                        ("$text", decision.Text),
                        ("$number", decision.Number));
                }

                Execute(connection, transaction, "DELETE FROM note_todos WHERE note = $note", ("$note", note.Name));
                foreach (ParsedNoteTodo todo in noteTodos)
                {
                    Execute(
                        connection,
                        transaction,
                        "INSERT INTO note_todos(note, line, due, text, raw) VALUES($note, $line, $due, $text, $raw)",
                        ("$note", note.Name),
                        ("$line", todo.Line),
                        ("$due", todo.Due?.ToString(DueFormat, System.Globalization.CultureInfo.InvariantCulture)),
                        ("$text", todo.Text),
                        ("$raw", todo.Raw));
                }

                Execute(connection, transaction, "DELETE FROM note_topics WHERE note = $note", ("$note", note.Name));
                foreach (ParsedTopic topic in topics)
                {
                    Execute(
                        connection,
                        transaction,
                        "INSERT INTO note_topics(note, line, person, text, raw) VALUES($note, $line, $person, $text, $raw)",
                        ("$note", note.Name),
                        ("$line", topic.Line),
                        ("$person", topic.Person),
                        ("$text", topic.Text),
                        ("$raw", topic.Raw));
                }

                Execute(connection, transaction, "DELETE FROM note_reviews WHERE note = $note", ("$note", note.Name));
                if (review is not null)
                {
                    Execute(
                        connection,
                        transaction,
                        "INSERT INTO note_reviews(note, line, due, raw) VALUES($note, $line, $due, $raw)",
                        ("$note", note.Name),
                        ("$line", review.Line),
                        ("$due", review.Due.ToString(DueFormat, System.Globalization.CultureInfo.InvariantCulture)),
                        ("$raw", review.Raw));
                }

                Execute(connection, transaction, "DELETE FROM note_items WHERE note = $note", ("$note", note.Name));
                foreach (int item in items)
                {
                    Execute(
                        connection,
                        transaction,
                        "INSERT INTO note_items(note, item) VALUES($note, $item)",
                        ("$note", note.Name),
                        ("$item", item));
                }

                if (!upgrade)
                {
                    DeleteText(connection, transaction, note.Name);
                }

                if (body is not null)
                {
                    Execute(
                        connection,
                        transaction,
                        "INSERT INTO note_text(note, body) VALUES($note, $body)",
                        ("$note", note.Name),
                        ("$body", body));
                    Execute(
                        connection,
                        transaction,
                        "INSERT INTO note_text_rows(note, row) VALUES($note, last_insert_rowid())",
                        ("$note", note.Name));
                }

                Execute(connection, transaction, "DELETE FROM note_skipped WHERE note = $note", ("$note", note.Name));
                if (tooLarge)
                {
                    Execute(connection, transaction, "INSERT INTO note_skipped(note) VALUES($note)", ("$note", note.Name));
                }

                Execute(
                    connection,
                    transaction,
                    "INSERT INTO notes(name, modified) VALUES($note, $modified) "
                        + "ON CONFLICT(name) DO UPDATE SET modified = excluded.modified",
                    ("$note", note.Name),
                    ("$modified", newModified));
            }

            foreach (string gone in stored.Keys.Where(n => !current.Contains(n)))
            {
                Execute(connection, transaction, "DELETE FROM note_tags WHERE note = $note", ("$note", gone));
                Execute(connection, transaction, "DELETE FROM note_links WHERE note = $note", ("$note", gone));
                Execute(connection, transaction, "DELETE FROM note_people WHERE note = $note", ("$note", gone));
                Execute(connection, transaction, "DELETE FROM note_attributes WHERE note = $note", ("$note", gone));
                Execute(connection, transaction, "DELETE FROM note_decisions WHERE note = $note", ("$note", gone));
                Execute(connection, transaction, "DELETE FROM note_todos WHERE note = $note", ("$note", gone));
                Execute(connection, transaction, "DELETE FROM note_topics WHERE note = $note", ("$note", gone));
                Execute(connection, transaction, "DELETE FROM note_reviews WHERE note = $note", ("$note", gone));
                Execute(connection, transaction, "DELETE FROM note_items WHERE note = $note", ("$note", gone));
                DeleteText(connection, transaction, gone);
                Execute(connection, transaction, "DELETE FROM note_skipped WHERE note = $note", ("$note", gone));
                Execute(connection, transaction, "DELETE FROM notes WHERE name = $note", ("$note", gone));
            }

            // Tag and link rows whose note row is missing (e.g. edited by hand) would otherwise never go away.
            Execute(connection, transaction, "DELETE FROM note_tags WHERE note NOT IN (SELECT name FROM notes)");
            Execute(connection, transaction, "DELETE FROM note_links WHERE note NOT IN (SELECT name FROM notes)");
            Execute(connection, transaction, "DELETE FROM note_people WHERE note NOT IN (SELECT name FROM notes)");
            Execute(connection, transaction, "DELETE FROM note_attributes WHERE note NOT IN (SELECT name FROM notes)");
            Execute(connection, transaction, "DELETE FROM note_decisions WHERE note NOT IN (SELECT name FROM notes)");
            Execute(connection, transaction, "DELETE FROM note_todos WHERE note NOT IN (SELECT name FROM notes)");
            Execute(connection, transaction, "DELETE FROM note_topics WHERE note NOT IN (SELECT name FROM notes)");
            Execute(connection, transaction, "DELETE FROM note_reviews WHERE note NOT IN (SELECT name FROM notes)");
            Execute(connection, transaction, "DELETE FROM note_items WHERE note NOT IN (SELECT name FROM notes)");
            Execute(connection, transaction, "DELETE FROM note_skipped WHERE note NOT IN (SELECT name FROM notes)");
            PruneOrphanText(connection, transaction);
            AssignDecisionOwners(connection, transaction);
            if (upgrade)
            {
                // A constant, not user input: PRAGMA takes no parameters.
                Execute(connection, transaction, $"PRAGMA user_version = {ParserVersion}");
            }

            transaction.Commit();
            return 0;
        });
    }

    /// <summary>
    /// Runs <paramref name="select"/> on each <c>(note, body)</c> row of <c>note_text</c> that contains
    /// <paramref name="text"/> by the trigram tokenizer (ignoring case), unsorted, and keeps only its non-null results:
    /// no body is kept, so each can be released once its row is done. The text reaches SQL only as a parameter, quoted
    /// as one FTS5 phrase, so FTS syntax in it (<c>OR</c>, <c>NEAR</c>, <c>*</c>, <c>-</c>, quotes) is literal text.
    /// Under 3 characters nothing matches. The match is only a pre-filter: <see cref="SearchView.Build"/> checks each
    /// line itself.
    /// </summary>
    public IReadOnlyList<T> Search<T>(string text, Func<string, string, T?> select)
        where T : class
    {
        string phrase = "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return Run(connection =>
        {
            List<T> rows = [];
            using SqliteCommand command = Command(connection, null, "SELECT note, body FROM note_text WHERE note_text MATCH $q");
            command.Parameters.AddWithValue("$q", phrase);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (select(reader.GetString(0), reader.GetString(1)) is { } row)
                {
                    rows.Add(row);
                }
            }

            return (IReadOnlyList<T>)rows;
        });
    }

    /// <summary>
    /// The notes the last <see cref="Sync"/> left out because they are over <see cref="NoteStore.MaxNoteBytes"/> (the
    /// <c>note_skipped</c> rows), sorted by name ignoring case, then ordinal.
    /// </summary>
    public IReadOnlyList<string> Skipped() => Run(connection =>
    {
        List<string> names = [];
        using SqliteCommand command = Command(connection, null, "SELECT note FROM note_skipped");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return (IReadOnlyList<string>)SortNames(names);
    });

    /// <summary>
    /// Every <c>(note, body)</c> row of <c>note_text</c> in one <c>SELECT</c> (no parameters), sorted by note
    /// (ordinal): the whole text of each plain note the last <see cref="Sync"/> could read. Sensitive notes, notes that
    /// couldn't be read and notes over <see cref="NoteStore.MaxNoteBytes"/> have no row. Used by
    /// <see cref="SimilarNotesView.Build"/>.
    /// </summary>
    public IReadOnlyList<(string Note, string Body)> NoteTexts() => Run(connection =>
    {
        List<(string Note, string Body)> rows = [];
        using SqliteCommand command = Command(connection, null, "SELECT note, body FROM note_text");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return (IReadOnlyList<(string Note, string Body)>)rows.OrderBy(r => r.Note, StringComparer.Ordinal).ToList();
    });

    /// <summary>
    /// The decisions of the log, unsorted: every unnumbered <c>note_decisions</c> row, plus for each number only the
    /// first row (lowest line) of its owner (<c>decision_owners</c>), each with its note's stored last-write time (UTC).
    /// Rows of notes without a stored time (they couldn't be read) and of non-owners are left out. With a non-blank
    /// <paramref name="tag"/> (normalized by <see cref="NormalizeTag"/>, reaching SQL only as a parameter) only the
    /// decisions of notes that have that tag.
    /// </summary>
    public IReadOnlyList<IndexedDecision> Decisions(string? tag)
    {
        string? wanted = string.IsNullOrWhiteSpace(tag) ? null : NormalizeTag(tag);
        return Run(connection =>
        {
            string sql = DecisionSelect + " WHERE n.modified IS NOT NULL AND " + OwnedDecision;
            if (wanted is not null)
            {
                sql += " AND d.note IN (SELECT note FROM note_tags WHERE tag = $tag)";
            }

            using SqliteCommand command = Command(connection, null, sql);
            if (wanted is not null)
            {
                command.Parameters.AddWithValue("$tag", wanted);
            }

            return (IReadOnlyList<IndexedDecision>)ReadDecisions(command);
        });
    }

    /// <summary>
    /// The decision numbered <paramref name="number"/> as its owner holds it (the owner's first row with that number), or
    /// <c>null</c> when no readable owner holds it. The number reaches SQL only as a parameter.
    /// </summary>
    public IndexedDecision? DecisionByNumber(int number) => Run(connection =>
    {
        using SqliteCommand command = Command(
            connection, null, DecisionSelect + " WHERE n.modified IS NOT NULL AND d.number = $number AND " + OwnedDecision);
        command.Parameters.AddWithValue("$number", number);
        return ReadDecisions(command).FirstOrDefault();
    });

    /// <summary>
    /// The headline (first text line, raw) of each of <paramref name="numbers"/> that a readable owner holds, read in
    /// one connection; unknown numbers are not in the result. Each number reaches SQL only as a parameter.
    /// </summary>
    public IReadOnlyDictionary<int, string> DecisionHeadlines(IEnumerable<int> numbers)
    {
        List<int> wanted = numbers.Distinct().ToList();
        return Run(connection =>
        {
            Dictionary<int, string> headlines = [];
            foreach (int number in wanted)
            {
                using SqliteCommand command = Command(
                    connection, null, DecisionSelect + " WHERE n.modified IS NOT NULL AND d.number = $number AND " + OwnedDecision);
                command.Parameters.AddWithValue("$number", number);
                if (ReadDecisions(command).FirstOrDefault() is { } decision)
                {
                    headlines[number] = decision.Headline;
                }
            }

            return (IReadOnlyDictionary<int, string>)headlines;
        });
    }

    /// <summary>Every <c>decision_owners</c> row: number -> owning note's full name.</summary>
    public IReadOnlyDictionary<int, string> DecisionOwners() => Run(connection =>
    {
        Dictionary<int, string> owners = [];
        using SqliteCommand command = Command(connection, null, "SELECT number, note FROM decision_owners");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            long number = reader.GetInt64(0);
            if (number is >= 1 and <= DecisionParser.MaxNumber)
            {
                owners[(int)number] = reader.GetString(1);
            }
        }

        return (IReadOnlyDictionary<int, string>)owners;
    });

    /// <summary>
    /// Reserves <paramref name="count"/> consecutive new decision numbers in one <c>BEGIN IMMEDIATE</c> transaction and
    /// returns the first. It is one above the highest of <c>decision_counter</c>, every number in <c>note_decisions</c>
    /// and <c>decision_owners</c>, and <paramref name="floor"/>; the counter then holds the last one, so a number is
    /// never handed out twice while the database lasts (a failed write only leaves a gap). Throws
    /// <see cref="TagIndexException"/> when the numbers would pass <see cref="DecisionParser.MaxNumber"/>.
    /// </summary>
    public int ReserveDecisionNumbers(int count, int floor = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        PrivateFile.CreateFolder(root);
        return Run(connection =>
        {
            using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            long high;
            using (SqliteCommand select = Command(
                connection,
                transaction,
                "SELECT MAX(COALESCE((SELECT MAX(high) FROM decision_counter), 0), "
                    + "COALESCE((SELECT MAX(number) FROM note_decisions), 0), "
                    + "COALESCE((SELECT MAX(number) FROM decision_owners), 0))"))
            {
                high = Convert.ToInt64(select.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            }

            long first = Math.Max(high, floor) + 1;
            long last = first + count - 1;
            if (first < 1 || last > DecisionParser.MaxNumber)
            {
                throw new TagIndexException("No decision numbers are left.");
            }

            Execute(connection, transaction, "DELETE FROM decision_counter");
            Execute(connection, transaction, "INSERT INTO decision_counter(high) VALUES($high)", ("$high", last));
            transaction.Commit();
            return (int)first;
        });
    }

    /// <summary>
    /// After the app renamed or moved <paramref name="oldName"/> to <paramref name="newName"/>: its owner rows, and
    /// those of its subpages (<c>&lt;oldName&gt;/...</c>), name the new note. Compared ordinally, never with
    /// <c>LIKE</c>; both names reach SQL only as parameters.
    /// </summary>
    public void MoveDecisionOwners(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return;
        }

        PrivateFile.CreateFolder(root);
        Run(connection =>
        {
            using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            Execute(
                connection,
                transaction,
                "UPDATE decision_owners SET note = $new WHERE note = $old",
                ("$new", newName),
                ("$old", oldName));
            Execute(
                connection,
                transaction,
                "UPDATE decision_owners SET note = $new || substr(note, length($prefix)) "
                    + "WHERE length(note) > length($prefix) AND substr(note, 1, length($prefix)) = $prefix",
                ("$new", newName),
                ("$prefix", oldName + "/"));
            transaction.Commit();
            return 0;
        });
    }

    /// <summary>
    /// Every row of <c>note_todos</c> of a note with a stored time, unsorted (<see cref="NoteTodos.Merge"/> orders
    /// them). A row edited by hand out of range (line, a due that is no <c>yyyy-MM-dd</c> date) is skipped.
    /// </summary>
    public IReadOnlyList<NoteTodo> NoteTodos() => Run(connection =>
    {
        List<NoteTodo> rows = [];
        using SqliteCommand command = Command(
            connection,
            null,
            "SELECT t.note, t.line, t.due, t.text, t.raw FROM note_todos t "
                + "JOIN notes n ON n.name = t.note WHERE n.modified IS NOT NULL");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            long line = reader.GetInt64(1);
            if (line < 1 || line > int.MaxValue)
            {
                continue;
            }

            DateOnly? due = null;
            if (!reader.IsDBNull(2))
            {
                if (!DateOnly.TryParseExact(
                    reader.GetString(2),
                    DueFormat,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateOnly parsed))
                {
                    continue;
                }

                due = parsed;
            }

            rows.Add(new NoteTodo(reader.GetString(0), (int)line, reader.GetString(3), due, reader.GetString(4)));
        }

        return (IReadOnlyList<NoteTodo>)rows;
    });

    /// <summary>
    /// Every row of <c>note_topics</c> of a note with a stored time, unsorted (<see cref="MeetingTopics.For"/> picks and
    /// orders them). A row edited by hand with a line out of range is skipped.
    /// </summary>
    public IReadOnlyList<NoteTopic> Topics() => Run(connection =>
    {
        List<NoteTopic> rows = [];
        using SqliteCommand command = Command(
            connection,
            null,
            "SELECT t.note, t.line, t.person, t.text, t.raw FROM note_topics t "
                + "JOIN notes n ON n.name = t.note WHERE n.modified IS NOT NULL");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            long line = reader.GetInt64(1);
            if (line < 1 || line > int.MaxValue)
            {
                continue;
            }

            rows.Add(new NoteTopic(
                reader.GetString(0), (int)line, reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }

        return (IReadOnlyList<NoteTopic>)rows;
    });

    /// <summary>
    /// Every row of <c>note_reviews</c> of a note with a stored time, unsorted (<see cref="NoteReviews.Due"/> picks and
    /// orders them). A row edited by hand out of range (line, a due that is no <c>yyyy-MM-dd</c> date) is skipped.
    /// </summary>
    public IReadOnlyList<NoteReview> Reviews() => Run(connection =>
    {
        List<NoteReview> rows = [];
        using SqliteCommand command = Command(
            connection,
            null,
            "SELECT r.note, r.line, r.due, r.raw FROM note_reviews r "
                + "JOIN notes n ON n.name = r.note WHERE n.modified IS NOT NULL");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            long line = reader.GetInt64(1);
            if (line < 1 || line > int.MaxValue
                || !DateOnly.TryParseExact(
                    reader.GetString(2),
                    DueFormat,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateOnly due))
            {
                continue;
            }

            rows.Add(new NoteReview(reader.GetString(0), (int)line, due, reader.GetString(3)));
        }

        return (IReadOnlyList<NoteReview>)rows;
    });

    /// <summary>Every tag with the number of notes that have it, sorted by tag (ordinal).</summary>
    public IReadOnlyList<TagCount> Tags() => Run(connection =>
    {
        List<TagCount> tags = [];
        using SqliteCommand command = Command(connection, null, "SELECT tag, COUNT(*) FROM note_tags GROUP BY tag");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            tags.Add(new TagCount(reader.GetString(0), reader.GetInt32(1)));
        }

        return tags.OrderBy(t => t.Tag, StringComparer.Ordinal).ToList();
    });

    /// <summary>
    /// Every <c>(note, tag)</c> row of <c>note_tags</c> in one <c>SELECT</c> (tags normalized, no <c>#</c>), sorted by
    /// note, then tag (ordinal). Used by the current view to match all appointments in memory.
    /// </summary>
    public IReadOnlyList<(string Note, string Tag)> NoteTags() => Run(connection =>
    {
        List<(string Note, string Tag)> rows = [];
        using SqliteCommand command = Command(connection, null, "SELECT note, tag FROM note_tags");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows
            .OrderBy(r => r.Note, StringComparer.Ordinal)
            .ThenBy(r => r.Tag, StringComparer.Ordinal)
            .ToList();
    });

    /// <summary>
    /// The notes tagged <paramref name="tag"/>, sorted by name ignoring case. The tag is normalized
    /// first (<see cref="NormalizeTag"/>), so <c>#Work</c> finds <c>work</c>.
    /// </summary>
    public IReadOnlyList<string> NotesWithTag(string tag)
    {
        string wanted = NormalizeTag(tag);
        return Run(connection =>
        {
            List<string> names = [];
            using SqliteCommand command = Command(connection, null, "SELECT note FROM note_tags WHERE tag = $tag");
            command.Parameters.AddWithValue("$tag", wanted);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return SortNames(names);
        });
    }

    /// <summary>
    /// The notes that mention the work item <c>#<paramref name="item"/></c> (<see cref="WorkItemParser"/>), sorted by
    /// name ignoring case, then ordinal. Never the item's own page (<see cref="WorkItems.ItemOf"/>).
    /// </summary>
    public IReadOnlyList<string> NotesWithItem(int item) => Run(connection =>
    {
        List<string> names = [];
        using SqliteCommand command = Command(connection, null, "SELECT note FROM note_items WHERE item = $item");
        command.Parameters.AddWithValue("$item", item);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string note = reader.GetString(0);
            if (WorkItems.ItemOf(note) != item)
            {
                names.Add(note);
            }
        }

        return (IReadOnlyList<string>)SortNames(names);
    });

    /// <summary>
    /// The stored mention spellings (<c>@[...]</c>) of each of <paramref name="notes"/>, as <c>(note, spelling)</c> rows
    /// in no order.
    /// </summary>
    public IReadOnlyList<(string Note, string Spelling)> MentionsIn(IReadOnlyCollection<string> notes) => Run(connection =>
    {
        HashSet<string> wanted = new(notes, StringComparer.Ordinal);
        List<(string Note, string Spelling)> found = [];
        using SqliteCommand command = Command(connection, null, "SELECT note, spelling FROM note_people");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string note = reader.GetString(0);
            if (wanted.Contains(note))
            {
                found.Add((note, reader.GetString(1)));
            }
        }

        return (IReadOnlyList<(string Note, string Spelling)>)found;
    });

    /// <summary>
    /// The notes with the attribute <paramref name="key"/><c>::</c> <paramref name="value"/>, matched by the key as
    /// given (already normalized with <see cref="AttributeParser.NormalizeKey"/>) and by
    /// <see cref="AttributeParser.ValueKey"/> of the value, so <c>ACME</c> finds <c>[[ACME]]</c>. Each note once, sorted
    /// by name ignoring case, then ordinal.
    /// </summary>
    public IReadOnlyList<string> NotesWithAttribute(string key, string value)
    {
        string valueKey = AttributeParser.ValueKey(value);
        return Run(connection =>
        {
            List<string> names = [];
            using SqliteCommand command = Command(
                connection,
                null,
                "SELECT DISTINCT note FROM note_attributes WHERE key = $key AND value_key = $valueKey");
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$valueKey", valueKey);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return SortNames(names);
        });
    }

    /// <summary>
    /// The indexed notes that have no attribute with the key <paramref name="key"/> (normalized), sorted by name
    /// ignoring case, then ordinal. A note that couldn't be read has no attributes, so it is among them.
    /// </summary>
    public IReadOnlyList<string> NotesWithoutAttribute(string key) => Run(connection =>
    {
        List<string> names = [];
        using SqliteCommand command = Command(
            connection,
            null,
            "SELECT name FROM notes WHERE name NOT IN (SELECT note FROM note_attributes WHERE key = $key)");
        command.Parameters.AddWithValue("$key", key);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return (IReadOnlyList<string>)SortNames(names);
    });

    /// <summary>
    /// Every stored attribute with the key <paramref name="key"/> (already normalized with
    /// <see cref="AttributeParser.NormalizeKey"/>) as <c>(Note, Value)</c> rows, the value spelled as stored (raw),
    /// ordered by note (ordinal), then line order (<c>rowid</c>). A key nobody uses gives an empty list.
    /// </summary>
    public IReadOnlyList<(string Note, string Value)> AttributeValues(string key) => Run(connection =>
    {
        List<(string Note, string Value)> rows = [];
        using SqliteCommand command = Command(
            connection,
            null,
            "SELECT note, value FROM note_attributes WHERE key = $key ORDER BY rowid");
        command.Parameters.AddWithValue("$key", key);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        // A stable sort by note (ordinal, the same on every OS) keeps each note's rows in line order.
        return (IReadOnlyList<(string Note, string Value)>)rows.OrderBy(r => r.Note, StringComparer.Ordinal).ToList();
    });

    /// <summary>
    /// The indexed note called <paramref name="name"/> (trimmed): an exact match first, then one ignoring
    /// case. <c>null</c> if there is none.
    /// </summary>
    public string? FindNote(string name)
    {
        return Run(connection => new NameLookup(NoteNames(connection)).Match(name.Trim()));
    }

    /// <summary>
    /// Every other note linked to <paramref name="noteName"/> (matched like <see cref="FindNote"/>): notes
    /// that share at least one tag with it (shared tags sorted), notes it links to with <c>[[...]]</c>, and
    /// notes that link to it. Each note appears once, sorted by name ignoring case, then ordinal. A link
    /// target resolves like <c>/open</c> (<see cref="NameLookup.Resolve"/>); one that names no note gives a
    /// <see cref="NoteLink.Missing"/> row with the target as written. A note never links to itself. An
    /// unknown note has no links.
    /// </summary>
    public IReadOnlyList<NoteLink> Links(string noteName) => Run(connection =>
    {
        NameLookup names = new NameLookup(NoteNames(connection));
        string? note = names.Match(noteName.Trim());
        if (note is null)
        {
            return (IReadOnlyList<NoteLink>)[];
        }

        Dictionary<string, List<string>> shared = new(StringComparer.Ordinal);
        using (SqliteCommand command = Command(
            connection,
            null,
            "SELECT other.note, other.tag FROM note_tags AS mine "
                + "JOIN note_tags AS other ON other.tag = mine.tag AND other.note <> mine.note "
                + "WHERE mine.note = $note"))
        {
            command.Parameters.AddWithValue("$note", note);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string other = reader.GetString(0);
                if (!shared.TryGetValue(other, out List<string>? tags))
                {
                    tags = [];
                    shared[other] = tags;
                }

                tags.Add(reader.GetString(1));
            }
        }

        Dictionary<string, NoteLinkDirection> directions = new(StringComparer.Ordinal);
        Dictionary<string, string> missing = new(StringComparer.OrdinalIgnoreCase);
        // Sorted, so of two spellings of one missing note ("Gamma#A", "gamma") the first stored is the one shown.
        // Only the rows that concern the note (LinksSql): its own, and those whose whole target or note part is its name
        // or its name + ".md" ignoring case, which are exactly the targets NameLookup.ResolveLink could resolve to it.
        string key = note.ToUpperInvariant();
        using (SqliteCommand command = Command(connection, null, LinksSql))
        {
            command.Parameters.AddWithValue("$note", note);
            command.Parameters.AddWithValue("$k", key);
            command.Parameters.AddWithValue("$kmd", key + ".MD");
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string from = reader.GetString(0);
                string target = reader.GetString(1);
                string? to = names.ResolveLink(target)?.Note;
                if (string.Equals(from, note, StringComparison.Ordinal))
                {
                    if (to is null)
                    {
                        // One row per missing note, shown by its note part: "Gamma", "gamma.md" and "Gamma#A" are one
                        // note. "[[#Intro]]" names no note at all, so it gives no row.
                        string part = NoteLinkParser.Split(target).Note;
                        if (part.Length > 0)
                        {
                            missing.TryAdd(StripMd(part), part);
                        }
                    }
                    else if (!string.Equals(to, note, StringComparison.Ordinal))
                    {
                        AddDirection(directions, to, NoteLinkDirection.To);
                    }
                }
                else if (string.Equals(to, note, StringComparison.Ordinal))
                {
                    AddDirection(directions, from, NoteLinkDirection.From);
                }
            }
        }

        Dictionary<string, NoteLink> rows = new(StringComparer.Ordinal);
        foreach (string other in shared.Keys.Concat(directions.Keys))
        {
            if (rows.ContainsKey(other))
            {
                continue;
            }

            IReadOnlyList<string> tags = shared.TryGetValue(other, out List<string>? found)
                ? found.Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList()
                : [];
            NoteLinkDirection direction = directions.GetValueOrDefault(other, NoteLinkDirection.None);
            rows[other] = new NoteLink(other, tags, direction, missing: false);
        }

        foreach (string target in missing.Values)
        {
            rows.TryAdd(target, new NoteLink(target, [], NoteLinkDirection.To, missing: true));
        }

        return SortNames(rows.Keys).Select(n => rows[n]).ToList();
    });

    /// <summary>
    /// Every person mentioned with <c>@[Name]</c> in a note other than their own page, once, with the number of such
    /// notes, sorted by the shown name ignoring case, then ordinal. A mention is resolved by <see cref="Aliases"/>, so a
    /// note that mentions <c>@[Anna]</c> and <c>@[Anna Schmidt]</c> counts once under <c>Anna Schmidt</c>; an unresolved
    /// name is its own entry. The shown name is the title of the person's page (<see cref="Context.People.PersonOf"/>)
    /// when it exists, else the ordinal-first spelling among the mentions.
    /// </summary>
    public IReadOnlyList<PersonCount> People() => Run(connection =>
    {
        Dictionary<string, string> pages = PersonPages(connection);
        PersonAliases aliases = BuildAliases(connection);
        Dictionary<string, (HashSet<string> Notes, string Spelling)> people = new(StringComparer.Ordinal);
        using (SqliteCommand command = Command(connection, null, "SELECT note, person, spelling FROM note_people"))
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                string note = reader.GetString(0);
                string spelling = reader.GetString(2);
                string person = aliases.PersonKey(reader.GetString(1));
                if (IsOwnPage(note, person))
                {
                    continue;
                }

                if (people.TryGetValue(person, out (HashSet<string> Notes, string Spelling) known))
                {
                    known.Notes.Add(note);
                    if (string.CompareOrdinal(spelling, known.Spelling) < 0)
                    {
                        people[person] = (known.Notes, spelling);
                    }
                }
                else
                {
                    people[person] = (new HashSet<string>(StringComparer.Ordinal) { note }, spelling);
                }
            }
        }

        return (IReadOnlyList<PersonCount>)people
            .Select(p => new PersonCount(pages.GetValueOrDefault(p.Key, p.Value.Spelling), p.Value.Notes.Count))
            .OrderBy(p => p.Person, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Person, StringComparer.Ordinal)
            .ToList();
    });

    /// <summary>
    /// The notes that mention <paramref name="person"/> with <c>@[...]</c>, matched by <see cref="PersonParser.Key"/>
    /// of the trimmed name with whitespace runs collapsed (so <c>anna schmidt</c> finds <c>@[ANNA  schmidt]</c>) after
    /// resolving both the name and each mention through <see cref="Aliases"/> (so <c>AS</c> and <c>Anna Schmidt</c>
    /// both find <c>@[AS]</c>), sorted by name ignoring case, then ordinal. Never the person's own page.
    /// </summary>
    public IReadOnlyList<string> NotesMentioning(string person) => Run(connection =>
    {
        PersonAliases aliases = BuildAliases(connection);
        string key = aliases.PersonKey(person);
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach ((string note, string _) in MentionsOf(connection, aliases, key))
        {
            names.Add(note);
        }

        return (IReadOnlyList<string>)SortNames(names);
    });

    /// <summary>
    /// The name shown for <paramref name="person"/> (resolved and matched like <see cref="NotesMentioning"/>): the
    /// title of their page when it exists, else the ordinal-first spelling among the mentions in other notes, else
    /// <c>null</c>.
    /// </summary>
    public string? PersonName(string person) => Run(connection =>
    {
        PersonAliases aliases = BuildAliases(connection);
        string key = aliases.PersonKey(person);
        if (PersonPages(connection).TryGetValue(key, out string? title))
        {
            return title;
        }

        string? first = null;
        foreach ((string _, string spelling) in MentionsOf(connection, aliases, key))
        {
            if (first is null || string.CompareOrdinal(spelling, first) < 0)
            {
                first = spelling;
            }
        }

        return first;
    });

    /// <summary>
    /// The alias rule (<see cref="PersonAliases"/>) as indexed: the person pages among the indexed notes and the
    /// <c>aliases::</c> values stored for them. Re-read on every call, so an alias change counts after the next
    /// <see cref="Sync"/>.
    /// </summary>
    public PersonAliases Aliases() => Run(BuildAliases);

    // The (note, spelling) of every mention whose resolved key is the person key, never on the person's own page.
    private static List<(string Note, string Spelling)> MentionsOf(SqliteConnection connection, PersonAliases aliases, string key)
    {
        List<(string Note, string Spelling)> found = [];
        using SqliteCommand command = Command(connection, null, "SELECT note, person, spelling FROM note_people");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string note = reader.GetString(0);
            if (string.Equals(aliases.PersonKey(reader.GetString(1)), key, StringComparison.Ordinal) && !IsOwnPage(note, key))
            {
                found.Add((note, reader.GetString(2)));
            }
        }

        return found;
    }

    // The alias rule from the indexed person pages, in full-name order (ordinal), with their stored aliases:: values
    // in the order they were parsed.
    private static PersonAliases BuildAliases(SqliteConnection connection)
    {
        Dictionary<string, List<PageAttribute>> values = new(StringComparer.Ordinal);
        using (SqliteCommand command = Command(
            connection,
            null,
            "SELECT note, value FROM note_attributes WHERE key = $key ORDER BY rowid"))
        {
            command.Parameters.AddWithValue("$key", PersonAliases.AttributeKey);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string note = reader.GetString(0);
                if (Context.People.PersonOf(note) is null)
                {
                    continue;
                }

                if (!values.TryGetValue(note, out List<PageAttribute>? list))
                {
                    list = [];
                    values[note] = list;
                }

                list.Add(new PageAttribute(PersonAliases.AttributeKey, reader.GetString(1)));
            }
        }

        List<(string Title, IReadOnlyList<string> Aliases)> pages = [];
        foreach (string name in NoteNames(connection).Order(StringComparer.Ordinal))
        {
            if (Context.People.PersonOf(name) is { } title)
            {
                pages.Add((title, values.TryGetValue(name, out List<PageAttribute>? found) ? PersonAliases.Parse(found, title) : []));
            }
        }

        return new PersonAliases(pages);
    }

    // The person pages in the index by key: the title of each note directly under the top-level people note. With two
    // pages of one key (People/anna and people/Anna) the ordinal-first full name wins.
    private static Dictionary<string, string> PersonPages(SqliteConnection connection)
    {
        Dictionary<string, string> pages = new(StringComparer.Ordinal);
        foreach (string name in NoteNames(connection).Order(StringComparer.Ordinal))
        {
            if (Context.People.PersonOf(name) is { } title)
            {
                pages.TryAdd(PersonParser.Key(title), title);
            }
        }

        return pages;
    }

    private static bool IsOwnPage(string note, string key) =>
        Context.People.PersonOf(note) is { } title && string.Equals(PersonParser.Key(title), key, StringComparison.Ordinal);

    // The stored lookup key of a link target or its note part: upper-cased invariant, NULL when empty.
    private static string? LinkKey(string text) => text.Length == 0 ? null : text.ToUpperInvariant();

    // Deletes the note's note_text row by its rowid (from note_text_rows), so the FTS table is never scanned by note.
    // The row goes only if it still names the note; the mapping goes either way.
    private static void DeleteText(SqliteConnection connection, SqliteTransaction transaction, string note)
    {
        object? row;
        using (SqliteCommand select = Command(connection, transaction, "SELECT row FROM note_text_rows WHERE note = $note"))
        {
            select.Parameters.AddWithValue("$note", note);
            row = select.ExecuteScalar();
        }

        if (row is long rowid)
        {
            Execute(
                connection,
                transaction,
                "DELETE FROM note_text WHERE rowid = $row AND note = $note",
                ("$row", rowid),
                ("$note", note));
            Execute(connection, transaction, "DELETE FROM note_text_rows WHERE note = $note", ("$note", note));
        }
    }

    // Once per sync, before the notes are read: a note_text row that is not its note's mapped row (none, or a
    // mapping to another row) goes by rowid, and so does a mapping whose row is gone or names another note. An older
    // version writing the same context.db leaves such rows. The notes they belonged to lose their stored time, so
    // this sync re-parses those still there and writes their text and mapping again.
    private static void RepairText(SqliteConnection connection, SqliteTransaction transaction)
    {
        List<(long Row, string Note)> extra = [];
        using (SqliteCommand select = Command(
            connection,
            transaction,
            "SELECT t.rowid, t.note FROM note_text t WHERE NOT EXISTS "
                + "(SELECT 1 FROM note_text_rows r WHERE r.note = t.note AND r.row = t.rowid)"))
        using (SqliteDataReader reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                extra.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        List<string> stale = [];
        using (SqliteCommand select = Command(
            connection,
            transaction,
            "SELECT r.note FROM note_text_rows r WHERE NOT EXISTS "
                + "(SELECT 1 FROM note_text t WHERE t.rowid = r.row AND t.note = r.note)"))
        using (SqliteDataReader reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                stale.Add(reader.GetString(0));
            }
        }

        foreach ((long row, _) in extra)
        {
            Execute(connection, transaction, "DELETE FROM note_text WHERE rowid = $row", ("$row", row));
        }

        foreach (string note in stale)
        {
            Execute(connection, transaction, "DELETE FROM note_text_rows WHERE note = $note", ("$note", note));
        }

        foreach (string note in extra.Select(e => e.Note).Concat(stale).Distinct(StringComparer.Ordinal))
        {
            Execute(connection, transaction, "UPDATE notes SET modified = NULL WHERE name = $note", ("$note", note));
        }
    }

    // The note_text rows of notes whose note row is missing (edited by hand) go by rowid, through note_text_rows,
    // only while the row still names that note.
    private static void PruneOrphanText(SqliteConnection connection, SqliteTransaction transaction)
    {
        List<(long Row, string Note)> rows = [];
        using (SqliteCommand select = Command(
            connection, transaction, "SELECT row, note FROM note_text_rows WHERE note NOT IN (SELECT name FROM notes)"))
        using (SqliteDataReader reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        foreach ((long row, string note) in rows)
        {
            Execute(
                connection,
                transaction,
                "DELETE FROM note_text WHERE rowid = $row AND note = $note",
                ("$row", row),
                ("$note", note));
        }

        if (rows.Count > 0)
        {
            Execute(connection, transaction, "DELETE FROM note_text_rows WHERE note NOT IN (SELECT name FROM notes)");
        }
    }

    private static string StripMd(string target) =>
        target.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && target.Length > 3 ? target[..^3] : target;

    private static void AddDirection(Dictionary<string, NoteLinkDirection> directions, string other, NoteLinkDirection add)
    {
        NoteLinkDirection now = directions.GetValueOrDefault(other, NoteLinkDirection.None);
        directions[other] = now == NoteLinkDirection.None || now == add ? add : NoteLinkDirection.Both;
    }

    private static List<string> NoteNames(SqliteConnection connection)
    {
        List<string> all = [];
        using SqliteCommand command = Command(connection, null, "SELECT name FROM notes");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            all.Add(reader.GetString(0));
        }

        return all;
    }

    /// <summary>
    /// Trims <paramref name="tag"/>, removes one leading <c>#</c> and one surrounding <c>[</c>...<c>]</c>
    /// pair, turns each whitespace run into one space and lower-cases it (invariant), so
    /// <c>#[Project  Alpha]</c> gives <c>project alpha</c>.
    /// </summary>
    public static string NormalizeTag(string tag)
    {
        string trimmed = tag.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length >= 2 && trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1];
        }

        return TagParser.CollapseWhitespace(trimmed).ToLowerInvariant();
    }

    private static List<string> SortNames(IEnumerable<string> names) =>
        names
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n, StringComparer.Ordinal)
            .ToList();

    // Turns on FTS5's secure-delete for note_text (stored in the table, so set once): a deleted or rewritten note's
    // text is then removed from the index pages instead of only being marked deleted. Checked first, so a sync with
    // nothing to do writes nothing.
    private static void EnsureSecureDelete(SqliteConnection connection, SqliteTransaction transaction)
    {
        using (SqliteCommand check = Command(connection, transaction, "SELECT v FROM note_text_config WHERE k = 'secure-delete'"))
        {
            object? value = check.ExecuteScalar();
            if (value is long on && on == 1)
            {
                return;
            }
        }

        Execute(connection, transaction, "INSERT INTO note_text(note_text, rank) VALUES('secure-delete', 1)");
    }

    // Opens the database, creates the tables if needed and runs the work. Any SQLite error becomes a
    // TagIndexException. The file is never deleted or replaced.
    private T Run<T>(Func<SqliteConnection, T> work)
    {
        Interlocked.Increment(ref reads);
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            // Microsoft.Data.Sqlite retries a busy statement until this many seconds have passed.
            DefaultTimeout = (int)Math.Max(1, Math.Ceiling(busyTimeout.TotalSeconds)),
        }.ToString();

        PreparePrivateFile();
        try
        {
            using SqliteConnection connection = new SqliteConnection(connectionString);
            connection.Open();
            // A constant number, not user input: PRAGMA takes no parameters.
            Execute(
                connection,
                null,
                "PRAGMA busy_timeout = "
                    + ((long)busyTimeout.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
            // Freed pages are zeroed, so the text of a deleted note (one just marked sensitive) doesn't linger in the file.
            Execute(connection, null, "PRAGMA secure_delete = ON");
            Execute(connection, null, Schema);
            EnsureLinkColumn(connection, "heading");
            EnsureLinkColumn(connection, "whole_key");
            EnsureLinkColumn(connection, "part_key");
            EnsureDecisionNumberColumn(connection);
            Execute(
                connection,
                null,
                "CREATE INDEX IF NOT EXISTS note_links_whole_key ON note_links(whole_key); "
                    + "CREATE INDEX IF NOT EXISTS note_links_part_key ON note_links(part_key); "
                    + "CREATE INDEX IF NOT EXISTS note_decisions_number ON note_decisions(number);");
            return work(connection);
        }
        catch (SqliteException ex)
        {
            throw new TagIndexException($"Can't read {FileName}.", ex);
        }
    }

    // Job 274: before SQLite opens it, context.db is an owner-only (0600 off Windows) file, so its journal, which SQLite
    // gives the database's mode, is too. A missing file is created empty (SQLite takes an empty file as a new database)
    // when the root exists; a missing root is left to SQLite as before. An existing file with any group or other bits
    // is set to 0600 (best effort: a file system that can't change modes still opens).
    private void PreparePrivateFile()
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            using (PrivateFile.OpenNew(FilePath))
            {
            }
        }
        catch (IOException) when (File.Exists(FilePath) || Directory.Exists(FilePath))
        {
            // Already there (or made by another process meanwhile).
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TagIndexException($"Can't read {FileName}.", ex);
        }

        try
        {
            PrivateFile.Tighten(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left as it is; the database still opens. Its journal then takes the same loose mode, which exposes
            // nothing the readable database doesn't already.
        }
    }

    // A database from before ParserVersion 10 has note_links without the heading column, one from before 14 without
    // whole_key and part_key: add the column (NULL until the next Sync re-parses every note). Another process may add it
    // at the same time; then its column is the one kept. The column name is one of three constants, never user input.
    private static void EnsureLinkColumn(SqliteConnection connection, string column)
    {
        if (column is not ("heading" or "whole_key" or "part_key"))
        {
            throw new ArgumentException("Not a note_links column: " + column, nameof(column));
        }

        if (LinksHaveColumn(connection, column))
        {
            return;
        }

        try
        {
            Execute(connection, null, $"ALTER TABLE note_links ADD COLUMN {column} TEXT");
        }
        catch (SqliteException) when (LinksHaveColumn(connection, column))
        {
            // Added by another process meanwhile.
        }
    }

    private static bool LinksHaveColumn(SqliteConnection connection, string column) =>
        HasColumn(connection, "note_links", column);

    private const string DecisionSelect =
        "SELECT d.note, d.line, d.number, d.text, n.modified FROM note_decisions d JOIN notes n ON n.name = d.note";

    // A row is in the log when it is unnumbered, or its note owns the number and it is that note's first row with it.
    private const string OwnedDecision =
        "(d.number IS NULL OR (EXISTS (SELECT 1 FROM decision_owners o WHERE o.number = d.number AND o.note = d.note) "
            + "AND d.line = (SELECT MIN(e.line) FROM note_decisions e WHERE e.note = d.note AND e.number = d.number)))";

    private static List<IndexedDecision> ReadDecisions(SqliteCommand command)
    {
        List<IndexedDecision> rows = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            // A line, number or time edited by hand out of range is skipped, like a note that couldn't be read.
            long line = reader.GetInt64(1);
            long? number = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            long ticks = reader.GetInt64(4);
            if (line < 1 || line > int.MaxValue || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks
                || number is < 1 or > DecisionParser.MaxNumber)
            {
                continue;
            }

            rows.Add(new IndexedDecision(
                reader.GetString(0), (int)line, (int?)number, reader.GetString(3), new DateTime(ticks, DateTimeKind.Utc)));
        }

        return rows;
    }

    // Job 343: owner rows of notes that are gone, or that are readable and no longer hold the number, go; then each
    // number without an owner goes to the note holding it with the oldest last-write time, then the lower name
    // ignoring case, then ordinal. An unreadable or oversized owner (no stored time) keeps its numbers.
    private static void AssignDecisionOwners(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, "DELETE FROM decision_owners WHERE note NOT IN (SELECT name FROM notes)");
        Execute(
            connection,
            transaction,
            "DELETE FROM decision_owners WHERE note IN (SELECT name FROM notes WHERE modified IS NOT NULL) "
                + "AND NOT EXISTS (SELECT 1 FROM note_decisions d WHERE d.note = decision_owners.note "
                + "AND d.number = decision_owners.number)");
        List<(long Number, string Note, long Modified)> holders = [];
        using (SqliteCommand select = Command(
            connection,
            transaction,
            "SELECT DISTINCT d.number, d.note, n.modified FROM note_decisions d JOIN notes n ON n.name = d.note "
                + "WHERE d.number IS NOT NULL AND n.modified IS NOT NULL "
                + "AND d.number NOT IN (SELECT number FROM decision_owners)"))
        using (SqliteDataReader reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                holders.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2)));
            }
        }

        foreach (IGrouping<long, (long Number, string Note, long Modified)> group in holders.GroupBy(h => h.Number))
        {
            (long Number, string Note, long Modified) owner = group
                .OrderBy(h => h.Modified)
                .ThenBy(h => h.Note, StringComparer.OrdinalIgnoreCase)
                .ThenBy(h => h.Note, StringComparer.Ordinal)
                .First();
            Execute(
                connection,
                transaction,
                "INSERT INTO decision_owners(number, note) VALUES($number, $note)",
                ("$number", owner.Number),
                ("$note", owner.Note));
        }
    }

    // A database from before ParserVersion 17 has note_decisions without the number column: add it (NULL until the next
    // Sync re-parses every note). Another process may add it at the same time; then its column is the one kept.
    private static void EnsureDecisionNumberColumn(SqliteConnection connection)
    {
        if (HasColumn(connection, "note_decisions", "number"))
        {
            return;
        }

        try
        {
            Execute(connection, null, "ALTER TABLE note_decisions ADD COLUMN number INTEGER");
        }
        catch (SqliteException) when (HasColumn(connection, "note_decisions", "number"))
        {
            // Added by another process meanwhile.
        }
    }

    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        if (table is not ("note_links" or "note_decisions"))
        {
            throw new ArgumentException("Not an index table: " + table, nameof(table));
        }

        using SqliteCommand command = Command(
            connection, null, $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $column");
        command.Parameters.AddWithValue("$column", column);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void InsertAttribute(
        SqliteConnection connection, SqliteTransaction transaction, string note, string key, string valueKey, string value) =>
        Execute(
            connection,
            transaction,
            "INSERT INTO note_attributes(note, key, value_key, value) VALUES($note, $key, $valueKey, $value)",
            ("$note", note),
            ("$key", key),
            ("$valueKey", valueKey),
            ("$value", value));

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = Command(connection, transaction, sql);
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }
}
