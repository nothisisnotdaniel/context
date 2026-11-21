using System.Text;

namespace Context.Tests;

/// <summary>Job 367 AC-3: <c>SuitePro/activation.json</c>, byte for byte Times' format.</summary>
public sealed class LicenceActivationFileTests : IDisposable
{
    private const string TimesJson =
        "{\"format\":1,\"licenceId\":\"AAECAwQFBgcICQoLDA0ODw\",\"licensee\":\"Ada Lovelace\",\"updatesUntil\":\"2027-12-31\"}";

    private readonly string root = Path.Combine(Path.GetTempPath(), "context-pro-" + Guid.NewGuid().ToString("N"));

    public LicenceActivationFileTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Licence Sample(string name = TestLicences.Name) =>
        new Licence(TestLicences.KeyId, TestLicences.LicenceId, name, new DateOnly(2027, 12, 31), new DateOnly(2026, 10, 27));

    private string Folder => Path.Combine(root, LicenceActivationFile.FolderName);

    [Fact]
    public void Serialize_GivesTimesExactBytes()
    {
        byte[] bytes = LicenceActivationFile.Serialize(Sample());

        Assert.Equal(Encoding.UTF8.GetBytes(TimesJson), bytes);
        Assert.NotEqual(0xEF, bytes[0]);
    }

    [Fact]
    public void Write_CreatesTheFolderAndTheExactJson_LeavingNoTempFile()
    {
        LicenceActivationFile file = new LicenceActivationFile(Folder);

        file.Write(Sample());
        file.Write(Sample());

        Assert.Equal(Path.Combine(Folder, "activation.json"), file.FilePath);
        Assert.Equal(TimesJson, File.ReadAllText(file.FilePath, Encoding.UTF8));
        Assert.Single(Directory.GetFiles(Folder));
    }

    [Fact]
    public void Write_OffWindows_Folder0700_File0600()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(Folder);
        File.SetUnixFileMode(Folder, (UnixFileMode)0b111_101_101);
        LicenceActivationFile file = new LicenceActivationFile(Folder);

        file.Write(Sample());
        file.Write(Sample("Grace Hopper"));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Folder));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file.FilePath));
    }

    [Fact]
    public void Write_WhenTheFolderIsAFile_Throws()
    {
        File.WriteAllText(Folder, "x");
        LicenceActivationFile file = new LicenceActivationFile(Folder);

        Assert.ThrowsAny<IOException>(() => file.Write(Sample()));
    }

    [Fact]
    public void Read_ParsesTimesFile_MissingIsNull()
    {
        LicenceActivationFile file = new LicenceActivationFile(Folder);
        Assert.Null(file.Read());

        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(file.FilePath, Encoding.UTF8.GetBytes(TimesJson));
        LicenceActivation read = file.Read()!;

        Assert.Equal(TestLicences.LicenceId, read.LicenceId);
        Assert.Equal(TestLicences.Name, read.Licensee);
        Assert.Equal(new DateOnly(2027, 12, 31), read.UpdatesUntil);
    }

    [Fact]
    public void Read_RoundTripsAWrite()
    {
        LicenceActivationFile file = new LicenceActivationFile(Folder);

        file.Write(Sample("Zoë"));

        Assert.Equal("Zoë", file.Read()!.Licensee);
    }

    [Theory]
    [InlineData("{ corrupt")]
    [InlineData("{\"format\":2,\"licenceId\":\"AAECAwQFBgcICQoLDA0ODw\",\"licensee\":\"Ada\",\"updatesUntil\":\"2027-12-31\"}")]
    [InlineData("{\"format\":\"1\",\"licenceId\":\"AAECAwQFBgcICQoLDA0ODw\",\"licensee\":\"Ada\",\"updatesUntil\":\"2027-12-31\"}")]
    [InlineData("{\"format\":1,\"licenceId\":\"AAECAwQFBgcICQoLDA0ODw\",\"licensee\":\"Ada\",\"updatesUntil\":\"2027-12-31\",\"x\":1}")]
    [InlineData("{\"format\":1,\"format\":1,\"licenceId\":\"AAECAwQFBgcICQoLDA0ODw\",\"licensee\":\"Ada\",\"updatesUntil\":\"2027-12-31\"}")]
    [InlineData("{\"format\":1,\"licenceId\":\"AAECAwQFBgcICQoLDA0ODw\",\"licensee\":\"Ada\"}")]
    [InlineData("{\"format\":1,\"licenceId\":\"short\",\"licensee\":\"Ada\",\"updatesUntil\":\"2027-12-31\"}")]
    [InlineData("{\"format\":1,\"licenceId\":\"AAECAwQFBgcICQoLDA0ODw\",\"licensee\":\"Ada\",\"updatesUntil\":\"2027-02-30\"}")]
    [InlineData("[1]")]
    public void Read_BrokenFile_Throws(string json)
    {
        Directory.CreateDirectory(Folder);
        LicenceActivationFile file = new LicenceActivationFile(Folder);
        File.WriteAllText(file.FilePath, json);

        Assert.Throws<InvalidDataException>(() => file.Read());
    }

    [Fact]
    public void Read_IsCappedAt4KiB()
    {
        Directory.CreateDirectory(Folder);
        LicenceActivationFile file = new LicenceActivationFile(Folder);
        File.WriteAllText(file.FilePath, TimesJson + new string(' ', 4097 - TimesJson.Length));

        Assert.Throws<InvalidDataException>(() => file.Read());
    }

    [Fact]
    public void Read_ADirectoryNamedActivationJson_Throws()
    {
        LicenceActivationFile file = new LicenceActivationFile(Folder);
        Directory.CreateDirectory(file.FilePath);

        Assert.Throws<InvalidDataException>(() => file.Read());
    }

    [Fact]
    public void Delete_RemovesTheFile_FalseWhenNone()
    {
        LicenceActivationFile file = new LicenceActivationFile(Folder);
        Assert.False(file.Delete());

        file.Write(Sample());
        Assert.True(file.Delete());
        Assert.False(File.Exists(file.FilePath));
    }

    [Fact]
    public void DefaultFolder_IsSuiteProUnderApplicationData_NextToContext()
    {
        string appData = Path.Combine(root, "AppData");

        string folder = LicenceActivationFile.DefaultFolder(appData);

        Assert.Equal(Path.Combine(appData, "SuitePro"), folder);
        Assert.Equal(Path.GetDirectoryName(Path.Combine(appData, "Context")), Path.GetDirectoryName(folder));
    }
}
