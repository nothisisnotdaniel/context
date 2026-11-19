namespace Context;

/// <summary>
/// The window icon file (job 284): <c>Assets/Context.ico</c> on Windows, else <c>Assets/Context.png</c>, under the
/// app's own folder. The macOS Dock icon comes from the bundle's <c>Contents/Resources/Context.icns</c> instead.
/// </summary>
internal static class AppIcon
{
    /// <summary>
    /// The full path of the window icon under <paramref name="baseDirectory"/>, or <c>null</c> when that file does
    /// not exist (the window then keeps the default icon).
    /// </summary>
    public static string? File(string baseDirectory, bool windows)
    {
        string path = Path.Combine(baseDirectory, "Assets", windows ? "Context.ico" : "Context.png");
        return System.IO.File.Exists(path) ? path : null;
    }
}
