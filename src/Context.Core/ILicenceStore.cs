namespace Context;

/// <summary>
/// Keeps the one Suite Pro licence key in the OS credential store, so Context and Times share it (the same item as
/// Times' <c>ILicenceStore</c>). Failures throw <see cref="LicenceStoreException"/> with a fixed message that never
/// holds the key. Tests inject a fake, so no test touches a real store; <c>Program.cs</c> picks the real one.
/// </summary>
internal interface ILicenceStore
{
    /// <summary>False where there is no supported store; then nothing else may be called.</summary>
    public bool IsSupported { get; }

    /// <summary>A fixed reason why <see cref="Write"/> refuses in this run, or <c>null</c> when a write may go ahead.</summary>
    public string? WriteRefusal { get; }

    /// <summary>The stored key text, or <c>null</c> if none.</summary>
    public string? Read();

    /// <summary>Stores the key text, replacing any earlier one.</summary>
    public void Write(string key);

    /// <summary>Removes the stored key. False if there was none.</summary>
    public bool Delete();
}

/// <summary>The names of the shared licence item, the same as Times' <c>LicenceStore</c> constants.</summary>
internal static class LicenceStoreNames
{
    /// <summary>The macOS Keychain service of the generic password that holds the key.</summary>
    public const string MacService = "Suite Pro licence";

    /// <summary>The macOS Keychain account of that item.</summary>
    public const string MacAccount = "key";

    /// <summary>The Windows Credential Manager target that holds the key.</summary>
    public const string WindowsTarget = "Suite/ProLicence";

    /// <summary>The Windows Credential Manager user name of that credential.</summary>
    public const string WindowsUserName = "key";
}

/// <summary>A licence store failure. The message is always a fixed text, never the key or an OS message.</summary>
internal sealed class LicenceStoreException : Exception
{
    public const string NotAvailable = "No credential store is available on this system.";

    public LicenceStoreException(string message)
        : base(message)
    {
    }
}

/// <summary>Linux and anything else: no store. It never touches a file and refuses every call.</summary>
internal sealed class UnsupportedLicenceStore : ILicenceStore
{
    public bool IsSupported => false;

    public string? WriteRefusal => LicenceStoreException.NotAvailable;

    public string? Read() => throw new LicenceStoreException(LicenceStoreException.NotAvailable);

    public void Write(string key) => throw new LicenceStoreException(LicenceStoreException.NotAvailable);

    public bool Delete() => throw new LicenceStoreException(LicenceStoreException.NotAvailable);
}
