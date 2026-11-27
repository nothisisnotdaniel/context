using System.Globalization;

namespace Context.Tests;

/// <summary>
/// Blocking waits with a bound (job 491): a test or fake never blocks a thread forever, so a hang fails the test that
/// caused it with a message that names what it waited for, instead of stalling the whole run.
/// <c>UnboundedWaitSourceTests</c> checks that no file under <c>tests/Context.Desktop.Tests/</c> blocks without one.
/// </summary>
internal static class BoundedWait
{
    /// <summary>The bound of a test's synchronous read through the renderer's dispatcher.</summary>
    public static readonly TimeSpan Dispatcher = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The result of <paramref name="task"/>, waiting at most <paramref name="bound"/>; past it a
    /// <see cref="TimeoutException"/> says that <paramref name="what"/> did not finish in time.
    /// </summary>
    public static T Result<T>(Task<T> task, TimeSpan bound, string what)
    {
        try
        {
            return task.WaitAsync(bound).GetAwaiter().GetResult();
        }
        catch (TimeoutException e) when (!task.IsCompleted)
        {
            throw new TimeoutException($"{what} did not finish within {Describe(bound)}", e);
        }
    }

    /// <summary>A bound as "30 s" or, below a second, "50 ms".</summary>
    public static string Describe(TimeSpan bound) =>
        bound >= TimeSpan.FromSeconds(1)
            ? bound.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " s"
            : bound.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";
}
