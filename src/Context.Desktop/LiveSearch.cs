using System.Globalization;

namespace Context;

/// <summary>
/// The search the top bar's Search popover runs as you type (job 492): text in, the <see cref="SearchView"/> it finds.
/// Public only because it is a component parameter (<see cref="SearchView"/> is internal, so the parameter can't be a
/// plain <c>Func</c>); nothing outside this assembly can create or run one.
/// </summary>
public sealed class LiveSearch
{
    private readonly Func<string, Task<SearchView>> search;

    internal LiveSearch(Func<string, Task<SearchView>> search) => this.search = search;

    /// <summary>Searches <paramref name="text"/>.</summary>
    internal Task<SearchView> Run(string text) => search(text);

    /// <summary>
    /// The hint under a list cut at <see cref="TopBar.MaxLiveHits"/> rows: Return shows all <paramref name="count"/>
    /// on the Search page. (Kept here, not in the markup: no <c>.razor</c> file may carry a Return hint, which is
    /// meant for placeholders; this is a list's overflow line.)
    /// </summary>
    internal static string MoreText(int count) =>
        $"Press Return to see all {count.ToString(CultureInfo.InvariantCulture)}.";
}
