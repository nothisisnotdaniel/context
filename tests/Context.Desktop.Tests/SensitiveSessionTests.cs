namespace Context.Tests;

// 136 AC-6: the session of the one open sensitive note, on an injected clock.
public sealed class SensitiveSessionTests
{
    private DateTime now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    // AC-6: nothing is open at first; Tick does nothing then.
    [Fact]
    public void Tick_NothingOpen_DoesNothing()
    {
        SensitiveSession session = new SensitiveSession(() => now);
        int locked = 0;
        session.Locked += () => locked++;
        now = now.AddHours(1);

        Assert.False(session.Tick());
        Assert.Null(session.Note);
        Assert.Equal(0, locked);
    }

    // AC-6: five minutes without use lock the note; a second less does not.
    [Fact]
    public void Tick_AfterFiveMinutes_DropsTheNoteAndRaisesLocked()
    {
        SensitiveSession session = new SensitiveSession(() => now);
        int locked = 0;
        session.Locked += () => locked++;
        session.Open(Note("Plan"));

        now = now.Add(SensitiveSession.IdleAfter).AddSeconds(-1);
        Assert.False(session.Tick());
        Assert.NotNull(session.Note);

        now = now.AddSeconds(1);
        Assert.True(session.Tick());
        Assert.Null(session.Note);
        Assert.Equal(1, locked);
        Assert.Equal(TimeSpan.FromMinutes(5), SensitiveSession.IdleAfter);
        Assert.False(session.Tick());
    }

    // AC-6: an open, a save (which opens the saved note) and editor input (Touch) each start the five minutes again.
    [Fact]
    public void Tick_UseStartsTheIdleTimeAgain()
    {
        SensitiveSession session = new SensitiveSession(() => now);
        session.Open(Note("Plan"));

        now = now.AddMinutes(4);
        session.Touch();
        now = now.AddMinutes(4);
        Assert.False(session.Tick());
        session.Open(Note("Plan"));
        now = now.AddMinutes(4);
        Assert.False(session.Tick());

        now = now.AddMinutes(1);
        Assert.True(session.Tick());
        Assert.Null(session.Note);
    }

    // AC-6: with a listener (the editor) Tick only asks; the listener saves and locks. The note stays open until it
    // does, and it is asked again five minutes later.
    [Fact]
    public void Tick_WithAListener_AsksInsteadOfLocking()
    {
        SensitiveSession session = new SensitiveSession(() => now);
        int idle = 0;
        int locked = 0;
        session.IdleReached += () => idle++;
        session.Locked += () => locked++;
        session.Open(Note("Plan"));

        now = now.AddMinutes(5);
        Assert.True(session.Tick());
        Assert.Equal(1, idle);
        Assert.Equal(0, locked);
        Assert.NotNull(session.Note);
        Assert.False(session.Tick());

        now = now.AddMinutes(5);
        Assert.True(session.Tick());
        Assert.Equal(2, idle);

        session.Lock();
        Assert.Null(session.Note);
        Assert.Equal(1, locked);
    }

    // AC-6: Lock drops the note and tells every listener, also when one of them throws.
    [Fact]
    public void Lock_DropsTheNote_EvenWhenAListenerThrows()
    {
        SensitiveSession session = new SensitiveSession(() => now);
        int locked = 0;
        session.Locked += () => throw new InvalidOperationException("listener");
        session.Locked += () => locked++;
        session.Open(Note("Plan"));

        session.Lock();

        Assert.Null(session.Note);
        Assert.Equal(1, locked);
    }

    // AC-6: leaving a note drops it without the Locked event; another note's name drops nothing.
    [Fact]
    public void Close_DropsOnlyTheNamedNote_Quietly()
    {
        SensitiveSession session = new SensitiveSession(() => now);
        int locked = 0;
        session.Locked += () => locked++;
        session.Open(Note("Plan"));

        session.Close("Other");
        Assert.NotNull(session.Note);
        session.Close("plan");
        Assert.NotNull(session.Note);
        session.Close("Plan");

        Assert.Null(session.Note);
        Assert.Equal(0, locked);
    }

    // AC-6: a clock that fails or runs backwards never keeps a note open for longer.
    [Fact]
    public void Tick_FailingClock_Locks()
    {
        bool fail = false;
        SensitiveSession session = new SensitiveSession(() => fail ? throw new InvalidOperationException("clock") : now);
        session.Open(Note("Plan"));
        fail = true;

        Assert.True(session.Tick());
        Assert.Null(session.Note);
    }

    // The sealed text is the note as a plain file would hold it: the mark and CRLF are remembered and put back.
    [Theory]
    [InlineData("a\nb\n", "a\nb\n", false, false)]
    [InlineData("a\r\nb\r\n", "a\nb\n", true, false)]
    [InlineData("﻿a\r\nb", "a\nb", true, true)]
    [InlineData("﻿", "", false, true)]
    public void ToEditable_ThenToSealedText_RoundTrips(string sealedText, string text, bool crlf, bool bom)
    {
        NoteInfo found = new NoteInfo("Plan", DateTime.UnixEpoch, "/x/Plan.ctxs");

        EditableNote note = SensitiveSession.ToEditable(found, sealedText, "v1");

        Assert.Equal(text, note.Text);
        Assert.Equal(crlf, note.HadCrlf);
        Assert.Equal(bom, note.HadBom);
        Assert.Equal("v1", note.Version);
        Assert.Equal("/x/Plan.ctxs", note.Path);
        Assert.Equal(sealedText, SensitiveSession.ToSealedText(note, note.Text));
    }

    private static EditableNote Note(string name) => new EditableNote(name, "/x/" + name + ".ctxs", "text", "v", false, false);
}
