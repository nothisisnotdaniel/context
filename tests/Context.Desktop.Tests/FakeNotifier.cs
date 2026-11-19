namespace Context.Tests;

/// <summary>A notifier that records the notifications it was asked to show and can throw instead; it never shows anything.</summary>
internal sealed class FakeNotifier : INotifier
{
    private readonly object gate = new();
    private readonly List<(string Title, string Body)> shown = [];

    /// <summary>When set, <see cref="Show"/> throws this after recording the call.</summary>
    public Exception? Throws { get; set; }

    /// <summary>Every <c>(title, body)</c> passed to <see cref="Show"/>, in order.</summary>
    public IReadOnlyList<(string Title, string Body)> Shown
    {
        get
        {
            lock (gate)
            {
                return [.. shown];
            }
        }
    }

    public void Show(string title, string body)
    {
        lock (gate)
        {
            shown.Add((title, body));
        }

        if (Throws is not null)
        {
            throw Throws;
        }
    }
}
