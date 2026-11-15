namespace Context;

/// <summary>
/// Opens a note file or an attachment in the app the OS uses for it. The desktop write path is the only caller and
/// passes only a checked note or attachment path (a regular <c>.md</c> file inside the data folder, found through the
/// note list, or an attachment PDF that <c>NoteAttachments.TryResolve</c> accepted).
/// </summary>
internal interface INoteLauncher
{
    /// <summary>Opens <paramref name="path"/> in the default app. May throw when the OS can't open it.</summary>
    public void Launch(string path);
}
