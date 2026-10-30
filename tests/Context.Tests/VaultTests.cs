using System.Text;
using System.Text.Json;

namespace Context.Tests;

public sealed class VaultTests : IDisposable
{
    internal const int TestIterations = 1000;
    internal const string Passphrase = "correct horse battery";

    private static readonly string[] Kinds =
    [
        Vault.KindNote, Vault.KindTodos, Vault.KindCalendarLink, Vault.KindCalendarCache, Vault.KindCalendar,
    ];

    private readonly string root;

    public VaultTests()
    {
        root = Path.Combine(Path.GetTempPath(), "context-vault-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private Vault NewVault() => Vault.Create(root, Passphrase, TestIterations);

    public static TheoryData<int> Sizes() => [0, 1, 15, 16, 17, 1000, 70_000];

    // AC-2
    [Theory]
    [MemberData(nameof(Sizes))]
    public void Encrypt_ThenDecrypt_RoundTripsEveryKind(int size)
    {
        using Vault vault = NewVault();
        byte[] plain = new byte[size];
        new Random(size).NextBytes(plain);

        foreach (string kind in Kinds)
        {
            byte[] encrypted = vault.Encrypt(kind, plain);
            Assert.True(Vault.IsEncrypted(encrypted));
            Assert.Equal(plain.Length + Vault.Overhead, encrypted.Length);
            Assert.Equal(plain, vault.Decrypt(kind, encrypted));
        }
    }

    // AC-2
    [Fact]
    public void Encrypt_HasMagicVersionAndFreshNonce()
    {
        using Vault vault = NewVault();
        byte[] plain = Encoding.UTF8.GetBytes("same text");

        byte[] first = vault.Encrypt(Vault.KindNote, plain);
        byte[] second = vault.Encrypt(Vault.KindNote, plain);

        Assert.Equal(new byte[] { 0x00, 0x43, 0x54, 0x58, 0x45, 0x01 }, first[..6]);
        Assert.NotEqual(first[6..18], second[6..18]);
        Assert.NotEqual(first, second);
    }

    // AC-2
    [Fact]
    public void Decrypt_WithOtherKind_Throws()
    {
        using Vault vault = NewVault();
        byte[] encrypted = vault.Encrypt(Vault.KindTodos, Encoding.UTF8.GetBytes("[]"));

        foreach (string kind in Kinds.Where(k => k != Vault.KindTodos))
        {
            Assert.Throws<VaultException>(() => vault.Decrypt(kind, encrypted));
        }
    }

    // AC-2
    [Fact]
    public void Decrypt_AnyFlippedBit_Throws()
    {
        using Vault vault = NewVault();
        byte[] encrypted = vault.Encrypt(Vault.KindNote, Encoding.UTF8.GetBytes("# Secret\nhello"));

        for (int i = 0; i < encrypted.Length; i++)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                byte[] changed = (byte[])encrypted.Clone();
                changed[i] ^= (byte)(1 << bit);
                Assert.Throws<VaultException>(() => vault.Decrypt(Vault.KindNote, changed));
            }
        }
    }

    // AC-2
    [Fact]
    public void Decrypt_Truncated_Throws()
    {
        using Vault vault = NewVault();
        byte[] encrypted = vault.Encrypt(Vault.KindNote, Encoding.UTF8.GetBytes("# Secret\nhello"));

        for (int length = 0; length < encrypted.Length; length++)
        {
            Assert.Throws<VaultException>(() => vault.Decrypt(Vault.KindNote, encrypted[..length]));
        }
    }

    // AC-2
    [Fact]
    public void Decrypt_PlainInput_Throws()
    {
        using Vault vault = NewVault();

        Assert.Throws<VaultException>(() => vault.Decrypt(Vault.KindNote, Encoding.UTF8.GetBytes("# plain\n")));
    }

    // AC-2
    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0x00, 0x43, 0x54, 0x58 })]
    [InlineData(new byte[] { 0x01, 0x43, 0x54, 0x58, 0x45, 0x01 })]
    [InlineData(new byte[] { 0x00, 0x43, 0x54, 0x58, 0x46, 0x01 })]
    [InlineData(new byte[] { 0x23, 0x20, 0x54, 0x69, 0x74, 0x6c, 0x65 })]
    public void IsEncrypted_FalseWithoutMagic(byte[] bytes)
    {
        Assert.False(Vault.IsEncrypted(bytes));
    }

    // AC-2
    [Fact]
    public void IsEncrypted_FalseForRandomBytesWithoutMagic()
    {
        Random random = new Random(42);
        for (int i = 0; i < 2000; i++)
        {
            byte[] bytes = new byte[random.Next(0, 40)];
            random.NextBytes(bytes);
            bool hasMagic = bytes.Length >= 5 && bytes[0] == 0 && bytes[1] == 0x43 && bytes[2] == 0x54
                && bytes[3] == 0x58 && bytes[4] == 0x45;
            Assert.Equal(hasMagic, Vault.IsEncrypted(bytes));
        }
    }

    [Fact]
    public void Unlock_WithSamePassphrase_DecryptsWhatCreateEncrypted()
    {
        byte[] encrypted;
        using (Vault vault = NewVault())
        {
            encrypted = vault.Encrypt(Vault.KindNote, Encoding.UTF8.GetBytes("hello"));
        }

        using Vault unlocked = Vault.Unlock(root, Passphrase);
        Assert.Equal("hello", Encoding.UTF8.GetString(unlocked.Decrypt(Vault.KindNote, encrypted)));
    }

    // AC-6, AC-11
    [Fact]
    public void Unlock_WrongPassphrase_ThrowsWithoutPassphraseAndChangesNothing()
    {
        NewVault().Dispose();
        byte[] before = File.ReadAllBytes(Vault.KeyFilePath(root));

        WrongPassphraseException ex = Assert.Throws<WrongPassphraseException>(
            () => Vault.Unlock(root, "wrong passphrase 123"));

        Assert.DoesNotContain("wrong passphrase 123", ex.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(Vault.KeyFilePath(root)));
        Assert.Single(Directory.GetFiles(root));
    }

    [Fact]
    public void Unlock_MissingOrBrokenKeyFile_ThrowsVaultException()
    {
        Assert.Throws<VaultException>(() => Vault.Unlock(root, Passphrase));

        Directory.CreateDirectory(root);
        File.WriteAllText(Vault.KeyFilePath(root), "{ not json");
        Assert.Throws<VaultException>(() => Vault.Unlock(root, Passphrase));

        File.WriteAllText(Vault.KeyFilePath(root), "{\"version\":1}");
        Assert.Throws<VaultException>(() => Vault.Unlock(root, Passphrase));
    }

    [Fact]
    public void Create_WritesKeyFileWithNothingSecretInPlainForm()
    {
        using Vault vault = NewVault();

        string json = File.ReadAllText(Vault.KeyFilePath(root));
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement file = document.RootElement;
        Assert.Equal(1, file.GetProperty("version").GetInt32());
        Assert.Equal("PBKDF2-HMAC-SHA256", file.GetProperty("kdf").GetString());
        Assert.Equal(TestIterations, file.GetProperty("iterations").GetInt32());
        Assert.Equal(16, Convert.FromBase64String(file.GetProperty("salt").GetString()!).Length);
        Assert.Equal(48, Convert.FromBase64String(file.GetProperty("wrappedKey").GetString()!).Length);
        Assert.DoesNotContain(Passphrase, json, StringComparison.Ordinal);
        Assert.True(Vault.Exists(root));
    }

    [Fact]
    public void Create_NeverReplacesAnExistingKeyFile()
    {
        NewVault().Dispose();
        byte[] before = File.ReadAllBytes(Vault.KeyFilePath(root));

        Assert.ThrowsAny<IOException>(() => Vault.Create(root, "another passphrase", TestIterations));

        Assert.Equal(before, File.ReadAllBytes(Vault.KeyFilePath(root)));
    }

    [Fact]
    public void ProductionDefault_IsAtLeast600000Iterations()
    {
        Assert.True(Vault.DefaultIterations >= 600_000);
    }

    [Fact]
    public void Dispose_ZeroesTheKeySoTheVaultCantBeUsed()
    {
        Vault vault = NewVault();
        vault.Dispose();

        Assert.Throws<ObjectDisposedException>(() => vault.Encrypt(Vault.KindNote, [1, 2, 3]));
        byte[] key = (byte[])typeof(Vault)
            .GetField("key", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(vault)!;
        Assert.All(key, b => Assert.Equal(0, b));
    }

    [Fact]
    public void UnknownKind_Throws()
    {
        using Vault vault = NewVault();

        Assert.Throws<ArgumentException>(() => vault.Encrypt("other", [1]));
    }
}
