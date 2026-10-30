using System.Security.Cryptography;
using System.Text;

namespace Context.Tests;

/// <summary>
/// A Context folder with one file of every kind. Each holds a unique plaintext marker, so a test can check that
/// no marker is left anywhere on disk after <c>/encrypt</c>.
/// </summary>
internal static class SampleData
{
    public const string NoteMarker = "noteSecret7f3a";
    public const string OtherNoteMarker = "otherSecret2b9c";
    public const string TodoMarker = "todoSecret5d1e";
    public const string CalendarMarker = "calSecret8a4f";
    public const string CacheMarker = "cacheSecret3c6b";

    public static readonly string[] Markers =
    [
        NoteMarker, OtherNoteMarker, TodoMarker, CalendarMarker, CacheMarker, FakeHttpHandler.PathToken,
        FakeHttpHandler.QueryToken,
    ];

    /// <summary>The root-relative names of the files <c>/encrypt</c> converts.</summary>
    public static readonly string[] DataFiles =
    [
        "Plan.md", "Other.md", "todos.json", "calendar-link.txt", "calendar-link.ics", "calendar/work.ics",
    ];

    /// <summary>A session for <paramref name="root"/> with a new vault (low iterations, for speed).</summary>
    public static Encryption On(string root)
    {
        Encryption encryption = new Encryption(root, VaultTests.TestIterations);
        encryption.Use(Vault.Create(root, VaultTests.Passphrase, VaultTests.TestIterations));
        return encryption;
    }

    /// <summary>Flips the last bit of <paramref name="path"/>, so an encrypted file fails authentication.</summary>
    public static byte[] Tamper(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 1;
        File.WriteAllBytes(path, bytes);
        return bytes;
    }

    public static readonly DateTime Modified = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    /// <summary>Writes the sample files (plain) into <paramref name="root"/>, all with <see cref="Modified"/>.</summary>
    public static void Write(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "calendar", "sub"));
        WriteFile(root, "Plan.md", $"# Plan\n{NoteMarker} #work\n");
        WriteFile(root, "Other.md", $"# Other\n{OtherNoteMarker} #work #home\n");
        WriteFile(
            root,
            "todos.json",
            "[\n  {\n    \"text\": \"" + TodoMarker + " #work\",\n    \"due\": \"2026-10-29\",\n"
                + "    \"created\": \"2026-10-01T00:00:00Z\"\n  }\n]\n");
        WriteFile(root, "calendar-link.txt", FakeHttpHandler.Link + "\n");
        WriteFile(root, "calendar-link.ics", Ics(CacheMarker));
        WriteFile(root, "calendar/work.ics", Ics(CalendarMarker));
        // Not part of the data set: left alone.
        WriteFile(root, "calendar/sub/nested.ics", Ics("nestedPlain"));
        WriteFile(root, "readme.txt", "plain readme");
    }

    public static string Ics(string summary) =>
        "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:" + summary + "\r\nDTSTART:20261029T080000Z\r\n"
        + "DTEND:20261029T081500Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    /// <summary>SHA-256 of every file under <paramref name="root"/>, by root-relative name.</summary>
    public static Dictionary<string, string> Hashes(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                p => Path.GetRelativePath(root, p).Replace('\\', '/'),
                p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));

    /// <summary>The bytes of every file under <paramref name="root"/>, by root-relative name.</summary>
    public static Dictionary<string, byte[]> Contents(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(root, p).Replace('\\', '/'), File.ReadAllBytes);

    /// <summary>Asserts that no file under <paramref name="root"/> holds any marker (as UTF-8 bytes).</summary>
    public static void AssertNoMarkers(string root)
    {
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
            foreach (string marker in Markers)
            {
                Assert.False(text.Contains(marker, StringComparison.Ordinal), $"{marker} found in {path}");
            }
        }
    }

    private static void WriteFile(string root, string name, string text)
    {
        string path = Path.Combine(root, name);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text));
        File.SetLastWriteTimeUtc(path, Modified);
    }
}

public sealed class EncryptionTests : IDisposable
{
    private readonly string root;

    public EncryptionTests()
    {
        root = Path.Combine(Path.GetTempPath(), "context-encryption-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        SampleData.Write(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private Encryption On()
    {
        Encryption encryption = new Encryption(root, VaultTests.TestIterations);
        encryption.Use(Vault.Create(root, VaultTests.Passphrase, VaultTests.TestIterations));
        return encryption;
    }

    private void WriteDb()
    {
        foreach (string suffix in new[] { string.Empty, "-journal", "-wal", "-shm" })
        {
            File.WriteAllText(Path.Combine(root, "context.db" + suffix), "db " + SampleData.NoteMarker);
        }
    }

    // AC-3, AC-4
    [Fact]
    public void Migrate_EncryptsEveryDataFile_KeepsTimes_DeletesDb()
    {
        WriteDb();
        Encryption encryption = On();

        MigrationResult result = encryption.Migrate();

        Assert.Equal(SampleData.DataFiles.Length, result.Converted);
        Assert.Empty(result.Failed);
        foreach (string name in SampleData.DataFiles)
        {
            string path = Path.Combine(root, name);
            Assert.True(Vault.IsEncrypted(File.ReadAllBytes(path)), name);
            Assert.Equal(SampleData.Modified, File.GetLastWriteTimeUtc(path));
        }

        Assert.False(File.Exists(Path.Combine(root, "context.db")));
        Assert.Empty(Directory.GetFiles(root, "context.db*"));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        SampleData.AssertNoMarkers(root);
        Assert.Equal("plain readme", File.ReadAllText(Path.Combine(root, "readme.txt")));
        Assert.Contains("nestedPlain", File.ReadAllText(Path.Combine(root, "calendar", "sub", "nested.ics")));
    }

    // AC-3
    [Fact]
    public void Migrate_EachFileDecryptsToItsOriginalBytes()
    {
        Dictionary<string, byte[]> before = SampleData.Contents(root);
        Encryption encryption = On();

        encryption.Migrate();

        Vault vault = encryption.Vault!;
        Assert.Equal(before["Plan.md"], vault.Decrypt(Vault.KindNote, File.ReadAllBytes(Path.Combine(root, "Plan.md"))));
        Assert.Equal(
            before["todos.json"], vault.Decrypt(Vault.KindTodos, File.ReadAllBytes(Path.Combine(root, "todos.json"))));
        Assert.Equal(
            before["calendar-link.txt"],
            vault.Decrypt(Vault.KindCalendarLink, File.ReadAllBytes(Path.Combine(root, "calendar-link.txt"))));
        Assert.Equal(
            before["calendar-link.ics"],
            vault.Decrypt(Vault.KindCalendarCache, File.ReadAllBytes(Path.Combine(root, "calendar-link.ics"))));
        Assert.Equal(
            before["calendar/work.ics"],
            vault.Decrypt(Vault.KindCalendar, File.ReadAllBytes(Path.Combine(root, "calendar", "work.ics"))));
    }

    // AC-3
    [Fact]
    public void Migrate_FaultMidway_LeavesFailedFilesPlainAndIntact_ReRunFinishes()
    {
        Dictionary<string, string> before = SampleData.Hashes(root);
        Encryption encryption = On();
        encryption.BeforeReplace = name =>
        {
            if (name is "todos.json" or "calendar/work.ics")
            {
                throw new IOException("injected");
            }
        };

        MigrationResult first = encryption.Migrate();

        Assert.Equal(["calendar/work.ics", "todos.json"], first.Failed);
        Assert.Equal(SampleData.DataFiles.Length - 2, first.Converted);
        Dictionary<string, string> after = SampleData.Hashes(root);
        Assert.Equal(before["todos.json"], after["todos.json"]);
        Assert.Equal(before["calendar/work.ics"], after["calendar/work.ics"]);
        Assert.NotEqual(before["Plan.md"], after["Plan.md"]);
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));

        // AC-5: a re-run only converts what is still plain.
        encryption.BeforeReplace = null;
        string planHash = after["Plan.md"];
        MigrationResult second = encryption.Migrate();

        Assert.Equal(2, second.Converted);
        Assert.Empty(second.Failed);
        Assert.Equal(planHash, SampleData.Hashes(root)["Plan.md"]);
        SampleData.AssertNoMarkers(root);
    }

    // AC-5
    [Fact]
    public void Migrate_WhenAllEncrypted_ConvertsNothingAndChangesNothing()
    {
        Encryption encryption = On();
        encryption.Migrate();
        Dictionary<string, string> before = SampleData.Hashes(root);

        MigrationResult result = encryption.Migrate();

        Assert.Equal(0, result.Converted);
        Assert.Equal(before, SampleData.Hashes(root));
    }

    // AC-8
    [Fact]
    public void DecryptAll_RestoresByteIdenticalFiles_DeletesKeyFileLast_TurnsOff()
    {
        Dictionary<string, string> original = SampleData.Hashes(root);
        Encryption encryption = On();
        encryption.Migrate();

        MigrationResult result = encryption.DecryptAll();

        Assert.Equal(SampleData.DataFiles.Length, result.Converted);
        Assert.Empty(result.Failed);
        Assert.False(encryption.IsOn);
        Assert.False(Vault.Exists(root));
        Assert.Equal(original, SampleData.Hashes(root));
        foreach (string name in SampleData.DataFiles)
        {
            Assert.Equal(SampleData.Modified, File.GetLastWriteTimeUtc(Path.Combine(root, name)));
        }
    }

    // AC-8
    [Fact]
    public void DecryptAll_WithAFailure_KeepsKeyFileAndStaysOn_ReRunFinishes()
    {
        Dictionary<string, string> original = SampleData.Hashes(root);
        Encryption encryption = On();
        encryption.Migrate();
        encryption.BeforeReplace = name =>
        {
            if (name == "Plan.md")
            {
                throw new IOException("injected");
            }
        };

        MigrationResult first = encryption.DecryptAll();

        Assert.Equal(["Plan.md"], first.Failed);
        Assert.True(encryption.IsOn);
        Assert.True(Vault.Exists(root));
        Assert.True(Vault.IsEncrypted(File.ReadAllBytes(Path.Combine(root, "Plan.md"))));

        encryption.BeforeReplace = null;
        MigrationResult second = encryption.DecryptAll();

        Assert.Equal(1, second.Converted);
        Assert.False(encryption.IsOn);
        Assert.Equal(original, SampleData.Hashes(root));
    }

    // AC-7
    [Fact]
    public void DecryptAll_TamperedFile_IsReportedAndNeverOverwritten()
    {
        Encryption encryption = On();
        encryption.Migrate();
        string path = Path.Combine(root, "Other.md");
        byte[] tampered = File.ReadAllBytes(path);
        tampered[^1] ^= 1;
        File.WriteAllBytes(path, tampered);

        MigrationResult result = encryption.DecryptAll();

        Assert.Equal(["Other.md"], result.Failed);
        Assert.Equal(tampered, File.ReadAllBytes(path));
        Assert.True(encryption.IsOn);
    }

    [Fact]
    public void Migrate_WithoutVault_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new Encryption(root).Migrate());
        Assert.Throws<InvalidOperationException>(() => new Encryption(root).DecryptAll());
    }

    [Fact]
    public void Unlock_WrongPassphrase_LeavesSessionOff()
    {
        On().Migrate();
        Encryption session = new Encryption(root, VaultTests.TestIterations);

        Assert.True(session.IsConfigured);
        Assert.Throws<WrongPassphraseException>(() => session.Unlock("not the passphrase"));
        Assert.False(session.IsOn);

        session.Unlock(VaultTests.Passphrase);
        Assert.True(session.IsOn);
    }

    // AC-1
    [Fact]
    public void Protect_WithoutVault_ReturnsTheSameBytes()
    {
        byte[] plain = Encoding.UTF8.GetBytes("plain");

        Assert.Same(plain, Encryption.Protect(null, Vault.KindNote, plain));
        Assert.Same(plain, Encryption.Protect(new Encryption(root), Vault.KindNote, plain));
        Assert.Same(plain, Encryption.Unprotect(new Encryption(root), Vault.KindNote, plain));
    }

    // AC-7
    [Fact]
    public void Unprotect_EncryptedWithoutVault_ThrowsVaultException()
    {
        Encryption encryption = On();
        byte[] encrypted = encryption.Vault!.Encrypt(Vault.KindNote, [1, 2]);

        Assert.Throws<VaultException>(() => Encryption.Unprotect(null, Vault.KindNote, encrypted));
    }

    [Fact]
    public void DecodeText_DropsBomLikeReadAllText()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i'];
        string path = Path.Combine(root, "bom.txt");
        File.WriteAllBytes(path, bytes);

        Assert.Equal(File.ReadAllText(path), Encryption.DecodeText(bytes));
    }

    // Feedback 3
    [Fact]
    public void EncryptedFilesWithoutKey_CountsEncryptedDataFiles_OnlyWithoutKeyFile()
    {
        Encryption encryption = On();
        Assert.Equal(0, encryption.EncryptedFilesWithoutKey());
        encryption.Migrate();
        Assert.Equal(0, encryption.EncryptedFilesWithoutKey());

        File.Delete(Vault.KeyFilePath(root));

        Assert.Equal(SampleData.DataFiles.Length, new Encryption(root).EncryptedFilesWithoutKey());
    }

    // Feedback 3
    [Fact]
    public void EncryptedFilesWithoutKey_PlainFolder_IsZero()
    {
        Assert.Equal(0, new Encryption(root).EncryptedFilesWithoutKey());
        Assert.Equal(0, new Encryption(Path.Combine(root, "missing")).EncryptedFilesWithoutKey());
    }
}
