using System.Globalization;

namespace Context;

/// <summary>What the desktop's Times panel books (job 383); each is one <c>tm</c> one-shot command.</summary>
internal enum TimesAction
{
    /// <summary><c>arrive &lt;time&gt;</c>.</summary>
    Arrive,

    /// <summary><c>break &lt;time&gt;</c>.</summary>
    Break,

    /// <summary><c>breakend &lt;time&gt;</c>.</summary>
    BreakEnd,

    /// <summary><c>leave &lt;time&gt;</c>.</summary>
    Leave,

    /// <summary><c>checkpoint &lt;time&gt; [#&lt;position&gt;] &lt;name&gt;</c>.</summary>
    Checkpoint,
}

/// <summary>
/// The arguments, the name clean-up and the result texts of a Times booking (jobs 381 and 383). Every booking runs
/// through <see cref="ITimesRunner"/> with these arguments, one argument each, never a shell word; Times stays the only
/// writer of its folder.
/// </summary>
internal static class TimesBooking
{
    /// <summary>The longest name a Times booking gets.</summary>
    public const int MaxName = 200;

    /// <summary>The longest Times output line shown after a refused booking.</summary>
    public const int MaxRefusalText = 200;

    /// <summary>The error when no <c>tm</c> was found or started.</summary>
    public const string NotFoundText = "Times isn't installed, so nothing was booked.";

    /// <summary>The error when <c>tm</c> was stopped after its timeout.</summary>
    public const string TimedOutText = "Times didn't answer in time.";

    /// <summary>The error for a checkpoint whose name is blank after <see cref="CleanName"/> (job 383); nothing runs.</summary>
    public const string NameBlankText = "Name the checkpoint.";

    /// <summary>The start of the error when Times exited with another code than 0.</summary>
    public const string RefusedPrefix = "Times refused the booking: ";

    private const string ClockFormat = "H:mm";

    /// <summary><paramref name="time"/> as Times reads it: <c>H:mm</c> (invariant).</summary>
    public static string Time(TimeOnly time) => time.ToString(ClockFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// A booking name as Times gets it: one line (<see cref="SafeText.Clean"/>, line breaks as spaces), whitespace runs
    /// collapsed to one space, trimmed, every leading <c>#</c> and <c>-</c> dropped (Times joins its arguments again and
    /// would read a first word like <c>#05</c> as its project or <c>--version</c> as an option), cut to
    /// <see cref="MaxName"/> characters (never inside a surrogate pair, trimmed again). Empty when nothing is left.
    /// </summary>
    public static string CleanName(string? name)
    {
        string cleaned = TagParser.CollapseWhitespace(SafeText.Clean(name ?? string.Empty).Replace('\n', ' '));
        cleaned = cleaned.TrimStart('#', '-', ' ');
        return Cut(cleaned, MaxName).TrimEnd();
    }

    /// <summary>
    /// The <c>tm</c> arguments for <paramref name="action"/> at <paramref name="now"/> (<see cref="Time"/>): the
    /// command and the time; a checkpoint then gets <c>#&lt;position&gt;</c> for a positive 1-based quicklist
    /// <paramref name="workItemPosition"/>, and <paramref name="name"/> through <see cref="CleanName"/>.
    /// </summary>
    public static IReadOnlyList<string> Arguments(TimesAction action, TimeOnly now, int? workItemPosition = null, string? name = null)
    {
        string time = Time(now);
        switch (action)
        {
            case TimesAction.Arrive:
                return ["arrive", time];
            case TimesAction.Break:
                return ["break", time];
            case TimesAction.BreakEnd:
                return ["breakend", time];
            case TimesAction.Leave:
                return ["leave", time];
            case TimesAction.Checkpoint:
                List<string> arguments = ["checkpoint", time];
                if (workItemPosition is > 0 and { } position)
                {
                    arguments.Add("#" + position.ToString(CultureInfo.InvariantCulture));
                }

                arguments.Add(CleanName(name));
                return arguments;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    /// <summary>
    /// The line after a booking Times accepted (job 383): <c>Booked &lt;arrival|break|break end|leaving|checkpoint
    /// &lt;name&gt;&gt; at &lt;time&gt; in Times.</c>, the name cleaned by <see cref="CleanName"/>.
    /// </summary>
    public static string BookedText(TimesAction action, TimeOnly now, string? name = null)
    {
        string what = action switch
        {
            TimesAction.Arrive => "arrival",
            TimesAction.Break => "break",
            TimesAction.BreakEnd => "break end",
            TimesAction.Leave => "leaving",
            TimesAction.Checkpoint => "checkpoint " + CleanName(name),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        return $"Booked {what} at {Time(now)} in Times.";
    }

    /// <summary>
    /// What a booking run gave: <c>true</c> for exit code 0 (with an empty <paramref name="text"/>; the caller shows its
    /// own booked line), else <c>false</c> with <see cref="NotFoundText"/>, <see cref="TimedOutText"/> or
    /// <c>Times refused the booking: &lt;line&gt;</c>, the line being the first non-empty line of stderr, else of stdout
    /// (cleaned, whitespace collapsed, cut to <see cref="MaxRefusalText"/>), or <c>exit code &lt;n&gt;</c> when both are
    /// empty. Times' output is never shown otherwise.
    /// </summary>
    public static bool Outcome(TimesRunResult result, out string text)
    {
        switch (result.Kind)
        {
            case TimesRunKind.NotFound:
                text = NotFoundText;
                return false;
            case TimesRunKind.TimedOut:
                text = TimedOutText;
                return false;
        }

        if (result.ExitCode == 0)
        {
            text = string.Empty;
            return true;
        }

        string? line = FirstLine(result.Stderr) ?? FirstLine(result.Stdout);
        text = RefusedPrefix + (line ?? "exit code " + result.ExitCode.ToString(CultureInfo.InvariantCulture));
        return false;
    }

    /// <summary><paramref name="part"/> cut to <paramref name="max"/> characters, never inside a surrogate pair.</summary>
    internal static string Cut(string part, int max)
    {
        if (part.Length <= max)
        {
            return part;
        }

        int cut = max;
        if (char.IsHighSurrogate(part[cut - 1]))
        {
            cut--;
        }

        return part[..cut];
    }

    // The first non-empty line of output, cleaned, trimmed and cut, or null.
    private static string? FirstLine(string output)
    {
        foreach (string line in SafeText.Clean(output).Split('\n'))
        {
            string trimmed = TagParser.CollapseWhitespace(line);
            if (trimmed.Length > 0)
            {
                return Cut(trimmed, MaxRefusalText).TrimEnd();
            }
        }

        return null;
    }
}
