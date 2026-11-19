namespace Context.Tests;

/// <summary>An area picker that returns <see cref="Bytes"/> (null: cancelled) or throws, and counts its calls.</summary>
internal sealed class FakeScreenCapture : IScreenCapture
{
    private int calls;

    /// <summary>What the next capture returns; null means the user cancelled.</summary>
    public byte[]? Bytes { get; set; }

    /// <summary>When set, the capture throws this instead.</summary>
    public Exception? Throws { get; set; }

    /// <summary>How often the area picker was opened.</summary>
    public int Calls => Volatile.Read(ref calls);

    public Task<byte[]?> CaptureAreaAsync()
    {
        Interlocked.Increment(ref calls);
        if (Throws is not null)
        {
            return Task.FromException<byte[]?>(Throws);
        }

        return Task.FromResult(Bytes);
    }
}
