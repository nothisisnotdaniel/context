namespace Context.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3+abc123", "1.2.3")]
    [InlineData("1.2.3-beta.1+abc", "1.2.3-beta.1")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData(" 1.2.3 +abc", "1.2.3")]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    [InlineData("  ", "unknown")]
    [InlineData("+abc", "unknown")]
    public void Format_StripsBuildMetadataAndFallsBackToUnknown(string? informationalVersion, string expected)
    {
        Assert.Equal(expected, AppVersion.Format(informationalVersion));
    }

    [Fact]
    public void Current_IsTheProjectVersionWithoutBuildMetadata()
    {
        Assert.Equal("0.1.0", AppVersion.Current);
        Assert.DoesNotContain('+', AppVersion.Current);
    }
}
