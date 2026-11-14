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
        """;

    /// <summary>
    /// The parser version the stored tags were made with, kept in <c>PRAGMA user_version</c>. When the
    /// database has an older one, <see cref="Sync"/> re-parses every note once. 1: <c>#[bracket tags]</c>.
    /// 2: <c>[[note links]]</c>. 3: subpages (full names, link targets up to <see cref="NoteLinkParser.MaxTargetLength"/>).
    /// 4: <c>@[person]</c> mentions (and <c>@[[x]]</c> is no link). 5: <c>key:: value</c> attributes.
    /// </summary>
    public const int ParserVersion = 5;

    /// <summary>
    /// How long opening <c>context.db</c> and starting <see cref="Sync"/>'s write transaction wait while another
    /// process (the desktop app, another command line run) holds a lock on it, before giving up with a
    /// <see cref="TagIndexException"/>.
    /// </summary>
    public static readonly TimeSpan BusyTimeout = TimeSpan.FromSeconds(5);

    private readonly string root;
    private readonly TimeSpan busyTimeout;

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

    /// <summary>The full path of <c>context.db</c>.</summary>
    public string FilePath => Path.Combine(root, FileName);

    /// <summary>
    /// Brings the index up to date with the notes in <paramref name="store"/>, in one transaction. Creates
    /// the root folder and the database if missing. A note is re-parsed (tags and <c>[[note links]]</c>) only
    /// when its last-write time (UTC ticks) differs from the stored one. Notes that are gone lose their rows.
    /// A note that can't be read is kept with no tags, no links and no stored time, so it is retried on the
    /// next sync. Link targets are stored as written and resolved against note names only when queried.
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

            bool upgrade = version < ParserVersion;
            if (upgrade)
            {
                // Tags were parsed by an older parser: forget the stored times so every note is re-parsed.
                Execute(connection, transaction, "UPDATE notes SET modified = NULL");
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
                long? newModified = modified;
                try
                {
                    string text = store.Read(note);
                    tags = TagParser.Parse(text);
                    targets = NoteLinkParser.Parse(text);
                    people = PersonParser.Parse(text);
                    attributes = AttributeParser.Parse(text);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    tags = [];
                    targets = [];
                    people = [];
                    attributes = [];
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
                        "INSERT INTO note_links(note, target) VALUES($note, $target)",
                        ("$note", note.Name),
                        ("$target", target));
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
                Execute(connection, transaction, "DELETE FROM notes WHERE name = $note", ("$note", gone));
            }

            // Tag and link rows whose note row is missing (e.g. edited by hand) would otherwise never go away.
            Execute(connection, transaction, "DELETE FROM note_tags WHERE note NOT IN (SELECT name FROM notes)");
            Execute(connection, transaction, "DELETE FROM note_links WHERE note NOT IN (SELECT name FROM notes)");
            Execute(connection, transaction, "DELETE FROM note_people WHERE note NOT IN (SELECT name FROM notes)");
            Execute(connection, transaction, "DELETE FROM note_attributes WHERE note NOT IN (SELECT name FROM notes)");
            if (upgrade)
            {
                // A constant, not user input: PRAGMA takes no parameters.
                Execute(connection, transaction, $"PRAGMA user_version = {ParserVersion}");
            }

            transaction.Commit();
            return 0;
        });
    }

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
        using (SqliteCommand command = Command(connection, null, "SELECT note, target FROM note_links"))
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                string from = reader.GetString(0);
                string target = reader.GetString(1);
                string? to = names.Resolve(target);
                if (string.Equals(from, note, StringComparison.Ordinal))
                {
                    if (to is null)
                    {
                        // One row per missing note: "Gamma" and "gamma.md" would both become the same note.
                        missing.TryAdd(StripMd(target), target);
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
    /// notes, sorted by the shown name ignoring case, then ordinal. The shown name is the title of the person's page
    /// (<see cref="Context.People.PersonOf"/>) when it exists, else the ordinal-first spelling among the mentions.
    /// </summary>
    public IReadOnlyList<PersonCount> People() => Run(connection =>
    {
        Dictionary<string, string> pages = PersonPages(connection);
        Dictionary<string, (int Count, string Spelling)> people = new(StringComparer.Ordinal);
        using (SqliteCommand command = Command(connection, null, "SELECT note, person, spelling FROM note_people"))
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                string note = reader.GetString(0);
                string person = reader.GetString(1);
                string spelling = reader.GetString(2);
                if (IsOwnPage(note, person))
                {
                    continue;
                }

                people[person] = people.TryGetValue(person, out (int Count, string Spelling) known)
                    ? (known.Count + 1, string.CompareOrdinal(spelling, known.Spelling) < 0 ? spelling : known.Spelling)
                    : (1, spelling);
            }
        }

        return (IReadOnlyList<PersonCount>)people
            .Select(p => new PersonCount(pages.GetValueOrDefault(p.Key, p.Value.Spelling), p.Value.Count))
            .OrderBy(p => p.Person, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Person, StringComparer.Ordinal)
            .ToList();
    });

    /// <summary>
    /// The notes that mention <paramref name="person"/> with <c>@[...]</c>, matched by <see cref="PersonParser.Key"/>
    /// of the trimmed name with whitespace runs collapsed (so <c>anna schmidt</c> finds <c>@[ANNA  schmidt]</c>), sorted
    /// by name ignoring case, then ordinal. Never the person's own page.
    /// </summary>
    public IReadOnlyList<string> NotesMentioning(string person)
    {
        string key = PersonParser.Key(TagParser.CollapseWhitespace(person));
        return Run(connection =>
        {
            List<string> names = [];
            using SqliteCommand command = Command(connection, null, "SELECT note FROM note_people WHERE person = $person");
            command.Parameters.AddWithValue("$person", key);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string note = reader.GetString(0);
                if (!IsOwnPage(note, key))
                {
                    names.Add(note);
                }
            }

            return SortNames(names);
        });
    }

    /// <summary>
    /// The name shown for <paramref name="person"/> (matched like <see cref="NotesMentioning"/>): the title of their
    /// page when it exists, else the ordinal-first spelling among the mentions in other notes, else <c>null</c>.
    /// </summary>
    public string? PersonName(string person)
    {
        string key = PersonParser.Key(TagParser.CollapseWhitespace(person));
        return Run(connection =>
        {
            if (PersonPages(connection).TryGetValue(key, out string? title))
            {
                return title;
            }

            string? first = null;
            using SqliteCommand command = Command(connection, null, "SELECT note, spelling FROM note_people WHERE person = $person");
            command.Parameters.AddWithValue("$person", key);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string spelling = reader.GetString(1);
                if (!IsOwnPage(reader.GetString(0), key) && (first is null || string.CompareOrdinal(spelling, first) < 0))
                {
                    first = spelling;
                }
            }

            return first;
        });
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

    // Opens the database, creates the tables if needed and runs the work. Any SQLite error becomes a
    // TagIndexException. The file is never deleted or replaced.
    private T Run<T>(Func<SqliteConnection, T> work)
    {
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
            Execute(connection, null, Schema);
            return work(connection);
        }
        catch (SqliteException ex)
        {
            throw new TagIndexException($"Can't read {FileName}.", ex);
        }
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
