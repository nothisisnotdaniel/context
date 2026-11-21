namespace Context.Tests;

/// <summary>Job 367 AC-4 and AC-8: the one Pro gate and its locked hint.</summary>
public sealed class ProTests : IDisposable
{
    private static readonly DateOnly Built = TestLicences.BuildDate;

    private readonly TestLicences licences = new();
    private readonly string activation = Path.Combine(Path.GetTempPath(), "context-pro-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        licences.Dispose();
        if (Directory.Exists(activation))
        {
            Directory.Delete(activation, recursive: true);
        }
    }

    [Fact]
    public void Unlocked_ForAValidCoveringKey_AndOnTheUntilDate()
    {
        Assert.True(licences.Pro(new FakeLicenceStore(stored: licences.Token(until: "2027-01-01")), Built).IsUnlocked);
        Assert.True(licences.Pro(new FakeLicenceStore(stored: licences.Token(until: "2026-10-27")), Built).IsUnlocked);
    }

    [Fact]
    public void Locked_WhenTheKeyEndsOneDayBeforeTheBuildDate()
    {
        Pro pro = licences.Pro(new FakeLicenceStore(stored: licences.Token(until: "2026-10-26")), Built);

        Assert.False(pro.IsUnlocked);
        ProStatus status = pro.Status();
        Assert.Equal(ProStatusKind.Valid, status.Kind);
        Assert.False(status.Covered);
    }

    [Fact]
    public void Locked_WithNoKey_UnsupportedStore_ThrowingStore_OrNoBuildDate()
    {
        string token = licences.Token();

        Assert.False(licences.Pro(new FakeLicenceStore(), Built).IsUnlocked);
        Assert.False(licences.Pro(new FakeLicenceStore(supported: false, stored: token), Built).IsUnlocked);
        Assert.False(licences.Pro(new FakeLicenceStore(stored: token) { Throws = true }, Built).IsUnlocked);
        Assert.False(licences.Pro(new UnsupportedLicenceStore(), Built).IsUnlocked);
        Assert.False(licences.Pro(new FakeLicenceStore(stored: token), null).IsUnlocked);
        Assert.False(licences.Pro(new ThrowingStore(), Built).IsUnlocked);
    }

    [Fact]
    public void Status_NamesTheKind_AndNeverThrows()
    {
        Assert.Equal(ProStatusKind.None, licences.Pro(new FakeLicenceStore(), Built).Status().Kind);
        Assert.Equal(ProStatusKind.Unsupported, licences.Pro(new UnsupportedLicenceStore(), Built).Status().Kind);
        Assert.Equal(ProStatusKind.Unreadable, licences.Pro(new FakeLicenceStore { Throws = true }, Built).Status().Kind);
        Assert.Equal(ProStatusKind.Unreadable, licences.Pro(new ThrowingStore(), Built).Status().Kind);
        ProStatus invalid = licences.Pro(new FakeLicenceStore(stored: "CTX1-garbage"), Built).Status();
        Assert.Equal(ProStatusKind.Invalid, invalid.Kind);
        Assert.Equal(LicenceKey.Malformed, invalid.Problem);
        Assert.Equal(nameof(ProStatus), invalid.ToString());
    }

    [Fact]
    public void Locked_ForRevokedUnknownOrBrokenKeys()
    {
        Assert.False(licences.Pro(new FakeLicenceStore(stored: licences.Token()), Built, TestLicences.LicenceId).IsUnlocked);
        Assert.False(licences.Pro(new FakeLicenceStore(stored: licences.Token(kid: "other")), Built).IsUnlocked);
        Assert.False(licences.Pro(new FakeLicenceStore(stored: "CTX1-garbage"), Built).IsUnlocked);
        Assert.False(new Pro(new FakeLicenceStore(stored: licences.Token()), LicencePublicKeys.None, LicenceRevocations.None, Built).IsUnlocked);
    }

    // The gate never reads the activation file: a file with no stored key stays locked, a forged one changes nothing.
    [Fact]
    public void IsUnlocked_NeverReadsTheActivationFile()
    {
        LicenceActivationFile file = new LicenceActivationFile(activation);
        file.Write(new Licence(TestLicences.KeyId, TestLicences.LicenceId, "Forged", new DateOnly(2099, 1, 1), Built));

        Assert.False(licences.Pro(new FakeLicenceStore(), Built).IsUnlocked);
        Assert.False(licences.Pro(new FakeLicenceStore(stored: licences.Token(kid: "other")), Built).IsUnlocked);

        File.WriteAllText(file.FilePath, "{ corrupt");
        Assert.True(licences.Pro(new FakeLicenceStore(stored: licences.Token()), Built).IsUnlocked);
    }

    [Fact]
    public void Status_IsReadOnce_AndCachedUntilForgotten()
    {
        FakeLicenceStore store = new FakeLicenceStore(stored: licences.Token());
        Pro pro = licences.Pro(store, Built);

        Assert.True(pro.IsUnlocked);
        Assert.True(pro.IsUnlocked);
        _ = pro.Status();
        Assert.True(pro.IsUnlocked);
        Assert.Equal(1, store.Reads);

        store.Stored = null;
        Assert.True(pro.IsUnlocked);
        pro.Forget();
        Assert.False(pro.IsUnlocked);
        Assert.Equal(2, store.Reads);
    }

    [Fact]
    public void ForBuild_UsesTheAssemblyBuildDate_AndTheCompiledTables()
    {
        Pro pro = Pro.ForBuild(new FakeLicenceStore(stored: licences.Token()));

        Assert.Equal(BuildInfo.BuildDate, pro.BuildDate);
        Assert.NotNull(BuildInfo.BuildDate);
        Assert.False(pro.IsUnlocked);
    }

    [Theory]
    [InlineData("2026-10-27", true)]
    [InlineData("2026-1-27", false)]
    [InlineData("2026-10-27T00:00", false)]
    [InlineData("27.10.2026", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ParseBuildDate_IsExactlyIsoDate(string? value, bool parses)
    {
        Assert.Equal(parses, BuildInfo.ParseBuildDate(value) is not null);
    }

    // AC-8: one sentence, without Times' CLI unlock hint.
    [Fact]
    public void LockedHint_IsTheOneSentence_WithoutTheUnlockCommand()
    {
        string hint = Pro.LockedHint("Meeting flow");

        Assert.Equal("Meeting flow needs Suite Pro, the licence for Times and Context.", hint);
        Assert.DoesNotContain("pro activate", hint, StringComparison.Ordinal);
    }

    private sealed class ThrowingStore : ILicenceStore
    {
        public bool IsSupported => true;

        public string? WriteRefusal => null;

        public string? Read() => throw new InvalidOperationException("boom");

        public void Write(string key) => throw new InvalidOperationException("boom");

        public bool Delete() => throw new InvalidOperationException("boom");
    }
}
