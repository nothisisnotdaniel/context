using System.Globalization;
using System.Text;

namespace Context;

/// <summary>
/// The open request from the command line to the desktop app (job 280): <c>&lt;root&gt;/.desktop/open-request</c>
/// holds exactly <c>item &lt;n&gt;\n</c>, or <c>show\n</c> from a second desktop copy (job 315), never a path. The desktop holds <c>&lt;root&gt;/.desktop/running.lock</c>
/// open (<see cref="PrivateFile.OpenLock"/>) while it runs, so the command line can tell whether to start it.
/// </summary>
internal static class DesktopRequest
{
    /// <summary>The folder in the root that holds the request and the lock.</summary>
    public const string FolderName = ".desktop";

    /// <summary>The request file's name.</summary>
    public const string RequestFileName = "open-request";

    /// <summary>The lock file's name.</summary>
    public const string LockFileName = "running.lock";

    /// <summary>The most bytes of a request that are read.</summary>
    public const int MaxBytes = 64;

    private const string ItemPrefix = "item ";

    private const string ShowText = "show";

    /// <summary>The folder <c>&lt;root&gt;/.desktop</c>.</summary>
    public static string Folder(string root) => Path.Combine(root, FolderName);

    /// <summary>The request file <c>&lt;root&gt;/.desktop/open-request</c>.</summary>
    public static string RequestPath(string root) => Path.Combine(Folder(root), RequestFileName);

    /// <summary>The lock file <c>&lt;root&gt;/.desktop/running.lock</c>.</summary>
    public static string LockPath(string root) => Path.Combine(Folder(root), LockFileName);

    /// <summary>The request's bytes for work item <paramref name="item"/>: <c>item &lt;n&gt;\n</c>.</summary>
    public static byte[] ItemRequest(int item) =>
        Encoding.ASCII.GetBytes(ItemPrefix + item.ToString(CultureInfo.InvariantCulture) + "\n");

    /// <summary>The show request's bytes (job 315): <c>show\n</c>, which brings the running window to the front.</summary>
    public static byte[] ShowRequest() => Encoding.ASCII.GetBytes(ShowText + "\n");

    /// <summary>
    /// Writes the request for <paramref name="item"/>: the folder through <see cref="PrivateFile.CreateFolder"/>
    /// (<c>0700</c> off Windows), a new temp file through <see cref="PrivateFile.Write"/> (<c>0600</c>), then a move
    /// over any older request. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    public static void WriteItem(string root, int item) => Write(root, ItemRequest(item));

    /// <summary>
    /// Writes the show request (<see cref="ShowRequest"/>) the same way as <see cref="WriteItem"/>. Throws
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    public static void WriteShow(string root) => Write(root, ShowRequest());

    private static void Write(string root, byte[] bytes)
    {
        string folder = Folder(root);
        PrivateFile.CreateFolder(folder);
        string temp = Path.Combine(folder, RequestFileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            PrivateFile.Write(temp, bytes);
            File.Move(temp, RequestPath(root), overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// True when no desktop app holds the lock: the lock file could be opened (and was closed again at once). False
    /// while another holder has it open. The folder is created when missing.
    /// </summary>
    public static bool IsLockFree(string root)
    {
        PrivateFile.CreateFolder(Folder(root));
        try
        {
            using FileStream stream = PrivateFile.OpenLock(LockPath(root));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Takes the request in <paramref name="root"/> (the desktop's side): reads at most <see cref="MaxBytes"/> bytes
    /// through <see cref="BoundedFile"/> (a larger or unreadable file counts as a bad request), deletes the file and
    /// parses it (<see cref="ParseItem"/>; <c>null</c> for anything else than <c>item &lt;n&gt;</c>). False, with
    /// nothing done, when there is no request or it can't be deleted (it is tried again later). Never throws.
    /// </summary>
    public static bool TryTake(string root, out int? item) => TryTake(root, out item, out _);

    /// <summary>
    /// Like <see cref="TryTake(string, out int?)"/>, and also tells a show request (<see cref="IsShow"/>) apart:
    /// <paramref name="show"/> is true only for exactly <c>show</c>, and then <paramref name="item"/> is <c>null</c>.
    /// </summary>
    public static bool TryTake(string root, out int? item, out bool show)
    {
        item = null;
        show = false;
        string path = RequestPath(root);
        if (!File.Exists(path))
        {
            return false;
        }

        byte[]? bytes;
        try
        {
            bytes = BoundedFile.Read(path, MaxBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            bytes = null;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        item = bytes is null ? null : ParseItem(bytes);
        show = bytes is not null && IsShow(bytes);
        return true;
    }

    /// <summary>
    /// The work item of a request's bytes: exactly <c>item &lt;n&gt;</c> with an optional final <c>\n</c> (or
    /// <c>\r\n</c>) and a valid number (<see cref="WorkItemParser.TryParseNumber"/>, no <c>#</c>); else <c>null</c>.
    /// </summary>
    public static int? ParseItem(byte[] bytes)
    {
        string? text = Line(bytes);
        if (text is null || !text.StartsWith(ItemPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string number = text[ItemPrefix.Length..];
        return number.Length > 0 && number[0] != '#' && WorkItemParser.TryParseNumber(number, out int item) ? item : null;
    }

    /// <summary>
    /// True when a request's bytes are exactly <c>show</c> with an optional final <c>\n</c> (or <c>\r\n</c>), the
    /// request of a second desktop copy (job 315); anything else, <c>SHOW</c> and <c>show me</c> included, is false.
    /// </summary>
    public static bool IsShow(byte[] bytes) => string.Equals(Line(bytes), ShowText, StringComparison.Ordinal);

    // The request as one ASCII line without its optional final \n or \r\n; null when too long or not ASCII.
    private static string? Line(byte[] bytes)
    {
        if (bytes.Length > MaxBytes || bytes.Any(b => b > 0x7F))
        {
            return null;
        }

        string text = Encoding.ASCII.GetString(bytes);
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        return text.EndsWith('\n') ? text[..^1] : text;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind: a temp file is never read as a request.
        }
    }
}
