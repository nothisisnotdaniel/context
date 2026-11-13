namespace Context;

/// <summary>The OS file dialogs for the recovery file. Both return the chosen path, or <c>null</c> when cancelled.</summary>
internal interface IFileDialog
{
    /// <summary>Asks where to save the new recovery file (<c>.pfx</c>).</summary>
    public string? SaveRecoveryFile();

    /// <summary>Asks for the recovery file (<c>.pfx</c>) to restore from.</summary>
    public string? OpenRecoveryFile();
}
