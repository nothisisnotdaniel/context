using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Context.Tests;

// 136 AC-1 / AC-2: the sealed note format, with P-256 keys that live only in this test process.
public sealed class SealedNoteTests : IDisposable
{
    private const string Marker = "plain-marker-7c1d";

    private readonly ECDiffieHellman machine = SealedNote.NewKey();
    private readonly ECDiffieHellman recovery = SealedNote.NewKey();
    private int agrees;

    public void Dispose()
    {
        machine.Dispose();
        recovery.Dispose();
    }

    // AC-1: what was sealed comes back, whatever the text.
    [Theory]
    [InlineData("")]
    [InlineData("# Plan\n\nText #work\n")]
    [InlineData("line one\r\nline two\r\n")]
    [InlineData("﻿# With a mark\r\n")]
    [InlineData("Grüße, 日本語, 🙂\n")]
    public void SealThenOpen_ReturnsTheText(string text)
    {
        byte[] file = SealedNote.Seal("Plan", text, Recipients());

        Assert.Equal(text, SealedNote.Open("Plan", file, MachineKey(), Agree));
        Assert.Equal(1, agrees);
        Assert.Equal(text, SealedNote.Open("Plan", file, recovery.ExportSubjectPublicKeyInfo(), p => SealedNote.Agree(recovery, p)));
    }

    // AC-1: the largest note (5 MB) fits the 8 MB file limit and comes back.
    [Fact]
    public void SealThenOpen_FiveMegabytes()
    {
        string text = new string('x', (int)NoteStore.MaxEditBytes);

        byte[] file = SealedNote.Seal("Big", text, Recipients());

        Assert.True(file.Length <= SealedNote.MaxFileBytes, file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(text, SealedNote.Open("Big", file, MachineKey(), Agree));
    }

    // AC-1: the file is the v1 format and holds no plain text.
    [Fact]
    public void Seal_WritesTheV1Format_WithoutPlainText()
    {
        byte[] file = SealedNote.Seal("Plan", "before " + Marker + " after", Recipients());

        string json = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(file);
        Assert.DoesNotContain(Marker, json, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(Marker)), json, StringComparison.Ordinal);
        Assert.DoesNotContain("Plan", json, StringComparison.Ordinal);
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(["v", "nonce", "tag", "text", "keys"], root.Select(p => p.Key).ToArray());
        Assert.Equal(1, root["v"]!.GetValue<int>());
        Assert.Equal(12, Bytes(root["nonce"]).Length);
        Assert.Equal(16, Bytes(root["tag"]).Length);
        Assert.Equal(Encoding.UTF8.GetByteCount("before " + Marker + " after"), Bytes(root["text"]).Length);
        JsonArray keys = root["keys"]!.AsArray();
        Assert.Equal(2, keys.Count);
        Assert.Equal(["machine", "recovery"], keys.Select(k => k!["kind"]!.GetValue<string>()).ToArray());
        Assert.Equal(
            [SealedNote.KeyId(MachineKey()), SealedNote.KeyId(recovery.ExportSubjectPublicKeyInfo())],
            keys.Select(k => k!["id"]!.GetValue<string>()).ToArray());
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(MachineKey())), keys[0]!["id"]!.GetValue<string>());
        Assert.All(keys, k =>
        {
            Assert.Equal(["id", "kind", "epk", "nonce", "tag", "key"], k!.AsObject().Select(p => p.Key).ToArray());
            Assert.Equal(65, Bytes(k["epk"]).Length);
            Assert.Equal(12, Bytes(k["nonce"]).Length);
            Assert.Equal(16, Bytes(k["tag"]).Length);
            Assert.Equal(32, Bytes(k["key"]).Length);
        });
    }

    // AC-1: two seals of the same text share no nonce, wrapped key, ephemeral key or sealed text.
    [Fact]
    public void Seal_Twice_SharesNothing()
    {
        JsonObject first = Parse(SealedNote.Seal("Plan", "The same text, twice.\n", Recipients()));
        JsonObject second = Parse(SealedNote.Seal("Plan", "The same text, twice.\n", Recipients()));

        List<string> one = Values(first);
        List<string> two = Values(second);
        Assert.Equal(11, one.Count);
        Assert.Equal(one.Count, one.Distinct().Count());
        Assert.Empty(one.Intersect(two));
    }

    // AC-2: a note opens only under its own name, exact case.
    [Theory]
    [InlineData("plan")]
    [InlineData("Plan ")]
    [InlineData("Other")]
    public void Open_OtherName_Fails(string name)
    {
        byte[] file = SealedNote.Seal("Plan", "text", Recipients());

        AssertFails(name, file);
    }

    // AC-2: any changed byte of the sealed text, its tag or nonce, or of the machine wrap, fails.
    [Theory]
    [InlineData("text")]
    [InlineData("tag")]
    [InlineData("nonce")]
    [InlineData("keys/epk")]
    [InlineData("keys/nonce")]
    [InlineData("keys/tag")]
    [InlineData("keys/key")]
    public void Open_ChangedByte_Fails(string field)
    {
        JsonObject root = Parse(SealedNote.Seal("Plan", "some text", Recipients()));
        JsonObject holder = field.StartsWith("keys/", StringComparison.Ordinal) ? root["keys"]![0]!.AsObject() : root;
        string property = field.Split('/')[^1];
        byte[] bytes = Bytes(holder[property]);
        for (int at = 0; at < bytes.Length; at += Math.Max(1, bytes.Length / 4))
        {
            byte[] changed = [.. bytes];
            changed[at] ^= 0x01;
            holder[property] = Convert.ToBase64String(changed);

            AssertFails("Plan", Encoding.UTF8.GetBytes(root.ToJsonString()));
        }

        holder[property] = Convert.ToBase64String(bytes);
        Assert.Equal("some text", SealedNote.Open("Plan", Encoding.UTF8.GetBytes(root.ToJsonString()), MachineKey(), Agree));
    }

    // AC-2: a wrap's id is part of what is sealed: under another id the wrap is not found, and a wrap of another key
    // under the machine's id does not unwrap.
    [Fact]
    public void Open_WrapUnderAnotherId_Fails()
    {
        JsonObject root = Parse(SealedNote.Seal("Plan", "text", Recipients()));
        string machineId = root["keys"]![0]!["id"]!.GetValue<string>();
        root["keys"]![0]!["id"] = root["keys"]![1]!["id"]!.GetValue<string>();
        root["keys"]![1]!["id"] = machineId;

        AssertFails("Plan", Encoding.UTF8.GetBytes(root.ToJsonString()));
    }

    // AC-2: the wraps of another note (same recipients) don't open this one.
    [Fact]
    public void Open_WrapCopiedFromAnotherNote_Fails()
    {
        JsonObject mine = Parse(SealedNote.Seal("Plan", "my text", Recipients()));
        JsonObject other = Parse(SealedNote.Seal("Plan", "other text", Recipients()));
        mine["keys"] = other["keys"]!.DeepClone();

        AssertFails("Plan", Encoding.UTF8.GetBytes(mine.ToJsonString()));
    }

    // AC-2: only version 1 is read.
    [Theory]
    [InlineData("2")]
    [InlineData("0")]
    [InlineData("\"1\"")]
    [InlineData("1.5")]
    public void Open_OtherVersion_Fails(string version)
    {
        JsonObject root = Parse(SealedNote.Seal("Plan", "text", Recipients()));
        root["v"] = JsonNode.Parse(version);

        AssertFails("Plan", Encoding.UTF8.GetBytes(root.ToJsonString()));
    }

    // AC-2: without a wrap for the given key the private key is never asked.
    [Fact]
    public void Open_NoWrapForTheKey_FailsWithoutAgree()
    {
        byte[] file = SealedNote.Seal("Plan", "text", [new SealedRecipient(SealedRecipient.Recovery, recovery.ExportSubjectPublicKeyInfo())]);

        Assert.Throws<SealedNoteException>(() => SealedNote.Open("Plan", file, MachineKey(), Agree));

        Assert.Equal(0, agrees);
        Assert.False(SealedNote.HasKey(file, MachineKey()));
        Assert.True(SealedNote.HasKey(file, recovery.ExportSubjectPublicKeyInfo()));
    }

    // Nits (security 1): a point in the file that is not on P-256 fails before the private key is asked.
    [Fact]
    public void Open_PointNotOnTheCurve_FailsWithoutAgree()
    {
        byte[] file = SealedNote.Seal("Plan", "text", Recipients());
        JsonObject root = Parse(file);
        byte[] point = new byte[65];
        Array.Fill(point, (byte)1);
        point[0] = 4;
        foreach (JsonNode? key in root["keys"]!.AsArray())
        {
            key!["epk"] = Convert.ToBase64String(point);
        }

        Assert.Throws<SealedNoteException>(
            () => SealedNote.Open("Plan", Encoding.UTF8.GetBytes(root.ToJsonString()), MachineKey(), Agree));

        Assert.Equal(0, agrees);
    }

    // Nits (security 1): an agreement that is not 32 bytes is refused, whatever the key store returned.
    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void Open_AgreementOfAnotherLength_Fails(int length)
    {
        byte[] file = SealedNote.Seal("Plan", "text", Recipients());

        Assert.Throws<SealedNoteException>(() => SealedNote.Open("Plan", file, MachineKey(), _ => new byte[length]));
    }

    // AC-2: files that are not the format fail the same way, and agree is not called.
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"v\":1}")]
    [InlineData("{\"v\":1,\"nonce\":\"AAAA\",\"tag\":\"AAAA\",\"text\":\"\",\"keys\":[]}")]
    [InlineData("{\"v\":1,\"nonce\":\"!!\",\"tag\":\"AAAA\",\"text\":\"\",\"keys\":[{}]}")]
    public void Open_InvalidFile_Fails(string json)
    {
        Assert.Throws<SealedNoteException>(() => SealedNote.Open("Plan", Encoding.UTF8.GetBytes(json), MachineKey(), Agree));
        Assert.Equal(0, agrees);
        Assert.False(SealedNote.HasKey(Encoding.UTF8.GetBytes(json), MachineKey()));
    }

    // AC-2: more than eight wraps, or a file over 8 MB, is not read.
    [Fact]
    public void Open_TooManyKeysOrTooLarge_Fails()
    {
        JsonObject root = Parse(SealedNote.Seal("Plan", "text", Recipients()));
        JsonArray keys = root["keys"]!.AsArray();
        while (keys.Count <= SealedNote.MaxKeys)
        {
            keys.Add(keys[1]!.DeepClone());
        }

        AssertFails("Plan", Encoding.UTF8.GetBytes(root.ToJsonString()));

        byte[] valid = SealedNote.Seal("Plan", "text", Recipients());
        byte[] large = new byte[SealedNote.MaxFileBytes + 1];
        Array.Fill(large, (byte)' ');
        valid.CopyTo(large, 0);
        AssertFails("Plan", large);
    }

    // AC-2: a failing private-key call is the same failure; the exception carries no detail.
    [Fact]
    public void Open_AgreeThrows_FailsWithoutDetail()
    {
        byte[] file = SealedNote.Seal("Plan", "text", Recipients());

        SealedNoteException failure = Assert.Throws<SealedNoteException>(
            () => SealedNote.Open("Plan", file, MachineKey(), _ => throw new InvalidOperationException("secret detail")));

        Assert.Null(failure.InnerException);
        Assert.DoesNotContain("secret detail", failure.ToString(), StringComparison.Ordinal);
        Assert.Equal(new SealedNoteException().Message, failure.Message);
    }

    // AC-2: the wrong private key does not open the note.
    [Fact]
    public void Open_WrongPrivateKey_Fails()
    {
        byte[] file = SealedNote.Seal("Plan", "text", Recipients());

        Assert.Throws<SealedNoteException>(() => SealedNote.Open("Plan", file, MachineKey(), p => SealedNote.Agree(recovery, p)));
    }

    // AC-8 (format): a recipient is added without changing the sealed text; the new key then opens the note.
    [Fact]
    public void AddRecipient_NewKeyOpensTheNote()
    {
        using ECDiffieHellman second = SealedNote.NewKey();
        byte[] secondKey = second.ExportSubjectPublicKeyInfo();
        byte[] file = SealedNote.Seal("Plan", "text for two", Recipients());

        byte[] added = SealedNote.AddRecipient(
            "Plan",
            file,
            recovery.ExportSubjectPublicKeyInfo(),
            p => SealedNote.Agree(recovery, p),
            new SealedRecipient(SealedRecipient.Machine, secondKey));

        Assert.Equal("text for two", SealedNote.Open("Plan", added, secondKey, p => SealedNote.Agree(second, p)));
        Assert.Equal("text for two", SealedNote.Open("Plan", added, MachineKey(), Agree));
        Assert.Equal(Parse(file)["text"]!.GetValue<string>(), Parse(added)["text"]!.GetValue<string>());
        Assert.Equal(3, Parse(added)["keys"]!.AsArray().Count);
        Assert.Throws<SealedNoteException>(() => SealedNote.AddRecipient(
            "Other", file, MachineKey(), Agree, new SealedRecipient(SealedRecipient.Machine, secondKey)));
    }

    // Seal needs one to eight recipients.
    [Fact]
    public void Seal_NoRecipients_Throws()
    {
        Assert.Throws<ArgumentException>(() => SealedNote.Seal("Plan", "text", []));
    }

    private static JsonObject Parse(byte[] file) => JsonNode.Parse(Encoding.UTF8.GetString(file))!.AsObject();

    private static byte[] Bytes(JsonNode? node) => Convert.FromBase64String(node!.GetValue<string>());

    // Every random value of a sealed file: the nonce, the sealed text, and per wrap the ephemeral key, nonce and key.
    // The tags are left out (they follow from the rest).
    private static List<string> Values(JsonObject root)
    {
        List<string> values = [root["nonce"]!.GetValue<string>(), root["text"]!.GetValue<string>(), root["tag"]!.GetValue<string>()];
        foreach (JsonNode? key in root["keys"]!.AsArray())
        {
            values.Add(key!["epk"]!.GetValue<string>());
            values.Add(key["nonce"]!.GetValue<string>());
            values.Add(key["key"]!.GetValue<string>());
            values.Add(key["tag"]!.GetValue<string>());
        }

        return values;
    }

    private byte[] MachineKey() => machine.ExportSubjectPublicKeyInfo();

    private SealedRecipient[] Recipients() =>
    [
        new SealedRecipient(SealedRecipient.Machine, MachineKey()),
        new SealedRecipient(SealedRecipient.Recovery, recovery.ExportSubjectPublicKeyInfo()),
    ];

    private byte[] Agree(byte[] point)
    {
        agrees++;
        return SealedNote.Agree(machine, point);
    }

    private void AssertFails(string name, byte[] file)
    {
        SealedNoteException failure = Assert.Throws<SealedNoteException>(() => SealedNote.Open(name, file, MachineKey(), Agree));
        Assert.Null(failure.InnerException);
    }
}
