namespace Context.Tests;

/// <summary>A file dialog that returns the paths a test set and counts how often it was shown; it opens no window.</summary>
internal sealed class FakeFileDialog : IFileDialog
{
    /// <summary>What <see cref="SaveRecoveryFile"/> returns; <c>null</c> means cancelled.</summary>
    public string? SavePath { get; set; }

    /// <summary>What <see cref="OpenRecoveryFile"/> returns; <c>null</c> means cancelled.</summary>
    public string? OpenPath { get; set; }

    public int Saves { get; private set; }

    public int Opens { get; private set; }

    public string? SaveRecoveryFile()
    {
        Saves++;
        return SavePath;
    }

    public string? OpenRecoveryFile()
    {
        Opens++;
        return OpenPath;
    }
}
