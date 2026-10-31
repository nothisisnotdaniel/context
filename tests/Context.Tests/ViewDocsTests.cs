using System.Reflection;
using System.Text.RegularExpressions;

namespace Context.Tests;

// Source-text check of the view records' doc comments: another front end draws the views only from them, so
// the rendering contract of each field must stay right above it. A missing or moved comment fails here.
// Like CoreDependencyTests, this is an allowed exception to "one test class per production class".
public sealed class ViewDocsTests
{
    private const string CurrentViewFile = "src/Context.Core/CurrentView.cs";
    private const string TagsViewFile = "src/Context.Core/TagsView.cs";
    private const string LinksViewFile = "src/Context.Core/LinksView.cs";

    public static TheoryData<string, string, string[]> RequiredWords => new()
    {
        // AC-1
        { CurrentViewFile, "Status", ["NotConfigured", "Fresh", "Stale", "Failed", "CachedAtUtc", "Reason"] },

        // AC-2
        { CurrentViewFile, "CachedAtUtc", ["Stale", "UTC", "null"] },
        { CurrentViewFile, "Reason", ["Stale", "Failed", "fixed", "never an exception message"] },

        // AC-3
        { TagsViewFile, "Kind", ["AllTags", "NotesWithTag", "NoSuchTag", "IndexUnreadable"] },
        { TagsViewFile, "Tag", ["NotesWithTag", "empty"] },
        { TagsViewFile, "Tags", ["AllTags", "empty"] },
        { TagsViewFile, "Notes", ["NotesWithTag", "empty"] },
        { LinksViewFile, "Kind", ["MissingArgument", "NoSuchNote", "NoLinks", "Links", "IndexUnreadable"] },
        { LinksViewFile, "Typed", ["MissingArgument", "NoSuchNote", "empty"] },
        { LinksViewFile, "Note", ["NoLinks", "Links", "null"] },
        { LinksViewFile, "Links", ["Links", "empty"] },
    };

    public static TheoryData<string, string> AllProperties()
    {
        TheoryData<string, string> data = new();
        (Type Type, string File)[] views =
        [
            (typeof(CurrentView), CurrentViewFile),
            (typeof(TagsView), TagsViewFile),
            (typeof(LinksView), LinksViewFile),
        ];
        foreach ((Type type, string file) in views)
        {
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                data.Add(file, property.Name);
            }
        }

        return data;
    }

    // AC-1, AC-2, AC-3
    [Theory]
    [MemberData(nameof(RequiredWords))]
    public void DocBlock_NamesRequiredWords(string file, string property, string[] words)
    {
        string doc = DocAbove(file, property);

        Assert.Contains("<summary>", doc, StringComparison.Ordinal);
        foreach (string word in words)
        {
            Assert.True(
                doc.Contains(word, StringComparison.Ordinal),
                $"The doc of {property} in {file} doesn't mention '{word}':\n{doc}");
        }
    }

    // AC-2: every public property of the three views (including Today and both Kinds) has a summary.
    [Theory]
    [MemberData(nameof(AllProperties))]
    public void EveryPublicProperty_HasSummary(string file, string property)
    {
        Assert.Contains("<summary>", DocAbove(file, property), StringComparison.Ordinal);
    }

    // The theory above must really cover the properties the spec names.
    [Fact]
    public void AllProperties_IncludeTodayAndBothKinds()
    {
        List<(string File, string Property)> rows = AllProperties()
            .Select(r => ((string)r[0], (string)r[1]))
            .ToList();

        Assert.Contains((CurrentViewFile, "Today"), rows);
        Assert.Contains((CurrentViewFile, "Status"), rows);
        Assert.Contains((TagsViewFile, "Kind"), rows);
        Assert.Contains((LinksViewFile, "Kind"), rows);
        Assert.Equal(12, rows.Count(r => r.File == CurrentViewFile));
    }

    // Control: the lookup stays inside the view record, so DueTodo.Due (same file) is not taken for CurrentView.Due.
    [Fact]
    public void DocAbove_OnlyReadsTheViewRecord()
    {
        string doc = DocAbove(CurrentViewFile, "Due");

        Assert.Contains("due today or earlier", doc, StringComparison.Ordinal);
    }

    // The contiguous /// lines directly above `public <type> <property> { get; }` inside the file's view record
    // (named after the file); empty when there are none.
    private static string DocAbove(string file, string property)
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepoRoot(), file));
        string record = "internal sealed record " + Path.GetFileNameWithoutExtension(file);
        int start = Array.FindIndex(lines, l => l.StartsWith(record, StringComparison.Ordinal));
        Assert.True(start >= 0, $"No '{record}' in {file}.");
        int end = Array.FindIndex(lines, start + 1, l => l == "}");
        Assert.True(end > start, $"No end of '{record}' in {file}.");

        Regex declaration = new(@"^\s*public\s+\S+\s+" + Regex.Escape(property) + @"\s*\{\s*get;\s*\}\s*$");
        List<int> matches = Enumerable.Range(start, end - start).Where(i => declaration.IsMatch(lines[i])).ToList();
        Assert.True(matches.Count == 1, $"Expected one declaration of {property} in {record}, found {matches.Count}.");

        List<string> doc = [];
        for (int i = matches[0] - 1; i >= 0 && lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal); i--)
        {
            doc.Insert(0, lines[i].Trim());
        }

        return string.Join("\n", doc);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Context.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail("No folder containing Context.slnx found above " + AppContext.BaseDirectory);
        return string.Empty;
    }
}
