using System.Globalization;

namespace Context.Tests;

// 171: settings.json for the automatic day's end.
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string parent;
    private readonly string root;

    public SettingsStoreTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-settings-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private string FilePath => Path.Combine(root, SettingsStore.FileName);

    [Fact]
    public void ReadDayEnd_MissingFile_GivesNulls_AndCreatesNothing()
    {
        DayEndSettings settings = new SettingsStore(root).ReadDayEnd();

        Assert.Null(settings.Worked);
        Assert.Null(settings.Moved);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void SaveDayEnd_WritesUtf8WithoutBom_AndKeepsOtherProperties()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, "{\"theme\":\"dark\",\"dayEnd\":{\"other\":1,\"moved\":\"2026-11-01\"}}");
        SettingsStore store = new SettingsStore(root);

        store.SaveDayEnd(new DayEndSettings("2026-11-14T17:30", new DateOnly(2026, 11, 14)));

        byte[] bytes = File.ReadAllBytes(FilePath);
        Assert.NotEqual(0xEF, bytes[0]);
        string text = File.ReadAllText(FilePath);
        Assert.Contains("\"theme\": \"dark\"", text, StringComparison.Ordinal);
        Assert.Contains("\"other\": 1", text, StringComparison.Ordinal);
        DayEndSettings read = store.ReadDayEnd();
        Assert.Equal("2026-11-14T17:30", read.Worked);
        Assert.Equal(new DateOnly(2026, 11, 14), read.Moved);
        Assert.Equal([SettingsStore.FileName], Directory.GetFiles(root).Select(Path.GetFileName));
    }

    [Fact]
    public void SaveDayEnd_CreatesTheFolder_AndReadsABom()
    {
        SettingsStore store = new SettingsStore(root);
        store.SaveDayEnd(new DayEndSettings(null, new DateOnly(2026, 11, 14)));
        Assert.Equal(new DayEndSettings(null, new DateOnly(2026, 11, 14)), store.ReadDayEnd());

        File.WriteAllBytes(FilePath, [0xEF, 0xBB, 0xBF, .. "{\"dayEnd\":{\"worked\":\"2026-11-14T08:00\"}}"u8]);
        Assert.Equal("2026-11-14T08:00", store.ReadDayEnd().Worked);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("[]")]
    [InlineData("{\"dayEnd\":[]}")]
    [InlineData("{\"dayEnd\":{\"worked\":\"2026-11-14T8:00\"}}")]
    [InlineData("{\"dayEnd\":{\"worked\":null}}")]
    [InlineData("{\"dayEnd\":{\"moved\":20261114}}")]
    [InlineData("{\"dayEnd\":{\"moved\":\"2026-11-14\",\"moved\":\"2026-11-13\"}}")]
    public void Broken_Throws_AndIsNeverOverwritten(string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, text);
        SettingsStore store = new SettingsStore(root);

        Assert.Throws<SettingsStoreException>(() => store.ReadDayEnd());
        Assert.Throws<SettingsStoreException>(() => store.SaveDayEnd(new DayEndSettings(null, null)));
        Assert.Equal(text, File.ReadAllText(FilePath));
    }

    [Fact]
    public void Oversized_Throws()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, "{" + new string(' ', (int)SettingsStore.MaxBytes) + "}");

        Assert.Throws<SettingsStoreException>(() => new SettingsStore(root).ReadDayEnd());
    }
    // 207 AC-7: a missing file or property is an empty list, and nothing is created.
    [Theory]
    [InlineData(null)]
    [InlineData("{\"dayEnd\":{\"moved\":\"2026-11-01\"}}")]
    [InlineData("{\"savedSearches\":[]}")]
    public void ReadSavedSearches_MissingOrEmpty_GivesEmpty(string? text)
    {
        if (text is not null)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(FilePath, text);
        }

        Assert.Empty(new SettingsStore(root).ReadSavedSearches());
        Assert.Equal(text is not null, Directory.Exists(root));
    }

    // 207 AC-7: saved in order, read back the same, every other property kept, written without BOM and no temp left.
    [Fact]
    public void SaveSavedSearches_RoundTrip_KeepsOtherProperties()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, "{\"theme\":\"dark\",\"dayEnd\":{\"moved\":\"2026-11-01\",\"x\":1}}");
        SettingsStore store = new SettingsStore(root);
        SavedSearch[] searches =
        [
            new SavedSearch("Open work", "#work and not #done"),
            new SavedSearch("Budget", "budget 2027"),
        ];

        store.SaveSavedSearches(searches);

        IReadOnlyList<SavedSearch> read = store.ReadSavedSearches();
        Assert.Equal(searches.Select(s => (s.Name, s.Query)), read.Select(s => (s.Name, s.Query)));
        Assert.Equal(new DateOnly(2026, 11, 1), store.ReadDayEnd().Moved);
        string json = File.ReadAllText(FilePath);
        Assert.Contains("\"theme\": \"dark\"", json, StringComparison.Ordinal);
        Assert.Contains("\"x\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"savedSearches\": [", json, StringComparison.Ordinal);
        Assert.True(json.IndexOf("Open work", StringComparison.Ordinal) < json.IndexOf("Budget", StringComparison.Ordinal));
        Assert.NotEqual(0xEF, File.ReadAllBytes(FilePath)[0]);
        Assert.Equal([SettingsStore.FileName], Directory.GetFiles(root).Select(f => Path.GetFileName(f)!).ToArray());

        store.SaveSavedSearches([searches[1]]);
        Assert.Equal(["Budget"], store.ReadSavedSearches().Select(s => s.Name).ToArray());
    }

    // 207 AC-7: SaveDayEnd keeps savedSearches; ReadDayEnd ignores them.
    [Fact]
    public void SaveDayEnd_KeepsSavedSearches()
    {
        SettingsStore store = new SettingsStore(root);
        store.SaveSavedSearches([new SavedSearch("Work", "#work")]);

        store.SaveDayEnd(new DayEndSettings("2026-11-14T17:00", new DateOnly(2026, 11, 14)));

        Assert.Equal("2026-11-14T17:00", store.ReadDayEnd().Worked);
        SavedSearch saved = Assert.Single(store.ReadSavedSearches());
        Assert.Equal(("Work", "#work"), (saved.Name, saved.Query));
    }

    // 207 AC-7: names are read back normalized (trimmed, whitespace runs collapsed).
    [Fact]
    public void ReadSavedSearches_NormalizesNames()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, "{\"savedSearches\":[{\"query\":\"#work\",\"name\":\"  Open \\t work \"}]}");

        SavedSearch saved = Assert.Single(new SettingsStore(root).ReadSavedSearches());

        Assert.Equal("Open work", saved.Name);
        Assert.Equal("#work", saved.Query);
    }

    // 207 AC-7: every broken shape throws on read and on save, and the file is never overwritten.
    [Theory]
    [InlineData("{\"savedSearches\":{}}")]
    [InlineData("{\"savedSearches\":null}")]
    [InlineData("{\"savedSearches\":\"#work\"}")]
    [InlineData("{\"savedSearches\":[1]}")]
    [InlineData("{\"savedSearches\":[null]}")]
    [InlineData("{\"savedSearches\":[[]]}")]
    [InlineData("{\"savedSearches\":[{\"name\":\"a\"}]}")]
    [InlineData("{\"savedSearches\":[{\"query\":\"#a\"}]}")]
    [InlineData("{\"savedSearches\":[{\"name\":\"a\",\"query\":\"#a\",\"x\":1}]}")]
    [InlineData("{\"savedSearches\":[{\"name\":1,\"query\":\"#a\"}]}")]
    [InlineData("{\"savedSearches\":[{\"name\":\"a\",\"query\":null}]}")]
    [InlineData("{\"savedSearches\":[{\"name\":\"a\",\"name\":\"b\",\"query\":\"#a\"}]}")]
    [InlineData("{\"savedSearches\":[{\"name\":\"   \",\"query\":\"#a\"}]}")]
    [InlineData("{\"savedSearches\":[{\"name\":\"a\\u0001b\",\"query\":\"#a\"}]}")]
    [InlineData("{\"savedSearches\":[{\"name\":\"a\",\"query\":\"ab\"}]}")]
    [InlineData("{\"savedSearches\":[{\"name\":\"a\",\"query\":\"  \"}]}")]
    [InlineData("{\"savedSearches\":[{\"name\":\"a\",\"query\":\"#a or #b\"}]}")]
    [InlineData("{\"savedSearches\":[{\"name\":\"a\",\"query\":\"#a\"},{\"name\":\"A\",\"query\":\"#b\"}]}")]
    [InlineData("long-name")]
    [InlineData("long-query")]
    [InlineData("too-many")]
    public void SavedSearches_Broken_Throws_AndIsNeverOverwritten(string text)
    {
        text = text switch
        {
            "long-name" => "{\"savedSearches\":[{\"name\":\"" + new string('n', 51) + "\",\"query\":\"#a\"}]}",
            "long-query" => "{\"savedSearches\":[{\"name\":\"a\",\"query\":\"" + new string('q', 201) + "\"}]}",
            "too-many" => "{\"savedSearches\":[" + string.Join(
                ",", Enumerable.Range(0, 51).Select(i => $"{{\"name\":\"n{i}\",\"query\":\"#a\"}}")) + "]}",
            _ => text,
        };
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, text);
        byte[] before = File.ReadAllBytes(FilePath);
        SettingsStore store = new SettingsStore(root);

        Assert.Throws<SettingsStoreException>(() => store.ReadSavedSearches());
        Assert.Throws<SettingsStoreException>(() => store.SaveSavedSearches([new SavedSearch("Work", "#work")]));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    // 207 AC-7: a broken dayEnd is never overwritten by a saved-search write either.
    [Fact]
    public void SaveSavedSearches_BrokenDayEnd_Throws_Unchanged()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, "{\"dayEnd\":{\"moved\":5}}");
        byte[] before = File.ReadAllBytes(FilePath);

        Assert.Throws<SettingsStoreException>(() => new SettingsStore(root).SaveSavedSearches([new SavedSearch("Work", "#work")]));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    // 207 AC-7: the store writes only lists that keep the rules; 50 entries and 50-character names are fine.
    [Fact]
    public void SaveSavedSearches_BadList_ThrowsArgument_WritesNothing()
    {
        SettingsStore store = new SettingsStore(root);
        SavedSearch[] fifty = Enumerable.Range(0, 50).Select(i => new SavedSearch($"n{i}", "#a")).ToArray();

        Assert.Throws<ArgumentException>(() => store.SaveSavedSearches([.. fifty, new SavedSearch("n50", "#a")]));
        Assert.Throws<ArgumentException>(() => store.SaveSavedSearches([new SavedSearch(" a", "#a")]));
        Assert.Throws<ArgumentException>(() => store.SaveSavedSearches([new SavedSearch("a", "#a"), new SavedSearch("A", "#b")]));
        Assert.Throws<ArgumentException>(() => store.SaveSavedSearches([new SavedSearch("a", "x")]));
        Assert.False(File.Exists(FilePath));

        store.SaveSavedSearches([.. fifty.Take(49), new SavedSearch(new string('n', 50), "#a")]);
        Assert.Equal(50, store.ReadSavedSearches().Count);
    }

    // 207 security: the longest names and queries in non-ASCII and HTML-special characters are written unescaped, stay
    // under MaxBytes and read back.
    [Fact]
    public void SaveSavedSearches_LongestNonAscii_RoundTrips_UnderTheLimit()
    {
        SettingsStore store = new SettingsStore(root);
        string query = string.Concat(Enumerable.Repeat("äö<>&'+\"", 25));
        SavedSearch[] fifty = Enumerable.Range(0, SavedSearches.MaxCount)
            .Select(i => new SavedSearch(new string('ü', 48) + i.ToString("00", CultureInfo.InvariantCulture), query))
            .ToArray();

        store.SaveSavedSearches(fifty);

        Assert.True(new FileInfo(FilePath).Length <= SettingsStore.MaxBytes);
        Assert.Contains("üüü", File.ReadAllText(FilePath), StringComparison.Ordinal);
        Assert.Equal(
            fifty.Select(s => (s.Name, s.Query)).ToArray(),
            store.ReadSavedSearches().Select(s => (s.Name, s.Query)).ToArray());
    }

    // 207 security: a save that would make the file over MaxBytes throws and writes nothing, leaving no temp file.
    [Fact]
    public void SaveSavedSearches_OverTheLimit_Throws_Unchanged()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, "{\"padding\":\"" + new string('x', 65_500) + "\"}");
        byte[] before = File.ReadAllBytes(FilePath);
        SettingsStore store = new SettingsStore(root);

        Assert.Throws<SettingsTooLargeException>(
            () => store.SaveSavedSearches([new SavedSearch("Mine", "#work and not #done")]));
        Assert.Throws<SettingsTooLargeException>(() => store.SaveDayEnd(new DayEndSettings("2026-10-28T17:00", null)));

        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Equal([SettingsStore.FileName], Directory.GetFiles(root).Select(f => Path.GetFileName(f)!).ToArray());
    }

    // 207 security: a lone surrogate makes a name invalid; a pair is fine. Built here because xUnit replaces lone
    // surrogates in InlineData.
    [Fact]
    public void NormalizeName_LoneSurrogate_GivesNull()
    {
        string high = char.ConvertFromUtf32(0x1F600)[..1];
        string low = char.ConvertFromUtf32(0x1F600)[1..];

        Assert.Null(SavedSearches.NormalizeName("a" + high + "b"));
        Assert.Null(SavedSearches.NormalizeName("a" + low));
        Assert.Null(SavedSearches.NormalizeName(low + high));
        Assert.Equal("a" + high + low, SavedSearches.NormalizeName("a" + high + low));
    }

    // 207 AC-6: name normalizing and the query rules.
    [Theory]
    [InlineData("  My \t  search  ", "My search")]
    [InlineData("a", "a")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("a\u0001", null)]
    [InlineData("a\u007f", null)]
    [InlineData("a\u202Eb", null)]
    [InlineData("a\u2066b", null)]
    [InlineData("a\u200Bb", null)]
    public void NormalizeName(string name, string? expected)
    {
        Assert.Equal(expected, SavedSearches.NormalizeName(name));
    }

    [Theory]
    [InlineData("abc", true)]
    [InlineData("  abc  ", true)]
    [InlineData("ab", false)]
    [InlineData("", false)]
    [InlineData("#a", true)]
    [InlineData("not #a and #b", true)]
    [InlineData("#a or #b", false)]
    [InlineData("not #a", false)]
    public void IsValidQuery(string query, bool expected)
    {
        Assert.Equal(expected, SavedSearches.IsValidQuery(query));
        Assert.True(SavedSearches.IsValidQuery(new string('q', 200)));
        Assert.False(SavedSearches.IsValidQuery(new string('q', 201)));
    }

    // 217 AC-1: a missing file or section, or anything other than exactly {"enabled":<bool>}, is off; nothing is created.
    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("{\"outlook\":{\"enabled\":false}}")]
    [InlineData("{\"outlook\":true}")]
    [InlineData("{\"outlook\":null}")]
    [InlineData("{\"outlook\":{}}")]
    [InlineData("{\"outlook\":{\"enabled\":\"true\"}}")]
    [InlineData("{\"outlook\":{\"enabled\":1}}")]
    [InlineData("{\"outlook\":{\"enabled\":true,\"x\":1}}")]
    [InlineData("{\"outlook\":[true]}")]
    public void ReadOutlook_MissingOrOther_IsOff(string? text)
    {
        if (text is not null)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(FilePath, text);
        }

        Assert.False(new SettingsStore(root).ReadOutlook());
        Assert.Equal(text is not null, Directory.Exists(root));
    }

    // 217 AC-1: exactly {"enabled":true} is on.
    [Fact]
    public void ReadOutlook_EnabledTrue_IsOn()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, "{\"outlook\":{\"enabled\":true}}");

        Assert.True(new SettingsStore(root).ReadOutlook());
    }

    // 217 AC-1: SaveOutlook writes exactly {"enabled":<bool>} and keeps dayEnd, savedSearches and every other
    // property equal in value; no temp file is left.
    [Fact]
    public void SaveOutlook_RoundTrip_KeepsDayEndAndSavedSearches()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            FilePath,
            "{\"theme\":\"dark\",\"dayEnd\":{\"worked\":\"2026-11-14T17:00\",\"moved\":\"2026-11-14\"},"
            + "\"savedSearches\":[{\"name\":\"Work\",\"query\":\"#work\"},{\"name\":\"Budget\",\"query\":\"budget 2027\"}]}");
        SettingsStore store = new SettingsStore(root);
        System.Text.Json.Nodes.JsonNode before = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!;

        store.SaveOutlook(true);

        Assert.True(store.ReadOutlook());
        System.Text.Json.Nodes.JsonNode after = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!;
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(before["dayEnd"], after["dayEnd"]));
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(before["savedSearches"], after["savedSearches"]));
        Assert.Equal("dark", (string?)after["theme"]);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse("{\"enabled\":true}"), after["outlook"]));
        Assert.Equal([SettingsStore.FileName], Directory.GetFiles(root).Select(f => Path.GetFileName(f)!).ToArray());

        store.SaveOutlook(false);

        Assert.False(store.ReadOutlook());
        System.Text.Json.Nodes.JsonNode off = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!;
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(before["dayEnd"], off["dayEnd"]));
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(before["savedSearches"], off["savedSearches"]));
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse("{\"enabled\":false}"), off["outlook"]));
    }

    // 217 AC-1: SaveOutlook replaces a malformed outlook section, but creates the file when there is none.
    [Fact]
    public void SaveOutlook_ReplacesAnOtherOutlookValue_AndCreatesTheFile()
    {
        SettingsStore store = new SettingsStore(root);
        store.SaveOutlook(true);
        Assert.True(store.ReadOutlook());

        File.WriteAllText(FilePath, "{\"outlook\":{\"enabled\":true,\"x\":1}}");
        store.SaveOutlook(true);

        Assert.True(store.ReadOutlook());
        Assert.DoesNotContain("\"x\"", File.ReadAllText(FilePath), StringComparison.Ordinal);
    }

    // 217 AC-1: a broken settings.json (its dayEnd and savedSearches included) is never overwritten by SaveOutlook.
    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("{\"dayEnd\":{\"moved\":5}}")]
    [InlineData("{\"savedSearches\":5}")]
    public void SaveOutlook_Broken_Throws_AndIsNeverOverwritten(string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, text);
        byte[] before = File.ReadAllBytes(FilePath);

        Assert.Throws<SettingsStoreException>(() => new SettingsStore(root).SaveOutlook(true));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Equal([SettingsStore.FileName], Directory.GetFiles(root).Select(f => Path.GetFileName(f)!).ToArray());
    }

    // 217 AC-1: a save that would make the file over MaxBytes throws and writes nothing.
    [Fact]
    public void SaveOutlook_OverTheLimit_Throws_Unchanged()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, "{\"padding\":\"" + new string('x', 65_516) + "\"}");
        byte[] before = File.ReadAllBytes(FilePath);

        Assert.Throws<SettingsTooLargeException>(() => new SettingsStore(root).SaveOutlook(true));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }
}
