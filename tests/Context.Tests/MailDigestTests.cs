namespace Context.Tests;

// 286 AC-1 to AC-4: the Mail digest's rules in Core (todos, follow-ups, meeting notes, order, dedupe and cap).
public sealed class MailDigestTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly DateTime Received = new(2026, 10, 28, 8, 30, 0, DateTimeKind.Utc);
    private const string MeetingClass = "IPM.Schedule.Meeting.Request";

    private static MailMessage Mail(
        string body, string subject = "Status", string sender = "Anna", DateTime? received = null, string messageClass = "IPM.Note") =>
        new(subject, sender, received ?? Received, messageClass, body);

    private static IReadOnlyList<MailSuggestion> Suggest(params MailMessage[] mails) => MailDigest.Suggest(mails, Today);

    // AC-1: a sentence starting with a request phrase (any case, English or German) becomes a todo.
    [Theory]
    [InlineData("Please send the report.", "Please send the report.")]
    [InlineData("Hi. can you book the room! Thanks.", "can you book the room!")]
    [InlineData("Could you check the numbers", "Could you check the numbers")]
    [InlineData("Bitte die Folien schicken.", "Bitte die Folien schicken.")]
    [InlineData("KANNST DU das prüfen.", "KANNST DU das prüfen.")]
    [InlineData("Könntest du anrufen.", "Könntest du anrufen.")]
    [InlineData("  please, call back.", "please, call back.")]
    public void Request_BecomesTodo(string body, string text)
    {
        MailSuggestion todo = Assert.Single(Suggest(Mail(body)));

        Assert.Equal(MailSuggestionKind.Todo, todo.Kind);
        Assert.Equal(text, todo.Text);
        Assert.Null(todo.Due);
        Assert.Null(todo.Title);
    }

    // AC-1: a phrase that only starts a longer word, or appears mid-sentence, is no request.
    [Theory]
    [InlineData("Pleased to meet you.")]
    [InlineData("Bittersweet news.")]
    [InlineData("We said please send it.")]
    [InlineData("")]
    public void NoRequest_NoTodo(string body)
    {
        Assert.Empty(Suggest(Mail(body)));
    }

    // AC-1: at most three todos per mail, across lines.
    [Fact]
    public void Todos_AtMostThreePerMail()
    {
        IReadOnlyList<MailSuggestion> found = Suggest(Mail("Please a. Please b.\r\nPlease c.\nPlease d."));

        Assert.Equal(["Please a.", "Please b.", "Please c."], found.Select(s => s.Text).ToArray());
    }

    // AC-1: a todo is cut to 200 characters and has no control characters.
    [Fact]
    public void Todo_CutAndControlsRemoved()
    {
        string body = "Please \u0007send\u001b it " + new string('x', 300);

        MailSuggestion todo = Assert.Single(Suggest(Mail(body)));

        Assert.Equal(200, todo.Text.Length);
        Assert.StartsWith("Please send it x", todo.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(todo.Text, char.IsControl);
    }

    // AC-1: a cut never leaves a lone high surrogate.
    [Fact]
    public void Todo_CutKeepsSurrogatePairs()
    {
        string body = "Please " + new string('a', 192) + "😀😀";

        MailSuggestion todo = Assert.Single(Suggest(Mail(body)));

        Assert.Equal(199, todo.Text.Length);
        Assert.False(char.IsHighSurrogate(todo.Text[^1]));
    }

    // AC-2: a line ending in "?" gives one follow-up to reply, due today + 2.
    [Fact]
    public void Question_BecomesFollowUp()
    {
        MailSuggestion follow = Assert.Single(Suggest(Mail("Hello,\nwhat do you think?  \nBest", "Budget", "Bob")));

        Assert.Equal(MailSuggestionKind.FollowUp, follow.Kind);
        Assert.Equal("Reply to Bob: Budget", follow.Text);
        Assert.Equal(Today.AddDays(2), follow.Due);
    }

    // AC-2: a question mark mid-line is no question; todos come before the follow-up.
    [Fact]
    public void FollowUp_OnlyForALineEndingInQuestionMark()
    {
        Assert.Empty(Suggest(Mail("Why? Because.")));

        IReadOnlyList<MailSuggestion> found = Suggest(Mail("Can you come?"));
        Assert.Equal([MailSuggestionKind.Todo, MailSuggestionKind.FollowUp], found.Select(s => s.Kind).ToArray());
        Assert.Equal("Can you come?", found[0].Text);
    }

    // AC-2: a 300-character sender and subject give a 200-character follow-up without a split surrogate pair or controls.
    [Fact]
    public void FollowUp_LongSenderAndSubject_Cut()
    {
        string sender = new string('s', 150) + "\u0007";
        string subject = new string('t', 39) + "😀" + new string('u', 300);

        MailSuggestion follow = Assert.Single(Suggest(Mail("Ok?", subject, sender)));

        Assert.True(follow.Text.Length <= 200);
        Assert.StartsWith("Reply to " + new string('s', 150) + ": ", follow.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(follow.Text, char.IsControl);
        Assert.False(char.IsHighSurrogate(follow.Text[^1]));
    }

    // AC-2: a cut that falls inside a surrogate pair drops the whole pair.
    [Fact]
    public void FollowUp_CutInsidePair_DropsIt()
    {
        string prefix = "Reply to A: ";
        string subject = new string('x', 200 - prefix.Length - 1) + "😀tail";

        MailSuggestion follow = Assert.Single(Suggest(Mail("Ok?", subject, "A")));

        Assert.Equal(199, follow.Text.Length);
        Assert.EndsWith("x", follow.Text, StringComparison.Ordinal);
    }

    // AC-3: a meeting request (any case, any suffix) gives only a meeting note named after its subject.
    [Theory]
    [InlineData(MeetingClass)]
    [InlineData("ipm.schedule.meeting.request")]
    [InlineData("IPM.Schedule.Meeting.Request.Custom")]
    public void MeetingRequest_BecomesOnlyMeetingNote(string messageClass)
    {
        MailSuggestion note = Assert.Single(Suggest(Mail("Please come. Ok?", "Planning Q4", messageClass: messageClass)));

        Assert.Equal(MailSuggestionKind.MeetingNote, note.Kind);
        Assert.Equal("Planning Q4", note.Title);
        Assert.Equal("Planning Q4", note.Text);
        Assert.Null(note.Due);
    }

    // AC-3: other classes are no meeting request.
    [Theory]
    [InlineData("IPM.Schedule.Meeting.Canceled")]
    [InlineData("IPM.Note")]
    [InlineData("")]
    public void OtherClass_NoMeetingNote(string messageClass)
    {
        Assert.Empty(Suggest(Mail("Hi.", "Planning", messageClass: messageClass)));
    }

    // AC-3: the title has path characters and controls as spaces, runs collapsed, trimmed, no edge dots or ".md".
    [Theory]
    [InlineData("Q4/Q1: plan", "Q4 Q1 plan")]
    [InlineData("a\\b*c?d\"e<f>g|h", "a b c d e f g h")]
    [InlineData("  Team\t\u0007 sync  ", "Team sync")]
    [InlineData("..Review..", "Review")]
    [InlineData(". . x . .", "x")]
    [InlineData("Report.md", "Report")]
    [InlineData("Notes.md.", "Notes")]
    [InlineData("...", "Mail note")]
    [InlineData("", "Mail note")]
    [InlineData("/:*", "Mail note")]
    [InlineData("\u0001\u0002", "Mail note")]
    public void Title_Cleaned(string subject, string title)
    {
        Assert.Equal(title, MailDigest.TitleFor(subject));
    }

    // AC-3: a long title is cut to 100 without a split surrogate pair, then trimmed of dots and spaces.
    [Fact]
    public void Title_CutTo100()
    {
        Assert.Equal(new string('a', 100), MailDigest.TitleFor(new string('a', 300)));
        Assert.Equal(new string('a', 99), MailDigest.TitleFor(new string('a', 99) + "😀b"));
        Assert.Equal(new string('a', 97), MailDigest.TitleFor(new string('a', 97) + " .." + new string('b', 10)));
    }

    // AC-4: newest mail first, the input order kept for equal times.
    [Fact]
    public void Order_NewestFirst()
    {
        MailMessage old = Mail("Please old.", received: Received.AddHours(-2));
        MailMessage newest = Mail("Please new.", received: Received.AddHours(1));
        MailMessage first = Mail("Please first.");
        MailMessage second = Mail("Please second.");

        IReadOnlyList<MailSuggestion> found = Suggest(old, first, newest, second);

        Assert.Equal(["Please new.", "Please first.", "Please second.", "Please old."], found.Select(s => s.Text).ToArray());
    }

    // AC-4: the same kind and text is suggested once (the newest); the same text of another kind is kept.
    [Fact]
    public void Dedupe_ByKindAndText()
    {
        MailMessage a = Mail("Please send it.", received: Received.AddHours(1), sender: "A");
        MailMessage b = Mail("Please send it.", sender: "B");
        MailMessage meeting = Mail(string.Empty, "Please send it.", messageClass: MeetingClass);

        IReadOnlyList<MailSuggestion> found = Suggest(a, b, meeting);

        Assert.Equal(2, found.Count);
        Assert.Equal("A", found[0].Source.Sender);
        Assert.Equal(MailSuggestionKind.MeetingNote, found[1].Kind);
    }

    // AC-4: at most 50 suggestions.
    [Fact]
    public void Cap_AtMostFifty()
    {
        MailMessage[] mails = [.. Enumerable.Range(0, 30).Select(i => Mail($"Please a{i}. Please b{i}.", received: Received.AddMinutes(-i)))];

        IReadOnlyList<MailSuggestion> found = Suggest(mails);

        Assert.Equal(50, found.Count);
        Assert.Equal("Please a0.", found[0].Text);
        Assert.Equal("Please b24.", found[^1].Text);
    }

    // AC-4, AC-9: hidden suggestions are left out before the dedupe and the cap.
    [Fact]
    public void Hidden_LeftOut()
    {
        MailMessage a = Mail("Please send it.", received: Received.AddHours(1), sender: "A");
        MailMessage b = Mail("Please send it.", sender: "B");

        IReadOnlyList<MailSuggestion> found = MailDigest.Suggest([a, b], Today, s => s.Source.Sender == "A");

        Assert.Equal("B", Assert.Single(found).Source.Sender);
    }

    // The records show only their type names, never mail text.
    [Fact]
    public void ToString_NamesOnlyTheType()
    {
        MailMessage mail = Mail("Please secret.");

        Assert.Equal(nameof(MailMessage), mail.ToString());
        Assert.Equal(nameof(MailSuggestion), Suggest(mail)[0].ToString());
    }
}
