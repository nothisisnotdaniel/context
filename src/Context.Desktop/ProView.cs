namespace Context;

/// <summary>Which Suite Pro line Settings shows (job 367).</summary>
internal enum ProViewKind
{
    /// <summary>A stored key verifies and covers this build.</summary>
    Active,

    /// <summary>No key is stored (<see cref="ProView.Licensee"/> is set when only <c>activation.json</c> names one).</summary>
    None,

    /// <summary>A stored key does not verify (<see cref="ProView.Problem"/>).</summary>
    Invalid,

    /// <summary>A stored key verifies, but its updates end before this build's date.</summary>
    NotCovered,

    /// <summary>A stored key verifies, but this build has no build date.</summary>
    NoBuildDate,

    /// <summary>There is no licence store on this OS.</summary>
    Unsupported,

    /// <summary>The licence store can't be read.</summary>
    Unreadable,
}

/// <summary>The Suite Pro state for Settings: raw data only, drawn by <c>SettingsPanel</c>.</summary>
internal sealed class ProView
{
    public ProView(
        ProViewKind kind,
        Licence? licence = null,
        string? problem = null,
        string? licensee = null,
        DateOnly? buildDate = null,
        bool keyStored = false)
    {
        Kind = kind;
        Licence = licence;
        Problem = problem;
        Licensee = licensee;
        BuildDate = buildDate;
        KeyStored = keyStored;
    }

    public ProViewKind Kind { get; }

    /// <summary>The verified licence (Active, NotCovered, NoBuildDate).</summary>
    public Licence? Licence { get; }

    /// <summary>The fixed <see cref="LicenceKey"/> reason (Invalid).</summary>
    public string? Problem { get; }

    /// <summary>The licensee <c>activation.json</c> names while no key is stored (None), else <c>null</c>.</summary>
    public string? Licensee { get; }

    /// <summary>This build's date (NotCovered).</summary>
    public DateOnly? BuildDate { get; }

    /// <summary>True when a key is stored, so <c>Remove licence</c> shows.</summary>
    public bool KeyStored { get; }

    /// <summary>The type name only, so no licensee leaks through formatting.</summary>
    public override string ToString() => nameof(ProView);
}
