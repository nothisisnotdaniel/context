namespace Context.Tests;

// 280: "repositories" in settings.json.
public sealed class RepositorySettingsTests : IDisposable
{
    private readonly string parent;
    private readonly string root;

    public RepositorySettingsTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-repos-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private string SettingsPath => Path.Combine(root, SettingsStore.FileName);

    private string Folder(string name)
    {
        string folder = Path.Combine(parent, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void Missing_IsEmpty()
    {
        Assert.Empty(new SettingsStore(root).ReadRepositories());
    }

    // Saved order kept, other properties kept, and an empty list removes the property.
    [Fact]
    public void Save_KeepsOrderAndOtherProperties()
    {
        File.WriteAllText(
            SettingsPath,
            "{\"dayEnd\":{\"worked\":\"2026-11-10T17:00\",\"moved\":\"2026-11-09\"},\"savedSearches\":[{\"name\":\"Work\",\"query\":\"#work\"}],\"other\":1}");
        SettingsStore store = new SettingsStore(root);
        string b = Folder("b");
        string a = Folder("a");

        store.SaveRepositories([b, a]);

        Assert.Equal([b, a], store.ReadRepositories());
        Assert.Equal(["Work"], store.ReadSavedSearches().Select(s => s.Name));
        System.Text.Json.Nodes.JsonNode saved = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(SettingsPath))!;
        Assert.Equal("2026-11-10T17:00", (string?)saved["dayEnd"]!["worked"]);
        Assert.Equal("2026-11-09", (string?)saved["dayEnd"]!["moved"]);
        Assert.Equal(1, (int)saved["other"]!);

        store.SaveRepositories([]);

        Assert.Empty(store.ReadRepositories());
        Assert.DoesNotContain("repositories", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"repositories\":\"x\"}")]
    [InlineData("{\"repositories\":[1]}")]
    [InlineData("{\"repositories\":[\"relative\"]}")]
    [InlineData("{\"repositories\":[\"\"]}")]
    [InlineData("{ broken")]
    public void Read_BadValue_Throws_AndIsNeverOverwritten(string json)
    {
        File.WriteAllText(SettingsPath, json);
        SettingsStore store = new SettingsStore(root);

        Assert.Throws<SettingsStoreException>(() => store.ReadRepositories());
        Assert.Throws<SettingsStoreException>(() => store.SaveRepositories([Folder("a")]));
        Assert.Equal(json, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Read_DuplicatesIgnoringCase_Throw()
    {
        string a = Folder("a");
        File.WriteAllText(
            SettingsPath,
            "{\"repositories\":[" + System.Text.Json.JsonSerializer.Serialize(a) + "," + System.Text.Json.JsonSerializer.Serialize(a.ToUpperInvariant()) + "]}");

        Assert.Throws<SettingsStoreException>(() => new SettingsStore(root).ReadRepositories());
    }

    [Fact]
    public void Read_MoreThan20_Throws()
    {
        string list = string.Join(",", Enumerable.Range(0, 21).Select(i => System.Text.Json.JsonSerializer.Serialize(Path.Combine(parent, "r" + i))));
        File.WriteAllText(SettingsPath, "{\"repositories\":[" + list + "]}");

        Assert.Throws<SettingsStoreException>(() => new SettingsStore(root).ReadRepositories());
    }

    // The add rules, in order: blank, too long, relative, missing, duplicate, full.
    [Fact]
    public void CheckAdd_Rules()
    {
        string a = Folder("a");
        Assert.Equal(RepositoryCheck.Blank, Repositories.CheckAdd([], "  ", out _));
        Assert.Equal(RepositoryCheck.TooLong, Repositories.CheckAdd([], Path.Combine(parent, new string('x', 401)), out _));
        Assert.Equal(RepositoryCheck.Relative, Repositories.CheckAdd([], "relative/path", out _));
        Assert.Equal(RepositoryCheck.Missing, Repositories.CheckAdd([], Path.Combine(parent, "nope"), out _));
        // 281: a folder that exists and differs from a only in case (on a case-insensitive disk it is the same folder).
        string upper = Folder("A");
        Assert.NotEqual(a, upper, StringComparer.Ordinal);
        Assert.Equal(RepositoryCheck.Duplicate, Repositories.CheckAdd([a], " " + upper + " ", out _));
        string[] full = [.. Enumerable.Range(0, 20).Select(i => Path.Combine(parent, "r" + i))];
        Assert.Equal(RepositoryCheck.Full, Repositories.CheckAdd(full, a, out _));
        Assert.Equal(RepositoryCheck.Ok, Repositories.CheckAdd([], "  " + a + " ", out string folder));
        Assert.Equal(a, folder);
    }

    [Fact]
    public void Save_BadList_Throws_WritesNothing()
    {
        SettingsStore store = new SettingsStore(root);

        Assert.Throws<ArgumentException>(() => store.SaveRepositories(["relative"]));
        Assert.False(File.Exists(SettingsPath));
    }
}
