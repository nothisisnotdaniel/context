using System.Text.RegularExpressions;

namespace Context.Tests;

// 147: SensitiveFlowTests waits for work behind Task.Run (real ECDH and AES-GCM), so every WaitForAssertion passes the
// class's bounded Wait constant; no test file changes bUnit's global default wait instead.
// 150: MainTests and EditingFlowTests open notes through DesktopState.ReadForEdit on Task.Run and follow the same rule.
// 154: TagsPanelTests waits on the tag load behind Task.Run and follows it too.
// 156: PersonPanelTests waits on the person load behind Task.Run and follows it too.
// 174: HistoryPanelTests waits on the history and version loads behind Task.Run and follows it too.
// 175: SettingsPanelTests waits on the settings load (two Task.Run hops) and follows it too.
// 176: the guard finds every test file that waits, so a new file needs no registration.
public sealed class WaitTimeoutSourceTests
{
    private const string TestsFolder = "tests/Context.Desktop.Tests";

    // Spelled in pieces so this file does not hold the name it looks for.
    private static readonly Regex DefaultAssigned = new(
        @"\b" + "DefaultWait" + @"Timeout\s*=(?!=)", RegexOptions.CultureInvariant);

    private static readonly Regex WaitConstant = new(
        @"private\s+static\s+readonly\s+TimeSpan\s+Wait\s*=\s*TimeSpan\.FromSeconds\((\d+)\)\s*;",
        RegexOptions.CultureInvariant);

    private static readonly string[] CallNames = ["WaitForAssertion(", "WaitForState(", "WaitForElement("];

    private static readonly string[] FormerlyListed =
    [
        "SensitiveFlowTests.cs", "MainTests.cs", "EditingFlowTests.cs", "TagsPanelTests.cs",
        "PersonPanelTests.cs", "HistoryPanelTests.cs", "SettingsPanelTests.cs",
    ];

    public static TheoryData<string> WaitingFiles()
    {
        TheoryData<string> data = [];
        foreach (string file in Discover())
        {
            data.Add(file);
        }

        return data;
    }

    private static List<string> Discover()
    {
        string root = RepoFiles.Root();
        return RepoFiles.Sources(root, TestsFolder, ".cs")
            .Where(f => Path.GetFileName(f) != nameof(WaitTimeoutSourceTests) + ".cs")
            .Where(f => CallNames.Any(n => File.ReadAllText(Path.Combine(root, f)).Contains(n, StringComparison.Ordinal)))
            .ToList();
    }

    [Fact]
    public void Discovery_FindsTheFormerlyListedFiles()
    {
        List<string> names = Discover().Select(Path.GetFileName).ToList()!;

        Assert.All(FormerlyListed, f => Assert.Contains(f, names));
        Assert.True(names.Count >= FormerlyListed.Length, $"found only {names.Count} files");
    }

    [Theory]
    [MemberData(nameof(WaitingFiles))]
    public void EveryWait_PassesTheWaitConstant(string file)
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), file));

        Assert.Empty(Problems(source).Select(p => $"{file}: {p}"));
    }

    [Fact]
    public void Problems_FlagsACallWithoutWait()
    {
        string source = "private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);\n"
            + "cut.WaitForAssertion(() => Assert.True(x));\n";

        string problem = Assert.Single(Problems(source));
        Assert.Contains("line 2", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Problems_FlagsTwoConstants()
    {
        string source = "private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);\n"
            + "private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);\n"
            + "cut.WaitForAssertion(() => Assert.True(x), Wait);\n";

        Assert.Contains(Problems(source), p => p.Contains("exactly one", StringComparison.Ordinal));
    }

    [Fact]
    public void Problems_FlagsNoConstant()
    {
        Assert.Contains(
            Problems("cut.WaitForAssertion(() => Assert.True(x), Wait);\n"),
            p => p.Contains("exactly one", StringComparison.Ordinal));
    }

    [Fact]
    public void Problems_AcceptsWaitForStateWithWait()
    {
        string source = "private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);\n"
            + "cut.WaitForState(() => ok, Wait);\n";

        Assert.Empty(Problems(source));
    }

    // Everything wrong with one test source: its Wait constant and the last argument of each wait call.
    private static List<string> Problems(string source)
    {
        List<string> problems = [];
        MatchCollection constants = WaitConstant.Matches(source);
        if (constants.Count != 1)
        {
            problems.Add(
                "needs exactly one 'private static readonly TimeSpan Wait = TimeSpan.FromSeconds(<n>);', "
                + $"found {constants.Count}");
        }
        else
        {
            int seconds = int.Parse(constants[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (seconds is < 10 or > 30)
            {
                problems.Add($"Wait is {seconds} s, expected 10-30");
            }
        }

        foreach (string call in CallNames)
        {
            for (int start = source.IndexOf(call, StringComparison.Ordinal); start >= 0;
                 start = source.IndexOf(call, start + call.Length, StringComparison.Ordinal))
            {
                string last = LastArgument(source, start + call.Length);
                if (last != "Wait")
                {
                    int line = source.AsSpan(0, start).Count('\n') + 1;
                    problems.Add($"line {line}: {call} last argument is '{last}'");
                }
            }
        }

        return problems;
    }

    [Fact]
    public void DesktopTests_NeverAssignTheDefaultWaitTimeout()
    {
        string root = RepoFiles.Root();
        List<string> offenders = RepoFiles.Sources(root, TestsFolder, ".cs", ".razor")
            .Where(f => DefaultAssigned.IsMatch(File.ReadAllText(Path.Combine(root, f))))
            .ToList();

        Assert.Empty(offenders);
    }

    // The text of the last top-level argument of the call whose argument list starts at open (just after '(').
    private static string LastArgument(string source, int open)
    {
        int depth = 1;
        int lastStart = open;
        char quote = '\0';
        int i = open;
        for (; i < source.Length; i++)
        {
            char c = source[i];
            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
                if (depth == 0)
                {
                    break;
                }
            }
            else if (c == ',' && depth == 1)
            {
                lastStart = i + 1;
            }
        }

        return lastStart == open ? string.Empty : source[lastStart..i].Trim();
    }
}
