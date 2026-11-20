using System.Text;

namespace Context;

/// <summary>
/// One Inbox mail as the Mail digest reads it (job 286): subject, sender's display name, when it came in (UTC), its
/// message class and the start of its plain-text body. All raw outside text.
/// </summary>
internal sealed record MailMessage(string Subject, string Sender, DateTime Received, string MessageClass, string Body)
{
    /// <summary>Only the type name: the contents are raw outside text.</summary>
    public override string ToString() => nameof(MailMessage);
}

/// <summary>What a mail suggestion turns into when it is accepted.</summary>
internal enum MailSuggestionKind
{
    /// <summary>A todo due today, from a request sentence in the body.</summary>
    Todo,

    /// <summary>A todo to reply, due in two days, for a mail that asks a question.</summary>
    FollowUp,

    /// <summary>A new top-level note for a meeting request.</summary>
    MeetingNote,
}

/// <summary>
/// One suggestion from a mail: its <see cref="Kind"/>, the todo text (or, for a meeting note, the note's title), the
/// follow-up's due date, the meeting note's <see cref="Title"/> and the mail it came from.
/// </summary>
internal sealed record MailSuggestion(MailSuggestionKind Kind, string Text, DateOnly? Due, string? Title, MailMessage Source)
{
    /// <summary>Only the type name: the contents are raw outside text.</summary>
    public override string ToString() => nameof(MailSuggestion);
}

/// <summary>
/// The Mail digest's rules (job 286, pure, no model): request sentences become todos, a question becomes a follow-up,
/// a meeting request becomes a meeting note.
/// </summary>
internal static class MailDigest
{
    /// <summary>The most body characters a mail is read with.</summary>
    public const int MaxBodyLength = 4000;

    /// <summary>The longest todo or follow-up text (equal to the desktop's todo limit).</summary>
    public const int MaxTextLength = 200;

    /// <summary>The longest meeting note title.</summary>
    public const int MaxTitleLength = 100;

    /// <summary>The most todos taken from one mail.</summary>
    public const int MaxTodosPerMail = 3;

    /// <summary>The most suggestions shown.</summary>
    public const int MaxSuggestions = 50;

    /// <summary>The days after today a follow-up is due.</summary>
    public const int FollowUpDays = 2;

    /// <summary>The meeting note title when the subject leaves nothing.</summary>
    public const string FallbackTitle = "Mail note";

    private const string MeetingClass = "IPM.Schedule.Meeting.Request";
    private const string PathChars = "/\\:*?\"<>|";

    private static readonly string[] RequestPhrases =
        ["please", "can you", "could you", "bitte", "kannst du", "könntest du"];

    /// <summary>
    /// The suggestions for <paramref name="messages"/>, newest mail first (stable for equal times), each kind and text
    /// once, without those <paramref name="hidden"/> says to leave out, at most <see cref="MaxSuggestions"/>.
    /// </summary>
    public static IReadOnlyList<MailSuggestion> Suggest(
        IEnumerable<MailMessage> messages, DateOnly today, Func<MailSuggestion, bool>? hidden = null)
    {
        List<MailSuggestion> found = [];
        HashSet<(MailSuggestionKind, string)> seen = [];
        foreach (MailMessage message in messages.OrderByDescending(m => m.Received))
        {
            foreach (MailSuggestion suggestion in ForMessage(message, today))
            {
                if (hidden is not null && hidden(suggestion))
                {
                    continue;
                }

                if (!seen.Add((suggestion.Kind, suggestion.Text)))
                {
                    continue;
                }

                found.Add(suggestion);
                if (found.Count == MaxSuggestions)
                {
                    return found;
                }
            }
        }

        return found;
    }

    /// <summary>Whether <paramref name="messageClass"/> is a meeting request (ordinal, ignoring case).</summary>
    public static bool IsMeetingRequest(string? messageClass) =>
        messageClass is not null && messageClass.StartsWith(MeetingClass, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The meeting note's name for <paramref name="subject"/>: each <c>/ \ : * ? " &lt; &gt; |</c> and control
    /// character as a space, whitespace runs collapsed, trimmed, cut to <see cref="MaxTitleLength"/> (never inside a
    /// surrogate pair), then leading and trailing dots and spaces (and a trailing <c>.md</c>) removed;
    /// <see cref="FallbackTitle"/> when nothing is left.
    /// </summary>
    public static string TitleFor(string? subject)
    {
        string raw = subject ?? string.Empty;
        StringBuilder spaced = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            spaced.Append(char.IsControl(c) || PathChars.Contains(c, StringComparison.Ordinal) ? ' ' : c);
        }

        string title = Cut(TagParser.CollapseWhitespace(spaced.ToString()).Trim(), MaxTitleLength);
        string before;
        do
        {
            before = title;
            title = title.Trim('.', ' ');
            if (title.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                title = title[..^3];
            }
        }
        while (title.Length != before.Length);

        return title.Length == 0 ? FallbackTitle : title;
    }

    /// <summary>The follow-up text: <c>Reply to &lt;sender&gt;: &lt;subject&gt;</c>, cleaned and cut like a todo.</summary>
    public static string FollowUpText(MailMessage message) =>
        CleanText("Reply to " + message.Sender + ": " + message.Subject);

    // Control characters removed, trimmed, cut to MaxTextLength (never inside a surrogate pair) and trimmed again.
    private static string CleanText(string text)
    {
        StringBuilder kept = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (!char.IsControl(c))
            {
                kept.Append(c);
            }
        }

        return Cut(kept.ToString().Trim(), MaxTextLength).Trim();
    }

    private static IEnumerable<MailSuggestion> ForMessage(MailMessage message, DateOnly today)
    {
        if (IsMeetingRequest(message.MessageClass))
        {
            string title = TitleFor(message.Subject);
            yield return new MailSuggestion(MailSuggestionKind.MeetingNote, title, null, title, message);
            yield break;
        }

        string body = message.Body ?? string.Empty;
        if (body.Length > MaxBodyLength)
        {
            body = Cut(body, MaxBodyLength);
        }

        string[] lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', '\r');
        int todos = 0;
        foreach (string line in lines)
        {
            foreach (string sentence in Sentences(line))
            {
                if (todos == MaxTodosPerMail)
                {
                    break;
                }

                if (!StartsWithRequest(sentence))
                {
                    continue;
                }

                string text = CleanText(sentence);
                if (text.Length > 0)
                {
                    todos++;
                    yield return new MailSuggestion(MailSuggestionKind.Todo, text, null, null, message);
                }
            }
        }

        if (lines.Any(l => l.TrimEnd().EndsWith('?')))
        {
            string text = FollowUpText(message);
            if (text.Length > 0)
            {
                yield return new MailSuggestion(MailSuggestionKind.FollowUp, text, today.AddDays(FollowUpDays), null, message);
            }
        }
    }

    // The sentences of one line: each ends after '.', '!' or '?' that is followed by whitespace or the line's end.
    private static IEnumerable<string> Sentences(string line)
    {
        int start = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] is '.' or '!' or '?' && (i + 1 == line.Length || char.IsWhiteSpace(line[i + 1])))
            {
                yield return line[start..(i + 1)];
                start = i + 1;
            }
        }

        if (start < line.Length)
        {
            yield return line[start..];
        }
    }

    // Whether the trimmed sentence starts with a request phrase (any case) that is not the start of a longer word.
    private static bool StartsWithRequest(string sentence)
    {
        string trimmed = sentence.TrimStart();
        foreach (string phrase in RequestPhrases)
        {
            if (trimmed.StartsWith(phrase, StringComparison.OrdinalIgnoreCase)
                && (trimmed.Length == phrase.Length || !char.IsLetterOrDigit(trimmed[phrase.Length])))
            {
                return true;
            }
        }

        return false;
    }

    // At most max chars, never ending in a lone high surrogate.
    private static string Cut(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        int cut = max;
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut];
    }
}
