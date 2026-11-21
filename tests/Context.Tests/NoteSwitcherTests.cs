namespace Context.Tests;

// 364: the quick switcher's matching and order, pure (no folder).
public sealed class NoteSwitcherTests
{
    private static readonly string[] None = [];

    // AC-1: every non-space character of the trimmed query in order, ignoring case.
    [Theory]
    [InlineData("mtg", true)]
    [InlineData("MTG", true)]
    [InlineData("  m t g  ", true)]
    [InlineData("meeting notes", true)]
    [InlineData("gtm", false)]
    [InlineData("mtgx", false)]
    public void Match_Subsequence_IgnoringCase(string query, bool matches)
    {
        IReadOnlyList<string> rows = NoteSwitcher.Match(["Meeting notes"], None, None, query);

        Assert.Equal(matches, rows.Count == 1);
    }

    // AC-1: per-character, ordinal ignore-case: non-ASCII letters match their other case.
    [Fact]
    public void Match_NonAscii_IgnoringCase()
    {
        Assert.Equal(["Café Übersicht"], NoteSwitcher.Match(["Café Übersicht", "Cafe"], None, None, "éü"));
    }

    // AC-2: recent (newest first), then pinned not yet listed, then the rest.
    [Fact]
    public void Match_RecentThenPinnedThenRest()
    {
        string[] names = ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

        IReadOnlyList<string> rows = NoteSwitcher.Match(names, ["Gamma", "Beta"], ["Beta", "Epsilon"], "a");

        Assert.Equal(["Gamma", "Beta", "Alpha", "Delta"], rows);
        Assert.Equal(["Gamma", "Beta", "Epsilon", "Alpha", "Delta"], NoteSwitcher.Match(names, ["Gamma", "Beta"], ["Beta", "Epsilon"], ""));
    }

    // AC-2: a run match before a scattered one, then by name ignoring case, then ordinal.
    [Fact]
    public void Match_Rest_RunFirstThenName()
    {
        string[] names = ["plan b", "Plan", "xPlan", "Pxlxaxn", "PLAN"];

        IReadOnlyList<string> rows = NoteSwitcher.Match(names, None, None, "plan");

        Assert.Equal(["PLAN", "Plan", "plan b", "xPlan", "Pxlxaxn"], rows);
    }

    // AC-2: each name once; recent/pinned entries not in names are left out; at most 50 rows.
    [Fact]
    public void Match_OnceKnownOnlyCapped()
    {
        string[] names = Enumerable.Range(1, 60).Select(i => $"N{i:00}").Append("N01").ToArray();

        IReadOnlyList<string> rows = NoteSwitcher.Match(names, ["Gone", "N30", "N30"], ["N30", "Missing", "N02"], "n");

        Assert.Equal(NoteSwitcher.MaxRows, rows.Count);
        Assert.Equal(["N30", "N02", "N01", "N03"], rows.Take(4));
        Assert.Equal(rows.Count, rows.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("Gone", rows);
        Assert.DoesNotContain("Missing", rows);
    }

    // AC-2: the cap also applies inside the recent and pinned part.
    [Fact]
    public void Match_CapInsideRecent()
    {
        string[] names = Enumerable.Range(1, 60).Select(i => $"N{i:00}").ToArray();

        IReadOnlyList<string> rows = NoteSwitcher.Match(names, names.Reverse().ToArray(), None, "");

        Assert.Equal(names.Reverse().Take(50), rows);
    }

    // AC-2: recent and pinned entries that do not match the query are left out.
    [Fact]
    public void Match_RecentNotMatching_LeftOut()
    {
        Assert.Equal(["Plan"], NoteSwitcher.Match(["Plan", "Other"], ["Other"], ["Other"], "pl"));
    }

    // AC-3: a blank query gives recent, then pinned, then the rest by name.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Match_Blank_RecentPinnedThenByName(string query)
    {
        string[] names = ["delta", "Charlie", "bravo", "Alpha", "Echo"];

        IReadOnlyList<string> rows = NoteSwitcher.Match(names, ["Echo"], ["Charlie"], query);

        Assert.Equal(["Echo", "Charlie", "Alpha", "bravo", "delta"], rows);
    }

    // AC-3: no names, blank query: nothing.
    [Fact]
    public void Match_NoNames_Empty()
    {
        Assert.Empty(NoteSwitcher.Match(None, ["Gone"], ["Gone"], ""));
    }
}
