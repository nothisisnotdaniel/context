using System.Text;

namespace Context.Tests;

// 268: merging one plain note into another through DesktopActions.MergeNotes and DesktopState.MergeLinkCount, against
// real files, the fixture's fixed clock (it fixes the history ids) and a key store in memory.
public sealed class MergeNotesActionsTests : IDisposable
{
    private const string Id = "20261029T090000000Z";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-5: the count is the other plain notes whose links change; it writes nothing.
    [Fact]
    public async Task MergeLinkCount_CountsOtherNotes_WritesNothing()
    {
        WriteMergeNotes();
        data.WriteNote("B", "[[Other]]\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        Assert.Equal(1, await data.State!.MergeLinkCount("Plan", "Plan old"));
        Assert.Equal(0, await data.State.MergeLinkCount("Plan", "plan"));
        Assert.Equal(0, await data.State.MergeLinkCount("Plan", "Missing"));
        Assert.Equal(before, Files());
    }

    // AC-6, AC-7: the target keeps its CRLF and BOM and gets the source's text (BOM dropped, self-links rewritten);
    // the source's last bytes and the target's previous bytes are versions; other notes' links follow; one refresh.
    [Fact]
    public async Task MergeNotes_Confirmed_AppendsKeepsHistoryAndRewritesLinks()
    {
        WriteMergeNotes();
        await data.RefreshAsync();
        string source = Read("Plan old.md");
        string target = Read("Plan.md");
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.MergeNotes("Plan", "Plan old", new Confirmation());

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Equal("Merged Plan old into Plan. Updated links in 1 note.", result.Text);
        Assert.Equal("Plan", result.Name);
        Assert.Equal(builds + 1, data.State.Builds);
        Assert.Equal("﻿# Plan\r\nbody [[Plan old]]\r\n\r\n# Plan old\r\nold text [[Plan]]\r\n".Replace("[[Plan old]]", "[[Plan]]"), Read("Plan.md"));
        Assert.False(File.Exists(Path.Combine(data.Root, "Plan old.md")));
        Assert.Equal(source, Read(".history/Plan old/" + Id + ".md"));
        Assert.Equal(target, Read(".history/Plan/" + Id + ".md"));
        Assert.Equal("see [[Plan#H|alias]] and [[Plan.md]] and [[Other]]\n", Read("A.md"));
        Assert.Equal("see [[plan old#H|alias]] and [[Plan old.md]] and [[Other]]\n", Read(".history/A/" + Id + ".md"));
        Assert.Equal(0, data.Keys.Agrees);
    }

    // AC-6: with no other note to change the text says only what was merged.
    [Fact]
    public async Task MergeNotes_NoOtherLinks_PlainText()
    {
        data.WriteNote("Target", "t\n\n\n");
        data.WriteNote("Source", "s\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.MergeNotes("Target", "Source", new Confirmation());

        Assert.Equal((ActionKind.Done, ActionReason.None, "Merged Source into Target."), (result.Kind, result.Reason, result.Text));
        Assert.Equal("t\n\ns\n", Read("Target.md"));
        Assert.Equal("s\n", Read(".history/Source/" + Id + ".md"));
    }

    // AC-7: a note that changed on disk once is read again and updated on the second try.
    [Fact]
    public async Task MergeNotes_LinkChangedOnDiskOnce_ReadsAgain()
    {
        WriteMergeNotes();
        await data.RefreshAsync();
        int calls = 0;
        data.Actions!.BeforeLinkSave = name =>
        {
            if (calls++ == 0)
            {
                File.WriteAllText(Path.Combine(data.Root, "A.md"), "[[Plan old]] typed\n");
            }
        };

        ActionResult result = await data.Actions.MergeNotes("Plan", "Plan old", new Confirmation());

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Equal("[[Plan]] typed\n", Read("A.md"));
    }

    // AC-7: notes whose links can't be updated are named, at most 3, the rest counted; the merge is still done.
    [Fact]
    public async Task MergeNotes_HistoryFailsForSomeNotes_LinksLeft()
    {
        data.WriteNote("New", "n\n");
        data.WriteNote("Old", "o\n");
        data.WriteNote("Work", "w\n");
        foreach (string name in new[] { "A", "B", "C", "D", "E" })
        {
            data.WriteFile(Path.Combine("Work", name + ".md"), "[[Old]]\n");
        }

        data.WriteFile(Path.Combine("Work", ".history"), "in the way");
        data.WriteNote("Top", "[[old]]\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.MergeNotes("New", "Old", new Confirmation());

        Assert.Equal((ActionKind.Done, ActionReason.LinksLeft), (result.Kind, result.Reason));
        Assert.Equal(
            "Merged Old into New, but links in 5 notes couldn't be updated: Work/A, Work/B, Work/C and 2 more.",
            result.Text);
        Assert.Equal("New", result.Name);
        Assert.Equal("[[New]]\n", Read("Top.md"));
        Assert.Equal("[[Old]]\n", Read("Work/A.md"));
        Assert.Equal("n\n\no\n", Read("New.md"));
    }

    // AC-7: a target name links can't be written with is refused before anything is written.
    [Fact]
    public async Task MergeNotes_LinksCantBeWritten_RefusedAndNothingWritten()
    {
        data.WriteNote("Pro#ject", "p\n");
        data.WriteNote("Old", "o\n");
        data.WriteNote("A", "[[Old]]\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.MergeNotes("Pro#ject", "Old", new Confirmation());

        Assert.Equal((ActionKind.Refused, ActionReason.Invalid), (result.Kind, result.Reason));
        Assert.Equal("Links to Old can't be written with the name Pro#ject. Nothing was merged.", result.Text);
        Assert.Equal(before, Files());
    }

    // AC-8: no confirmation, a gone note and the same note are refused; nothing is written.
    [Fact]
    public async Task MergeNotes_NotConfirmedMissingOrSame_RefusedAndNothingWritten()
    {
        WriteMergeNotes();
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult unconfirmed = await data.Actions!.MergeNotes("Plan", "Plan old", null);
        ActionResult noTarget = await data.Actions.MergeNotes("Gone", "Plan old", new Confirmation());
        ActionResult noSource = await data.Actions.MergeNotes("Plan", "Gone", new Confirmation());
        ActionResult same = await data.Actions.MergeNotes("Plan", "plan", new Confirmation());

        Assert.Equal((ActionKind.Refused, ActionReason.NotConfirmed, DesktopActions.NotConfirmedText), (unconfirmed.Kind, unconfirmed.Reason, unconfirmed.Text));
        Assert.Equal((ActionKind.Refused, ActionReason.Missing, "Gone no longer exists. Nothing was merged."), (noTarget.Kind, noTarget.Reason, noTarget.Text));
        Assert.Equal((ActionKind.Refused, ActionReason.Missing, "Gone no longer exists. Nothing was merged."), (noSource.Kind, noSource.Reason, noSource.Text));
        Assert.Equal((ActionKind.Refused, ActionReason.Invalid, DesktopActions.MergeSameText), (same.Kind, same.Reason, same.Text));
        Assert.Equal(before, Files());
    }

    // AC-8: either note sensitive is refused without any key-store call.
    [Fact]
    public async Task MergeNotes_Sensitive_LockedNoKeyCall()
    {
        data.Keys.Dispose();
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Secret", "secret\n");
        data.WriteNote("Plain", "p\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult into = await data.Actions!.MergeNotes("Secret", "Plain", new Confirmation());
        ActionResult from = await data.Actions.MergeNotes("Plain", "Secret", new Confirmation());

        Assert.Equal((ActionKind.Refused, ActionReason.Locked, "Sensitive notes can't be merged."), (into.Kind, into.Reason, into.Text));
        Assert.Equal((ActionKind.Refused, ActionReason.Locked, "Sensitive notes can't be merged."), (from.Kind, from.Reason, from.Text));
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(before, Files());
    }

    // AC-8: a source with subpages is refused.
    [Fact]
    public async Task MergeNotes_SourceWithSubpages_Refused()
    {
        data.WriteNote("New", "n\n");
        data.WriteNote("Old", "o\n");
        data.WriteFile(Path.Combine("Old", "Child.md"), "c\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.MergeNotes("New", "Old", new Confirmation());

        Assert.Equal((ActionKind.Refused, ActionReason.Invalid), (result.Kind, result.Reason));
        Assert.Equal("Old has subpages, so it can't be merged. Nothing was merged.", result.Text);
        Assert.Equal(before, Files());
    }

    // AC-8: a source naming an attachment is refused when the notes are in different folders, merged in the same one.
    [Fact]
    public async Task MergeNotes_Attachments_RefusedAcrossFoldersOnly()
    {
        data.WriteNote("New", "n\n");
        data.WriteNote("Work", "w\n");
        data.WriteFile(Path.Combine("Work", "Old.md"), "![x](.attachments/pic.png)\n");
        data.WriteNote("Same", "![y](.attachments/pic.png)\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult across = await data.Actions!.MergeNotes("New", "Work/Old", new Confirmation());

        Assert.Equal((ActionKind.Refused, ActionReason.Invalid), (across.Kind, across.Reason));
        Assert.Equal("Work/Old has attachments in another folder, so it can't be merged. Nothing was merged.", across.Text);
        Assert.Equal(before, Files());

        ActionResult same = await data.Actions.MergeNotes("New", "Same", new Confirmation());

        Assert.Equal(ActionKind.Done, same.Kind);
        Assert.Equal("n\n\n![y](.attachments/pic.png)\n", Read("New.md"));
    }

    // AC-8: a merged text over the edit limit is refused before anything is written.
    [Fact]
    public async Task MergeNotes_TooBig_Refused()
    {
        data.WriteNote("Big", new string('x', (int)NoteStore.MaxEditBytes - 10) + "\n");
        data.WriteNote("Old", "0123456789\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.MergeNotes("Big", "Old", new Confirmation());

        Assert.Equal((ActionKind.Refused, ActionReason.Invalid), (result.Kind, result.Reason));
        Assert.Equal("The merged note would be too big. Nothing was merged.", result.Text);
        Assert.Equal(before, Files());
    }

    // AC-8: a failing Keep of the source is Failed and leaves both notes as they were.
    [Fact]
    public async Task MergeNotes_KeepFails_FailedNothingChanged()
    {
        data.WriteNote("New", "n\n");
        data.WriteNote("Old", "o\n");
        data.WriteFile(".history", "in the way");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.MergeNotes("New", "Old", new Confirmation());

        Assert.Equal((ActionKind.Failed, ActionReason.WriteFailed), (result.Kind, result.Reason));
        Assert.Equal("Can't keep the history of Old. Nothing was merged.", result.Text);
        Assert.Equal(before, Files());
    }

    // AC-5, AC-6, AC-7: the texts.
    [Fact]
    public void Texts()
    {
        Assert.Equal(
            "Merge S into T? Its text is added to T, links in 2 notes are updated and S stays in its history.",
            DesktopActions.MergeQuestion("S", "T", 2));
        Assert.Equal(
            "Merge S into T? Its text is added to T, links in 1 note are updated and S stays in its history.",
            DesktopActions.MergeQuestion("S", "T", 1));
        Assert.Equal("Merged S into T.", DesktopActions.MergedText("S", "T"));
        Assert.Equal("Merged S into T. Updated links in 3 notes.", DesktopActions.MergedLinksText("S", "T", 3));
        Assert.Equal(
            "Merged S into T, but links in 3 notes couldn't be updated: a, b, c.",
            DesktopActions.MergeLinksLeftText("S", "T", ["a", "b", "c"]));
    }

    // Plan (BOM, CRLF, trailing blank lines, a link to Plan old) and Plan old (BOM, self-link); A links to Plan old
    // with a heading and alias, as .md, and to another note.
    private void WriteMergeNotes()
    {
        data.WriteNote("Plan", "﻿# Plan\r\nbody [[Plan old]]\r\n\r\n");
        data.WriteNote("Plan old", "﻿# Plan old\nold text [[Plan old]]\n");
        data.WriteNote("A", "see [[plan old#H|alias]] and [[Plan old.md]] and [[Other]]\n");
        data.WriteNote("Other", "o\n");
    }

    // The text as stored, a BOM included.
    private string Read(string relative) =>
        Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine([data.Root, .. relative.Split('/')])));

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);
}
