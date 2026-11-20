namespace Context;

/// <summary>
/// What <see cref="DesktopStartup.Begin"/> found (job 315): the running lock this copy holds until it exits (null when
/// another copy holds it or it can't be made), the crash hint of <see cref="CrashLog.Begin"/> (null when it wasn't
/// called or found nothing), and whether this copy hands over to the running one and exits at once.
/// </summary>
internal sealed record DesktopStart(FileStream? Lock, CrashHint? Hint, bool HandOver);

/// <summary>
/// The start of the desktop app before anything else is built (job 315): it takes <c>&lt;root&gt;/.desktop/running.lock</c>
/// first, so only the copy that holds it reads and writes the crash session (<see cref="CrashLog.Begin"/>). A second
/// copy, which finds the lock held, never touches the session and so never files a false <c>unclean-exit</c> log; on
/// Windows it leaves the show request (<see cref="DesktopRequest.WriteShow"/>) for the running window and hands over.
/// </summary>
internal static class DesktopStartup
{
    /// <summary>
    /// Takes the running lock in <paramref name="root"/>, then: taken, calls <paramref name="crashLog"/>'s
    /// <see cref="CrashLog.Begin"/>; held by another holder, calls nothing, and on Windows writes the show request and
    /// hands over; can't be made for another reason (the folder can't be made, access denied), calls
    /// <see cref="CrashLog.Begin"/> with no lock, as before job 315. Never throws.
    /// </summary>
    public static DesktopStart Begin(string root, CrashLog crashLog, bool isWindows)
    {
        FileStream? held;
        try
        {
            PrivateFile.CreateFolder(DesktopRequest.Folder(root));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return new DesktopStart(null, crashLog.Begin(), false);
        }

        try
        {
            held = PrivateFile.OpenLock(DesktopRequest.LockPath(root));
        }
        catch (IOException)
        {
            // Another copy runs: its session is left alone.
            if (!isWindows)
            {
                return new DesktopStart(null, null, false);
            }

            try
            {
                DesktopRequest.WriteShow(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The running window stays where it is; this copy still hands over.
            }

            return new DesktopStart(null, null, true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new DesktopStart(null, crashLog.Begin(), false);
        }

        return new DesktopStart(held, crashLog.Begin(), false);
    }
}
