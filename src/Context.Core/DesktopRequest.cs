using System.Globalization;
using System.Text;

namespace Context;

/// <summary>
/// The open request from the command line to the desktop app (job 280): <c>&lt;root&gt;/.desktop/open-request</c>
/// holds exactly <c>item &lt;n&gt;\n</c>, never a path. The desktop holds <c>&lt;root&gt;/.desktop/running.lock</c>
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

    /// <summary>The folder <c>&lt;root&gt;/.desktop</c>.</summary>
    public static string Folder(string root) => Path.Combine(root, FolderName);

    /// <summary>The request file <c>&lt;root&gt;/.desktop/open-request</c>.</summary>
    public static string RequestPath(string root) => Path.Combine(Folder(root), RequestFileName);

    /// <summary>The lock file <c>&lt;root&gt;/.desktop/running.lock</c>.</summary>
    public static string LockPath(string root) => Path.Combine(Folder(root), LockFileName);

    /// <summary>The request's bytes for work item <paramref name="item"/>: <c>item &lt;n&gt;\n</c>.</summary>
    public static byte[] ItemRequest(int item) =>
        Encoding.ASCII.GetBytes(ItemPrefix + item.ToString(CultureInfo.InvariantCulture) + "\n");

    /// <summary>
    /// Writes the request for <paramref name="item"/>: the folder through <see cref="PrivateFile.CreateFolder"/>
    /// (<c>0700</c> off Windows), a new temp file through <see cref="PrivateFile.Write"/> (<c>0600</c>), then a move
    /// over any older request. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    public static void WriteItem(string root, int item)
    {
        string folder = Folder(root);
        PrivateFile.CreateFolder(folder);
        string temp = Path.Combine(folder, RequestFileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            PrivateFile.Write(temp, ItemRequest(item));
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
    public static bool TryTake(string root, out int? item)
    {
        item = null;
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
        return true;
    }

    /// <summary>
    /// The work item of a request's bytes: exactly <c>item &lt;n&gt;</c> with an optional final <c>\n</c> (or
    /// <c>\r\n</c>) and a valid number (<see cref="WorkItemParser.TryParseNumber"/>, no <c>#</c>); else <c>null</c>.
    /// </summary>
    public static int? ParseItem(byte[] bytes)
    {
        if (bytes.Length > MaxBytes || bytes.Any(b => b > 0x7F))
        {
            return null;
        }

        string text = Encoding.ASCII.GetString(bytes);
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            text = text[..^2];
        }
        else if (text.EndsWith('\n'))
        {
            text = text[..^1];
        }

        if (!text.StartsWith(ItemPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string number = text[ItemPrefix.Length..];
        return number.Length > 0 && number[0] != '#' && WorkItemParser.TryParseNumber(number, out int item) ? item : null;
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
