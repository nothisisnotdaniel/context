using System.Reflection;
using System.Text.RegularExpressions;

namespace Context;

/// <summary>
/// The silent update at start-up: an installed copy asks its <see cref="IUpdateSource"/> for a newer version,
/// downloads it and has it put in place after the app closes. There is no UI and no restart, and nothing here reads
/// or writes the data folder. Every failure (offline, no release, a download that breaks off) is swallowed; the next
/// start simply tries again.
/// </summary>
internal sealed class StartupUpdate
{
    // SemVer with a prerelease label (X.Y.Z-label), ASCII only; build metadata is cut off before the match.
    private static readonly Regex Prerelease = new Regex(
        @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*\z",
        RegexOptions.CultureInvariant);

    private readonly IUpdateSource source;

    public StartupUpdate(IUpdateSource source)
    {
        this.source = source;
    }

    /// <summary>
    /// The desktop assembly's informational version (<c>&lt;Version&gt;</c>, or <c>-p:Version</c> in a release build);
    /// empty when there is none.
    /// </summary>
    public static string CurrentVersion =>
        typeof(StartupUpdate).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? string.Empty;

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
        try
        {
            if (!source.IsInstalled)
            {
                return;
            }

            if (!await source.CheckAsync().ConfigureAwait(false))
            {
                return;
            }

            await source.DownloadAsync().ConfigureAwait(false);
            source.ApplyOnExit();
        }
        catch (Exception)
        {
            // Silent by design: no hint, no log line, no retry now. The next start checks again.
        }
    }
}
