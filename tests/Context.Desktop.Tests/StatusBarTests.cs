namespace Context.Tests;

// 455 AC-2, AC-5: the StatusBar service on its own.
public sealed class StatusBarTests
{
    // AC-2: the bar starts empty.
    [Fact]
    public void NewBar_IsEmpty()
    {
        StatusBar bar = new();

        Assert.Equal(string.Empty, bar.Text);
        Assert.Equal(string.Empty, bar.Css);
        Assert.Empty(bar.Lines);
        Assert.Equal(0, bar.Current.Id);
    }

    // AC-2: Show sets text and class, raises Changed, and a newer Show replaces the older one.
    [Fact]
    public void Show_ReplacesTheShownMessage()
    {
        StatusBar bar = new();
        int changes = 0;
        bar.Changed += () => changes++;

        long first = bar.Show("Saved.", "accent");
        long second = bar.Show("Can't save.", "red");

        Assert.NotEqual(first, second);
        Assert.Equal("Can't save.", bar.Text);
        Assert.Equal("red", bar.Css);
        Assert.Equal(2, changes);
    }

    // AC-2: all text goes through DesktopText.OneLine.
    [Fact]
    public void Show_MakesTheTextOneLine()
    {
        StatusBar bar = new();

        bar.Show("Can't save:\r\nthe disk\tis full", "red");

        Assert.Equal(DesktopText.OneLine("Can't save:\r\nthe disk\tis full"), bar.Text);
        Assert.DoesNotContain('\n', bar.Text);
    }

    // AC-2: Clear with the current id empties the bar.
    [Fact]
    public void Clear_WithCurrentId_EmptiesTheBar()
    {
        StatusBar bar = new();
        long id = bar.Show("Saved.", "accent");

        bar.Clear(id);

        Assert.Equal(string.Empty, bar.Text);
        Assert.Equal(string.Empty, bar.Css);
    }

    // AC-5: Clear with an older id leaves a newer message of another panel.
    [Fact]
    public void Clear_WithStaleId_KeepsTheNewerMessage()
    {
        StatusBar bar = new();
        long older = bar.Show("Added: milk", "accent");
        bar.Show("Can't move the note.", "red");

        bar.Clear(older);
        bar.Clear(0);

        Assert.Equal("Can't move the note.", bar.Text);
        Assert.Equal("red", bar.Css);
    }

    // AC-5: Update with an empty text clears only the caller's own message.
    [Fact]
    public void Update_WithEmptyText_ClearsOnlyItsOwnMessage()
    {
        StatusBar bar = new();
        long mine = bar.Update(0, "Saved.", "accent");
        long other = bar.Show("Copied x.", "grey");

        Assert.Equal(0, bar.Update(mine, string.Empty, "grey"));
        Assert.Equal("Copied x.", bar.Text);

        bar.Update(other, string.Empty, "grey");
        Assert.Equal(string.Empty, bar.Text);
    }

    // AC-2: a multi-line message has one entry per line, each one line, and no single Text.
    [Fact]
    public void Show_Lines_KeepsEachLineWithItsClass()
    {
        StatusBar bar = new();

        bar.Show([("Attached a.png", "accent"), ("Can't attach\nb.exe", "red")]);

        Assert.Equal(string.Empty, bar.Text);
        Assert.Equal([("Attached a.png", "accent"), (DesktopText.OneLine("Can't attach\nb.exe"), "red")], bar.Lines);
    }

    // AC-4: Restore puts back the earlier message only while the given message is still shown.
    [Fact]
    public void Restore_OnlyWhileTheMessageIsShown()
    {
        StatusBar bar = new();
        bar.Show("Saved.", "accent");
        StatusBar.Message before = bar.Current;
        long saving = bar.Show("Saving…", "grey");

        bar.Restore(saving, before);
        Assert.Equal("Saved.", bar.Text);
        Assert.Equal("accent", bar.Css);

        long saving2 = bar.Show("Saving…", "grey");
        bar.Show("Copied x.", "grey");
        bar.Restore(saving2, before);
        Assert.Equal("Copied x.", bar.Text);
    }

    // AC-2: a throwing listener does not break Show.
    [Fact]
    public void Show_WithThrowingListener_StillShows()
    {
        StatusBar bar = new();
        bar.Changed += () => throw new InvalidOperationException("listener");

        bar.Show("Saved.", "accent");

        Assert.Equal("Saved.", bar.Text);
    }
}
