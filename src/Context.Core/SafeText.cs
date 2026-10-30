using System.Text;

namespace Context;

/// <summary>
/// Makes text from disk or the OS safe to render: control characters never reach the terminal.
/// Each one becomes a visible <see cref="Placeholder"/>, so the user can see something was there.
/// </summary>
internal static class SafeText
{
    /// <summary>Shown in place of each control character (U+FFFD REPLACEMENT CHARACTER).</summary>
    public const char Placeholder = '�';

    private const string Tab = "    ";

    /// <summary>
    /// Keeps <c>\n</c>, turns <c>\r\n</c> into <c>\n</c> and <c>\t</c> into 4 spaces, and replaces
    /// every other C0 control (U+0000–U+001F, including a lone <c>\r</c>), DEL (U+007F) and C1
    /// control (U+0080–U+009F) with one <see cref="Placeholder"/>. All other characters are kept.
    /// </summary>
    public static string Clean(string text)
    {
        if (!NeedsCleaning(text))
        {
            return text;
        }

        StringBuilder builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                continue;
            }

            if (c == '\n')
            {
                builder.Append('\n');
            }
            else if (c == '\t')
            {
                builder.Append(Tab);
            }
            else if (IsControl(c))
            {
                builder.Append(Placeholder);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static bool NeedsCleaning(string text)
    {
        foreach (char c in text)
        {
            if (c != '\n' && IsControl(c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsControl(char c) => c <= '\u001F' || (c >= '\u007F' && c <= '\u009F');
}
