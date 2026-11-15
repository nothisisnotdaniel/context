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
}
