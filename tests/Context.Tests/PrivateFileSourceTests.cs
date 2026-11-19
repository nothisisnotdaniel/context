using System.Text.RegularExpressions;

namespace Context.Tests;

// Job 274 AC-7: files and folders are created only through PrivateFile (0600 / 0700 off Windows). This test reads
// src/**/*.cs as text and fails on any other folder create or file write, apart from the allow-list below.
public sealed partial class PrivateFileSourceTests
{
    private static readonly string[] SkippedFolders = ["bin", "obj", "node_modules"];

    // (file relative to src/, enclosing method) -> why it may write without PrivateFile.
    private static readonly Dictionary<(string File, string Method), string> Allowed = new()
    {
        [("Context.Core/PrivateFile.cs", "OpenNew")] = "the helper itself opens the new 0600 file",
        [("Context.Core/PrivateFile.cs", "Rewrite")] = "the helper itself rewrites a file in place as 0600",
        [("Context.Core/PrivateFile.cs", "CreateFolder")] = "the helper itself creates the 0700 folder",
        [("Context.Core/NoteStore.cs", "WritePlainCopy")] =
            "export and share write to a path the user picks outside the data folder; the umask applies there",
    };

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(Directory\.CreateDirectory\(|File\.WriteAll|File\.AppendAll|File\.Create\(|File\.OpenWrite\(|File\.Open\(|File\.Copy\(|new StreamWriter\(|new FileStream\()")]
    private static partial Regex WriteCall();

    [GeneratedRegex(@"^\s*(?:(?:public|internal|private|protected|static|async|override|sealed|unsafe)\s+)+[\w<>\[\],.?() ]+?\s+(\w+)\s*\(")]
    private static partial Regex MethodDeclaration();

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

    private static IEnumerable<string> SourceFiles(string folder)
    {
        foreach (string file in Directory.GetFiles(folder, "*.cs"))
        {
            yield return file;
        }

        foreach (string sub in Directory.GetDirectories(folder))
        {
            if (SkippedFolders.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string file in SourceFiles(sub))
            {
                yield return file;
            }
        }
    }

    // The statement from the call to the next ';' (a FileStream's arguments can span lines).
    private static string Statement(string[] lines, int index, int column)
    {
        string text = lines[index][column..];
        for (int i = index + 1; !text.Contains(';', StringComparison.Ordinal) && i < lines.Length; i++)
        {
            text += " " + lines[i].Trim();
        }

        int end = text.IndexOf(';', StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }

    // A FileStream that only reads an existing file.
    private static bool IsReadOnlyStream(string statement) =>
        statement.Contains("FileMode.Open,", StringComparison.Ordinal)
        && statement.Contains("FileAccess.Read,", StringComparison.Ordinal);

    private static string? EnclosingMethod(string[] lines, int index)
    {
        for (int i = index; i >= 0; i--)
        {
            Match match = MethodDeclaration().Match(lines[i]);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private static List<string> Hits(string src)
    {
        List<string> hits = [];
        foreach (string file in SourceFiles(src))
        {
            string relative = Path.GetRelativePath(src, file).Replace('\\', '/');
            string[] lines = File.ReadAllText(file).ReplaceLineEndings("\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match match in WriteCall().Matches(lines[i]))
                {
                    if ((match.Value == "new FileStream(" || match.Value == "File.Open(")
                        && IsReadOnlyStream(Statement(lines, i, match.Index)))
                    {
                        continue;
                    }

                    string method = EnclosingMethod(lines, i) ?? "?";
                    if (!Allowed.ContainsKey((relative, method)))
                    {
                        hits.Add($"{relative}:{i + 1} ({method}): {match.Value}");
                    }
                }
            }
        }

        return hits;
    }

    [Fact]
    public void NoFileOrFolderIsCreated_OutsidePrivateFile()
    {
        string src = Path.Combine(RepoRoot(), "src");
        List<string> files = [.. SourceFiles(src)];
        Assert.Contains(files, f => f.EndsWith(Path.Combine("Context.Core", "PrivateFile.cs"), StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith(Path.Combine("Context.Desktop", "CrashLog.cs"), StringComparison.Ordinal));

        Assert.Empty(Hits(src));
    }

    // The allow-list names only places that still write that way, each with a reason.
    [Fact]
    public void AllowList_EntriesAreUsed_AndHaveAReason()
    {
        string src = Path.Combine(RepoRoot(), "src");
        foreach (((string file, string method), string reason) in Allowed)
        {
            Assert.False(string.IsNullOrWhiteSpace(reason));
            string[] lines = File.ReadAllText(Path.Combine(src, file)).ReplaceLineEndings("\n").Split('\n');
            bool used = Enumerable.Range(0, lines.Length)
                .Any(i => WriteCall().IsMatch(lines[i]) && EnclosingMethod(lines, i) == method);
            Assert.True(used, $"{file} {method} no longer writes; remove it from the allow-list.");
        }
    }

    // The scan itself finds a write outside the helper and lets a read-only stream through.
    [Fact]
    public void Scan_FlagsWrites_AndSkipsReadOnlyStreams()
    {
        string src = Path.Combine(Path.GetTempPath(), "context-guard-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(src, "Context.Core"));
            File.WriteAllText(
                Path.Combine(src, "Context.Core", "Sample.cs"),
                "internal static class Sample\n{\n    internal static void Bad(string p)\n    {\n"
                    + "        System.IO.File.WriteAllText(p, \"x\");\n"
                    + "        using var s = new FileStream(\n            p, FileMode.Open, FileAccess.Read, FileShare.Read);\n"
                    + "        using var w = new FileStream(p, FileMode.Create);\n"
                    + "        PrivateFile.OpenNew(p);\n"
                    + "        File.Copy(p, p + \".bak\");\n"
                    + "        using var r = File.Open(p, FileMode.Open, FileAccess.Read, FileShare.Read);\n    }\n}\n");

            List<string> hits = Hits(src);

            Assert.Equal(
                [
                    "Context.Core/Sample.cs:5 (Bad): File.WriteAll",
                    "Context.Core/Sample.cs:8 (Bad): new FileStream(",
                    "Context.Core/Sample.cs:10 (Bad): File.Copy(",
                ],
                hits);
        }
        finally
        {
            Directory.Delete(src, recursive: true);
        }
    }
}
