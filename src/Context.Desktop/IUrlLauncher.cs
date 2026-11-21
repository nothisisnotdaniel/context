namespace Context;

/// <summary>
/// Job 354: opens a web or mail link in the default browser or mail app. The open-web-link action is the only
/// caller and passes only a <see cref="Uri"/> that <see cref="WebLink.TryParse"/> accepted.
/// </summary>
internal interface IUrlLauncher
{
    /// <summary>Opens <paramref name="uri"/> in the default app. May throw when the OS can't open it.</summary>
    public void Open(Uri uri);
}
