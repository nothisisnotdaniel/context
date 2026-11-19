namespace Context.Tests;

// 293: the done log grouped by week.
public sealed class DoneLogViewTests : IDisposable
{
    private readonly string parent;
    private readonly string root;
    private readonly NoteStore store;

    public DoneLogViewTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-donelogview-" + Guid.NewGuid().ToString("N"));
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

    private string DonePath => Path.Combine(root, "done.md");

    // AC-8: weeks run Monday to Sunday, newest first; inside a week by date, then line, descending.
    [Fact]
    public void View_GroupsByWeekNewestFirst()
    {
        File.WriteAllText(DonePath, "# done\n- 2026-11-16 mon\n- 2026-11-22 sun a\n- 2026-11-23 next mon\n- 2026-11-22 sun b\n");

        DoneLogView view = DoneLogView.Build(store);

        Assert.False(view.Unreadable);
        Assert.Equal([new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 16)], view.Weeks.Select(w => w.Monday));
        Assert.Equal(["next mon"], view.Weeks[0].Entries.Select(e => e.Text));
        Assert.Equal(["sun b", "sun a", "mon"], view.Weeks[1].Entries.Select(e => e.Text));
        Assert.Equal(new DateOnly(2026, 11, 16), DoneLogView.Monday(new DateOnly(2026, 11, 22)));
    }

    // AC-8: an unreadable note sets Unreadable with no weeks; a missing one is just empty.
    [Fact]
    public void View_Unreadable_SetsFlag()
    {
        Assert.Equal((0, false), (DoneLogView.Build(store).Weeks.Count, DoneLogView.Build(store).Unreadable));
        using (FileStream big = File.Create(DonePath))
        {
            big.SetLength(NoteStore.MaxNoteBytes + 1);
        }

        DoneLogView view = DoneLogView.Build(store);

        Assert.True(view.Unreadable);
        Assert.Empty(view.Weeks);
    }
}
