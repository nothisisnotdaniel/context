using System.Text;

namespace Context.Tests;

// 206: renaming a note rewrites the [[links]] to it in the other plain notes, through DesktopActions and DesktopState,
// against real files, the fixture's fixed clock (it fixes the history ids) and a key store in memory.
public sealed class RenameLinkActionsTests : IDisposable
{
    private const string Id = "20261029T090000000Z";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-3: the count is the plain notes whose text would change, the renamed note's self-link included; it writes
    // nothing.
    [Fact]
    public async Task LinkUpdateCount_CountsChangedPlainNotes_WritesNothing()
    {
        WriteLinkedNotes();
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        int count = await data.State!.LinkUpdateCount("Plan", "Project");

        Assert.Equal(3, count);
        Assert.Equal(before, Files());
    }

    // AC-3: the same name or a case-only rename counts 0, and a case-only rename rewrites no link.
    [Fact]
    public async Task LinkUpdateCount_SameOrCaseOnly_Zero_CaseOnlyRewritesNothing()
    {
        WriteLinkedNotes();
        await data.RefreshAsync();

        Assert.Equal(0, await data.State!.LinkUpdateCount("Plan", "Plan"));
        Assert.Equal(0, await data.State.LinkUpdateCount("Plan", " plan "));
        Assert.Equal(0, await data.State.LinkUpdateCount("Missing", "Project"));
        Assert.Equal(0, await data.State.LinkUpdateCount("Plan", "bad/title"));

        ActionResult result = await data.Actions!.RenameNote("Plan", "plan", null);

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Equal("Renamed to plan.", result.Text);
        Assert.Equal("see [[Plan]] and [[Other]]\n", Read("A.md"));
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".history")));
    }

    // AC-4: links to change and no confirmation: refused, and nothing on disk changes, .history included.
    [Fact]
    public async Task RenameNote_LinksNotConfirmed_RefusedAndNothingWritten()
    {
        WriteLinkedNotes();
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.RenameNote("Plan", "Project", null);

        Assert.Equal((ActionKind.Refused, ActionReason.NotConfirmed), (result.Kind, result.Reason));
        Assert.Equal(DesktopActions.NotConfirmedText, result.Text);
        Assert.Equal(before, Files());
    }

    // AC-1, AC-2, AC-5, AC-7: confirmed, the note is renamed, every linking note is rewritten and keeps its previous
    // bytes as a version; code, other notes and sensitive notes stay; the state refreshes once.
    [Fact]
    public async Task RenameNote_Confirmed_RewritesLinksAndKeepsVersions()
    {
        WriteLinkedNotes();
        await data.RefreshAsync();
        string code = Files()["C.md"];
        string other = Files()["Other.md"];
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.RenameNote("Plan", "Project", new Confirmation());

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Equal("Renamed to Project. Updated links in 3 notes.", result.Text);
        Assert.Equal("Project", result.Name);
        Assert.Equal(builds + 1, data.State.Builds);
        Assert.False(File.Exists(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("# Project\nsee [[Project#Next steps]]\n", Read("Project.md"));
        Assert.Equal("see [[Project]] and [[Other]]\n", Read("A.md"));
        Assert.Equal("\uFEFFsee [[ Project/Child | the child ]]\r\nand [[#Intro]]\r\n", Read("B.md"));
        Assert.Equal("see [[Plan]] and [[Other]]\n", Read(".history/A/" + Id + ".md"));
        Assert.Equal("\uFEFFsee [[ plan/child | the child ]]\r\nand [[#Intro]]\r\n", Read(".history/B/" + Id + ".md"));
        Assert.Equal("# Plan\nsee [[Plan#Next steps]]\n", Read(".history/Project/" + Id + ".md"));
        Assert.Equal(code, Files()["C.md"]);
        Assert.Equal(other, Files()["Other.md"]);
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".history", "C")));
        Assert.Equal("child\n", Read("Project/Child.md"));
    }

    // 353 AC-7: the renamed note's title line and its self-link are rewritten in one save (one version); the title
    // line is no link update, so the count stays 3.
    [Fact]
    public async Task RenameNote_SelfLinkAndTitle_OneSaveOneVersion()
    {
        WriteLinkedNotes();
        await data.RefreshAsync();
        Assert.Equal(3, await data.State!.LinkUpdateCount("Plan", "Project"));

        ActionResult result = await data.Actions!.RenameNote("Plan", "Project", new Confirmation());

        Assert.Equal("Renamed to Project. Updated links in 3 notes.", result.Text);
        Assert.Equal(Encoding.UTF8.GetBytes("# Project\nsee [[Project#Next steps]]\n"), File.ReadAllBytes(Path.Combine(data.Root, "Project.md")));
        HistoryView history = await data.State.History("Project");
        NoteVersion version = Assert.Single(history.Versions);
        Assert.Equal("# Plan\nsee [[Plan#Next steps]]\n", Read(".history/Project/" + version.Id + ".md"));
    }

    // AC-7: one note changed says "1 note"; no note to change renames right away without a confirmation.
    [Fact]
    public async Task RenameNote_OneOrNoNote_Texts()
    {
        data.WriteNote("Plan", "p\n");
        data.WriteNote("Solo", "s\n");
        data.WriteNote("A", "[[Plan]]\n");
        await data.RefreshAsync();

        ActionResult one = await data.Actions!.RenameNote("Plan", "Project", new Confirmation());
        ActionResult none = await data.Actions.RenameNote("Solo", "Alone", null);

        Assert.Equal("Renamed to Project. Updated links in 1 note.", one.Text);
        Assert.Equal((ActionKind.Done, ActionReason.None, "Renamed to Alone.", "Alone"), (none.Kind, none.Reason, none.Text, none.Name));
        Assert.Equal("[[Project]]\n", Read("A.md"));
    }

    // AC-5: a refused rename touches no other note.
    [Fact]
    public async Task RenameNote_Exists_TouchesNoOtherNote()
    {
        WriteLinkedNotes();
        data.WriteNote("Project", "taken\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.RenameNote("Plan", "Project", new Confirmation());

        Assert.Equal((ActionKind.Refused, ActionReason.Exists), (result.Kind, result.Reason));
        Assert.Equal(before, Files());
    }

    // AC-5: changed on disk between read and save, the note is read again; the second try saves the latest text.
    [Fact]
    public async Task RenameNote_ChangedOnDiskOnce_ReadsAgainAndSaves()
    {
        data.WriteNote("Plan", "p\n");
        data.WriteNote("A", "[[Plan]]\n");
        await data.RefreshAsync();
        int calls = 0;
        data.Actions!.BeforeLinkSave = name =>
        {
            if (calls++ == 0)
            {
                File.WriteAllText(Path.Combine(data.Root, "A.md"), "[[Plan]] typed\n");
            }
        };

        ActionResult result = await data.Actions.RenameNote("Plan", "Project", new Confirmation());

        Assert.Equal(2, calls);
        Assert.Equal("Renamed to Project. Updated links in 1 note.", result.Text);
        Assert.Equal("[[Project]] typed\n", Read("A.md"));
        Assert.Equal("[[Plan]] typed\n", Read(".history/A/" + Id + ".md"));
    }

    // AC-5, AC-7: changed on disk on all 3 tries leaves that note, named; the others are still updated.
    [Fact]
    public async Task RenameNote_ChangedOnDiskThreeTimes_LinksLeft()
    {
        data.WriteNote("Plan", "p\n");
        data.WriteNote("A", "[[Plan]]\n");
        data.WriteNote("B", "[[Plan]]\n");
        await data.RefreshAsync();
        int calls = 0;
        data.Actions!.BeforeLinkSave = name =>
        {
            if (name == "A")
            {
                calls++;
                File.WriteAllText(Path.Combine(data.Root, "A.md"), "[[Plan]] " + calls + "\n");
            }
        };

        ActionResult result = await data.Actions.RenameNote("Plan", "Project", new Confirmation());

        Assert.Equal(DesktopActions.LinkUpdateTries, calls);
        Assert.Equal((ActionKind.Done, ActionReason.LinksLeft), (result.Kind, result.Reason));
        Assert.Equal("Renamed to Project, but links in 1 note couldn't be updated: A.", result.Text);
        Assert.Equal("Project", result.Name);
        Assert.Equal("[[Plan]] 3\n", Read("A.md"));
        Assert.Equal("[[Project]]\n", Read("B.md"));
        Assert.True(File.Exists(Path.Combine(data.Root, "Project.md")));
    }

    // AC-7: a history failure (a file in the way of .history) leaves those notes; more than 3 are counted.
    [Fact]
    public async Task RenameNote_HistoryFails_LinksLeft_NamesUpToThree()
    {
        data.WriteNote("Plan", "p\n");
        data.WriteNote("Work", "w\n");
        foreach (string name in new[] { "A", "B", "C", "D", "E" })
        {
            data.WriteFile(Path.Combine("Work", name + ".md"), "[[Plan]]\n");
        }

        data.WriteFile(Path.Combine("Work", ".history"), "in the way");
        data.WriteNote("Top", "[[Plan]]\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.RenameNote("Plan", "Project", new Confirmation());

        Assert.Equal((ActionKind.Done, ActionReason.LinksLeft), (result.Kind, result.Reason));
        Assert.Equal(
            "Renamed to Project, but links in 5 notes couldn't be updated: Work/A, Work/B, Work/C and 2 more.",
            result.Text);
        Assert.Equal("[[Plan]]\n", Read("Work/A.md"));
        Assert.Equal("[[Project]]\n", Read("Top.md"));
        Assert.Equal("[[Plan]]\n", Read(".history/Top/" + Id + ".md"));
    }

    // AC-7: a note the rewrite would push over the edit limit is left and named.
    [Fact]
    public async Task RenameNote_TooLarge_LinksLeft()
    {
        data.WriteNote("Plan", "p\n");
        string big = "[[Plan]]\n" + new string('x', (int)NoteStore.MaxEditBytes - 12) + "\n";
        data.WriteNote("Big", big);
        data.WriteNote("A", "[[Plan]]\n");
        await data.RefreshAsync();
        Assert.Equal(2, await data.State!.LinkUpdateCount("Plan", "Project"));

        ActionResult result = await data.Actions!.RenameNote("Plan", "Project", new Confirmation());

        Assert.Equal((ActionKind.Done, ActionReason.LinksLeft), (result.Kind, result.Reason));
        Assert.Equal("Renamed to Project, but links in 1 note couldn't be updated: Big.", result.Text);
        Assert.Equal(big, Read("Big.md"));
        Assert.Equal("[[Project]]\n", Read("A.md"));
    }

    // AC-6: a new title whose links would not read back is refused before anything is written.
    [Theory]
    [InlineData("Pro#ject")]
    [InlineData("Pro`ject")]
    [InlineData("Pro[ject")]
    public async Task RenameNote_LinksCantBeWritten_RefusedAndNothingWritten(string title)
    {
        WriteLinkedNotes();
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.RenameNote("Plan", title, new Confirmation());

        Assert.Equal((ActionKind.Refused, ActionReason.Invalid), (result.Kind, result.Reason));
        Assert.Equal($"Links to Plan can't be written with the name {title}. Nothing was renamed.", result.Text);
        Assert.Equal(before, Files());
    }

    // AC-6: a link with no change still lets a title with # rename.
    [Fact]
    public async Task RenameNote_HashTitle_NoLinks_Renames()
    {
        data.WriteNote("C", "c\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.RenameNote("C", "C# notes", null);

        Assert.Equal("Renamed to C# notes.", result.Text);
    }

    // AC-8, AC-2: renaming an open sensitive note rewrites plain notes' links to it; sensitive files are never rewritten.
    [Fact]
    public async Task RenameNote_Sensitive_RewritesPlainLinks()
    {
        data.Keys.Dispose();
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Plan", "secret\n");
        data.WriteSensitive("Secret", "[[Plan]]\n");
        data.WriteNote("A", "[[Plan#H]]\n");
        await data.RefreshAsync();
        await data.State!.ReadSensitive("Plan");
        string secret = Files()["Secret.ctxs"];
        Assert.Equal(1, await data.State.LinkUpdateCount("Plan", "Project"));

        ActionResult result = await data.Actions!.RenameNote("Plan", "Project", new Confirmation());

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Equal("Renamed to Project. Updated links in 1 note.", result.Text);
        Assert.Equal("Project", result.Name);
        Assert.Equal("[[Project#H]]\n", Read("A.md"));
        Assert.True(File.Exists(Path.Combine(data.Root, "Project.ctxs")));
        Assert.Equal(secret, Files()["Secret.ctxs"]);
        Assert.Equal("Project", data.Session!.Note!.Name);
    }

    // AC-7, AC-9: the texts.
    [Fact]
    public void Texts()
    {
        Assert.Equal("Renamed to P.", DesktopActions.RenamedText("P"));
        Assert.Equal("Renamed to P. Updated links in 2 notes.", DesktopActions.RenamedLinksText("P", 2));
        Assert.Equal("Rename A to B? Links in 1 note will be updated.", DesktopActions.RenameQuestion("A", "B", 1));
        Assert.Equal("Rename A to B? Links in 4 notes will be updated.", DesktopActions.RenameQuestion("A", "B", 4));
        Assert.Equal(
            "Renamed to P, but links in 3 notes couldn't be updated: a, b, c.",
            DesktopActions.LinksLeftText("P", ["a", "b", "c"]));
        Assert.Equal(
            "Renamed to P, but links in 4 notes couldn't be updated: a, b, c and 1 more.",
            DesktopActions.LinksLeftText("P", ["a", "b", "c", "d"]));
    }

    // Plan with a self-link and a subpage; A and B link to it (B with CRLF, BOM, alias and spaces); C only in code;
    // Other links elsewhere.
    private void WriteLinkedNotes()
    {
        data.WriteNote("Plan", "# Plan\nsee [[Plan#Next steps]]\n");
        data.WriteFile(Path.Combine("Plan", "Child.md"), "child\n");
        data.WriteNote("A", "see [[Plan]] and [[Other]]\n");
        data.WriteNote("B", "\uFEFFsee [[ plan/child | the child ]]\r\nand [[#Intro]]\r\n");
        data.WriteNote("C", "`[[Plan]]`\n```\n[[Plan]]\n```\n");
        data.WriteNote("Other", "[[Planet]] [[Missing]]\n");
    }

    // The text as stored, a BOM included.
    private string Read(string relative) =>
        Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine([data.Root, .. relative.Split('/')])));

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);
}
