using System.Text;

namespace Context.Tests;

public sealed class InboxTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 7, 14, 5, 0, DateTimeKind.Local);

    private readonly string parent;
    private readonly string root;
    private readonly NoteStore store;

    public InboxTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-inbox-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(root);
        store = new NoteStore(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private string InboxPath => Path.Combine(root, "inbox.md");

    // AC-3
    [Fact]
    public void Capture_MissingInbox_CreatesItWithHeadingAndLine()
    {
        Assert.Equal(CaptureResult.Captured, Inbox.Capture(store, "call Anna", Now));

        Assert.Equal("# inbox\n- 2026-11-07 14:05 call Anna\n", File.ReadAllText(InboxPath));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    // AC-3
    [Fact]
    public void Capture_ExistingInboxInAnyCase_AppendsAndAddsMissingNewline()
    {
        string path = Path.Combine(root, "Inbox.md");
        File.WriteAllText(path, "# Inbox\n- old");

        Assert.Equal(CaptureResult.Captured, Inbox.Capture(store, "new", Now));

        Assert.Equal("# Inbox\n- old\n- 2026-11-07 14:05 new\n", File.ReadAllText(path));
        Assert.Single(store.List());
    }

    // AC-3
    [Fact]
    public void Capture_CleansControlCharacters()
    {
        Inbox.Capture(store, " a\nb\tc\u001b[31m ", Now);

        Assert.Equal("# inbox\n- 2026-11-07 14:05 a b c [31m\n", File.ReadAllText(InboxPath));
    }

    [Fact]
    public void Clean_TurnsControlsIntoSpacesAndTrims()
    {
        Assert.Equal("a  b", Inbox.Clean("\r\na\r\nb\u0085"));
        Assert.Equal(string.Empty, Inbox.Clean("\n\t "));
    }

    // AC-3: the file changes once between read and save; the retry re-reads and keeps both.
    [Fact]
    public void Capture_ChangedOnceBetweenReadAndSave_RetriesAndKeepsOtherWrite()
    {
        File.WriteAllText(InboxPath, "# inbox\n");
        int calls = 0;

        CaptureResult result = Inbox.Capture(store, "mine", Now, () =>
        {
            calls++;
            if (calls == 1)
            {
                File.AppendAllText(InboxPath, "- theirs\n");
            }
        });

        Assert.Equal(CaptureResult.Captured, result);
        Assert.Equal(2, calls);
        Assert.Equal("# inbox\n- theirs\n- 2026-11-07 14:05 mine\n", File.ReadAllText(InboxPath));
    }

    // AC-3: the file changes on every attempt; nothing of ours is written and the other writer's bytes stay.
    [Fact]
    public void Capture_ChangedEveryTime_GivesUpWithOtherWritersBytes()
    {
        File.WriteAllText(InboxPath, "# inbox\n");
        int calls = 0;

        CaptureResult result = Inbox.Capture(store, "mine", Now, () =>
        {
            calls++;
            File.AppendAllText(InboxPath, $"- theirs {calls}\n");
        });

        Assert.Equal(CaptureResult.ChangedOnDisk, result);
        Assert.Equal(Inbox.MaxRetries + 1, calls);
        string expected = "# inbox\n" + string.Concat(Enumerable.Range(1, calls).Select(i => $"- theirs {i}\n"));
        Assert.Equal(expected, File.ReadAllText(InboxPath));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    // AC-3: a missing inbox created by another writer between the check and the create is appended to, not replaced.
    [Fact]
    public void Capture_InboxCreatedInBetween_AppendsToTheirs()
    {
        int calls = 0;

        CaptureResult result = Inbox.Capture(store, "mine", Now, () =>
        {
            calls++;
            if (calls == 1)
            {
                File.WriteAllText(InboxPath, "# theirs\n");
            }
        });

        Assert.Equal(CaptureResult.Captured, result);
        Assert.Equal("# theirs\n- 2026-11-07 14:05 mine\n", File.ReadAllText(InboxPath));
    }
}
