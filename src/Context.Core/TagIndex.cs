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

/// <summary>A note linked to another note, and the tags the two share (sorted).</summary>
internal sealed record NoteLink
{
    public NoteLink(string note, IReadOnlyList<string> sharedTags)
    {
        Note = note;
        SharedTags = sharedTags;
    }

    public string Note { get; }

    public IReadOnlyList<string> SharedTags { get; }
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
/// <para>
/// While encryption is on (the shared <see cref="Encryption"/> has a vault), the index lives in an in-memory
/// database instead (<c>PRAGMA temp_store=MEMORY</c>), rebuilt from the notes each session, and
/// <c>context.db</c> is never opened or created. Turning encryption off drops it and goes back to the file.
/// </para>
/// </summary>
internal sealed class TagIndex : IDisposable
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
        """;

    /// <summary>
    /// The parser version the stored tags were made with, kept in <c>PRAGMA user_version</c>. When the
    /// database has an older one, <see cref="Sync"/> re-parses every note once. 1: <c>#[bracket tags]</c>.
    /// </summary>
    public const int ParserVersion = 1;

    private readonly string root;
    private readonly Encryption? encryption;

    // The in-memory database used while encryption is on; null otherwise.
    private SqliteConnection? memory;

    /// <param name="root">The folder <c>context.db</c> lives in.</param>
    /// <param name="encryption">The session's encryption state; while it has a vault the index stays in memory.</param>
    public TagIndex(string root, Encryption? encryption = null)
    {
        this.root = Path.GetFullPath(root);
        this.encryption = encryption;
    }

    /// <summary>True while the index is kept in memory rather than in <c>context.db</c>.</summary>
    public bool InMemory => encryption?.IsOn == true;

    /// <summary>The full path of <c>context.db</c>.</summary>
    public string FilePath => Path.Combine(root, FileName);

    /// <summary>
    /// Brings the index up to date with the notes in <paramref name="store"/>, in one transaction. Creates
    /// the root folder and the database if missing. A note is re-parsed only when its last-write time
    /// (UTC ticks) differs from the stored one. Notes that are gone lose their rows. A note that can't be
    /// read is kept with no tags and no stored time, so it is retried on the next sync.
    /// Throws <see cref="TagIndexException"/> if the database can't be opened or isn't valid.
    /// </summary>
    public void Sync(NoteStore store)
    {
        Directory.CreateDirectory(root);
        IReadOnlyList<NoteInfo> notes = store.List();
        Run(connection =>
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
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
                long? newModified = modified;
                try
                {
                    tags = TagParser.Parse(store.Read(note));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    tags = [];
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
                Execute(connection, transaction, "DELETE FROM notes WHERE name = $note", ("$note", gone));
            }

            // Tag rows whose note row is missing (e.g. edited by hand) would otherwise never go away.
            Execute(connection, transaction, "DELETE FROM note_tags WHERE note NOT IN (SELECT name FROM notes)");
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
    /// The indexed note called <paramref name="name"/> (trimmed): an exact match first, then one ignoring
    /// case. <c>null</c> if there is none.
    /// </summary>
    public string? FindNote(string name)
    {
        string wanted = name.Trim();
        List<string> names = Run(connection =>
        {
            List<string> all = [];
            using SqliteCommand command = Command(connection, null, "SELECT name FROM notes");
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                all.Add(reader.GetString(0));
            }

            return all;
        });

        return names.FirstOrDefault(n => string.Equals(n, wanted, StringComparison.Ordinal))
            ?? names.FirstOrDefault(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every other note that shares at least one tag with <paramref name="noteName"/> (matched like
    /// <see cref="FindNote"/>), once each, sorted by name ignoring case, with the shared tags sorted.
    /// A note never links to itself. An unknown note has no links.
    /// </summary>
    public IReadOnlyList<NoteLink> Links(string noteName)
    {
        string? note = FindNote(noteName);
        if (note is null)
        {
            return [];
        }

        return Run(connection =>
        {
            Dictionary<string, List<string>> shared = new(StringComparer.Ordinal);
            using SqliteCommand command = Command(
                connection,
                null,
                "SELECT other.note, other.tag FROM note_tags AS mine "
                    + "JOIN note_tags AS other ON other.tag = mine.tag AND other.note <> mine.note "
                    + "WHERE mine.note = $note");
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

            return SortNames(shared.Keys)
                .Select(n => new NoteLink(n, shared[n].Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList()))
                .ToList();
        });
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
        if (InMemory)
        {
            try
            {
                return work(Memory());
            }
            catch (SqliteException ex)
            {
                throw new TagIndexException($"Can't read {FileName}.", ex);
            }
        }

        DropMemory();
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        try
        {
            using SqliteConnection connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, null, Schema);
            return work(connection);
        }
        catch (SqliteException ex)
        {
            throw new TagIndexException($"Can't read {FileName}.", ex);
        }
    }

    /// <summary>Closes the in-memory database, if one is open.</summary>
    public void Dispose() => DropMemory();

    // The open in-memory database, created (with the schema) on first use.
    private SqliteConnection Memory()
    {
        if (memory is null)
        {
            SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = ":memory:",
                Pooling = false,
            }.ToString());
            try
            {
                connection.Open();
                Execute(connection, null, "PRAGMA temp_store=MEMORY");
                Execute(connection, null, Schema);
            }
            catch
            {
                connection.Dispose();
                throw;
            }

            memory = connection;
        }

        return memory;
    }

    private void DropMemory()
    {
        memory?.Dispose();
        memory = null;
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
