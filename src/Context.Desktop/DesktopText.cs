using System.Globalization;

namespace Context;

/// <summary>Text helpers for the desktop components.</summary>
internal static class DesktopText
{
    /// <summary>The date format of every date the desktop shows.</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Outside text made safe for one line: <see cref="SafeText.Clean"/>, then every <c>\n</c> becomes a space.
    /// Blazor still encodes the result when it renders it; this only removes control characters and line breaks.
    /// </summary>
    public static string OneLine(string raw) => SafeText.Clean(raw).Replace('\n', ' ');

    /// <summary>
    /// A note's name for the note editor (job 235): <see cref="OneLine"/>, then every bidi embedding, override or
    /// isolate control (U+202A to U+202E, U+2066 to U+2069) becomes one <see cref="SafeText.Placeholder"/>, so a name
    /// can't reorder the text shown around it.
    /// </summary>
    public static string NoteName(string raw)
    {
        string line = OneLine(raw);
        if (!line.Any(IsBidiControl))
        {
            return line;
        }

        return new string(line.Select(c => IsBidiControl(c) ? SafeText.Placeholder : c).ToArray());
    }

    private static bool IsBidiControl(char c) => c is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069';

    /// <summary>
    /// A todo's due date as typed in the palette, like the console's <c>/todo</c>: <c>today</c>, <c>tomorrow</c>
    /// (ignoring case, from <paramref name="today"/>) or <c>yyyy-MM-dd</c>.
    /// </summary>
    public static bool TryParseDue(string token, DateOnly today, out DateOnly due)
    {
        if (string.Equals(token, "today", StringComparison.OrdinalIgnoreCase))
        {
            due = today;
            return true;
        }

        if (string.Equals(token, "tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            due = today.AddDays(1);
            return true;
        }

        return DateOnly.TryParseExact(token, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out due);
    }

    // Suite Pro (job 367): the Settings section, its results and the locked hint. Raw text; the panel cleans names.

    /// <summary>The Settings heading of the Suite Pro section.</summary>
    public const string ProHeading = "Suite Pro";

    public const string ProNoneText = "No Pro licence activated.";

    public const string ProUnsupportedText = "Suite Pro needs macOS or Windows.";

    public const string ProUnreadableText = "Can't read the licence store.";

    public const string ProNoBuildDateText = "This build has no build date, so Pro stays locked in it.";

    public const string ProEmptyKeyText = "Paste your licence key first.";

    public const string ProStoreFailedText = "Can't save the licence. Nothing was stored.";

    public const string ProFileFailedText = "Can't write the activation file. Nothing was stored.";

    public const string ProFileFailedKeyLeftText =
        "Can't write the activation file, and the stored key could not be removed. Click Remove licence.";

    public const string ProRemoveQuestion = "Remove the Suite Pro licence from this computer? Times uses it too.";

    public const string ProRemovedText = "Suite Pro licence removed from this computer.";

    public const string ProNothingToRemoveText = "No Pro licence was activated.";

    public const string ProFileLeftText = "The licence key was removed, but the activation file could not be deleted.";

    public const string ProRemoveFailedText = "Can't remove the licence. Nothing was changed.";

    public const string ProActivateLabel = "Activate";

    public const string ProRemoveLabel = "Remove licence";

    public const string ProOpenSettingsLabel = "Open Settings";

    /// <summary>The accent state line of an active licence.</summary>
    public static string ProActiveText(string name, DateOnly until) =>
        $"Suite Pro: {name} · updates until {Date(until)}.";

    /// <summary>The grey state line when only <c>activation.json</c> names a licensee.</summary>
    public static string ProFileOnlyText(string licensee) =>
        $"No Pro licence activated. activation.json names {licensee}, but no key is stored on this computer.";

    /// <summary>The red state line of a stored key that does not verify.</summary>
    public static string ProInvalidText(string reason) => $"The stored licence key cannot be used: {reason}. Pro stays locked.";

    /// <summary>The yellow state line of a licence whose updates end before this build.</summary>
    public static string ProNotCoveredText(DateOnly until, DateOnly buildDate) =>
        $"Your licence covers updates until {Date(until)}; this build is from {Date(buildDate)}, so Pro stays locked in it.";

    /// <summary>The refusal of a typed key that does not verify.</summary>
    public static string ProKeyRefusedText(string reason) => $"This licence key cannot be used: {reason}. Nothing was stored.";

    /// <summary>The accent result of a successful activation.</summary>
    public static string ProActivatedText(string name, DateOnly until) =>
        $"Suite Pro activated for {name} · updates until {Date(until)}.";

    private static string Date(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
