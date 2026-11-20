namespace Context;

/// <summary>How the app was ending, as recorded in the crash session (job 334) while it still ran.</summary>
internal enum ExitReason
{
    /// <summary>The .NET runtime shut down (<c>AppDomain.ProcessExit</c>).</summary>
    ProcessExit,

    /// <summary>Windows was logging off (<c>WM_ENDSESSION</c> with <c>ENDSESSION_LOGOFF</c>).</summary>
    Logoff,

    /// <summary>Windows was shutting down or restarting (<c>WM_ENDSESSION</c> with no flag).</summary>
    Shutdown,

    /// <summary>Windows asked the app to close (<c>WM_ENDSESSION</c> with <c>ENDSESSION_CLOSEAPP</c>).</summary>
    CloseApp,
}

/// <summary>The fixed session codes and log texts of <see cref="ExitReason"/>, and the <c>WM_ENDSESSION</c> mapping.</summary>
internal static class ExitReasons
{
    /// <summary><c>ENDSESSION_CLOSEAPP</c>: the Restart Manager (an installer or update) closes the app.</summary>
    public const long EndSessionCloseApp = 0x1;

    /// <summary><c>ENDSESSION_LOGOFF</c>: the user is logging off.</summary>
    public const long EndSessionLogoff = 0x80000000;

    /// <summary>The fixed code written into the session file.</summary>
    public static string Code(ExitReason reason) => reason switch
    {
        ExitReason.ProcessExit => "process-exit",
        ExitReason.Logoff => "logoff",
        ExitReason.Shutdown => "shutdown",
        ExitReason.CloseApp => "close-app",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>The fixed text the next start's <c>unclean-exit</c> log shows.</summary>
    public static string Text(ExitReason reason) => reason switch
    {
        ExitReason.ProcessExit => "the .NET runtime shut down (Environment.Exit or a normal process exit)",
        ExitReason.Logoff => "Windows was logging off",
        ExitReason.Shutdown => "Windows was shutting down or restarting (also for updates)",
        ExitReason.CloseApp => "Windows asked the app to close (an installer or update)",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>The reason for a session code, or <c>null</c> when it is none of the four.</summary>
    public static ExitReason? FromCode(string code) => code switch
    {
        "process-exit" => ExitReason.ProcessExit,
        "logoff" => ExitReason.Logoff,
        "shutdown" => ExitReason.Shutdown,
        "close-app" => ExitReason.CloseApp,
        _ => null,
    };

    /// <summary>
    /// The reason for a <c>WM_ENDSESSION</c> (<paramref name="ending"/> is <c>wParam != 0</c>, <paramref name="flags"/>
    /// is <c>lParam</c>): <c>null</c> when the session isn't ending; logoff wins over close-app; anything else is a
    /// shutdown or restart. <c>ENDSESSION_CRITICAL</c> is ignored.
    /// </summary>
    public static ExitReason? FromEndSession(bool ending, long flags)
    {
        if (!ending)
        {
            return null;
        }

        if ((flags & EndSessionLogoff) != 0)
        {
            return ExitReason.Logoff;
        }

        if ((flags & EndSessionCloseApp) != 0)
        {
            return ExitReason.CloseApp;
        }

        return ExitReason.Shutdown;
    }
}
