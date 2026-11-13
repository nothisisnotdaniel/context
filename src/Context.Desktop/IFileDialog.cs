namespace Context;

/// <summary>
/// The OS file dialogs: for the recovery file, and for importing and exporting notes. A cancelled dialog gives
/// <c>null</c> (or no files). What they return is what the user picked, and still unchecked.
/// </summary>
internal interface IFileDialog
{
    /// <summary>Asks where to save the new recovery file (<c>.pfx</c>).</summary>
    public string? SaveRecoveryFile();

    /// <summary>Asks for the recovery file (<c>.pfx</c>) to restore from.</summary>
    public string? OpenRecoveryFile();

    /// <summary>Asks for the Markdown files (<c>.md</c>) to import, several at once; empty when cancelled.</summary>
    public IReadOnlyList<string> OpenMarkdownFiles();

    /// <summary>
    /// Asks where to save an exported note called <paramref name="suggestedName"/> (<c>Title.md</c>); a chosen name
    /// without an extension gets <c>.md</c>.
    /// </summary>
    public string? SaveExportFile(string suggestedName);
}
