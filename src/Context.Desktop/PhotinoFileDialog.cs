using Photino.NET;

namespace Context;

/// <summary>The file dialogs of the Photino window. Before <see cref="Window"/> is set, and on any failure, nothing is chosen.</summary>
internal sealed class PhotinoFileDialog : IFileDialog
{
    private const string Extension = ".pfx";

    private static readonly (string Name, string[] Extensions)[] Filters = [("Recovery file", ["*" + Extension])];

    /// <summary>The app's window; set once it is built.</summary>
    public PhotinoWindow? Window { get; set; }

    public string? SaveRecoveryFile()
    {
        if (Window is not { } window)
        {
            return null;
        }

        try
        {
            string? chosen = window.ShowSaveFile("Save the recovery file", null, Filters);
            if (string.IsNullOrWhiteSpace(chosen))
            {
                return null;
            }

            return Path.HasExtension(chosen) ? chosen : chosen + Extension;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string? OpenRecoveryFile()
    {
        if (Window is not { } window)
        {
            return null;
        }

        try
        {
            string[]? chosen = window.ShowOpenFile("Choose the recovery file", null, false, Filters);
            return chosen is { Length: 1 } && !string.IsNullOrWhiteSpace(chosen[0]) ? chosen[0] : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
