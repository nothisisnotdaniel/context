using System.Text;

namespace Context.Tests;

// 179: InboxTriage reads the inbox lines into a queue and removes or snoozes one line, against real files.
public sealed class InboxTriageTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 29);

    private readonly string parent;
    private readonly string root;
    private readonly NoteStore store;

    public InboxTriageTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-triage-" + Guid.NewGuid().ToString("N"));
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

    // AC-1: only "- " lines are items; the stamp and a final snooze token are taken off the text.
    [Fact]
    public void Items_ParsesStampAndSnooze()
    {
        string text = "# inbox\n\nplain line\n-no space\n- 2026-10-28 09:15 call Anna\n- just text\n- 2026-10-20 08:00 pay bill snooze:2026-10-29\n";

        InboxQueue queue = InboxTriage.Items(text, Today);

        Assert.Equal(0, queue.SnoozedCount);
        Assert.Equal(
            ["- 2026-10-20 08:00 pay bill snooze:2026-10-29", "- 2026-10-28 09:15 call Anna", "- just text"],
            queue.Items.Select(i => i.Line).ToArray());
        InboxItem due = queue.Items[0];
        Assert.Equal("pay bill", due.Text);
        Assert.Equal(new DateTime(2026, 10, 20, 8, 0, 0), due.Captured);
        Assert.Equal(new DateOnly(2026, 10, 29), due.Snooze);
        Assert.Equal("call Anna", queue.Items[1].Text);
        Assert.Equal(new DateTime(2026, 10, 28, 9, 15, 0), queue.Items[1].Captured);
        Assert.Null(queue.Items[1].Snooze);
        Assert.Equal("just text", queue.Items[2].Text);
        Assert.Null(queue.Items[2].Captured);
        Assert.Null(queue.Items[2].Snooze);
    }

    // AC-1: an invalid stamp or snooze day stays in the text; a token that is not last is text too.
    [Theory]
    [InlineData("- 2026-13-01 10:00 text", "2026-13-01 10:00 text")]
    [InlineData("- 2026-10-28 25:00 text", "2026-10-28 25:00 text")]
    [InlineData("- 2026-10-28 10:00", "2026-10-28 10:00")]
    [InlineData("- text snooze:2026-02-30", "text snooze:2026-02-30")]
    [InlineData("- text snooze:tomorrow", "text snooze:tomorrow")]
    [InlineData("- snooze:2026-10-01 text", "snooze:2026-10-01 text")]
    [InlineData("- text snooze:2026-10-01x", "text snooze:2026-10-01x")]
    public void Parse_InvalidParts_StayInTheText(string line, string text)
    {
        InboxItem? item = InboxTriage.Parse(line);

        Assert.NotNull(item);
        Assert.Equal(text, item.Text);
        Assert.Null(item.Captured);
        Assert.Null(item.Snooze);
        Assert.Equal(line, item.Line);
    }

    // AC-1: an item may be empty after its stamp and snooze token.
    [Fact]
    public void Parse_StampAndSnoozeOnly_EmptyText()
    {
        InboxItem? item = InboxTriage.Parse("- 2026-10-28 10:00  snooze:2026-11-01");

        Assert.NotNull(item);
        Assert.Equal(string.Empty, item.Text);
        Assert.Equal(new DateOnly(2026, 11, 1), item.Snooze);
    }

    // AC-1: CRLF line ends and a BOM don't change the items.
    [Fact]
    public void Items_CrlfAndBom_SameItems()
    {
        InboxQueue queue = InboxTriage.Items("﻿# inbox\r\n- one\r\n- two snooze:2026-10-28\r\n", Today);

        Assert.Equal(["- two snooze:2026-10-28", "- one"], queue.Items.Select(i => i.Line).ToArray());
        Assert.Equal("two", queue.Items[0].Text);
    }

    // AC-2: a line snoozed past today is left out and counted; due snoozed lines come first by day, then file order.
    [Fact]
    public void Items_Order_DueSnoozedFirstThenFileOrder()
    {
        string text = "# inbox\n"
            + "- a\n"
            + "- b snooze:2026-10-29\n"
            + "- c snooze:2026-10-30\n"
            + "- d snooze:2026-10-01\n"
            + "- e\n"
            + "- f snooze:2026-10-29\n"
            + "- g snooze:2027-01-01\n";

        InboxQueue queue = InboxTriage.Items(text, Today);

        Assert.Equal(["d", "b", "f", "a", "e"], queue.Items.Select(i => i.Text).ToArray());
        Assert.Equal(2, queue.SnoozedCount);
    }

    // AC-2: no items at all.
    [Theory]
    [InlineData("")]
    [InlineData("# inbox\n")]
    [InlineData("# inbox\nnotes without dashes\n")]
    public void Items_NoLines_Empty(string text)
    {
        InboxQueue queue = InboxTriage.Items(text, Today);

        Assert.Empty(queue.Items);
        Assert.Equal(0, queue.SnoozedCount);
    }

    // AC-11: the snooze token is added, or an old one replaced.
    [Theory]
    [InlineData("- call Anna", "- call Anna snooze:2026-11-05")]
    [InlineData("- call Anna snooze:2026-10-01", "- call Anna snooze:2026-11-05")]
    [InlineData("- call Anna snooze:bad", "- call Anna snooze:bad snooze:2026-11-05")]
    public void WithSnooze_AddsOrReplacesTheToken(string line, string expected)
    {
        Assert.Equal(expected, InboxTriage.WithSnooze(line, new DateOnly(2026, 11, 5)));
    }

    // AC-11 and AC-12: the line is rewritten in place; the other lines, CRLF and BOM stay byte-identical.
    [Fact]
    public void Snooze_RewritesInPlace_KeepsEverythingElse()
    {
        byte[] bom = Encoding.UTF8.GetPreamble();
        File.WriteAllBytes(InboxPath, [.. bom, .. Encoding.UTF8.GetBytes("# inbox\r\n- one\r\n- two snooze:2026-10-01\r\n- three\r\n")]);

        TriageResult result = InboxTriage.Snooze(store, "- two snooze:2026-10-01", new DateOnly(2026, 11, 5));

        Assert.Equal(TriageResult.Done, result);
        Assert.Equal(
            [.. bom, .. Encoding.UTF8.GetBytes("# inbox\r\n- one\r\n- two snooze:2026-11-05\r\n- three\r\n")],
            File.ReadAllBytes(InboxPath));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    // AC-10 and AC-12: only the first equal line is removed, with its line end; the rest stays byte-identical.
    [Theory]
    [InlineData("# inbox\n- a\n- b\n- a\n", "# inbox\n- b\n- a\n")]
    [InlineData("# inbox\r\n- a\r\n- b\r\n", "# inbox\r\n- b\r\n")]
    [InlineData("# inbox\n- b\n- a", "# inbox\n- b\n")]
    [InlineData("# inbox\n- ab\n- a\n", "# inbox\n- ab\n")]
    public void Remove_FirstEqualLine_KeepsTheRest(string before, string after)
    {
        File.WriteAllText(InboxPath, before);

        Assert.Equal(TriageResult.Done, InboxTriage.Remove(store, "- a"));

        Assert.Equal(after, File.ReadAllText(InboxPath));
    }

    // AC-12: a BOM survives a remove.
    [Fact]
    public void Remove_Bom_Kept()
    {
        byte[] bom = Encoding.UTF8.GetPreamble();
        File.WriteAllBytes(InboxPath, [.. bom, .. Encoding.UTF8.GetBytes("# inbox\n- a\n- b\n")]);

        Assert.Equal(TriageResult.Done, InboxTriage.Remove(store, "- a"));

        Assert.Equal([.. bom, .. Encoding.UTF8.GetBytes("# inbox\n- b\n")], File.ReadAllBytes(InboxPath));
    }

    // AC-9: a line that is no longer there, or a missing inbox, is Gone and nothing is written or created.
    [Fact]
    public void Remove_LineOrInboxGone_WritesNothing()
    {
        Assert.Equal(TriageResult.Gone, InboxTriage.Remove(store, "- a"));
        Assert.Equal(TriageResult.Gone, InboxTriage.Snooze(store, "- a", new DateOnly(2026, 11, 5)));
        Assert.False(File.Exists(InboxPath));

        File.WriteAllText(InboxPath, "# inbox\n- a b\n");
        Assert.Equal(TriageResult.Gone, InboxTriage.Remove(store, "- a"));
        Assert.Equal("# inbox\n- a b\n", File.ReadAllText(InboxPath));
    }

    // The inbox changes once in between: it is read again and the line removed from the new text.
    [Fact]
    public void Remove_ChangedOnce_ReadsAgain()
    {
        File.WriteAllText(InboxPath, "# inbox\n- a\n");
        int calls = 0;

        TriageResult result = InboxTriage.Remove(store, "- a", () =>
        {
            calls++;
            if (calls == 1)
            {
                File.AppendAllText(InboxPath, "- theirs\n");
            }
        });

        Assert.Equal(TriageResult.Done, result);
        Assert.Equal(2, calls);
        Assert.Equal("# inbox\n- theirs\n", File.ReadAllText(InboxPath));
    }

    // The inbox changes on every attempt: nothing of ours is written and the other writer's bytes stay.
    [Fact]
    public void Snooze_ChangedEveryTime_GivesUp()
    {
        File.WriteAllText(InboxPath, "# inbox\n- a\n");
        int calls = 0;

        TriageResult result = InboxTriage.Snooze(store, "- a", new DateOnly(2026, 11, 5), () =>
        {
            calls++;
            File.AppendAllText(InboxPath, $"- theirs {calls}\n");
        });

        Assert.Equal(TriageResult.ChangedOnDisk, result);
        Assert.Equal(Inbox.MaxRetries + 1, calls);
        string expected = "# inbox\n- a\n" + string.Concat(Enumerable.Range(1, calls).Select(i => $"- theirs {i}\n"));
        Assert.Equal(expected, File.ReadAllText(InboxPath));
    }

    // A snooze that would grow the inbox past the edit limit is refused.
    [Fact]
    public void Snooze_TooLarge_WritesNothing()
    {
        string big = "# inbox\n- a\n" + new string('x', (int)NoteStore.MaxEditBytes - 20) + "\n";
        File.WriteAllText(InboxPath, big);

        Assert.Equal(TriageResult.TooLarge, InboxTriage.Snooze(store, "- a", new DateOnly(2026, 11, 5)));

        Assert.Equal(big, File.ReadAllText(InboxPath));
    }
}
