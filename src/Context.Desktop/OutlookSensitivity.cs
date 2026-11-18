namespace Context;

/// <summary>
/// Decides from an Outlook item's <c>Sensitivity</c> (OlSensitivity) whether Context must keep it out: only Normal (0)
/// and Personal (1) are shown. Private (2), Confidential (3), any other number, a missing value and anything that isn't
/// a number are hidden, so an item whose sensitivity can't be read fails closed. Always on, with no setting.
/// </summary>
internal static class OutlookSensitivity
{
    private const int Normal = 0;
    private const int Personal = 1;

    /// <summary>True unless <paramref name="sensitivity"/> is a number equal to 0 (Normal) or 1 (Personal).</summary>
    internal static bool IsPrivate(object? sensitivity)
    {
        decimal? value = sensitivity switch
        {
            int i => i,
            long l => l,
            short s => s,
            byte b => b,
            sbyte sb => sb,
            uint ui => ui,
            ulong ul => ul,
            ushort us => us,
            decimal d => d,
            double d when double.IsFinite(d) && Math.Abs(d) < 1e15 => (decimal)d,
            float f when float.IsFinite(f) && Math.Abs(f) < 1e15f => (decimal)f,
            _ => null,
        };
        return value is not (Normal or Personal);
    }
}
