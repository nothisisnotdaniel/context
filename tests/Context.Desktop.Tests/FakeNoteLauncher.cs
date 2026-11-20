namespace Context.Tests;

/// <summary>A note launcher that records the paths it was asked to open and can throw instead; it never starts anything.</summary>
internal sealed class FakeNoteLauncher : INoteLauncher
{
    private readonly List<string> launched = [];

    /// <summary>When set, <see cref="Launch"/> throws this after recording the path.</summary>
    public Exception? Throws { get; set; }

    /// <summary>Every path passed to <see cref="Launch"/>, in order.</summary>
    public IReadOnlyList<string> Launched => launched;

    public void Launch(string path)
    {
        launched.Add(path);
        if (Throws is not null)
        {
            throw Throws;
        }
    }
}
