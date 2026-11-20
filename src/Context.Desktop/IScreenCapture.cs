namespace Context;

/// <summary>
/// Lets the user pick a screen area and returns it as PNG bytes (job 260). <c>Program.cs</c> passes the real one on
/// macOS only; tests use a fake, so no test touches the screen.
/// </summary>
internal interface IScreenCapture
{
    /// <summary>
    /// The picked area as PNG bytes (at most <see cref="NoteAttachments.MaxBytes"/> plus one byte are read), or
    /// <c>null</c> when the user cancelled or nothing could be taken. May throw.
    /// </summary>
    public Task<byte[]?> CaptureAreaAsync();
}
