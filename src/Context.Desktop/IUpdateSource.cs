namespace Context;

/// <summary>
/// Where <see cref="StartupUpdate"/> gets a newer build of the desktop app from. The app has one real source (the
/// installed Windows copy and this repository's GitHub releases); tests use a fake, so no test touches the network.
/// </summary>
internal interface IUpdateSource
{
    /// <summary>True only for a copy put there by the installer; every other copy is never checked or changed.</summary>
    public bool IsInstalled { get; }

    /// <summary>Asks the release feed for a newer version and remembers it. True when there is one.</summary>
    public Task<bool> CheckAsync();

    /// <summary>Downloads the version the last <see cref="CheckAsync"/> found.</summary>
    public Task DownloadAsync();

    /// <summary>Has the downloaded version put in place once the app has closed; the app is not started again.</summary>
    public void ApplyOnExit();
}
