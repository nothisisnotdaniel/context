namespace Context;

/// <summary>
/// An action of the shown page in the top bar's More actions menu: its id (also the item's class), its label and,
/// for an on/off action such as History, whether it is on (null for a plain action). Public because it is the type of
/// a component parameter (TopBar.PageActions), which must be public.
/// </summary>
public sealed record PageAction
{
    public PageAction(string id, string label, bool? @checked = null)
    {
        Id = id;
        Label = label;
        Checked = @checked;
    }

    public string Id { get; }

    public string Label { get; }

    public bool? Checked { get; }
}
