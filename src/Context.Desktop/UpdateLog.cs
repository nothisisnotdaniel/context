using System.Globalization;
using System.Text;

namespace Context;

/// <summary>
/// The update log (job 396): each start's update check appends one line to <c>&lt;root&gt;/.logs/update.log</c>,
/// <c>&lt;yyyy-MM-ddTHH:mm:ssZ&gt; &lt;shown version&gt; &lt;outcome text&gt;</c>, so a copy that never updates can be
/// diagnosed afterwards. A line holds no note content, no stack trace, no query string and no home folder (shown as
/// <c>~</c>). Only the newest <see cref="MaxLines"/> lines are kept, below <see cref="CrashLog.MaxBytes"/>. Like the
/// crash log it never writes through a link and never throws.
/// </summary>
internal sealed class UpdateLog
{
    /// <summary>The log's file name inside the crash folder.</summary>
    public const string FileName = "update.log";

    /// <summary>How many lines are kept; older ones are dropped on each write.</summary>
    public const int MaxLines = 100;

    /// <summary>One line is cut to this many characters.</summary>
    public const int MaxLineLength = 1000;

    // A cut-off start of the home folder at the end of a line is hidden only from this many characters on.
    private const int MinHomeTail = 3;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Func<DateTime> utcNow;
    private readonly string? home;

    public UpdateLog(string root, Func<DateTime> utcNow, string? home)
    {
        Folder = Path.Combine(root, CrashLog.FolderName);
        LogPath = Path.Combine(Folder, FileName);
        this.utcNow = utcNow;
        string? trimmed = home?.TrimEnd('/', '\\');
        this.home = string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>The crash folder, <c>&lt;root&gt;/.logs</c>.</summary>
    public string Folder { get; }

    /// <summary>The log file, <c>&lt;root&gt;/.logs/update.log</c>.</summary>
    public string LogPath { get; }

    /// <summary>Appends one line for <paramref name="outcome"/> and drops the oldest lines. Never throws.</summary>
    public void Write(UpdateOutcome outcome)
    {
        try
        {
            string line = Line(outcome);

            // Never through a link: a linked .logs folder or update.log (or a folder in its place) is left alone.
            PrivateFile.CreateFolder(Folder);
            if (!CrashLog.IsPlainFolder(Folder))
            {
                return;
            }

            FileInfo file = new FileInfo(LogPath);
            if (file.LinkTarget is not null || Directory.Exists(LogPath)
                || (file.Exists && file.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            {
                return;
            }

            List<string> lines = [.. file.Exists ? Existing(file) : [], line];
            byte[] bytes = Bytes(lines);
            if (file.Exists)
            {
                PrivateFile.Rewrite(LogPath, bytes);
            }
            else
            {
                using FileStream stream = PrivateFile.OpenNew(LogPath);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        catch (Exception)
        {
            // The update log is best effort.
        }
    }

    // The lines already there; an unreadable, oversized or non-UTF-8 file counts as empty and is replaced.
    private static List<string> Existing(FileInfo file)
    {
        try
        {
            if (file.Length >= CrashLog.MaxBytes)
            {
                return [];
            }

            string text = StrictUtf8.GetString(File.ReadAllBytes(file.FullName));
            return [.. text.ReplaceLineEndings("\n").Split('\n').Where(l => l.Length > 0)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    // The newest MaxLines lines, dropping more of the oldest until the file stays below CrashLog.MaxBytes.
    private static byte[] Bytes(List<string> lines)
    {
        int skip = Math.Max(0, lines.Count - MaxLines);
        while (true)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(string.Concat(lines.Skip(skip).Select(l => l + "\n")));
            if (bytes.Length < CrashLog.MaxBytes || skip >= lines.Count - 1)
            {
                return bytes;
            }

            skip++;
        }
    }

    private string Line(UpdateOutcome outcome)
    {
        DateTime utc = utcNow();
        utc = utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        string text = utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            + " " + StartupUpdate.ShownVersion(outcome.Version)
            + " " + outcome.Text;
        if (home is not null)
        {
            text = HideHome(text, home);
            string swapped = home.Contains('\\') ? home.Replace('\\', '/') : home.Replace('/', '\\');
            if (swapped != home)
            {
                text = HideHome(text, swapped);
            }
        }

        text = UpdateOutcome.OneLine(text);
        return text.Length <= MaxLineLength ? text : text[..MaxLineLength];
    }

    // The home folder becomes ~, also a start of it left at the end of a failure message that was cut.
    private static string HideHome(string text, string home)
    {
        text = text.Replace(home, "~", StringComparison.OrdinalIgnoreCase);
        for (int length = home.Length - 1; length >= MinHomeTail; length--)
        {
            if (text.EndsWith(home[..length], StringComparison.OrdinalIgnoreCase))
            {
                return text[..^length] + "~";
            }
        }

        return text;
    }
}
