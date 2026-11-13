using System.Text;

namespace Context.Tests;

// 140 AC-1 to AC-5: DesktopActions.CaptureText, against real files and the fixture's fixed local clock.
public sealed class CaptureActionsTests : IDisposable
{
    private const string Stamp = "2026-10-29 10:05";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-1: no inbox note: it is created with the title line and the dated line, and the snapshot lists it.
    [Fact]
    public async Task CaptureText_NoInbox_CreatesItWithTheLine()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CaptureText("call Anna");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(ActionReason.None, result.Reason);
        Assert.Equal("Captured.", result.Text);
        Assert.Equal(
            Encoding.UTF8.GetBytes($"# inbox\n- {Stamp} call Anna\n"),
            File.ReadAllBytes(Path.Combine(data.Root, "inbox.md")));
        Assert.Equal(["inbox"], data.State!.Snapshot!.Notes.Select(n => n.Name).ToArray());
    }

    // AC-2: an existing inbox (any case) keeps its bytes as a prefix; a missing final newline is added first; no
    // other file changes.
    [Theory]
    [InlineData("# Inbox\n- old", "# Inbox\n- old\n")]
    [InlineData("# Inbox\r\n- old\r\n", "# Inbox\r\n- old\r\n")]
    [InlineData("", "")]
    public async Task CaptureText_ExistingInbox_AppendsAndTouchesNothingElse(string earlier, string prefix)
    {
        data.WriteNote("Inbox", earlier);
        data.WriteNote("Plan", "# Plan\n");
        data.AddTodo("Buy milk", DesktopFixture.Today);
        data.WriteCalendar("a.ics", DesktopFixture.Calendar());
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Others();

        ActionResult result = await data.Actions!.CaptureText("call Anna");

        Assert.Equal(ActionKind.Done, result.Kind);
        byte[] bytes = File.ReadAllBytes(Path.Combine(data.Root, "Inbox.md"));
        byte[] earlierBytes = Encoding.UTF8.GetBytes(earlier);
        Assert.Equal(earlierBytes, bytes[..earlierBytes.Length]);
        string ending = earlier.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        Assert.Equal(Encoding.UTF8.GetBytes($"{prefix}- {Stamp} call Anna{ending}"), bytes);
        Assert.Equal(before, Others());
        Assert.DoesNotContain(Directory.GetFiles(data.Root), f => Path.GetFileName(f) == "inbox.md");
    }

    // AC-3: control characters and line breaks become spaces and the text is trimmed: one capture is one line.
    [Fact]
    public async Task CaptureText_MultiLineText_IsOneLine()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CaptureText("  first\r\n# heading\n- second\tthird\u0007 \n");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(
            $"# inbox\n- {Stamp} first  # heading - second third\n",
            File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
    }

    // AC-3: empty after cleaning is refused and writes nothing.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \r\n\t\u0001 ")]
    public async Task CaptureText_Empty_RefusedNothingWritten(string text)
    {
        data.WriteNote("inbox", "# inbox\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.CaptureText(text);

        AssertNot(ActionKind.Refused, ActionReason.Invalid, "Type something to capture.", result);
        Assert.Equal(before, Files());
    }

    // AC-3: 1001 characters after cleaning are refused; exactly 1000 (with spaces around that are trimmed) are captured.
    [Fact]
    public async Task CaptureText_Length_1000CapturedAnd1001Refused()
    {
        data.WriteNote("inbox", "# inbox\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult tooLong = await data.Actions!.CaptureText(new string('x', 1001));

        AssertNot(ActionKind.Refused, ActionReason.Invalid, "A capture can be at most 1000 characters.", tooLong);
        Assert.Equal(before, Files());

        ActionResult fits = await data.Actions.CaptureText("  " + new string('x', 1000) + "\n");

        Assert.Equal(ActionKind.Done, fits.Kind);
        Assert.Equal($"# inbox\n- {Stamp} {new string('x', 1000)}\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
    }

    // AC-4: a sensitive inbox (any case) is refused without asking the key store; nothing is written.
    [Theory]
    [InlineData("inbox")]
    [InlineData("Inbox")]
    public async Task CaptureText_SensitiveInbox_RefusedWithoutAgree(string name)
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive(name, "# inbox\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.CaptureText("call Anna");

        AssertNot(ActionKind.Refused, ActionReason.Exists, "inbox is sensitive. Nothing was captured.", result);
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(before, Files());
    }

    // AC-4: an inbox that would grow past 5 MB is left alone.
    [Fact]
    public async Task CaptureText_WouldGrowPast5Mb_RefusedNothingWritten()
    {
        data.WriteNote("inbox", "# inbox\n" + new string('x', (int)NoteStore.MaxEditBytes - 20) + "\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.CaptureText("call Anna");

        AssertNot(ActionKind.Refused, ActionReason.Invalid, "inbox is too large. Nothing was captured.", result);
        Assert.Equal(before, Files());
    }

    // AC-4: an inbox that changes on disk before every save is never overwritten: the last writer's text stays.
    [Fact]
    public async Task CaptureText_ChangingOnEveryAttempt_RefusedTheirTextStays()
    {
        data.WriteNote("inbox", "# inbox\n");
        await data.RefreshAsync();
        int attempts = 0;
        data.Actions!.BeforeCaptureSave = () => data.WriteNote("inbox", $"# inbox\n- theirs {++attempts}\n");

        ActionResult result = await data.Actions.CaptureText("call Anna");

        AssertNot(
            ActionKind.Refused, ActionReason.ChangedOnDisk, "inbox kept changing on disk. Nothing was captured.", result);
        Assert.Equal(Inbox.MaxRetries + 1, attempts);
        Assert.Equal($"# inbox\n- theirs {attempts}\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
        Assert.Equal(["inbox.md"], NoteFiles());

        // One change in between is retried: the line lands after their text.
        data.Actions.BeforeCaptureSave = () =>
        {
            data.Actions.BeforeCaptureSave = null;
            data.WriteNote("inbox", "# inbox\n- theirs again\n");
        };
        ActionResult retried = await data.Actions.CaptureText("call Anna");

        Assert.Equal(ActionKind.Done, retried.Kind);
        Assert.Equal(
            $"# inbox\n- theirs again\n- {Stamp} call Anna\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
    }

    // AC-4: an inbox that can't be read as a note (a folder named inbox.md) gives the fixed text, never the
    // exception's, and nothing is written.
    [Fact]
    public async Task CaptureText_InboxIsAFolder_FailedNothingWritten()
    {
        Directory.CreateDirectory(Path.Combine(data.Root, "inbox.md"));
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.CaptureText("call Anna");

        AssertNot(ActionKind.Failed, ActionReason.WriteFailed, "Can't write inbox. Nothing was captured.", result);
        Assert.Equal(before, Files());
        Assert.True(Directory.Exists(Path.Combine(data.Root, "inbox.md")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(data.Root, "inbox.md")));
    }

    // AC-4: an inbox already over 5 MB can't be read for a capture: the same fixed text.
    [Fact]
    public async Task CaptureText_InboxOver5Mb_FailedNothingWritten()
    {
        data.WriteNote("inbox", new string('x', (int)NoteStore.MaxEditBytes + 1));
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.CaptureText("call Anna");

        AssertNot(ActionKind.Failed, ActionReason.WriteFailed, "Can't write inbox. Nothing was captured.", result);
        Assert.Equal(before, Files());
    }

    // AC-4: a capture made while another action runs gets the busy text and writes nothing; the first one finishes.
    [Fact]
    public async Task CaptureText_WhileRunning_SecondRefusedBusy()
    {
        await data.RefreshAsync();
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        Task<ActionResult> first = data.Actions.CaptureText("first");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        ActionResult second = await data.Actions.CaptureText("second");
        release.Set();
        ActionResult firstResult = await first;

        AssertNot(ActionKind.Refused, ActionReason.Busy, DesktopActions.BusyText, second);
        Assert.Equal(ActionKind.Done, firstResult.Kind);
        Assert.Equal($"# inbox\n- {Stamp} first\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
    }

    // AC-5: the state is refreshed after a capture, so its tags link the inbox right away.
    [Fact]
    public async Task CaptureText_WithTags_StateListsThem()
    {
        await data.RefreshAsync();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.CaptureText("idea #work #[Big Plan]");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(builds + 1, data.State.Builds);
        Assert.Equal(["big plan", "work"], (await data.State.Tags()).Tags.Select(t => t.Tag).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["inbox"], await data.State.NotesWithTag("work"));
        Assert.Equal(["inbox"], await data.State.NotesWithTag("big plan"));
    }

    // The time stamp is the injected local time when the capture runs, to the minute.
    [Fact]
    public async Task CaptureText_UsesTheInjectedLocalClock()
    {
        await data.RefreshAsync();
        data.LocalNow = new DateTime(2027, 1, 2, 23, 59, 58, DateTimeKind.Local);

        await data.Actions!.CaptureText("late");

        Assert.Equal("# inbox\n- 2027-01-02 23:59 late\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
    }

    private static void AssertNot(ActionKind kind, ActionReason reason, string text, ActionResult result)
    {
        Assert.Equal(kind, result.Kind);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(text, result.Text);
    }

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName);

    // Every file but the index and the inbox note.
    private SortedDictionary<string, string> Others() => FileSnapshot.Take(data.Root, TagIndex.FileName, "Inbox.md");

    private string[] NoteFiles() =>
        Directory.GetFiles(data.Root).Select(Path.GetFileName).Where(f => f != TagIndex.FileName).Order().ToArray()!;
}
