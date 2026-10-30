using System.Security.Cryptography;
using System.Text;

namespace Context;

/// <summary>The outcome of <see cref="Encryption.Migrate"/> or <see cref="Encryption.DecryptAll"/>.</summary>
internal sealed record MigrationResult
{
    public MigrationResult(int converted, IReadOnlyList<string> failed)
    {
        Converted = converted;
        Failed = failed;
    }

    /// <summary>How many files were converted.</summary>
    public int Converted { get; }

    /// <summary>
    /// The files that couldn't be converted, relative to the root with <c>/</c> (for example
    /// <c>calendar/work.ics</c>), sorted. They are left as they were.
    /// </summary>
    public IReadOnlyList<string> Failed { get; }
}

/// <summary>
/// The encryption state of one session, shared by every store of a root. <see cref="Vault"/> is <c>null</c> while
/// encryption is off (or still locked): stores then write plain bytes, exactly as before. While it is set, every
/// write is encrypted. Reads accept both: plain bytes as they are, encrypted bytes through the vault (and a
/// <see cref="VaultException"/> without one), so a folder left half-migrated stays readable.
/// </summary>
internal sealed class Encryption
{
    /// <summary>The temp-file prefix all stores use; temp files only ever hold ciphertext while the vault is set.</summary>
    private const string TempSuffix = ".tmp";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string root;

    public Encryption(string root)
    {
        this.root = Path.GetFullPath(root);
    }

    /// <summary>The folder the data lives in.</summary>
    public string Root => root;

    /// <summary>The unlocked vault, or <c>null</c> while encryption is off or locked.</summary>
    public Vault? Vault { get; private set; }

    /// <summary>True while a vault is set, so every write is encrypted.</summary>
    public bool IsOn => Vault is not null;

    /// <summary>True when the root has an <c>encryption.json</c>.</summary>
    public bool IsConfigured => Vault.Exists(root);

    /// <summary>
    /// Runs before a migrated file replaces its original, with the root-relative name. Tests use it to inject a
    /// fault part way through.
    /// </summary>
    internal Action<string>? BeforeReplace { get; set; }

    /// <summary>
    /// Unlocks <c>encryption.json</c> with <paramref name="passphrase"/> and keeps the vault for the session.
    /// Throws <see cref="WrongPassphraseException"/> or <see cref="VaultException"/>; nothing is written.
    /// </summary>
    public void Unlock(string passphrase)
    {
        Vault vault = Vault.Unlock(root, passphrase);
        Use(vault);
    }

    /// <summary>Sets the session's vault (disposing the previous one), or turns encryption off with <c>null</c>.</summary>
    public void Use(Vault? vault)
    {
        if (!ReferenceEquals(Vault, vault))
        {
            Vault?.Dispose();
        }

        Vault = vault;
    }

    /// <summary>
    /// Reads a whole file and returns its plain bytes. Plain files come back unchanged. Throws
    /// <see cref="VaultException"/> for an encrypted file that fails authentication or when no vault is set.
    /// </summary>
    public static byte[] ReadBytes(Encryption? encryption, string path, string kind) =>
        Unprotect(encryption, kind, File.ReadAllBytes(path));

    /// <summary>The plain bytes of <paramref name="data"/> (see <see cref="ReadBytes"/>).</summary>
    public static byte[] Unprotect(Encryption? encryption, string kind, byte[] data)
    {
        if (!Vault.IsEncrypted(data))
        {
            return data;
        }

        Vault? vault = encryption?.Vault;
        if (vault is null)
        {
            throw new VaultException($"Can't read this {kind} file: it is encrypted.");
        }

        return vault.Decrypt(kind, data);
    }

    /// <summary>The bytes to write: encrypted while a vault is set, else <paramref name="plain"/> unchanged.</summary>
    public static byte[] Protect(Encryption? encryption, string kind, byte[] plain)
    {
        Vault? vault = encryption?.Vault;
        return vault is null ? plain : vault.Encrypt(kind, plain);
    }

    /// <summary>
    /// Decodes text the way <see cref="File.ReadAllText(string)"/> does: UTF-8 unless a byte order mark says
    /// otherwise, and the mark is dropped.
    /// </summary>
    public static string DecodeText(byte[] bytes)
    {
        using MemoryStream stream = new MemoryStream(bytes, writable: false);
        using StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>Reads a whole text file through <see cref="ReadBytes"/> and <see cref="DecodeText"/>.</summary>
    public static string ReadText(Encryption? encryption, string path, string kind) =>
        DecodeText(ReadBytes(encryption, path, kind));

    /// <summary>UTF-8 without a byte order mark.</summary>
    public static byte[] EncodeText(string text) => Utf8NoBom.GetBytes(text);

    /// <summary>
    /// Encrypts every file of the data set that is still plain (top-level <c>*.md</c>, <c>todos.json</c>,
    /// <c>calendar-link.txt</c>, <c>calendar-link.ics</c>, top-level <c>calendar/*.ics</c>). Each file is written
    /// to a temp file next to it, read back and decrypted to check it matches the original, given the original's
    /// last-write time, and only then moved over it. A file that fails stays plain and intact. Finally
    /// <c>context.db</c> and its <c>-journal</c>/<c>-wal</c>/<c>-shm</c> files are deleted. Needs a vault.
    /// </summary>
    public MigrationResult Migrate()
    {
        Vault vault = Vault ?? throw new InvalidOperationException("Encryption is not unlocked.");
        int converted = 0;
        List<string> failed = [];
        foreach ((string path, string kind) in DataFiles(failed))
        {
            string name = RelativeName(path);
            try
            {
                byte[] original = File.ReadAllBytes(path);
                if (Vault.IsEncrypted(original))
                {
                    continue;
                }

                byte[] encrypted = vault.Encrypt(kind, original);
                Replace(path, name, encrypted, written => vault.Decrypt(kind, written).AsSpan().SequenceEqual(original));
                converted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                failed.Add(name);
            }
        }

        foreach (string suffix in new[] { string.Empty, "-journal", "-wal", "-shm" })
        {
            string db = Path.Combine(root, TagIndex.FileName + suffix);
            try
            {
                if (File.Exists(db))
                {
                    File.Delete(db);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(TagIndex.FileName + suffix);
            }
        }

        failed.Sort(StringComparer.OrdinalIgnoreCase);
        return new MigrationResult(converted, failed);
    }

    /// <summary>
    /// Decrypts every encrypted file of the same data set as <see cref="Migrate"/> in place (temp file, check,
    /// keep the last-write time, move). When every file worked, <c>encryption.json</c> is deleted last and
    /// encryption is turned off for the session; when any failed, encryption stays on so nothing becomes
    /// unreadable. Needs a vault.
    /// </summary>
    public MigrationResult DecryptAll()
    {
        Vault vault = Vault ?? throw new InvalidOperationException("Encryption is not unlocked.");
        int converted = 0;
        List<string> failed = [];
        foreach ((string path, string kind) in DataFiles(failed))
        {
            string name = RelativeName(path);
            try
            {
                byte[] data = File.ReadAllBytes(path);
                if (!Vault.IsEncrypted(data))
                {
                    continue;
                }

                byte[] plain = vault.Decrypt(kind, data);
                Replace(path, name, plain, written => written.AsSpan().SequenceEqual(plain));
                converted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                failed.Add(name);
            }
        }

        if (failed.Count == 0)
        {
            try
            {
                File.Delete(Vault.KeyFilePath(root));
                Use(null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(Vault.FileName);
            }
        }

        failed.Sort(StringComparer.OrdinalIgnoreCase);
        return new MigrationResult(converted, failed);
    }

    // Writes bytes to a new temp file next to path, checks them, gives the temp file the original's last-write
    // time and moves it over the original. The temp file is removed on any failure.
    private void Replace(string path, string name, byte[] bytes, Func<byte[], bool> verify)
    {
        string folder = Path.GetDirectoryName(path)!;
        string temp = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}{TempSuffix}");
        try
        {
            DateTime modified = File.GetLastWriteTimeUtc(path);
            CalendarLink.WritePrivate(temp, bytes);
            if (!verify(File.ReadAllBytes(temp)))
            {
                throw new IOException("The written copy doesn't match.");
            }

            File.SetLastWriteTimeUtc(temp, modified);
            BeforeReplace?.Invoke(name);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing more to do; it holds no more than the original does.
            }
        }
    }

    // Every file of the data set with its kind. A calendar folder that can't be listed is reported as failed.
    private List<(string Path, string Kind)> DataFiles(List<string> failed)
    {
        List<(string Path, string Kind)> files = [];
        if (!Directory.Exists(root))
        {
            return files;
        }

        EnumerationOptions options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            MatchCasing = MatchCasing.CaseInsensitive,
            IgnoreInaccessible = true,
        };
        foreach (string path in Directory.EnumerateFiles(root, "*.md", options)
            .Where(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal))
        {
            files.Add((path, Vault.KindNote));
        }

        AddIfExists(files, Path.Combine(root, TodoStore.FileName), Vault.KindTodos);
        AddIfExists(files, Path.Combine(root, CalendarLink.FileName), Vault.KindCalendarLink);
        AddIfExists(files, Path.Combine(root, CalendarFeed.CacheFileName), Vault.KindCalendarCache);

        string calendar = Path.Combine(root, CalendarStore.FolderName);
        if (Directory.Exists(calendar))
        {
            try
            {
                foreach (string path in Directory.EnumerateFiles(calendar, "*.ics", options)
                    .Where(p => p.EndsWith(".ics", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.Ordinal))
                {
                    files.Add((path, Vault.KindCalendar));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(CalendarStore.FolderName + "/");
            }
        }

        return files;
    }

    private static void AddIfExists(List<(string Path, string Kind)> files, string path, string kind)
    {
        if (File.Exists(path))
        {
            files.Add((path, kind));
        }
    }

    private string RelativeName(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
