using System.Text.Json;

namespace Context.Tests;

/// <summary>The command bar's message handler with a fake Times runner and a held close pause (job 282).</summary>
public sealed class CommandBarHostTests
{
    // The bound on every await of work the test holds; it passes at once when the code is right.
    private static readonly TimeSpan HeldWorkTimeout = TimeSpan.FromSeconds(30);

    private static readonly IReadOnlyList<(int WorkItem, string Title)> Quicklist =
    [
        (4711, "Billing export"),
        (1234, "Review queue"),
    ];

    private readonly FakeTimesRunner times = new FakeTimesRunner();
    private readonly CommandBarContext context = new CommandBarContext();
    private readonly List<string> sent = [];
    private readonly TaskCompletionSource pause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CommandBarHost host;
    private int closeRequests;
    private int pauses;
    private TimeSpan pauseAsked;

    public CommandBarHostTests()
    {
        host = new CommandBarHost(times, context, () => Quicklist, Delay);
        host.Send += json =>
        {
            lock (sent)
            {
                sent.Add(json);
            }
        };
        host.CloseRequested += () => Interlocked.Increment(ref closeRequests);
    }

    private List<string> Sent
    {
        get
        {
            lock (sent)
            {
                return sent.ToList();
            }
        }
    }

    private Task Delay(TimeSpan span)
    {
        pauseAsked = span;
        Interlocked.Increment(ref pauses);
        return pause.Task;
    }

    private static string Run(string line) => JsonSerializer.Serialize(new { type = "run", line });

    private static string Complete(string line) => JsonSerializer.Serialize(new { type = "complete", line });

    private static (string Type, string Kind, string Text) Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string type = root.GetProperty("type").GetString()!;
        string kind = root.TryGetProperty("kind", out JsonElement k) ? k.GetString()! : string.Empty;
        string text = root.TryGetProperty("text", out JsonElement t) ? t.GetString()! : string.Empty;
        return (type, kind, text);
    }

    // AC-2: the words reach the runner as separate arguments, unchanged, shell characters included.
    [Fact]
    public async Task Run_TimesLine_PassesWordsAsArguments()
    {
        times.Result = new TimesRunResult(TimesRunKind.Done, 1, string.Empty, "no");

        await host.Receive(Run("tm cp 9:30 1234 a;b $(x) \"q\" |y"));

        IReadOnlyList<string> call = Assert.Single(times.Calls);
        Assert.Equal(["cp", "9:30", "#2", "a;b", "$(x)", "\"q\"", "|y"], call);
    }

    // AC-7: an ok result shows, and the bar closes only after the pause the test completes.
    [Fact]
    public async Task Run_Ok_ClosesAfterThePause()
    {
        times.Result = new TimesRunResult(TimesRunKind.Done, 0, "Arrived at 09:00.\nmore", string.Empty);

        Task running = host.Receive(Run("a"));

        Assert.Equal([("result", "ok", "Arrived at 09:00.")], Sent.Select(Parse));
        Assert.False(running.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref closeRequests));
        Assert.Equal(CommandBarHost.CloseDelay, pauseAsked);
        Assert.Equal(TimeSpan.FromSeconds(1.5), pauseAsked);

        pause.SetResult();
        await running.WaitAsync(HeldWorkTimeout);

        Assert.Equal("close", Parse(Sent[^1]).Type);
        Assert.Equal(1, Volatile.Read(ref closeRequests));
        Assert.Equal(1, host.Runs);
    }

    // AC-7: an error stays: no pause, no close.
    [Fact]
    public async Task Run_Error_StaysOpen()
    {
        times.Result = new TimesRunResult(TimesRunKind.Done, 2, string.Empty, "\u001b[31mTimes is running.\u001b[0m");

        await host.Receive(Run("a"));

        Assert.Equal([("result", "error", "Times is running.")], Sent.Select(Parse));
        Assert.Equal(0, Volatile.Read(ref pauses));
        Assert.Equal(0, Volatile.Read(ref closeRequests));
    }

    // AC-7: a second run while one is running is ignored.
    [Fact]
    public async Task Run_WhileRunning_IsIgnored()
    {
        TaskCompletionSource held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        times.Hold = held.Task;
        times.Result = new TimesRunResult(TimesRunKind.Done, 1, "bad", string.Empty);

        Task first = host.Receive(Run("a"));
        await host.Receive(Run("l")).WaitAsync(HeldWorkTimeout);

        Assert.Single(times.Calls);
        held.SetResult();
        await first.WaitAsync(HeldWorkTimeout);

        Assert.Equal("a", Assert.Single(Assert.Single(times.Calls)));
        Assert.Single(Sent);

        await host.Receive(Run("l"));
        Assert.Equal(2, times.Calls.Count);
    }

    // AC-7: the close pause counts as running too.
    [Fact]
    public async Task Run_DuringThePause_IsIgnored()
    {
        Task first = host.Receive(Run("a"));
        await host.Receive(Run("l")).WaitAsync(HeldWorkTimeout);

        Assert.Single(times.Calls);
        pause.SetResult();
        await first.WaitAsync(HeldWorkTimeout);
    }

    // AC-7: close (Esc or blur) runs nothing and closes.
    [Fact]
    public async Task Close_RunsNothing()
    {
        await host.Receive("{\"type\":\"close\"}");

        Assert.Empty(times.Calls);
        Assert.Equal([("close", "", "")], Sent.Select(Parse));
        Assert.Equal(1, Volatile.Read(ref closeRequests));
    }

    // AC-1: a refused line shows its text and runs nothing.
    [Theory]
    [InlineData("   ", CommandBarRouter.EmptyText)]
    [InlineData("tm", CommandBarRouter.EmptyText)]
    public async Task Run_RefusedLine_ShowsError(string line, string text)
    {
        await host.Receive(Run(line));

        Assert.Empty(times.Calls);
        Assert.Equal([("result", "error", text)], Sent.Select(Parse));
    }

    [Fact]
    public async Task Run_TooLong_ShowsError()
    {
        await host.Receive(Run("a " + new string('x', CommandBarRouter.MaxLineLength)));

        Assert.Empty(times.Calls);
        Assert.Equal([("result", "error", CommandBarRouter.TooLongText)], Sent.Select(Parse));
    }

    // AC-4: the result line rules.
    [Theory]
    [InlineData(0, "\n\n  Booked.  \nsecond", "", "ok", "Booked.")]
    [InlineData(0, "", "warn", "ok", "warn")]
    [InlineData(0, "", "", "ok", CommandBarHost.DoneText)]
    [InlineData(3, "", "", "error", "Times failed (exit 3).")]
    [InlineData(1, "\u001b[1;31mNo session.\u001b[0m", "x", "error", "No session.")]
    [InlineData(1, "a\u0007b\u202Ec", "", "error", "a\uFFFDb\uFFFDc")]
    [InlineData(0, "\r\nBooked.\r\n", "", "ok", "Booked.")]
    [InlineData(0, "\u001b]8;;https://x.test\u0007Booked\u001b]8;;\u0007.", "", "ok", "Booked.")]
    [InlineData(0, "\u001b]0;title\u001b\\Booked.", "", "ok", "Booked.")]
    [InlineData(1, "   \r\n", "\u001b[2Kstill", "error", "still")]
    public void TimesResult_Lines(int exit, string stdout, string stderr, string kind, string text)
    {
        CommandBarResult result = CommandBarHost.TimesResult(new TimesRunResult(TimesRunKind.Done, exit, stdout, stderr));

        Assert.Equal(kind == "ok", result.Ok);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void TimesResult_LongLine_IsCut()
    {
        CommandBarResult result = CommandBarHost.TimesResult(
            new TimesRunResult(TimesRunKind.Done, 0, new string('x', 500), string.Empty));

        Assert.Equal(new string('x', CommandBarHost.MaxResultLength) + "…", result.Text);
    }

    [Fact]
    public void Shorten_NeverSplitsASurrogatePair()
    {
        string text = new string('x', CommandBarHost.MaxResultLength - 1) + "\U0001F600" + "tail";

        Assert.Equal(new string('x', CommandBarHost.MaxResultLength - 1) + "…", CommandBarHost.Shorten(text));
    }

    [Fact]
    public async Task Run_NotFound_ShowsError()
    {
        times.Result = TimesRunResult.NotFound();

        await host.Receive(Run("a"));

        Assert.Equal([("result", "error", "Can't find the tm command.")], Sent.Select(Parse));
    }

    [Fact]
    public async Task Run_TimedOut_ShowsError()
    {
        times.Result = TimesRunResult.TimedOut();

        await host.Receive(Run("a"));

        Assert.Equal([("result", "error", "Times didn't answer in 10 seconds.")], Sent.Select(Parse));
    }

    [Fact]
    public async Task Run_RunnerThrows_ShowsNotFound()
    {
        times.Throws = new InvalidOperationException("boom");

        await host.Receive(Run("a"));

        Assert.Equal([("result", "error", CommandBarHost.NotFoundText)], Sent.Select(Parse));
    }

    // AC-5: with no hook the bar says Context isn't ready.
    [Fact]
    public async Task Run_ContextLine_NoHook_IsNotReady()
    {
        await host.Receive(Run("todo today x"));

        Assert.Empty(times.Calls);
        Assert.Equal([("result", "error", "Context isn't ready yet.")], Sent.Select(Parse));
    }

    [Fact]
    public async Task Run_ContextLine_GoesToTheHook()
    {
        List<string> lines = [];
        context.Register(line =>
        {
            lines.Add(line);
            return Task.FromResult(CommandBarResult.Error("Added:\nx " + new string('y', 300)));
        });

        await host.Receive(Run("  note hello  "));

        Assert.Equal(["note hello"], lines);
        Assert.Empty(times.Calls);
        (string type, string kind, string text) = Parse(Assert.Single(Sent));
        Assert.Equal(("result", "error"), (type, kind));
        Assert.StartsWith("Added: x y", text, StringComparison.Ordinal);
        Assert.Equal(CommandBarHost.MaxResultLength + 1, text.Length);
    }

    [Fact]
    public async Task Run_ContextHookThrows_IsNotReady()
    {
        context.Register(_ => throw new InvalidOperationException("gone"));

        await host.Receive(Run("capture x"));

        Assert.Equal([("result", "error", CommandBarContext.NotReadyText)], Sent.Select(Parse));
    }

    [Fact]
    public async Task Run_UnregisteredHook_IsNotReady()
    {
        Func<string, Task<CommandBarResult>> hook = _ => Task.FromResult(CommandBarResult.Done("Added: x"));
        context.Register(hook);
        context.Unregister(hook);

        await host.Receive(Run("todo today x"));

        Assert.Equal([("result", "error", CommandBarContext.NotReadyText)], Sent.Select(Parse));
    }

    // AC-6: complete answers with the router's suggestions.
    [Fact]
    public async Task Complete_SendsSuggestions()
    {
        await host.Receive(Complete("cp 12"));

        using JsonDocument document = JsonDocument.Parse(Assert.Single(Sent));
        Assert.Equal("suggestions", document.RootElement.GetProperty("type").GetString());
        JsonElement item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("cp 1234 ", item.GetProperty("line").GetString());
        Assert.Equal("1234", item.GetProperty("label").GetString());
        Assert.Equal("Review queue", item.GetProperty("detail").GetString());
        Assert.Empty(times.Calls);
    }

    [Fact]
    public async Task Complete_ProjectsThrow_SuggestsNames()
    {
        CommandBarHost broken = new CommandBarHost(times, context, () => throw new IOException("x"), Delay);
        List<string> answers = [];
        broken.Send += answers.Add;

        await broken.Receive(Complete("cp "));
        await broken.Receive(Complete("a"));

        Assert.Equal(2, answers.Count);
        Assert.Contains("\"items\":[]", answers[0], StringComparison.Ordinal);
        Assert.Contains("arrive ", answers[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Focus_SendsFocus()
    {
        host.Focus();

        Assert.Equal([("focus", "", "")], Sent.Select(Parse));
    }

    // AC-9: malformed, unknown or oversize messages get no answer and run nothing.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("\"run\"")]
    [InlineData("{}")]
    [InlineData("{\"type\":1,\"line\":\"a\"}")]
    [InlineData("{\"type\":\"run\"}")]
    [InlineData("{\"type\":\"run\",\"line\":5}")]
    [InlineData("{\"type\":\"run\",\"line\":[\"a\"]}")]
    [InlineData("{\"type\":\"complete\",\"line\":null}")]
    [InlineData("{\"type\":\"delete\",\"line\":\"a\"}")]
    [InlineData("{\"type\":\"RUN\",\"line\":\"a\"}")]
    [InlineData("{\"type\":\"run\",\"line\":\"a\"")]
    public async Task Receive_BadMessage_IsIgnored(string? json)
    {
        await host.Receive(json);

        Assert.Empty(Sent);
        Assert.Empty(times.Calls);
        Assert.Equal(0, Volatile.Read(ref closeRequests));
    }

    [Fact]
    public async Task Receive_OverFourKiB_IsIgnored()
    {
        string padding = new string(' ', CommandBarHost.MaxMessageBytes);
        string json = "{\"type\":\"run\",\"line\":\"a\"}" + padding;

        await host.Receive(json);

        Assert.Equal(4096, CommandBarHost.MaxMessageBytes);
        Assert.Empty(Sent);
        Assert.Empty(times.Calls);
    }

    [Fact]
    public async Task Receive_AtFourKiB_Runs()
    {
        string head = "{\"type\":\"run\",\"line\":\"a\"}";
        string json = head + new string(' ', CommandBarHost.MaxMessageBytes - head.Length);
        times.Result = new TimesRunResult(TimesRunKind.Done, 1, "x", string.Empty);

        await host.Receive(json);

        Assert.Single(times.Calls);
    }

    [Fact]
    public async Task Send_Throwing_NeverEscapes()
    {
        CommandBarHost throwing = new CommandBarHost(times, context, () => Quicklist, _ => Task.CompletedTask);
        throwing.Send += _ => throw new InvalidOperationException("window gone");
        throwing.CloseRequested += () => throw new InvalidOperationException("window gone");

        await throwing.Receive(Run("a"));
        await throwing.Receive("{\"type\":\"close\"}");

        Assert.Equal(1, throwing.Runs);
    }
}
