using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Context.Tests;

// 491: CI once hung in Context.Desktop.Tests until the job was cancelled, with no test named. Every test project now
// runs with the VSTest hang blame (60 s per test, no dump), and no file in the Desktop test project blocks a thread
// without a bound: a blocked wait gives up and fails the test that waited, naming what it waited for.
public sealed class UnboundedWaitSourceTests
{
    private const string TestsFolder = "tests/Context.Desktop.Tests";

    private static readonly string[] Projects =
    [
        "tests/Context.Desktop.Tests/Context.Desktop.Tests.csproj",
        "tests/Context.Tests/Context.Tests.csproj",
    ];

    // Spelled in pieces, and this file is skipped by the scan, so it does not flag itself.
    private static readonly Regex BareWait = new(@"\.Wait" + @"\(\s*\)", RegexOptions.CultureInvariant);

    private static readonly Regex BareWaitOne = new(@"\.WaitOne" + @"\(\s*\)", RegexOptions.CultureInvariant);

    private const string SpinUntil = "SpinWait." + "SpinUntil(";

    private const string GetResult = ".GetAwaiter()" + ".GetResult()";

    private const string WaitAsync = ".WaitAsync" + "(";

    private const string MonitorEnter = "Monitor." + "Enter(";

    private const string MonitorExit = "Monitor." + "Exit(";

    private const string Invocations = "JSInterop." + "Invocations";

    // Spelled with a class, so this file's own pattern text doesn't match itself; the file is skipped by the scan anyway.
    private static readonly Regex FiredEvent = new(
        @"(?<!\bjs)\.(Click|Input|Change|KeyDown|KeyUp|DragStart|Drop)" + @"(Async)?\(", RegexOptions.CultureInvariant);

    public static TheoryData<string> TestSources()
    {
        TheoryData<string> data = [];
        foreach (string file in Discover())
        {
            data.Add(file);
        }

        return data;
    }

    private static List<string> Discover() =>
        RepoFiles.Sources(RepoFiles.Root(), TestsFolder, ".cs")
            .Where(f => Path.GetFileName(f) != nameof(UnboundedWaitSourceTests) + ".cs")
            .ToList();

    // AC-1: both test projects turn the hang blame on through MSBuild properties, so plain dotnet test (locally and
    // in CI) stops a test that runs 60 s and names it.
    [Theory]
    [InlineData("tests/Context.Desktop.Tests/Context.Desktop.Tests.csproj")]
    [InlineData("tests/Context.Tests/Context.Tests.csproj")]
    public void TestProject_SetsTheHangBlame(string project)
    {
        XDocument doc = XDocument.Load(Path.Combine(RepoFiles.Root(), project));

        Assert.Equal("true", Property(doc, "VSTestBlameHang"));
        Assert.Equal("60s", Property(doc, "VSTestBlameHangTimeout"));
        Assert.Equal("none", Property(doc, "VSTestBlameHangDumpType"));
    }

    [Fact]
    public void Projects_AreEveryTestProject()
    {
        string root = RepoFiles.Root();
        string[] found = Directory.GetFiles(Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories)
            .Select(f => RepoFiles.Relative(root, f))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Projects.Order(StringComparer.Ordinal).ToArray(), found);
    }

    // AC-4: no file in the Desktop test project blocks without a bound.
    [Theory]
    [MemberData(nameof(TestSources))]
    public void TestSource_BlocksOnlyWithABound(string file)
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), file));

        Assert.Empty(Problems(source).Select(p => $"{file}: {p}"));
    }

    // bunit records JS calls on the renderer's dispatcher; a test that enumerates them from its own thread while a load
    // the page started by itself records another one fails with "Collection was modified" (job 491, MainTests). So
    // only these files read the list, each on the dispatcher; every other test goes through DesktopFixture.JsCalls.
    [Fact]
    public void JsCalls_AreReadOnlyOnTheDispatcher()
    {
        string[] readers = ["DesktopFixture.cs", "ProSettingsTests.cs", "TopBarTests.cs"];
        string[] found = Discover()
            .Where(f => File.ReadAllText(Path.Combine(RepoFiles.Root(), f)).Contains(Invocations, StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(readers.Order(StringComparer.Ordinal).ToArray(), found);
    }

    // A page that renders again by itself gives its handlers new ids, so an element found from the test thread can
    // name a dropped handler by the time its event is fired (job 491: RecentNotesActionsTests, SimilarPanelTests).
    // Every event goes through StaleClick, which finds the element again; finding and firing inside cut.InvokeAsync
    // (or a helper that does) is atomic on the dispatcher and needs none. A line that records the exception of an event
    // on a stale element on purpose is not a problem.
    [Theory]
    [MemberData(nameof(TestSources))]
    public void TestSource_FiresEventsThroughStaleClick(string file)
    {
        if (Path.GetFileName(file) is "StaleClick.cs" or "StaleClickTests.cs")
        {
            return;
        }

        string[] lines = File.ReadAllLines(Path.Combine(RepoFiles.Root(), file));
        Assert.Empty(EventProblems(lines).Select(p => $"{file}: {p}"));
    }

    // Only the render that created an element carries its blazor:elementReference attribute, so a test reads the id
    // from the component's field through ElementRefs (job 491).
    [Theory]
    [MemberData(nameof(TestSources))]
    public void TestSource_ReadsNoElementReferenceFromMarkup(string file)
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepoFiles.Root(), file));
        string attribute = "GetAttribute(\"blazor:" + "elementReference\")";
        Assert.Empty(lines
            .Select((line, i) => (line, i))
            .Where(l => l.line.Contains(attribute, StringComparison.Ordinal))
            .Select(l => $"{file}: line {l.i + 1}: reads an element reference from the markup (use ElementRefs.Id)"));
    }

    [Theory]
    [InlineData("await cut.Find(\"button.x\").ClickAsync(new MouseEventArgs());")]
    [InlineData("Task click = cut.Find(\"button.x\").ClickAsync(new MouseEventArgs());")]
    [InlineData("await button.ClickAsync(new MouseEventArgs());")]
    [InlineData("await cut.FindAll(\"li\")[1].ChangeAsync(new ChangeEventArgs { Value = true });")]
    [InlineData("await field.InputAsync(new ChangeEventArgs { Value = \"a\" });")]
    [InlineData("PickFocusDay(cut).KeyDownAsync(new KeyboardEventArgs { Key = key });")]
    [InlineData("await cut.Find(\"x\").KeyUpAsync(new KeyboardEventArgs());")]
    [InlineData("cut.Find(\"button.x\").Click();")]
    [InlineData("Box(cut, 1).Change(true);")]
    [InlineData("FocusDay(cut).KeyDown(new KeyboardEventArgs { Key = key });")]
    [InlineData("await Column(cut, \"todo\").DropAsync(new DragEventArgs());")]
    [InlineData("await Card(cut, \"todo\", 0).DragStartAsync(new DragEventArgs());")]
    public void EventProblems_FlagsAnEventOnAFoundElement(string line)
    {
        Assert.Single(EventProblems(["// a comment line", line]), p => p.StartsWith("line 2: ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("await StaleClick.ClickAsync(cut, \"button.x\");")]
    [InlineData("await StaleClick.FireAsync(cut, () => cut.FindAll(\"li\")[1], e => e.ChangeAsync(new ChangeEventArgs()));")]
    [InlineData("await cut.InvokeAsync(() => cut.Find(\"x\").ClickAsync(new MouseEventArgs()));")]
    [InlineData("await Act(cut, () => cut.Find(\"x\").ClickAsync(new MouseEventArgs()));")]
    [InlineData("StaleClick.Fire(() => Box(cut, 1), e => e.ChangeAsync(new ChangeEventArgs { Value = true }));")]
    [InlineData("await js.Change(cut, \"# Plan\\n\");")]
    [InlineData("Assert.IsType<UnknownEventHandlerIdException>(Record.Exception(() => focus.Click()));")]
    public void EventProblems_AcceptsStaleClickAndTheDispatcher(string line)
    {
        Assert.Empty(EventProblems([line]));
    }

    [Fact]
    public void Discovery_FindsTheFakesAndHelpers()
    {
        List<string> names = Discover().Select(f => Path.GetFileName(f)).ToList();

        Assert.Contains("FakeOutlookSource.cs", names);
        Assert.Contains("EditorJs.cs", names);
        Assert.Contains("ProSettingsTests.cs", names);
        Assert.Contains("BoundedWait.cs", names);
    }

    [Theory]
    [InlineData("open.Wait" + "();\n")]
    [InlineData("gate.Wait" + "( );\n")]
    [InlineData("handle.WaitOne" + "();\n")]
    [InlineData("Assert.True(SpinWait." + "SpinUntil(() => done));\n")]
    [InlineData("Assert.True(SpinWait." + "SpinUntil(() => Check(a, b)));\n")]
    [InlineData("int n = task.GetAwaiter()" + ".GetResult();\n")]
    [InlineData("var x = cut.InvokeAsync(() => Read(a, b))\n    .GetAwaiter()" + ".GetResult();\n")]
    [InlineData("Monitor." + "Enter(gate);\ntry { await RunPalette(cut); }\nfinally { Monitor." + "Exit(gate); }\n")]
    public void Problems_FlagsAnUnboundedWait(string source)
    {
        // The source starts on line 2; a problem names the line of the wait, not the comment above it.
        string problem = Assert.Single(Problems("// a comment line\n" + source));
        Assert.Matches(@"^line [23]: ", problem);
    }

    [Theory]
    [InlineData("open.Wait" + "(bound);\n")]
    [InlineData("open.Wait" + "(TimeSpan.FromSeconds(30));\n")]
    [InlineData("handle.WaitOne" + "(Wait);\n")]
    [InlineData("Assert.True(SpinWait." + "SpinUntil(() => done, Wait), \"waited\");\n")]
    [InlineData("Assert.True(SpinWait." + "SpinUntil(() => Check(a, b), Wait));\n")]
    [InlineData("int n = task.WaitAsync" + "(bound).GetAwaiter()" + ".GetResult();\n")]
    [InlineData("var x = cut.InvokeAsync(() => Read(a, b))\n    .WaitAsync" + "(Wait)\n    .GetAwaiter()" + ".GetResult();\n")]
    [InlineData("await click.WaitAsync" + "(Wait);\n")]
    [InlineData("Monitor." + "Enter(gate);\ntry { cut = Render(); }\nfinally { Monitor." + "Exit(gate); }\nawait Later();\n")]
    [InlineData("using (GateHold.Take(gate))\n{\n    await RunPalette(cut);\n}\n")]
    public void Problems_AcceptsABoundedWait(string source)
    {
        Assert.Empty(Problems(source));
    }

    // A bounded read through the dispatcher that runs out names what it waited for.
    [Fact]
    public void BoundedWait_RunsOut_NamesWhatItWaitedFor()
    {
        TaskCompletionSource<int> never = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        TimeoutException e = Assert.Throws<TimeoutException>(
            () => BoundedWait.Result(never.Task, TimeSpan.FromMilliseconds(50), "the test's read"));

        Assert.Equal("the test's read did not finish within 50 ms", e.Message);
    }

    [Fact]
    public void BoundedWait_Finished_GivesTheResult()
    {
        Assert.Equal(7, BoundedWait.Result(Task.FromResult(7), TimeSpan.FromMilliseconds(50), "x"));
    }

    [Theory]
    [InlineData(30_000, "30 s")]
    [InlineData(1_500, "1.5 s")]
    [InlineData(50, "50 ms")]
    public void Describe_ShowsSecondsOrMilliseconds(int milliseconds, string text)
    {
        Assert.Equal(text, BoundedWait.Describe(TimeSpan.FromMilliseconds(milliseconds)));
    }

    // Every line that fires an event on an element outside StaleClick and outside the dispatcher, as "line <n>: ...".
    private static List<string> EventProblems(IReadOnlyList<string> lines)
    {
        List<string> found = [];
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (!FiredEvent.IsMatch(line) || line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            bool safe = line.Contains("StaleClick.", StringComparison.Ordinal)
                || line.Contains("InvokeAsync(() =>", StringComparison.Ordinal)
                || line.Contains("Act(cut, () =>", StringComparison.Ordinal)
                || line.Contains("Record.Exception(", StringComparison.Ordinal);
            if (!safe)
            {
                found.Add($"line {i + 1}: an event fired on a found element (use StaleClick)");
            }
        }

        return found;
    }

    private static string? Property(XDocument doc, string name) =>
        doc.Descendants(name).Select(e => e.Value.Trim()).SingleOrDefault();

    // Every unbounded blocking wait in one source, as "line <n>: <what>".
    private static List<string> Problems(string source)
    {
        List<(int Index, string What)> found = [];
        found.AddRange(BareWait.Matches(source).Select(m => (m.Index, "Wait() without a timeout")));
        found.AddRange(BareWaitOne.Matches(source).Select(m => (m.Index, "WaitOne() without a timeout")));

        for (int start = source.IndexOf(SpinUntil, StringComparison.Ordinal); start >= 0;
             start = source.IndexOf(SpinUntil, start + SpinUntil.Length, StringComparison.Ordinal))
        {
            if (TopLevelArguments(source, start + SpinUntil.Length) < 2)
            {
                found.Add((start, "SpinUntil with no timeout"));
            }
        }

        for (int start = source.IndexOf(GetResult, StringComparison.Ordinal); start >= 0;
             start = source.IndexOf(GetResult, start + GetResult.Length, StringComparison.Ordinal))
        {
            int from = source.LastIndexOf(';', start) + 1;
            if (!source.AsSpan(from, start - from).Contains(WaitAsync, StringComparison.Ordinal))
            {
                found.Add((start, "GetAwaiter().GetResult() with no WaitAsync in its statement"));
            }
        }

        // A Monitor is owned by the thread that entered it; an await between Enter and Exit can resume on another thread,
        // whose Exit throws and leaves the lock held for good (job 491: GlossaryPanelTests hung the run that way).
        for (int start = source.IndexOf(MonitorEnter, StringComparison.Ordinal); start >= 0;
             start = source.IndexOf(MonitorEnter, start + MonitorEnter.Length, StringComparison.Ordinal))
        {
            int exit = source.IndexOf(MonitorExit, start, StringComparison.Ordinal);
            int end = exit < 0 ? source.Length : exit;
            if (Regex.IsMatch(source.AsSpan(start, end - start).ToString(), @"\bawait\b"))
            {
                found.Add((start, "await while a Monitor is held (use GateHold)"));
            }
        }

        return found
            .OrderBy(f => f.Index)
            .Select(f => $"line {source.AsSpan(0, f.Index).Count('\n') + 1}: {f.What}")
            .ToList();
    }

    // The number of arguments of the call whose argument list starts at open (just after its '('): commas at the
    // call's own depth, skipping nested brackets and string or char literals.
    private static int TopLevelArguments(string source, int open)
    {
        int depth = 0;
        int commas = 0;
        bool any = false;
        for (int i = open; i < source.Length; i++)
        {
            char c = source[i];
            if (c is '"' or '\'')
            {
                i = SkipLiteral(source, i);
                any = true;
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0)
                {
                    return any ? commas + 1 : 0;
                }

                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                commas++;
            }

            if (!char.IsWhiteSpace(c))
            {
                any = true;
            }
        }

        return any ? commas + 1 : 0;
    }

    private static int SkipLiteral(string source, int start)
    {
        char quote = source[start];
        for (int i = start + 1; i < source.Length; i++)
        {
            if (source[i] == '\\')
            {
                i++;
            }
            else if (source[i] == quote)
            {
                return i;
            }
        }

        return source.Length;
    }
}
