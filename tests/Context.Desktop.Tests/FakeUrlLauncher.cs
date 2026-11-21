namespace Context.Tests;

/// <summary>A URL launcher (job 354) that records the URLs it was asked to open and can throw instead; it never starts anything.</summary>
internal sealed class FakeUrlLauncher : IUrlLauncher
{
    private readonly List<Uri> opened = [];

    /// <summary>When set, <see cref="Open"/> throws this after recording the URL.</summary>
    public Exception? Throws { get; set; }

    /// <summary>Every URL passed to <see cref="Open"/>, in order.</summary>
    public IReadOnlyList<Uri> Opened => opened;

    public void Open(Uri uri)
    {
        opened.Add(uri);
        if (Throws is not null)
        {
            throw Throws;
        }
    }
}
