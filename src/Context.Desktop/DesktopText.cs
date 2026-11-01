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
}
