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

    /// <summary>What <see cref="OpenMarkdownFiles"/> returns; empty means cancelled.</summary>
    public IReadOnlyList<string> ImportPaths { get; set; } = [];

    /// <summary>What <see cref="SaveExportFile"/> returns; <c>null</c> means cancelled.</summary>
    public string? ExportPath { get; set; }

    public int ImportOpens { get; private set; }

    public int ExportSaves { get; private set; }

    /// <summary>The name the last <see cref="SaveExportFile"/> call suggested.</summary>
    public string? SuggestedName { get; private set; }

    public IReadOnlyList<string> OpenMarkdownFiles()
    {
        ImportOpens++;
        return ImportPaths;
    }

    public string? SaveExportFile(string suggestedName)
    {
        ExportSaves++;
        SuggestedName = suggestedName;
        return ExportPath;
    }
}
