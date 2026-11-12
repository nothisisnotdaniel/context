namespace Context;

/// <summary>
/// Opens a note file in the app the OS uses for it. The desktop write path is the only caller and passes only a
/// checked note path (a regular <c>.md</c> file inside the data folder, found through the note list).
/// </summary>
internal interface INoteLauncher
{
    /// <summary>Opens <paramref name="path"/> in the default app. May throw when the OS can't open it.</summary>
    public void Launch(string path);
}
