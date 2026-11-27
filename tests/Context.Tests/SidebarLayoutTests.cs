namespace Context.Tests;

// 490: the pure sidebar layout steps: bounds, clamping, toggling and moving an edge.
public sealed class SidebarLayoutTests
{
    // 490 AC-1: the default layout and the bounds.
    [Fact]
    public void Default_IsBothOpenAtDefaultWidths()
    {
        Assert.Equal(new SidebarLayout(240, false, 280, false), SidebarLayout.Default);
        Assert.Equal((160, 480), (SidebarLayout.Min(SidebarSide.Left), SidebarLayout.Max(SidebarSide.Left)));
        Assert.Equal((200, 520), (SidebarLayout.Min(SidebarSide.Right), SidebarLayout.Max(SidebarSide.Right)));
        Assert.Equal(16, SidebarLayout.Step);
        Assert.True(SidebarLayout.Default.IsValid);
    }

    // 490 AC-2: a width is in bounds inclusively at both ends.
    [Theory]
    [InlineData(true, 159, false)]
    [InlineData(true, 160, true)]
    [InlineData(true, 480, true)]
    [InlineData(true, 481, false)]
    [InlineData(false, 199, false)]
    [InlineData(false, 200, true)]
    [InlineData(false, 520, true)]
    [InlineData(false, 521, false)]
    public void IsWidth_ChecksTheSidesBounds(bool left, int width, bool expected) =>
        Assert.Equal(expected, SidebarLayout.IsWidth(left ? SidebarSide.Left : SidebarSide.Right, width));

    // 490 AC-5: a width is clamped into its side's bounds.
    [Fact]
    public void WithWidth_Clamps()
    {
        Assert.Equal(160, SidebarLayout.Default.WithWidth(SidebarSide.Left, 10).LeftWidth);
        Assert.Equal(480, SidebarLayout.Default.WithWidth(SidebarSide.Left, 9000).LeftWidth);
        Assert.Equal(200, SidebarLayout.Default.WithWidth(SidebarSide.Right, -5).RightWidth);
        Assert.Equal(520, SidebarLayout.Default.WithWidth(SidebarSide.Right, 600).RightWidth);
        Assert.Equal(280, SidebarLayout.Default.WithWidth(SidebarSide.Left, 300).RightWidth);
    }

    // 490 AC-5, AC-6: moving an edge right widens the left sidebar and narrows the right one.
    [Fact]
    public void EdgeMoved_LeftWidens_RightNarrows()
    {
        Assert.Equal(256, SidebarLayout.Default.EdgeMoved(SidebarSide.Left, 16).LeftWidth);
        Assert.Equal(224, SidebarLayout.Default.EdgeMoved(SidebarSide.Left, -16).LeftWidth);
        Assert.Equal(264, SidebarLayout.Default.EdgeMoved(SidebarSide.Right, 16).RightWidth);
        Assert.Equal(296, SidebarLayout.Default.EdgeMoved(SidebarSide.Right, -16).RightWidth);
        Assert.Equal(480, SidebarLayout.Default.EdgeMoved(SidebarSide.Left, 1000).LeftWidth);
        Assert.Equal(200, SidebarLayout.Default.EdgeMoved(SidebarSide.Right, 1000).RightWidth);
    }

    // 490 AC-4: a toggle flips only its side.
    [Fact]
    public void Toggled_FlipsOneSide()
    {
        SidebarLayout left = SidebarLayout.Default.Toggled(SidebarSide.Left);
        Assert.True(left.LeftCollapsed);
        Assert.False(left.RightCollapsed);
        Assert.False(left.Toggled(SidebarSide.Left).LeftCollapsed);
        Assert.True(SidebarLayout.Default.Toggled(SidebarSide.Right).Collapsed(SidebarSide.Right));
    }
}
