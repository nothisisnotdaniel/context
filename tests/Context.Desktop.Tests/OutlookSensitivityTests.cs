namespace Context.Tests;

public sealed class OutlookSensitivityTests
{
    public static TheoryData<object> Visible() => new()
    {
        0, 1, 0L, 1L, (short)0, (short)1, (byte)0, (byte)1, (sbyte)1, 0u, 1u, 0UL, 1UL, (ushort)1, 0.0, 1.0, 1f, 0m, 1m,
    };

    public static TheoryData<object?> Hidden() => new()
    {
        2, 3, -1, 4, int.MaxValue, int.MinValue, 2L, (short)3, (byte)2, 3u, ulong.MaxValue, 1.5, -0.5, 2.0,
        double.NaN, double.PositiveInfinity, float.NaN, 1e300, 3m, null, "0", "1", "Normal", true, false, 'a',
        new object(), DayOfWeek.Monday,
    };

    // 227 AC-5: Normal (0) and Personal (1), as int or any other numeric type, are shown.
    [Theory]
    [MemberData(nameof(Visible))]
    public void IsPrivate_NormalOrPersonal_False(object sensitivity)
    {
        Assert.False(OutlookSensitivity.IsPrivate(sensitivity));
    }

    // 227 AC-5: Private (2), Confidential (3), any other number, null and a non-number are hidden (fails closed).
    [Theory]
    [MemberData(nameof(Hidden))]
    public void IsPrivate_OtherwiseTrue(object? sensitivity)
    {
        Assert.True(OutlookSensitivity.IsPrivate(sensitivity));
    }
}
