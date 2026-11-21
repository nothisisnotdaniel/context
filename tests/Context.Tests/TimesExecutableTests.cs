namespace Context.Tests;

/// <summary>Where the bar finds <c>tm</c> (job 282, AC-4, AC-10), with a fake file check and fixed separators.</summary>
public sealed class TimesExecutableTests
{
    [Fact]
    public void Find_HomeToolsFolder_ComesFirst()
    {
        HashSet<string> files = ["/home/u/.dotnet/tools/tm", "/usr/bin/tm"];

        Assert.Equal("/home/u/.dotnet/tools/tm", TimesExecutable.Find("/home/u", "/usr/bin", false, files.Contains));
    }

    [Fact]
    public void Find_Windows_LooksForTmExe()
    {
        HashSet<string> files = [@"C:\Users\u\.dotnet\tools\tm.exe", @"C:\Users\u\.dotnet\tools\tm"];

        Assert.Equal(@"C:\Users\u\.dotnet\tools\tm.exe", TimesExecutable.Find(@"C:\Users\u", null, true, files.Contains));
    }

    [Fact]
    public void Find_Windows_PathInOrder_QuotesStripped()
    {
        HashSet<string> files = [@"D:\second\tm.exe", @"E:\third\tm.exe"];

        Assert.Equal(
            @"D:\second\tm.exe",
            TimesExecutable.Find(@"C:\Users\u", @"C:\first;""D:\second"";E:\third", true, files.Contains));
    }

    [Fact]
    public void Find_Path_InOrder()
    {
        HashSet<string> files = ["/opt/b/tm", "/opt/c/tm"];

        Assert.Equal("/opt/b/tm", TimesExecutable.Find("/home/u", "/opt/a:/opt/b:/opt/c", false, files.Contains));
    }

    [Fact]
    public void Find_Path_SkipsEmptyAndRelativeEntries()
    {
        HashSet<string> files = ["tm", "bin/tm", "/opt/c/tm"];

        Assert.Equal("/opt/c/tm", TimesExecutable.Find(null, "::bin: :/opt/c", false, files.Contains));
    }

    [Fact]
    public void Find_Nothing_IsNull()
    {
        Assert.Null(TimesExecutable.Find("/home/u", "/usr/bin:/bin", false, _ => false));
        Assert.Null(TimesExecutable.Find(null, null, false, _ => true));
    }

    [Fact]
    public void Find_AFileNamedTimes_IsNotFound()
    {
        HashSet<string> files = ["/home/u/.dotnet/tools/times", "/usr/bin/times", "/usr/bin/Times"];

        Assert.Null(TimesExecutable.Find("/home/u", "/usr/bin", false, files.Contains));
    }

    [Fact]
    public void Find_ThrowingCheck_CountsAsMissing()
    {
        Assert.Equal(
            "/opt/b/tm",
            TimesExecutable.Find("/home/u", "/opt/b", false, p => p.StartsWith("/home", StringComparison.Ordinal)
                ? throw new UnauthorizedAccessException()
                : p == "/opt/b/tm"));
    }
}
