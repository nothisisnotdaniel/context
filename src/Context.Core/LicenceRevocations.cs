namespace Context;

/// <summary>
/// Licence ids (<c>lid</c>) that no longer unlock Pro, compiled into the build. It starts empty; add a
/// revoked id from the issuer's ledger as one more string, in Times' list (<c>src/Times/LicenceRevocations.cs</c>) and
/// here alike. Tests build their own set.
/// </summary>
internal sealed class LicenceRevocations
{
    /// <summary>The compiled list of revoked licence ids.</summary>
    private static readonly string[] Compiled =
    [
    ];

    private readonly HashSet<string> revoked;

    public LicenceRevocations(IEnumerable<string> revoked)
    {
        this.revoked = new HashSet<string>(revoked, StringComparer.Ordinal);
    }

    /// <summary>The ids compiled into this build.</summary>
    public static LicenceRevocations Embedded() => new(Compiled);

    public static LicenceRevocations None { get; } = new([]);

    public bool Contains(string licenceId) => revoked.Contains(licenceId);
}
