using System.Text.RegularExpressions;

namespace Context;

/// <summary>What one start's update check ended with (job 396).</summary>
internal enum UpdateResultKind
{
    /// <summary>This copy was not installed by <c>Setup.exe</c>, so nothing was checked.</summary>
    NotInstalled,

    /// <summary>The release feed had no newer version.</summary>
    NoNewer,

    /// <summary>A newer version was downloaded; it is put in place once the app has closed.</summary>
    Downloaded,

    /// <summary>A step threw or was cancelled; the next start tries again.</summary>
    Failed,
}

/// <summary>
/// The one outcome of a start's update check (job 396): when it ended (UTC), the running version, the result, the
/// version found and, for a failure, <c>&lt;short type name&gt;: &lt;message&gt;</c> on one line with at most
/// <see cref="MaxMessage"/> characters of message and never a stack trace.
/// </summary>
internal sealed record UpdateOutcome(DateTime Utc, string Version, UpdateResultKind Kind, string? NewVersion, string? Error)
{
    /// <summary>A failure keeps at most this many characters of the exception's message.</summary>
    public const int MaxMessage = 200;

    // An http(s) address up to its query or fragment, which is dropped (it may hold a token).
    private static readonly Regex UrlTail = new(
        @"(https?://[^\s?#]*)[?#]\S*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // The user name and password of an http(s) address, which are dropped as well.
    private static readonly Regex UrlUser = new(
        @"(https?://)[^/@\s]+@", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>The outcome as a short line for the log and Settings.</summary>
    public string Text => Kind switch
    {
        UpdateResultKind.NotInstalled => "not installed",
        UpdateResultKind.NoNewer => "no newer version",
        UpdateResultKind.Downloaded => $"downloaded {NewVersion ?? "a newer version"}, applies on exit",
        _ => "failed: " + (Error ?? "unknown error"),
    };

    /// <summary>The failed outcome for <paramref name="error"/>: its short type name and its message, one line, cut.</summary>
    public static UpdateOutcome Failure(DateTime utc, string version, Exception error)
    {
        string type;
        string message;
        try
        {
            type = error.GetType().Name;
            message = error.Message ?? string.Empty;
        }
        catch (Exception)
        {
            type = "Exception";
            message = string.Empty;
        }

        message = OneLine(message);
        if (message.Length > MaxMessage)
        {
            message = message[..MaxMessage];
        }

        return new UpdateOutcome(utc, version, UpdateResultKind.Failed, null, type + ": " + message);
    }

    /// <summary>
    /// <paramref name="raw"/> on one line: CR, LF and tab become spaces, other control characters a placeholder, and
    /// any http(s) address loses its user name, password, query and fragment.
    /// </summary>
    public static string OneLine(string raw)
    {
        string line = raw.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        line = SafeText.Clean(line);
        return UrlTail.Replace(UrlUser.Replace(line, "$1"), "$1");
    }
}
