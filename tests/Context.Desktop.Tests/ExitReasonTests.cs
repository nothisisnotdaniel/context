namespace Context.Tests;

// Job 334 AC-7: WM_ENDSESSION's parameters map to an exit reason; logoff wins over close-app, ENDSESSION_CRITICAL is
// ignored.
public sealed class ExitReasonTests
{
    [Theory]
    [InlineData(false, 0L, null)]
    [InlineData(false, 0x80000000L, null)]
    [InlineData(false, 0x1L, null)]
    [InlineData(true, 0L, nameof(ExitReason.Shutdown))]
    [InlineData(true, 0x80000000L, nameof(ExitReason.Logoff))]
    [InlineData(true, 0x1L, nameof(ExitReason.CloseApp))]
    [InlineData(true, 0x80000001L, nameof(ExitReason.Logoff))]
    [InlineData(true, 0x40000000L, nameof(ExitReason.Shutdown))]
    public void FromEndSession_MapsTheFlags(bool ending, long flags, string? expected)
    {
        ExitReason? want = expected is null ? null : Enum.Parse<ExitReason>(expected);
        Assert.Equal(want, ExitReasons.FromEndSession(ending, flags));
    }

    // The fixed codes read back to their reasons; anything else is no reason.
    [Theory]
    [InlineData(nameof(ExitReason.ProcessExit), "process-exit")]
    [InlineData(nameof(ExitReason.Logoff), "logoff")]
    [InlineData(nameof(ExitReason.Shutdown), "shutdown")]
    [InlineData(nameof(ExitReason.CloseApp), "close-app")]
    public void Code_RoundTrips(string name, string code)
    {
        ExitReason reason = Enum.Parse<ExitReason>(name);
        Assert.Equal(code, ExitReasons.Code(reason));
        Assert.Equal(reason, ExitReasons.FromCode(code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Logoff")]
    [InlineData("evil-text")]
    public void FromCode_Unknown_IsNull(string code)
    {
        Assert.Null(ExitReasons.FromCode(code));
    }
}
