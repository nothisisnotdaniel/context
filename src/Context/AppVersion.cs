using System.Reflection;

namespace Context;

/// <summary>
/// The app's version, taken from the assembly's <see cref="AssemblyInformationalVersionAttribute"/>
/// (set by <c>&lt;Version&gt;</c> in the project file, or by <c>-p:Version</c> in a release build), without the
/// SDK's <c>+&lt;commit&gt;</c> build metadata.
/// </summary>
internal static class AppVersion
{
    private const string Unknown = "unknown";

    /// <summary>
    /// The version of the Context assembly (not the entry assembly, which is the test host under
    /// <c>dotnet test</c>).
    /// </summary>
    internal static string Current =>
        Format(typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// Strips everything from the first <c>+</c> and trims; null, empty or whitespace gives <c>unknown</c>.
    /// </summary>
    internal static string Format(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return Unknown;
        }

        int plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        string version = (plus >= 0 ? informationalVersion[..plus] : informationalVersion).Trim();
        return version.Length == 0 ? Unknown : version;
    }
}
