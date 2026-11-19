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
            PRIMARY KEY(note, line));
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
        CREATE VIRTUAL TABLE IF NOT EXISTS note_text USING fts5(note UNINDEXED, body, tokenize='trigram');
        """;

    /// <summary>
    /// The parser version the stored tags were made with, kept in <c>PRAGMA user_version</c>. When the
    /// database has an older one, <see cref="Sync"/> re-parses every note once. 1: <c>#[bracket tags]</c>.
    /// 2: <c>[[note links]]</c>. 3: subpages (full names, link targets up to <see cref="NoteLinkParser.MaxTargetLength"/>).
    /// 4: <c>@[person]</c> mentions (and <c>@[[x]]</c> is no link). 5: <c>key:: value</c> attributes. 6: a link target
    /// that runs into a code span is no link. 7: note text (the full-text table <c>note_text</c>). 8: <c>Decision:</c> lines
    /// (<c>note_decisions</c>). 9: open <c>- [ ]</c> todos (<c>note_todos</c>). 10: the heading of a
    /// <c>[[Note#Heading]]</c> link (<c>note_links.heading</c>). 11: <c>next: @[Name]</c> topics (<c>note_topics</c>),
    /// which are no longer note todos. 12: <c>review:yyyy-MM-dd</c> lines (<c>note_reviews</c>).
    /// </summary>
    public const int ParserVersion = 12;

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
    /// A note that can't be read is kept with no tags, no links and no stored time, so it is retried on the
    /// next sync. Link targets are stored as written and resolved against note names only when queried. A re-parsed
    /// note's text is written to <c>note_text</c> again (no row for a note that can't be read or is over
    /// <see cref="NoteStore.MaxEditBytes"/>); sensitive notes are never listed, so never indexed.
    /// Throws <see cref="TagIndexException"/> if the database can't be opened or isn't valid.
    /// </summary>
    public void Sync(NoteStore store)
    {
        Directory.CreateDirectory(root);
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
                string? body;
                long? newModified = modified;
                try
                {
                    string text = store.Read(note);
                    // The full text only for a note the editor could open too; its tags and links still count.
                    body = new FileInfo(note.Path).Length <= NoteStore.MaxEditBytes ? text : null;
                    tags = TagParser.Parse(text);
                    targets = NoteLinkParser.Parse(text);
                    people = PersonParser.Parse(text);
                    attributes = AttributeParser.Parse(text);
                    decisions = DecisionParser.Parse(text);
                    noteTodos = NoteTodoParser.Parse(text);
                    topics = TopicParser.Parse(text);
                    review = ReviewDateParser.Parse(text);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    tags = [];
                    targets = [];
                    people = [];
                    attributes = [];
                    decisions = [];
                    noteTodos = [];
                    topics = [];
                    review = null;
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
                        "INSERT INTO note_links(note, target, heading) VALUES($note, $target, $heading)",
                        ("$note", note.Name),
                        ("$target", target),
                        ("$heading", NoteLinkParser.Split(target).Heading));
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
                foreach (PageAttribute attribute in attributes)
                {
                    Execute(
                        connection,
                        transaction,
                        "INSERT INTO note_attributes(note, key, value_key, value) VALUES($note, $key, $valueKey, $value)",
                        ("$note", note.Name),
                        ("$key", attribute.Key),
                        ("$valueKey", AttributeParser.ValueKey(attribute.Value)),
                        ("$value", attribute.Value));
                }

                Execute(connection, transaction, "DELETE FROM note_decisions WHERE note = $note", ("$note", note.Name));
                foreach (ParsedDecision decision in decisions)
                {
                    Execute(
                        connection,
                        transaction,
                        "INSERT INTO note_decisions(note, line, text) VALUES($note, $line, $text)",
                        ("$note", note.Name),
                        ("$line", decision.Line),
                        ("$text", decision.Text));
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

                if (!upgrade)
                {
                    Execute(connection, transaction, "DELETE FROM note_text WHERE note = $note", ("$note", note.Name));
                }

                if (body is not null)
                {
                    Execute(
                        connection,
                        transaction,
                        "INSERT INTO note_text(note, body) VALUES($note, $body)",
                        ("$note", note.Name),
                        ("$body", body));
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
                Execute(connection, transaction, "DELETE FROM note_text WHERE note = $note", ("$note", gone));
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
            Execute(connection, transaction, "DELETE FROM note_text WHERE note NOT IN (SELECT name FROM notes)");
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
    /// The <c>(note, body)</c> rows of <c>note_text</c> that contain <paramref name="text"/> by the trigram tokenizer
    /// (ignoring case), unsorted. The text reaches SQL only as a parameter, quoted as one FTS5 phrase, so FTS syntax in
    /// it (<c>OR</c>, <c>NEAR</c>, <c>*</c>, <c>-</c>, quotes) is literal text. Under 3 characters nothing matches. The
    /// match is only a pre-filter: <see cref="SearchView.Build"/> checks each line itself.
    /// </summary>
    public IReadOnlyList<(string Note, string Body)> Search(string text)
    {
        string phrase = "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return Run(connection =>
        {
            List<(string Note, string Body)> rows = [];
            using SqliteCommand command = Command(connection, null, "SELECT note, body FROM note_text WHERE note_text MATCH $q");
            command.Parameters.AddWithValue("$q", phrase);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetString(1)));
            }

            return (IReadOnlyList<(string Note, string Body)>)rows;
        });
    }

    /// <summary>
    /// Every row of <c>note_decisions</c> with its note's stored last-write time (UTC), unsorted. With a non-blank
    /// <paramref name="tag"/> (normalized by <see cref="NormalizeTag"/>, reaching SQL only as a parameter) only the
    /// decisions of notes that have that tag. A note without a stored time (it couldn't be read) has no rows.
    /// </summary>
    public IReadOnlyList<(string Note, int Line, string Text, DateTime Modified)> Decisions(string? tag)
    {
        string? wanted = string.IsNullOrWhiteSpace(tag) ? null : NormalizeTag(tag);
        return Run(connection =>
        {
            List<(string Note, int Line, string Text, DateTime Modified)> rows = [];
            string sql = "SELECT d.note, d.line, d.text, n.modified FROM note_decisions d "
                + "JOIN notes n ON n.name = d.note WHERE n.modified IS NOT NULL";
            if (wanted is not null)
            {
                sql += " AND d.note IN (SELECT note FROM note_tags WHERE tag = $tag)";
            }

            using SqliteCommand command = Command(connection, null, sql);
            if (wanted is not null)
            {
                command.Parameters.AddWithValue("$tag", wanted);
            }

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                // A line or time edited by hand out of range is skipped, like a note that couldn't be read.
                long line = reader.GetInt64(1);
                long ticks = reader.GetInt64(3);
                if (line < 1 || line > int.MaxValue || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                {
                    continue;
                }

                rows.Add((reader.GetString(0), (int)line, reader.GetString(2), new DateTime(ticks, DateTimeKind.Utc)));
            }

            return (IReadOnlyList<(string Note, int Line, string Text, DateTime Modified)>)rows;
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
        using (SqliteCommand command = Command(connection, null, "SELECT note, target FROM note_links ORDER BY note, target"))
        using (SqliteDataReader reader = command.ExecuteReader())
        {
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
            EnsureLinkHeading(connection);
            return work(connection);
        }
        catch (SqliteException ex)
        {
            throw new TagIndexException($"Can't read {FileName}.", ex);
        }
    }

    // A database from before ParserVersion 10 has note_links without the heading column: add it (NULL until the next
    // Sync re-parses every note). Another process may add it at the same time; then its column is the one kept.
    private static void EnsureLinkHeading(SqliteConnection connection)
    {
        if (LinksHaveHeading(connection))
        {
            return;
        }

        try
        {
            Execute(connection, null, "ALTER TABLE note_links ADD COLUMN heading TEXT");
        }
        catch (SqliteException) when (LinksHaveHeading(connection))
        {
            // Added by another process meanwhile.
        }
    }

    private static bool LinksHaveHeading(SqliteConnection connection)
    {
        using SqliteCommand command = Command(
            connection, null, "SELECT COUNT(*) FROM pragma_table_info('note_links') WHERE name = 'heading'");
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

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
