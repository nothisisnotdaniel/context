using System.Reflection;
using System.Text.RegularExpressions;

namespace Context;

/// <summary>
/// The silent update at start-up: an installed copy asks its <see cref="IUpdateSource"/> for a newer version,
/// downloads it and has it put in place after the app closes. There is no restart and no dialog, and nothing here
/// reads or writes the data folder itself. Each run ends in exactly one <see cref="UpdateOutcome"/>, which is handed
/// to the record callback (job 396: the update log) and shown through <see cref="UpdateStatus"/>. Every failure
/// (offline, no release, a download that breaks off) becomes a failed outcome; the next start simply tries again.
/// </summary>
internal sealed class StartupUpdate
{
    // SemVer with a prerelease label (X.Y.Z-label), ASCII only; build metadata is cut off before the match.
    private static readonly Regex Prerelease = new Regex(
        @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*\z",
        RegexOptions.CultureInvariant);

    /// <summary>What <see cref="ShownVersion"/> gives for a build without a version.</summary>
    public const string DevelopmentBuild = "development build";

    private readonly IUpdateSource source;
    private readonly UpdateStatus status;
    private readonly Action<UpdateOutcome> record;
    private readonly Func<DateTime> utcNow;

    public StartupUpdate(IUpdateSource source, UpdateStatus status, Action<UpdateOutcome> record, Func<DateTime> utcNow)
    {
        this.source = source;
        this.status = status;
        this.record = record;
        this.utcNow = utcNow;
    }

    /// <summary>
    /// The desktop assembly's informational version (<c>&lt;Version&gt;</c>, or <c>-p:Version</c> in a release build);
    /// empty when there is none.
    /// </summary>
    public static string CurrentVersion =>
        typeof(StartupUpdate).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? string.Empty;

    /// <summary>
    /// The version as the user sees it: without the SDK's <c>+&lt;commit&gt;</c> suffix, or
    /// <see cref="DevelopmentBuild"/> when there is none.
    /// </summary>
    public static string ShownVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return DevelopmentBuild;
        }

        int plus = version.IndexOf('+', StringComparison.Ordinal);
        string core = (plus >= 0 ? version[..plus] : version).Trim();
        return core.Length == 0 ? DevelopmentBuild : core;
    }

    /// <summary>
    /// True only for a version with a prerelease label, such as <c>0.3.1-develop.12</c>: such a build follows the
    /// prereleases, a proper release only proper releases. The SDK's <c>+&lt;commit&gt;</c> suffix is ignored.
    /// </summary>
    public static bool IncludePrereleases(string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return false;
        }

        int plus = version.IndexOf('+', StringComparison.Ordinal);
        string core = plus >= 0 ? version[..plus] : version;
        return Prerelease.IsMatch(core);
    }

    /// <summary>
    /// Starts the check on a pool thread and returns at once. The returned task ends when the flow is over and never
    /// fails; the caller need not wait for it.
    /// </summary>
    public Task Start()
    {
        return Task.Run(Run);
    }

    private async Task Run()
    {
        string version = CurrentVersion;
        UpdateOutcome outcome;
        try
        {
            bool installed = source.IsInstalled;
            status.Installed = installed;
            if (!installed)
            {
                outcome = new UpdateOutcome(Now(), version, UpdateResultKind.NotInstalled, null, null);
            }
            else if (!await source.CheckAsync().ConfigureAwait(false))
            {
                outcome = new UpdateOutcome(Now(), version, UpdateResultKind.NoNewer, null, null);
            }
            else
            {
                await source.DownloadAsync().ConfigureAwait(false);
                source.ApplyOnExit();
                outcome = new UpdateOutcome(Now(), version, UpdateResultKind.Downloaded, source.FoundVersion, null);
            }
        }
        catch (Exception e)
        {
            // No retry now and no dialog: the failure is recorded as one line, and the next start checks again.
            outcome = UpdateOutcome.Failure(Now(), version, e);
        }

        try
        {
            record(outcome);
        }
        catch (Exception)
        {
            // The log is best effort; the outcome is still shown.
        }

        status.Last = outcome;
    }

    private DateTime Now()
    {
        try
        {
            return utcNow();
        }
        catch (Exception)
        {
            return DateTime.UnixEpoch;
        }
    }
}
