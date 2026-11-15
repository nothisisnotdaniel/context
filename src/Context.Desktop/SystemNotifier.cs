using System.Diagnostics;
using System.Text;

namespace Context;

/// <summary>
/// The real <see cref="INotifier"/>: a macOS notification through <c>/usr/bin/osascript</c>, a Windows toast through
/// <c>powershell.exe</c>, nothing on other systems. Both run a fixed script with no shell; the title and body reach it
/// only as separate process arguments (macOS, <c>argv</c>) or environment values (Windows), never as script text, so
/// todo text can't inject script or XML. Each text is one line (<see cref="DesktopText.OneLine"/>) of at most
/// <see cref="MaxTextLength"/> characters. Every failure is swallowed.
/// </summary>
internal sealed class SystemNotifier : INotifier
{
    /// <summary>The longest title or body passed on; longer text is cut.</summary>
    public const int MaxTextLength = 200;

    private const string TitleVariable = "CONTEXT_NOTIFY_TITLE";
    private const string BodyVariable = "CONTEXT_NOTIFY_BODY";

    // AppleScript: the title and body are the script's run arguments.
    private static readonly string[] MacScript =
    [
        "on run argv",
        "display notification (item 2 of argv) with title (item 1 of argv)",
        "end run",
    ];

    // PowerShell (passed base64-encoded as a whole): reads the texts from the environment and puts them into the toast as
    // XML text nodes.
    private const string WindowsScript =
        "$ErrorActionPreference = 'Stop'\n"
        + "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null\n"
        + "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] > $null\n"
        + "$xml = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent("
        + "[Windows.UI.Notifications.ToastTemplateType]::ToastText02)\n"
        + "$texts = $xml.GetElementsByTagName('text')\n"
        + "$texts.Item(0).AppendChild($xml.CreateTextNode($env:" + TitleVariable + ")) > $null\n"
        + "$texts.Item(1).AppendChild($xml.CreateTextNode($env:" + BodyVariable + ")) > $null\n"
        + "$app = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe'\n"
        + "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($app).Show("
        + "[Windows.UI.Notifications.ToastNotification]::new($xml))\n";

    public void Show(string title, string body)
    {
        try
        {
            ProcessStartInfo? start = Start(Clean(title), Clean(body));
            if (start is not null)
            {
                using Process? process = Process.Start(start);
            }
        }
        catch (Exception)
        {
            // A notification that can't be shown is skipped; the todo stays notified.
        }
    }

    private static ProcessStartInfo? Start(string title, string body)
    {
        if (OperatingSystem.IsMacOS())
        {
            ProcessStartInfo mac = Fixed("/usr/bin/osascript");
            foreach (string line in MacScript)
            {
                mac.ArgumentList.Add("-e");
                mac.ArgumentList.Add(line);
            }

            mac.ArgumentList.Add(title);
            mac.ArgumentList.Add(body);
            return mac;
        }

        if (OperatingSystem.IsWindows())
        {
            ProcessStartInfo windows = Fixed(
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"));
            windows.ArgumentList.Add("-NoProfile");
            windows.ArgumentList.Add("-NonInteractive");
            windows.ArgumentList.Add("-EncodedCommand");
            windows.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(WindowsScript)));
            windows.Environment[TitleVariable] = title;
            windows.Environment[BodyVariable] = body;
            return windows;
        }

        return null;
    }

    // A program started directly, with no shell and no window.
    private static ProcessStartInfo Fixed(string program) => new ProcessStartInfo
    {
        FileName = program,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    // One line, at most MaxTextLength characters, never starting with '-' (so no argument reads as an option).
    private static string Clean(string text)
    {
        string line = DesktopText.OneLine(text).Trim();
        if (line.Length > MaxTextLength)
        {
            line = line[..(char.IsHighSurrogate(line[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength)];
        }

        return line.StartsWith('-') ? " " + line : line;
    }
}
