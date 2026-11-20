using Velopack;
using Velopack.Sources;

namespace Context;

/// <summary>
/// The real <see cref="IUpdateSource"/> (this project's interface, not Velopack's): Velopack's update manager over
/// the GitHub releases of this repository. The builds are unsigned, so an update is trusted only as an HTTPS download
/// from this repository's releases, which only the release workflow writes. No access token is embedded, the channel
/// is the one the copy was installed from (none is set here), and a lower version is never installed. Only a copy
/// installed by <c>Setup.exe</c> on Windows counts as installed; the portable copy and macOS never update.
/// </summary>
internal sealed class VelopackUpdateSource : IUpdateSource
{
    private const string Repository = "https://github.com/nothisisnotdaniel/context";

    private readonly bool prereleases;
    private UpdateManager? manager;
    private UpdateInfo? found;

    public VelopackUpdateSource(bool prereleases)
    {
        this.prereleases = prereleases;
    }

    public bool IsInstalled
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            UpdateManager updates = Manager();
            return updates.IsInstalled && !updates.IsPortable;
        }
    }

    public async Task<bool> CheckAsync()
    {
        found = await Manager().CheckForUpdatesAsync().ConfigureAwait(false);
        return found is not null;
    }

    public async Task DownloadAsync()
    {
        if (found is null)
        {
            return;
        }

        await Manager().DownloadUpdatesAsync(found).ConfigureAwait(false);
    }

    public void ApplyOnExit()
    {
        if (found is null)
        {
            return;
        }

        // silent: no updater window; restart: false, the app is not started again.
        Manager().WaitExitThenApplyUpdates(found.TargetFullRelease, true, false);
    }

    private UpdateManager Manager()
    {
        manager ??= new UpdateManager(
            new GithubSource(Repository, null, prereleases),
            new UpdateOptions { AllowVersionDowngrade = false });
        return manager;
    }
}
