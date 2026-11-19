namespace Context;

/// <summary>Starts the desktop app (job 280: <c>context item &lt;n&gt; --open</c> when no desktop app runs).</summary>
internal interface IDesktopLauncher
{
    /// <summary>Starts the desktop app. Throws when it can't be started or the OS is not supported.</summary>
    public void Launch();
}
