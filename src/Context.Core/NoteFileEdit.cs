namespace Context;

internal sealed record NoteFileEntry(string Name, bool Directory, long Length);

/// <summary>One editor operation against the selected store. Reads, new files, moves and
/// removals use the same observed entries. Disposing an uncommitted edit undoes its writes.</summary>
internal abstract class NoteFileEdit : IDisposable
{
    private Action? checkCommit;
    private Action? finishStaging;
    private Action? checkFinalState;
    private Action? checkOriginalHistory;
    internal string? RelocationTarget { get; set; }

    // Keep originals readable until publication and all caller-side staging have finished.
    public void BeforeCommit(Action check, Action? apply = null)
    {
        checkCommit += check;
        finishStaging += apply;
    }

    // Membership checks whose expected state differs once the originals have been retired.
    public void AfterRetirement(Action check) => checkFinalState += check;

    public void OnRollback(Action check) => checkOriginalHistory += check;
    protected void VerifyOriginalHistory() => checkOriginalHistory?.Invoke();

    protected void VerifyFinalState()
    {
        checkFinalState?.Invoke();
        Verify();
    }

    protected void PrepareCommit()
    {
        if (checkCommit is not null)
        {
            Verify();
            checkCommit();
            finishStaging?.Invoke();
        }
    }

    public abstract IReadOnlyList<NoteFileEntry> List(string folder);
    public abstract void Expect(string path, bool directory = false);
    public bool Exists(string path, bool directory = false)
    {
        try
        {
            Expect(path, directory);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }
    public abstract byte[] Read(string path, long limit);
    // Remember the original bytes separately from the names used during publication.
    public abstract byte[] ReadOriginal(string path, long limit, string? ownerPath = null);
    public abstract void RequireResolvedHistory(string folder);
    public abstract void EnsureFolder(string path);
    public abstract void WriteNew(string path, byte[] bytes);
    public abstract void Reserve(string path);
    public abstract void PublishNote(string from, string to, byte[]? rewritten);
    public abstract void Relocate(string from, string to, bool directory = false);
    public abstract void Discard(string path);
    public abstract void Verify();
    public abstract void Commit();
    public abstract void RemoveEmpty(string folder);
    public abstract void Dispose();
}
