namespace Context;

/// <summary>One of the desktop window's two sidebars (job 490).</summary>
internal enum SidebarSide
{
    /// <summary>The notes sidebar on the left.</summary>
    Left,

    /// <summary>The todos sidebar on the right.</summary>
    Right,
}

/// <summary>
/// The desktop sidebars' widths in CSS pixels and whether each is collapsed (job 490), stored in <c>settings.json</c>
/// as <c>"sidebars":{"left":{"width":N,"collapsed":B},"right":{…}}</c>. Every step is pure and returns a new layout;
/// widths are always kept inside their side's bounds.
/// </summary>
internal sealed record SidebarLayout(int LeftWidth, bool LeftCollapsed, int RightWidth, bool RightCollapsed)
{
    /// <summary>The narrowest left sidebar.</summary>
    public const int LeftMin = 160;

    /// <summary>The widest left sidebar.</summary>
    public const int LeftMax = 480;

    /// <summary>The left sidebar's default width.</summary>
    public const int LeftDefault = 240;

    /// <summary>The narrowest right sidebar.</summary>
    public const int RightMin = 200;

    /// <summary>The widest right sidebar.</summary>
    public const int RightMax = 520;

    /// <summary>The right sidebar's default width.</summary>
    public const int RightDefault = 280;

    /// <summary>How far one arrow key moves a sidebar's edge.</summary>
    public const int Step = 16;

    /// <summary>Both sidebars open at their default widths.</summary>
    public static SidebarLayout Default { get; } = new(LeftDefault, false, RightDefault, false);

    /// <summary>The narrowest width of <paramref name="side"/>.</summary>
    public static int Min(SidebarSide side) => side == SidebarSide.Left ? LeftMin : RightMin;

    /// <summary>The widest width of <paramref name="side"/>.</summary>
    public static int Max(SidebarSide side) => side == SidebarSide.Left ? LeftMax : RightMax;

    /// <summary>True when <paramref name="width"/> is inside the bounds of <paramref name="side"/>.</summary>
    public static bool IsWidth(SidebarSide side, int width) => width >= Min(side) && width <= Max(side);

    /// <summary><paramref name="width"/> moved into the bounds of <paramref name="side"/>.</summary>
    public static int Clamp(SidebarSide side, int width) => Math.Clamp(width, Min(side), Max(side));

    /// <summary>True when both widths are inside their bounds.</summary>
    public bool IsValid => IsWidth(SidebarSide.Left, LeftWidth) && IsWidth(SidebarSide.Right, RightWidth);

    /// <summary>The width of <paramref name="side"/>.</summary>
    public int Width(SidebarSide side) => side == SidebarSide.Left ? LeftWidth : RightWidth;

    /// <summary>Whether <paramref name="side"/> is collapsed.</summary>
    public bool Collapsed(SidebarSide side) => side == SidebarSide.Left ? LeftCollapsed : RightCollapsed;

    /// <summary>This layout with <paramref name="side"/> at <paramref name="width"/>, clamped to that side's bounds.</summary>
    public SidebarLayout WithWidth(SidebarSide side, int width) => side == SidebarSide.Left
        ? this with { LeftWidth = Clamp(side, width) }
        : this with { RightWidth = Clamp(side, width) };

    /// <summary>This layout with <paramref name="side"/> collapsed or expanded.</summary>
    public SidebarLayout WithCollapsed(SidebarSide side, bool collapsed) => side == SidebarSide.Left
        ? this with { LeftCollapsed = collapsed }
        : this with { RightCollapsed = collapsed };

    /// <summary>This layout with the collapsed state of <paramref name="side"/> flipped.</summary>
    public SidebarLayout Toggled(SidebarSide side) => WithCollapsed(side, !Collapsed(side));

    /// <summary>
    /// The layout after moving the edge of <paramref name="side"/> by <paramref name="delta"/> pixels to the right
    /// (negative: to the left): the left sidebar widens and the right one narrows as its inner edge moves right.
    /// </summary>
    public SidebarLayout EdgeMoved(SidebarSide side, int delta) =>
        WithWidth(side, side == SidebarSide.Left ? Width(side) + delta : Width(side) - delta);
}
