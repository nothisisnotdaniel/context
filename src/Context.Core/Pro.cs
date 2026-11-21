using System.Globalization;
using System.Reflection;

namespace Context;

/// <summary>What the licence store holds, as <see cref="Pro.Status"/> sees it.</summary>
internal enum ProStatusKind
{
    /// <summary>No key is stored.</summary>
    None,

    /// <summary>A key is stored but does not verify (bad, unknown key, revoked).</summary>
    Invalid,

    /// <summary>A key is stored and verifies; whether it covers this build is <see cref="ProStatus.Covered"/>.</summary>
    Valid,

    /// <summary>There is no licence store on this OS.</summary>
    Unsupported,

    /// <summary>The store threw (no access, a broken item, anything else).</summary>
    Unreadable,
}

/// <summary>The stored licence and whether it unlocks this build.</summary>
internal sealed class ProStatus
{
    public ProStatus(ProStatusKind kind, Licence? licence = null, string? problem = null, bool covered = false)
    {
        Kind = kind;
        Licence = licence;
        Problem = problem;
        Covered = covered;
    }

    public ProStatusKind Kind { get; }

    /// <summary>The verified licence (only for <see cref="ProStatusKind.Valid"/>).</summary>
    public Licence? Licence { get; }

    /// <summary>The fixed <see cref="LicenceKey"/> reason (only for <see cref="ProStatusKind.Invalid"/>).</summary>
    public string? Problem { get; }

    /// <summary>True when the licence's updates-until date is on or after this build's date.</summary>
    public bool Covered { get; }

    /// <summary>The type name only, so no licensee leaks through formatting.</summary>
    public override string ToString() => nameof(ProStatus);
}

/// <summary>
/// The one Suite Pro gate (a port of Times' <c>Pro</c>). <see cref="IsUnlocked"/> is true exactly when the licence
/// store holds a key that verifies against the public-key table, is not revoked, and whose updates-until date is on or
/// after this build's date. It never reads the activation file and never touches the network. The store is read once
/// and the status cached until <see cref="Forget"/> (called by <c>DesktopActions</c> after an activate or remove), so a
/// Keychain prompt shows at most once per change.
/// </summary>
internal sealed class Pro
{
    private readonly ILicenceStore store;
    private readonly LicencePublicKeys keys;
    private readonly LicenceRevocations revocations;
    private readonly object gate = new object();
    private ProStatus? cached;

    /// <param name="buildDate">This build's date; <c>null</c> (missing or unreadable) keeps Pro locked.</param>
    public Pro(ILicenceStore store, LicencePublicKeys keys, LicenceRevocations revocations, DateOnly? buildDate)
    {
        this.store = store;
        this.keys = keys;
        this.revocations = revocations;
        BuildDate = buildDate;
    }

    /// <summary>The gate for this build: the compiled key table and revocations, and the build date.</summary>
    public static Pro ForBuild(ILicenceStore store) =>
        new Pro(store, LicencePublicKeys.Embedded(), LicenceRevocations.Embedded(), BuildInfo.BuildDate);

    public DateOnly? BuildDate { get; }

    /// <summary>The store this gate reads (the one <c>DesktopActions</c> writes).</summary>
    internal ILicenceStore Store => store;

    /// <summary>True when Pro features may run. Any failure (no store, a store that throws, anything else) is locked.</summary>
    public bool IsUnlocked
    {
        get
        {
            try
            {
                ProStatus status = Status();
                return status.Kind == ProStatusKind.Valid && status.Covered;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>Checks a key against this build's table and revocations (no store is touched).</summary>
    public LicenceCheck Check(string? key) => LicenceKey.TryVerify(key, keys, revocations);

    /// <summary>True when this build's date is known and on or before the licence's updates-until date.</summary>
    public bool Covers(Licence licence) => BuildDate is { } date && licence.Covers(date);

    /// <summary>
    /// The stored key's status, read from the store on the first call and then cached. Never throws: an unsupported
    /// store is <see cref="ProStatusKind.Unsupported"/>, a store that throws <see cref="ProStatusKind.Unreadable"/>.
    /// </summary>
    public ProStatus Status()
    {
        lock (gate)
        {
            cached ??= ReadStatus();
            return cached;
        }
    }

    /// <summary>Drops the cached status, so the next <see cref="Status"/> reads the store again.</summary>
    internal void Forget()
    {
        lock (gate)
        {
            cached = null;
        }
    }

    /// <summary>The one line a locked Pro feature shows (plain text): what Pro is.</summary>
    public static string LockedHint(string feature) => $"{feature} needs Suite Pro, the licence for Times and Context.";

    private ProStatus ReadStatus()
    {
        try
        {
            if (!store.IsSupported)
            {
                return new ProStatus(ProStatusKind.Unsupported);
            }

            string? key = store.Read();
            if (key is null)
            {
                return new ProStatus(ProStatusKind.None);
            }

            LicenceCheck check = Check(key);
            if (check.Licence is not { } licence)
            {
                return new ProStatus(ProStatusKind.Invalid, problem: check.Problem);
            }

            return new ProStatus(ProStatusKind.Valid, licence, covered: Covers(licence));
        }
        catch (Exception)
        {
            return new ProStatus(ProStatusKind.Unreadable);
        }
    }
}

/// <summary>Facts the build stamps into the assembly.</summary>
internal static class BuildInfo
{
    /// <summary>The <c>AssemblyMetadata</c> key <c>Directory.Build.props</c> writes the UTC build date under.</summary>
    internal const string BuildDateKey = "BuildDate";

    /// <summary>The day this build was made (UTC), or <c>null</c> when the metadata is missing or not a <c>yyyy-MM-dd</c> date.</summary>
    internal static DateOnly? BuildDate =>
        ParseBuildDate(typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == BuildDateKey)?.Value);

    /// <summary>Exactly <c>yyyy-MM-dd</c>, or <c>null</c>.</summary>
    internal static DateOnly? ParseBuildDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date
            : null;
}
