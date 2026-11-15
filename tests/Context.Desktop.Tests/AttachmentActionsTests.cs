using System.Text;

namespace Context.Tests;

// 177: DesktopActions.AttachFiles, OpenAttachment, and MoveNote / DeleteNote with attachments, against real files.
public sealed class AttachmentActionsTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%...");
    private static readonly byte[] Svg = Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>");

    // Generous: the bounded scans take well under a second; the old quadratic ones took hours.
    private static readonly TimeSpan DeleteBound = TimeSpan.FromSeconds(60);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-3/AC-6: each file gets its own line in drop order; a stored one comes back with its link data.
    [Fact]
    public async Task AttachFiles_OneLinePerFile()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        byte[] noteBytes = File.ReadAllBytes(NotePath("Plan"));

        ActionResult result = await data.Actions!.AttachFiles("Plan", 5,
        [
            new DroppedFile("shot.png", Png.Length, Png),
            new DroppedFile("plan.pdf", Pdf.Length, Pdf),
            new DroppedFile("big.png", NoteAttachments.MaxBytes + 1, null),
            new DroppedFile("draw.svg", Svg.Length, Svg),
            new DroppedFile("odd.png", 3, null),
        ]);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(
            [
                "Attached shot.png.",
                "Attached plan.pdf.",
                "big.png is over 10 MB. Nothing was attached.",
                "draw.svg isn't a PNG, JPEG, GIF, WebP or PDF file. Nothing was attached.",
                "Can't attach odd.png.",
            ],
            result.Attached!.Select(l => l.Text));
        Assert.Equal(
            [ActionKind.Done, ActionKind.Done, ActionKind.Refused, ActionKind.Refused, ActionKind.Failed],
            result.Attached!.Select(l => l.Kind));
        AttachmentLine image = result.Attached![0];
        Assert.Equal((".attachments/shot.png", "shot.png", true, "image/png", Convert.ToBase64String(Png)),
            (image.Src, image.Name, image.IsImage, image.Mime, image.Base64));
        AttachmentLine pdf = result.Attached![1];
        Assert.Equal((".attachments/plan.pdf", false, "application/pdf", (string?)null),
            (pdf.Src, pdf.IsImage, pdf.Mime, pdf.Base64));
        Assert.Equal(["plan.pdf", "shot.png"],
            Directory.GetFiles(Path.Combine(data.Root, ".attachments")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(noteBytes, File.ReadAllBytes(NotePath("Plan")));
    }

    // AC-6: more than 10 files attaches none.
    [Theory]
    [InlineData(11, 0)]
    [InlineData(11, 11)]
    public async Task AttachFiles_OverTheLimit_AttachesNone(int count, int sent)
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.AttachFiles("Plan", count,
            Enumerable.Range(0, sent).Select(i => new DroppedFile($"s{i}.png", Png.Length, Png)).ToArray());

        Assert.Equal((ActionKind.Refused, "Drop at most 10 files."), (result.Kind, result.Text));
        Assert.Null(result.Attached);
        Assert.Equal(before, Files());
    }

    // AC-6: every file refused gives Refused with the lines.
    [Fact]
    public async Task AttachFiles_NoneStored_Refused()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.AttachFiles("Plan", 1, [new DroppedFile("draw.svg", Svg.Length, Svg)]);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Single(result.Attached!);
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".attachments")));
    }

    // AC-7: a sensitive note is refused with no key-store call and nothing written.
    [Fact]
    public async Task AttachFiles_Sensitive_RefusedWithoutAgree()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Secret", "# Secret\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.AttachFiles("Secret", 1, [new DroppedFile("shot.png", Png.Length, Png)]);

        Assert.Equal((ActionKind.Refused, "Attachments can't be added to a sensitive note."), (result.Kind, result.Text));
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(before, Files());
    }

    // AC-3: a note that is gone is refused.
    [Fact]
    public async Task AttachFiles_MissingNote_Refused()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.AttachFiles("Gone", 1, [new DroppedFile("shot.png", Png.Length, Png)]);

        Assert.Equal((ActionKind.Refused, ActionReason.Missing), (result.Kind, result.Reason));
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".attachments")));
    }

    // AC-9: a resolved PDF is launched once with its path.
    [Fact]
    public async Task OpenAttachment_Pdf_LaunchesOnce()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        await data.Actions!.AttachFiles("Plan", 1, [new DroppedFile("plan.pdf", Pdf.Length, Pdf)]);

        ActionResult result = await data.Actions.OpenAttachment("Plan", ".attachments/plan.pdf");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal([Path.Combine(data.Root, ".attachments", "plan.pdf")], data.Launcher.Launched);
    }

    // AC-9: anything but a resolved PDF is refused and never launched.
    [Theory]
    [InlineData(".attachments/shot.png", "shot.png")]
    [InlineData(".attachments/missing.pdf", "missing.pdf")]
    [InlineData(".attachments/fake.pdf", "fake.pdf")]
    [InlineData("../Plan.md", "Plan.md")]
    [InlineData(".attachments/a/b.pdf", "b.pdf")]
    public async Task OpenAttachment_NotAResolvedPdf_Refused(string src, string shown)
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteFile(Path.Combine(".attachments", "fake.pdf"), "<html></html>");
        await data.RefreshAsync();
        await data.Actions!.AttachFiles("Plan", 1, [new DroppedFile("shot.png", Png.Length, Png)]);

        ActionResult result = await data.Actions.OpenAttachment("Plan", src);

        Assert.Equal((ActionKind.Refused, $"Can't open {shown}."), (result.Kind, result.Text));
        Assert.Empty(data.Launcher.Launched);
    }

    // AC-9: a launcher that throws gives the same text as Failed.
    [Fact]
    public async Task OpenAttachment_LauncherThrows_Failed()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        await data.Actions!.AttachFiles("Plan", 1, [new DroppedFile("plan.pdf", Pdf.Length, Pdf)]);
        data.Launcher.Throws = new InvalidOperationException("secret detail");

        ActionResult result = await data.Actions.OpenAttachment("Plan", ".attachments/plan.pdf");

        Assert.Equal((ActionKind.Failed, "Can't open plan.pdf."), (result.Kind, result.Text));
        Assert.Single(data.Launcher.Launched);
    }

    // AC-12: MoveNote carries the sole-linked attachment and keeps the Done text.
    [Fact]
    public async Task MoveNote_CarriesAttachment()
    {
        data.WriteNote("Target", "# Target\n");
        await WriteLinked("Plan", "shot.png", Png);

        ActionResult result = await data.Actions!.MoveNote("Plan", "Target");

        Assert.Equal((ActionKind.Done, "Target/Plan"), (result.Kind, result.Text));
        Assert.True(File.Exists(Path.Combine(data.Root, "Target", ".attachments", "shot.png")));
        Assert.False(File.Exists(Path.Combine(data.Root, ".attachments", "shot.png")));
        Assert.NotNull(data.Actions.Attachments.TryResolve("Target/Plan", ".attachments/shot.png"));
    }

    // AC-14: a failed step gives the Failed text and changes nothing.
    [Theory]
    [InlineData("copy")]
    [InlineData("move")]
    [InlineData("rewrite")]
    public async Task MoveNote_FailedStep_NothingMoved(string step)
    {
        data.WriteNote("Target", "# Target\n");
        await WriteLinked("Plan", "shot.png", Png);
        SortedDictionary<string, string> before = Files();
        data.Actions!.Attachments.BeforeMoveStep = s =>
        {
            if (s == step)
            {
                throw new IOException("disk full");
            }
        };

        ActionResult result = await data.Actions.MoveNote("Plan", "Target");

        Assert.Equal(
            (ActionKind.Failed, "Can't move the attachments of Plan. Nothing was moved."),
            (result.Kind, result.Text));
        Assert.Equal(before, Files());
        Assert.NotNull(data.Actions.Attachments.TryResolve("Plan", ".attachments/shot.png"));
    }

    // Review round 2: a note linking more than NoteAttachments.MaxLinkedNames names is not moved, with the Failed text.
    [Fact]
    public async Task MoveNote_PastTheCap_NothingMoved()
    {
        data.WriteNote("Target", "# Target\n");
        await WriteLinked("Plan", "shot.png", Png);
        StringBuilder text = new StringBuilder(File.ReadAllText(NotePath("Plan")));
        for (int i = 0; i < NoteAttachments.MaxLinkedNames; i++)
        {
            text.Append("[x](.attachments/x").Append(i).Append(".png)\n");
        }

        data.WriteNote("Plan", text.ToString());
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.MoveNote("Plan", "Target");

        Assert.Equal(
            (ActionKind.Failed, "Can't move the attachments of Plan. Nothing was moved."),
            (result.Kind, result.Text));
        Assert.Equal(before, Files());
    }

    // AC-14: a failed cleanup still gives the normal Done text.
    [Fact]
    public async Task MoveNote_FailedCleanup_Done()
    {
        data.WriteNote("Target", "# Target\n");
        await WriteLinked("Plan", "shot.png", Png);
        data.Actions!.Attachments.BeforeMoveStep = s =>
        {
            if (s == "cleanup")
            {
                throw new IOException("busy");
            }
        };

        ActionResult result = await data.Actions.MoveNote("Plan", "Target");

        Assert.Equal((ActionKind.Done, "Target/Plan"), (result.Kind, result.Text));
        Assert.True(File.Exists(Path.Combine(data.Root, ".attachments", "shot.png")));
    }

    // AC-14: a sibling with the title at the target and a move under calendar keep today's texts, with no copy left.
    [Fact]
    public async Task MoveNote_Refused_TodaysTexts_NoCopy()
    {
        data.WriteNote("Target", "# Target\n");
        data.WriteFile(Path.Combine("Target", "Plan.md"), "# Plan\n");
        data.WriteNote("calendar", "# calendar\n");
        await WriteLinked("Plan", "shot.png", Png);
        SortedDictionary<string, string> before = Files();

        ActionResult exists = await data.Actions!.MoveNote("Plan", "Target");
        ActionResult invalid = await data.Actions.MoveNote("Plan", "calendar");

        Assert.Equal((ActionKind.Refused, ActionReason.Exists, DesktopActions.ExistsText("Target/Plan")),
            (exists.Kind, exists.Reason, exists.Text));
        Assert.Equal((ActionKind.Refused, ActionReason.Invalid, DesktopActions.CantMoveText("Plan", "calendar")),
            (invalid.Kind, invalid.Reason, invalid.Text));
        Assert.Equal(before, Files());
        Assert.False(Directory.Exists(Path.Combine(data.Root, "Target", ".attachments")));
        Assert.False(Directory.Exists(Path.Combine(data.Root, "calendar", ".attachments")));
    }

    // AC-11/AC-12: a sensitive note in the level keeps the source (copied) with zero Agree calls.
    [Fact]
    public async Task MoveNote_SensitiveSibling_Copies_NoAgree()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Secret", "# Secret\n");
        data.WriteNote("Target", "# Target\n");
        await WriteLinked("Plan", "shot.png", Png);

        ActionResult result = await data.Actions!.MoveNote("Plan", "Target");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.True(File.Exists(Path.Combine(data.Root, ".attachments", "shot.png")));
        Assert.True(File.Exists(Path.Combine(data.Root, "Target", ".attachments", "shot.png")));
        Assert.Equal(0, data.Keys.Agrees);
    }

    // AC-15: DeleteNote removes only the attachment no sibling links.
    [Fact]
    public async Task DeleteNote_SoleGoes_SharedStays()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        await data.Actions!.AttachFiles("Plan", 2,
            [new DroppedFile("sole.png", Png.Length, Png), new DroppedFile("shared.pdf", Pdf.Length, Pdf)]);
        data.WriteNote("Plan", "# Plan\n\n![sole.png](.attachments/sole.png)\n[shared.pdf](.attachments/shared.pdf)\n");
        data.WriteNote("Other", "# Other\n\n[shared.pdf](.attachments/shared.pdf)\n");
        string shared = Files()[".attachments/shared.pdf"];

        ActionResult result = await data.Actions.DeleteNote("Plan", new Confirmation());

        Assert.Equal((ActionKind.Done, "Plan"), (result.Kind, result.Text));
        Assert.False(File.Exists(NotePath("Plan")));
        Assert.False(File.Exists(Path.Combine(data.Root, ".attachments", "sole.png")));
        Assert.Equal(shared, Files()[".attachments/shared.pdf"]);
    }

    // Review round 1 (security): a crafted 5 MB note of distinct links, next to a 5 MB sibling, is deleted quickly and,
    // past NoteAttachments.MaxLinkedNames, removes no attachment.
    [Fact]
    public async Task DeleteNote_CraftedNote_FinishesAndKeepsAttachments()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        await data.Actions!.AttachFiles("Plan", 1, [new DroppedFile("sole.png", Png.Length, Png)]);
        StringBuilder text = new StringBuilder("![sole.png](.attachments/sole.png)\n");
        for (int i = 0; text.Length < NoteStore.MaxEditBytes - 64; i++)
        {
            text.Append("](.attachments/n").Append(i).Append(".png)");
        }

        data.WriteNote("Plan", text.ToString());
        data.WriteNote("Other", new string('n', (int)NoteStore.MaxEditBytes - 16));
        string sole = Files()[".attachments/sole.png"];

        Task<ActionResult> delete = data.Actions.DeleteNote("Plan", new Confirmation());

        Assert.Same(delete, await Task.WhenAny(delete, Task.Delay(DeleteBound)));
        ActionResult result = await delete;
        Assert.Equal((ActionKind.Done, "Plan"), (result.Kind, result.Text));
        Assert.False(File.Exists(NotePath("Plan")));
        Assert.Equal(sole, Files()[".attachments/sole.png"]);
    }

    // AC-4/AC-15: deleting a note with subpages removes the child .attachments, and a sole attachment of its level.
    [Fact]
    public async Task DeleteNote_WithSubpages_RemovesChildAttachments()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteFile(Path.Combine("Plan", "Step.md"), "# Step\n");
        await data.RefreshAsync();
        await data.Actions!.AttachFiles("Plan/Step", 1, [new DroppedFile("shot.png", Png.Length, Png)]);

        ActionResult result = await data.Actions.DeleteNote("Plan", new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.False(Directory.Exists(Path.Combine(data.Root, "Plan")));
    }

    private async Task WriteLinked(string title, string file, byte[] bytes)
    {
        data.WriteNote(title, $"# {title}\n");
        await data.RefreshAsync();
        ActionResult added = await data.Actions!.AttachFiles(title, 1, [new DroppedFile(file, bytes.Length, bytes)]);
        AttachmentLine line = added.Attached![0];
        data.WriteNote(title, $"# {title}\n\n![{line.Name}]({line.Src})\n");
    }

    private string NotePath(string name) => Path.Combine(data.Root, name + ".md");

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, "context.db");
}
