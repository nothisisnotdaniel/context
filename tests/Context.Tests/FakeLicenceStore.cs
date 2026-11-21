namespace Context.Tests;

/// <summary>
/// An in-memory licence store (job 367): supported or not, with an optional refusal, and failing on demand. It counts
/// every call; the actions call it on a pool thread, so the counters are guarded.
/// </summary>
internal sealed class FakeLicenceStore : ILicenceStore
{
    public const string Failure = "The test licence store failed.";

    private readonly object gate = new object();
    private readonly List<string> writes = [];
    private int reads;
    private int deletes;
    private string? stored;

    public FakeLicenceStore(bool supported = true, string? stored = null)
    {
        IsSupported = supported;
        this.stored = stored;
    }

    public bool IsSupported { get; set; }

    public string? WriteRefusal { get; set; }

    public string? Stored
    {
        get
        {
            lock (gate)
            {
                return stored;
            }
        }

        set
        {
            lock (gate)
            {
                stored = value;
            }
        }
    }

    /// <summary>Makes every call throw a <see cref="LicenceStoreException"/>.</summary>
    public bool Throws { get; set; }

    /// <summary>Makes only <see cref="Write"/> throw.</summary>
    public bool WriteThrows { get; set; }

    /// <summary>Makes only <see cref="Delete"/> throw.</summary>
    public bool DeleteThrows { get; set; }

    /// <summary>Runs at the start of <see cref="Read"/>, outside the guard (a test blocks it to hold a read).</summary>
    public Action? BeforeRead { get; set; }

    /// <summary>Runs at the start of <see cref="Write"/>, outside the guard (a test blocks it to hold an activation).</summary>
    public Action? BeforeWrite { get; set; }

    public int Reads
    {
        get
        {
            lock (gate)
            {
                return reads;
            }
        }
    }

    public IReadOnlyList<string> Writes
    {
        get
        {
            lock (gate)
            {
                return [.. writes];
            }
        }
    }

    public int Deletes
    {
        get
        {
            lock (gate)
            {
                return deletes;
            }
        }
    }

    public string? Read()
    {
        BeforeRead?.Invoke();
        lock (gate)
        {
            reads++;
            ThrowIfSet();
            return stored;
        }
    }

    public void Write(string key)
    {
        BeforeWrite?.Invoke();
        lock (gate)
        {
            writes.Add(key);
            ThrowIfSet();
            if (WriteThrows)
            {
                throw new LicenceStoreException(Failure);
            }

            stored = key;
        }
    }

    public bool Delete()
    {
        lock (gate)
        {
            deletes++;
            ThrowIfSet();
            if (DeleteThrows)
            {
                throw new LicenceStoreException(Failure);
            }

            bool had = stored is not null;
            stored = null;
            return had;
        }
    }

    private void ThrowIfSet()
    {
        if (Throws)
        {
            throw new LicenceStoreException(Failure);
        }
    }
}
