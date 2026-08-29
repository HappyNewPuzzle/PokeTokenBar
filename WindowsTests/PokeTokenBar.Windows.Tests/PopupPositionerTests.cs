using System.Windows;
using PokeTokenBar.Windows.App.Tray;

namespace PokeTokenBar.Windows.Tests;

public sealed class PopupPositionerTests
{
    [Fact]
    public void BottomRightAnchor_PlacesPopupAboveLeftOfTaskbarCorner()
    {
        var placement = PopupPositioner.Calculate(
            new Rect(0, 0, 1920, 1040),
            new Size(520, 620),
            new Point(1900, 1070),
            margin: 8);

        Assert.Equal(new PopupPlacement(1392, 412, 520, 620), placement);
    }

    [Fact]
    public void TopLeftAnchor_PlacesPopupInsideNearestWorkingAreaCorner()
    {
        var placement = PopupPositioner.Calculate(
            new Rect(100, 40, 1200, 800),
            new Size(520, 620),
            new Point(80, 20),
            margin: 8);

        Assert.Equal(new PopupPlacement(108, 48, 520, 620), placement);
    }

    [Fact]
    public void Placement_RemainsInsideOffsetWorkingArea()
    {
        var area = new Rect(-1280, 0, 1280, 984);

        var placement = PopupPositioner.Calculate(
            area,
            new Size(520, 620),
            new Point(-5, 1020),
            margin: 8);

        Assert.True(placement.Left >= area.Left);
        Assert.True(placement.Top >= area.Top);
        Assert.True(placement.Left + placement.Width <= area.Right);
        Assert.True(placement.Top + placement.Height <= area.Bottom);
    }

    [Fact]
    public void SmallerWorkingArea_ClampsPopupSizeAndPosition()
    {
        var area = new Rect(20, 30, 320, 240);

        var placement = PopupPositioner.Calculate(
            area,
            new Size(520, 620),
            new Point(400, 400),
            margin: 8);

        Assert.Equal(new PopupPlacement(28, 38, 304, 224), placement);
        Assert.True(placement.Left + placement.Width <= area.Right);
        Assert.True(placement.Top + placement.Height <= area.Bottom);
    }
}
