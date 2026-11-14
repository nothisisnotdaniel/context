using System.Reflection;
using System.Runtime.CompilerServices;

namespace Context.Tests;

// Architecture test: the desktop app and its tests are public-signed with the committed public key in Context.snk,
// and the app names its one friend by name and key (see "Strong name" in CLAUDE.md).
public sealed class DesktopStrongNameTests
{
    // AC-2
    [Fact]
    public void DesktopAndTests_CarryTheCommittedPublicKey()
    {
        byte[] expected = SnkBytes();
        Assert.NotEmpty(expected);

        foreach (Assembly assembly in new[] { typeof(DesktopState).Assembly, typeof(DesktopStrongNameTests).Assembly })
        {
            byte[]? key = assembly.GetName().GetPublicKey();
            Assert.True(key is { Length: > 0 }, assembly.GetName().Name + " has no public key");
            Assert.Equal(expected, key);
        }
    }

    // AC-3
    [Fact]
    public void Desktop_GrantsOnlyItsTestsByNameAndKey()
    {
        Assembly desktop = typeof(DesktopState).Assembly;
        Assert.Equal("Context.Desktop", desktop.GetName().Name);

        Assert.Equal(["Context.Desktop.Tests, PublicKey=" + Convert.ToHexStringLower(SnkBytes())], Friends(desktop));
    }

    // AC-3
    [Fact]
    public void DesktopTests_GrantNoFriends()
    {
        Assert.Empty(Friends(typeof(DesktopStrongNameTests).Assembly));
    }

    private static string[] Friends(Assembly assembly) =>
        [.. assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName)];

    private static byte[] SnkBytes() => File.ReadAllBytes(Path.Combine(RepoFiles.Root(), "Context.snk"));
}
