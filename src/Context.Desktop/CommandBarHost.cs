using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Context;

/// <summary>
/// The command bar's message handler (job 282). The bar page sends JSON messages (<c>complete</c>, <c>run</c>,
/// <c>close</c>, each with a <c>type</c> and, for the first two, a <c>line</c>); the host answers through
/// <see cref="Send"/> with <c>suggestions</c>, <c>result</c> (<c>kind</c> <c>ok</c> or <c>error</c>, <c>text</c>) or
/// <c>close</c>. A message over <see cref="MaxMessageBytes"/>, one that is not a JSON object with a known string
/// <c>type</c>, or a <c>line</c> that is not a string, gets no answer and runs nothing. One run at a time: a
/// <c>run</c> while another (or its closing pause) is running is ignored.
/// </summary>
internal sealed partial class CommandBarHost
{
    /// <summary>The largest message (UTF-8 bytes) that is read.</summary>
    public const int MaxMessageBytes = 4096;

    /// <summary>The longest result text shown, before the <c>…</c>.</summary>
    public const int MaxResultLength = 200;

    public const string NotFoundText = "Can't find the tm command.";

    public const string TimedOutText = "Times didn't answer in 10 seconds.";

    public const string DoneText = "Done.";

    /// <summary>How long an <c>ok</c> result stays before the bar closes.</summary>
    public static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(1.5);

    private readonly ITimesRunner times;
    private readonly CommandBarContext context;
    private readonly Func<IReadOnlyList<(int WorkItem, string Title)>> projects;
    private readonly Func<TimeSpan, Task> delay;
    private int running;
    private int runs;

    public CommandBarHost(
        ITimesRunner times,
        CommandBarContext context,
        Func<IReadOnlyList<(int WorkItem, string Title)>> projects,
        Func<TimeSpan, Task> delay)
    {
        this.times = times;
        this.context = context;
        this.projects = projects;
        this.delay = delay;
    }

    /// <summary>Raised with each JSON answer for the page.</summary>
    public event Action<string>? Send;

    /// <summary>Raised after each <c>close</c> answer, so the window closes itself.</summary>
    public event Action? CloseRequested;

    /// <summary>How many runs have finished, closing pause included (for tests to wait on).</summary>
    public int Runs => Volatile.Read(ref runs);

    /// <summary>Asks the page to focus its input (a hotkey press while the bar is open).</summary>
    public void Focus() => Answer(Message("focus", null));

    /// <summary>Handles one message from the page; never throws.</summary>
    public async Task Receive(string? json)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > MaxMessageBytes)
        {
            return;
        }

        string type;
        string? line = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out JsonElement typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                return;
            }

            type = typeElement.GetString() ?? string.Empty;
            if (root.TryGetProperty("line", out JsonElement lineElement))
            {
                if (lineElement.ValueKind != JsonValueKind.String)
                {
                    return;
                }

                line = lineElement.GetString();
            }
        }
        catch (JsonException)
        {
            return;
        }

        switch (type)
        {
            case "complete" when line is not null:
                Answer(Suggestions(CommandBarRouter.Suggest(line, Projects())));
                break;
            case "run" when line is not null:
                await RunAsync(line).ConfigureAwait(false);
                break;
            case "close":
                Close();
                break;
            default:
                return;
        }
    }

    /// <summary>
    /// The one line shown for a Times run: the first non-empty line of stdout, else of stderr, without ANSI CSI
    /// sequences, through <see cref="Shorten"/>, cut to <see cref="MaxResultLength"/> plus <c>…</c>.
    /// </summary>
    public static CommandBarResult TimesResult(TimesRunResult result)
    {
        switch (result.Kind)
        {
            case TimesRunKind.NotFound:
                return CommandBarResult.Error(NotFoundText);
            case TimesRunKind.TimedOut:
                return CommandBarResult.Error(TimedOutText);
        }

        string text = FirstLine(result.Stdout);
        if (text.Length == 0)
        {
            text = FirstLine(result.Stderr);
        }

        if (result.ExitCode == 0)
        {
            return CommandBarResult.Done(text.Length == 0 ? DoneText : text);
        }

        return CommandBarResult.Error(text.Length == 0
            ? "Times failed (exit " + result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")."
            : text);
    }

    /// <summary>
    /// A text as one shown line: through <see cref="DesktopText.NoteName"/> (cleaned, one line, bidi controls as the
    /// placeholder), trimmed, at most <see cref="MaxResultLength"/> plus <c>…</c>.
    /// </summary>
    public static string Shorten(string text)
    {
        string line = DesktopText.NoteName(text).Trim();
        if (line.Length <= MaxResultLength)
        {
            return line;
        }

        int cut = MaxResultLength;
        if (char.IsHighSurrogate(line[cut - 1]))
        {
            cut--;
        }

        return line[..cut] + "…";
    }

    private static string FirstLine(string output)
    {
        string plain = AnsiCsi().Replace(output ?? string.Empty, string.Empty);
        foreach (string raw in plain.Split(['\r', '\n']))
        {
            string line = Shorten(raw);
            if (line.Length > 0)
            {
                return line;
            }
        }

        return string.Empty;
    }

    private async Task RunAsync(string line)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            return;
        }

        try
        {
            CommandBarResult result = await RouteAsync(line).ConfigureAwait(false);
            Answer(Result(result));
            if (result.Ok)
            {
                await delay(CloseDelay).ConfigureAwait(false);
                Close();
            }
        }
        catch (Exception)
        {
            // A throwing runner or pause shows nothing more; the bar stays open.
        }
        finally
        {
            Volatile.Write(ref running, 0);
            Interlocked.Increment(ref runs);
        }
    }

    private async Task<CommandBarResult> RouteAsync(string line)
    {
        CommandBarRoute route = CommandBarRouter.Route(line, Projects());
        switch (route.Target)
        {
            case CommandBarTarget.Times:
                TimesRunResult run;
                try
                {
                    run = await times.Run(route.TimesArguments).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    run = TimesRunResult.NotFound();
                }

                return TimesResult(run);
            case CommandBarTarget.Context:
                CommandBarResult result = await context.Run(route.ContextLine).ConfigureAwait(false);
                return new CommandBarResult(result.Ok, Shorten(result.Text));
            default:
                return CommandBarResult.Error(route.Error);
        }
    }

    private IReadOnlyList<(int WorkItem, string Title)> Projects()
    {
        try
        {
            return projects();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void Close()
    {
        Answer(Message("close", null));
        try
        {
            CloseRequested?.Invoke();
        }
        catch (Exception)
        {
            // The window is gone.
        }
    }

    private void Answer(string json)
    {
        try
        {
            Send?.Invoke(json);
        }
        catch (Exception)
        {
            // The window is gone.
        }
    }

    private static string Message(string type, Action<Utf8JsonWriter>? body)
    {
        using MemoryStream stream = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", type);
            body?.Invoke(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Result(CommandBarResult result) => Message("result", writer =>
    {
        writer.WriteString("kind", result.Ok ? "ok" : "error");
        writer.WriteString("text", result.Text);
    });

    private static string Suggestions(IReadOnlyList<CommandBarSuggestion> suggestions) => Message("suggestions", writer =>
    {
        writer.WriteStartArray("items");
        foreach (CommandBarSuggestion suggestion in suggestions)
        {
            writer.WriteStartObject();
            writer.WriteString("line", suggestion.Line);
            writer.WriteString("label", suggestion.Label);
            writer.WriteString("detail", suggestion.Detail);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    });

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex AnsiCsi();
}
