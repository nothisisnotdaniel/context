using System.Text.RegularExpressions;

namespace Context.Tests;

/// <summary>
/// Job 132: the whole-folder protection is gone from the code, the tests and the conventions file. Job 145: so is
/// the interactive console. These tests read the repository as text.
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

    // 145 AC-1: the interactive console's ten source files and thirteen test files are gone.
    [Fact]
    public void TheInteractiveConsolesFiles_AreGone()
    {
        string root = RepoRoot();

        Assert.Equal(23, RemovedConsole.Files.Distinct().Count());
        Assert.DoesNotContain(RemovedConsole.Files, f => File.Exists(Path.Combine(root, f)));
        Assert.True(File.Exists(Path.Combine(root, "src/Context/Cli.cs")));
    }

    // 145 AC-1: no .cs or .razor file under src/ or tests/ uses one of the console's type names as a whole word.
    // An identifier that only contains one (a kept test method's name) does not count.
    [Fact]
    public void NoSourceFile_UsesAnInteractiveConsoleType()
    {
        string root = RepoRoot();
        List<string> files = [.. Files(Path.Combine(root, "src")), .. Files(Path.Combine(root, "tests"))];
        files = files.Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal)).ToList();

        Assert.True(files.Count > 100, "The scan found too few files to be looking at the repository.");
        Assert.Contains(files, f => f.EndsWith(nameof(RemovedConsole) + ".cs", StringComparison.Ordinal));
        Assert.Empty(TypeHits(root, files));
    }

    // 145 AC-1: the whole-word scan finds a use and leaves a longer identifier alone.
    [Fact]
    public void TheTypeScan_FindsWholeWordsOnly()
    {
        string folder = Path.Combine(Path.GetTempPath(), "context-removed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string name = RemovedConsole.TypeNames[0];
            File.WriteAllText(Path.Combine(folder, "used.cs"), $"x = {name}.Rows(1);");
            File.WriteAllText(Path.Combine(folder, "contained.cs"), $"void {name}_Case() {{ }} // Red{name}Kept");

            Assert.Equal([$"used.cs: {name}"], TypeHits(folder, Files(folder)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // 145 AC-4: Program.cs has one path only, the command line.
    [Fact]
    public void ProgramCs_OnlyRunsTheCommandLine()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Context", "Program.cs"));

        foreach (string word in RemovedConsole.ProgramWords)
        {
            Assert.DoesNotContain(word, source, StringComparison.Ordinal);
        }

        Assert.Single(Regex.Matches(source, Regex.Escape("new Cli(")));
        Assert.Contains("return new Cli(", source, StringComparison.Ordinal);
    }

    // 145 AC-7: the conventions file describes the desktop app and the command line only.
    [Fact]
    public void ClaudeMd_DoesNotMentionTheInteractiveConsole()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md"));

        foreach (string word in RemovedConsole.ConventionWords)
        {
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static List<string> TypeHits(string root, IEnumerable<string> files)
    {
        List<string> hits = [];
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (string name in RemovedConsole.TypeNames)
            {
                if (Regex.IsMatch(text, $@"\b{Regex.Escape(name)}\b"))
                {
                    hits.Add($"{Path.GetRelativePath(root, file).Replace('\\', '/')}: {name}");
                }
            }
        }

        return hits;
    }
}
