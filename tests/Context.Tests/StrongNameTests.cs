using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Context.Tests;

// Architecture test: every assembly is public-signed with the committed public key in Context.snk, and every
// InternalsVisibleTo names its friend by name and key (see "Strong name" in CLAUDE.md).
// Like CoreDependencyTests, it is an exception to "one test class per production class".
public sealed class StrongNameTests
{
    private static readonly string[] ConfigFiles =
    [
        "Directory.Build.props",
        "src/Context.Core/Context.Core.csproj",
        "src/Context/Context.csproj",
    ];

    // AC-1
    [Fact]
    public void CoreAppAndTests_CarryTheCommittedPublicKey()
    {
        byte[] expected = SnkBytes();
        Assert.NotEmpty(expected);

        foreach (Assembly assembly in new[] { CoreAssembly, AppAssembly, typeof(StrongNameTests).Assembly })
        {
            byte[]? key = assembly.GetName().GetPublicKey();
            Assert.True(key is { Length: > 0 }, assembly.GetName().Name + " has no public key");
            Assert.Equal(expected, key);
        }
    }

    // AC-3
    [Fact]
    public void Core_GrantsItsFourFriendsByNameAndKey()
    {
        string hex = SnkHex();

        Assert.Equal(
            new[] { "Context", "Context.Desktop", "Context.Desktop.Tests", "Context.Tests" }
                .Select(name => name + ", PublicKey=" + hex),
            Friends(CoreAssembly).Order(StringComparer.Ordinal));
    }

    // AC-3
    [Fact]
    public void App_GrantsOnlyItsTestsByNameAndKey()
    {
        Assert.Equal(["Context.Tests, PublicKey=" + SnkHex()], Friends(AppAssembly));
    }

    // AC-3
    [Fact]
    public void TestAssembly_GrantsNoFriends()
    {
        Assert.Empty(Friends(typeof(StrongNameTests).Assembly));
    }

    // AC-4
    [Fact]
    public void SameNameWithoutKey_CannotCallCoreInternals()
    {
        Func<string, long, byte[]> read = EmitBoundedFileReader(new AssemblyName("Context.Desktop"));

        Assert.ThrowsAny<MemberAccessException>(() => read("unused", 1));
    }

    // AC-4
    [Fact]
    public void SameNameWithKey_CanCallCoreInternals()
    {
        AssemblyName name = new AssemblyName("Context.Desktop");
        name.SetPublicKey(SnkBytes());
        Func<string, long, byte[]> read = EmitBoundedFileReader(name);

        string folder = Path.Combine(Path.GetTempPath(), "context-strongname-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "a.md");
            File.WriteAllBytes(file, [1, 2, 3]);

            Assert.Equal([1, 2, 3], read(file, 10));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // AC-5
    [Fact]
    public void Repo_TracksNoKeyFileButThePublicKey()
    {
        Assert.Equal(["Context.snk"], KeyFiles(RepoRoot()));
    }

    // AC-5
    [Fact]
    public void Props_PublicSignsWithTheCommittedKey()
    {
        string props = File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Build.props"));

        Assert.Contains("<SignAssembly>true</SignAssembly>", props, StringComparison.Ordinal);
        Assert.Contains("<PublicSign>true</PublicSign>", props, StringComparison.Ordinal);
        Assert.Contains("<AssemblyOriginatorKeyFile>$(MSBuildThisFileDirectory)Context.snk</AssemblyOriginatorKeyFile>",
            props, StringComparison.Ordinal);
        Assert.DoesNotContain("<DelaySign>", props, StringComparison.Ordinal);
    }

    // AC-6
    [Theory]
    [MemberData(nameof(ConfigFileNames))]
    public void ConfigFile_DoesNotSuppressCs8002(string file)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), file));

        Assert.DoesNotContain("CS8002", text, StringComparison.OrdinalIgnoreCase);
    }

    // AC-8
    [Fact]
    public void ClaudeMd_LayoutBulletNamesTheKeyFileAndPublicKey()
    {
        string bullet = CoreLayoutBullet(File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md")));

        Assert.Contains("InternalsVisibleTo", bullet, StringComparison.Ordinal);
        Assert.Contains("Context.snk", bullet, StringComparison.Ordinal);
        Assert.Contains("PublicKey", bullet, StringComparison.Ordinal);
    }

    public static TheoryData<string> ConfigFileNames() => [.. ConfigFiles];

    private static Assembly CoreAssembly => typeof(BoundedFile).Assembly;

    private static Assembly AppAssembly => typeof(AppVersion).Assembly;

    private static string[] Friends(Assembly assembly) =>
        [.. assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName)];

    private static byte[] SnkBytes() => File.ReadAllBytes(Path.Combine(RepoRoot(), "Context.snk"));

    private static string SnkHex() => Convert.ToHexStringLower(SnkBytes());

    /// <summary>
    /// A delegate into a new dynamic assembly whose one method calls the internal <c>BoundedFile.Read</c> with a
    /// plain <c>call</c>; the runtime checks access when the method is compiled, on the first call.
    /// </summary>
    private static Func<string, long, byte[]> EmitBoundedFileReader(AssemblyName name)
    {
        MethodInfo target = typeof(BoundedFile).GetMethod(
            nameof(BoundedFile.Read), BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(long)])!;

        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule(name.Name!);
        TypeBuilder type = module.DefineType("Reader", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder method = type.DefineMethod(
            "Read", MethodAttributes.Public | MethodAttributes.Static, typeof(byte[]), [typeof(string), typeof(long)]);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, target);
        il.Emit(OpCodes.Ret);

        Type built = type.CreateType();
        return built.GetMethod("Read")!.CreateDelegate<Func<string, long, byte[]>>();
    }

    /// <summary>The <c>src/Context.Core/</c> bullet of the layout section: its first line up to the next bullet.</summary>
    private static string CoreLayoutBullet(string claudeMd)
    {
        string[] lines = claudeMd.ReplaceLineEndings("\n").Split('\n');
        int start = Array.FindIndex(lines, l => l.StartsWith("  - `src/Context.Core/`", StringComparison.Ordinal));
        Assert.True(start >= 0, "CLAUDE.md has no src/Context.Core/ layout bullet");

        int end = start + 1;
        while (end < lines.Length && !lines[end].TrimStart().StartsWith("- ", StringComparison.Ordinal))
        {
            end++;
        }

        return string.Join("\n", lines[start..end]);
    }

    /// <summary>The tracked key files (<c>*.snk</c>, <c>*.pfx</c>, <c>*.p12</c>, <c>*.pem</c>, <c>*.pvk</c>, <c>*.key</c>), root-relative and sorted.</summary>
    private static List<string> KeyFiles(string root)
    {
        string[] extensions = [".snk", ".pfx", ".p12", ".pem", ".pvk", ".key"];
        List<string>? tracked = GitLsFiles(root);
        IEnumerable<string> candidates = tracked ?? WalkTree(root);

        return [.. candidates
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)];
    }

    private static List<string>? GitLsFiles(string root)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("ls-files");
        startInfo.ArgumentList.Add("-z");

        try
        {
            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries)] : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static IEnumerable<string> WalkTree(string root)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            string[] segments = relative.Split('/');
            if (segments.Any(s => s is "bin" or "obj" or "node_modules" or ".git"))
            {
                continue;
            }

            yield return relative;
        }
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
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
