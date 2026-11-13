using System.Text.RegularExpressions;

namespace Context.Tests;

/// <summary>
/// Job 132: the whole-folder protection is gone from the code, the tests and the conventions file. These tests read
/// the repository as text.
/// </summary>
public sealed class RemovedFeatureSourceTests
{
    private static readonly string[] SkippedFolders = ["bin", "obj", "node_modules"];

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Context.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    // Every file under the folder, leaving out build output and installed npm packages.
    private static IEnumerable<string> Files(string folder)
    {
        foreach (string file in Directory.GetFiles(folder))
        {
            yield return file;
        }

        foreach (string sub in Directory.GetDirectories(folder))
        {
            if (SkippedFolders.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string file in Files(sub))
            {
                yield return file;
            }
        }
    }

    private static List<string> Hits(string root, IEnumerable<string> files)
    {
        List<string> hits = [];
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            string text = File.ReadAllText(file);
            foreach (string word in RemovedFeature.Words)
            {
                if (relative.Contains(word, StringComparison.OrdinalIgnoreCase)
                    || text.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add($"{relative}: {word}");
                }
            }
        }

        return hits;
    }

    // AC-1: no file under src/ or tests/ names the removed feature, in its text or its file name. No file is left
    // out: the tests that need one of the names take it from RemovedFeature, which spells them in pieces.
    [Fact]
    public void NoFileUnderSrcOrTests_MentionsTheRemovedFeature()
    {
        string root = RepoRoot();
        List<string> files = [.. Files(Path.Combine(root, "src")), .. Files(Path.Combine(root, "tests"))];

        Assert.True(files.Count > 100, "The scan found too few files to be looking at the repository.");
        Assert.Contains(files, f => f.EndsWith(Path.Combine("Context.Core", "NoteStore.cs"), StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith(Path.Combine("Context.Desktop", "Main.razor"), StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith(nameof(RemovedFeatureSourceTests) + ".cs", StringComparison.Ordinal));
        Assert.Empty(Hits(root, files));
    }

    // AC-1: the scan does find the words when they are there.
    [Fact]
    public void TheScan_FindsEveryWord_IgnoringCase()
    {
        string folder = Path.Combine(Path.GetTempPath(), "context-removed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        try
        {
            File.WriteAllText(Path.Combine(folder, "clean.cs"), "nothing here");
            for (int i = 0; i < RemovedFeature.Words.Length; i++)
            {
                File.WriteAllText(
                    Path.Combine(folder, "sub", $"f{i}.cs"), "x " + RemovedFeature.Words[i].ToUpperInvariant() + " y");
            }

            File.WriteAllText(Path.Combine(folder, RemovedFeature.Words[4] + ".razor"), "by name only");

            List<string> hits = Hits(folder, Files(folder));

            Assert.Equal(RemovedFeature.Words.Length + 1, hits.Select(h => h.Split(':')[0]).Distinct().Count());
            Assert.DoesNotContain(hits, h => h.StartsWith("clean.cs", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // AC-10: the conventions file has no section on the removed feature and does not name it anywhere. 136: one line
    // says what is stored plain and that only sensitive notes are sealed, and one entry describes those.
    [Fact]
    public void ClaudeMd_DoesNotMentionTheRemovedFeature_AndSaysWhatIsSealed()
    {
        string root = RepoRoot();
        string path = Path.Combine(root, "CLAUDE.md");
        string text = File.ReadAllText(path);

        Assert.Empty(Hits(root, [path]));
        Assert.DoesNotContain("at rest", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("encrypt", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("decrypt", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ciphertext", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("**Data is plain:**", text, StringComparison.Ordinal);
        Assert.Single(
            text.Split('\n'),
            line => line.Contains("**Data is plain, except sensitive notes:**", StringComparison.Ordinal));
        Assert.Single(text.Split('\n'), line => line.StartsWith("- **Sensitive notes:**", StringComparison.Ordinal));
    }

    // AC-2: Program.cs registers exactly the command classes ConsoleSession does, so the tests on that session hold
    // for the real console.
    [Fact]
    public void ProgramCs_RegistersExactlyTheCommandsOfConsoleSession()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Context", "Program.cs"));

        List<string> registered = Regex.Matches(source, @"\b(\w+)\.Register\(")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.Equal(ConsoleSession.Registrations, registered);
    }
}
