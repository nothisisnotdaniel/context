using System.Text.RegularExpressions;

namespace Context.Tests;

// 147: SensitiveFlowTests waits for work behind Task.Run (real ECDH and AES-GCM), so every WaitForAssertion passes the
// class's bounded Wait constant; no test file changes bUnit's global default wait instead.
public sealed class WaitTimeoutSourceTests
{
    private const string SensitiveFlowFile = "tests/Context.Desktop.Tests/SensitiveFlowTests.cs";
    private const string TestsFolder = "tests/Context.Desktop.Tests";
    private const string Call = "WaitForAssertion(";

    // Spelled in pieces so this file does not hold the name it looks for.
    private static readonly Regex DefaultAssigned = new(
        @"\b" + "DefaultWait" + @"Timeout\s*=(?!=)", RegexOptions.CultureInvariant);

    private static readonly Regex WaitConstant = new(
        @"private\s+static\s+readonly\s+TimeSpan\s+Wait\s*=\s*TimeSpan\.FromSeconds\((\d+)\)\s*;",
        RegexOptions.CultureInvariant);

    [Fact]
    public void SensitiveFlowTests_EveryWaitForAssertion_PassesTheWaitConstant()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), SensitiveFlowFile));

        Match constant = Assert.Single(WaitConstant.Matches(source));
        int seconds = int.Parse(constant.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(seconds, 10, 30);

        List<string> missing = [];
        int calls = 0;
        for (int start = source.IndexOf(Call, StringComparison.Ordinal); start >= 0;
             start = source.IndexOf(Call, start + Call.Length, StringComparison.Ordinal))
        {
            calls++;
            string last = LastArgument(source, start + Call.Length);
            if (last != "Wait")
            {
                int line = source.AsSpan(0, start).Count('\n') + 1;
                missing.Add($"line {line}: last argument is '{last}'");
            }
        }

        Assert.True(calls > 0, "No " + Call + " call found in " + SensitiveFlowFile);
        Assert.Empty(missing);
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
