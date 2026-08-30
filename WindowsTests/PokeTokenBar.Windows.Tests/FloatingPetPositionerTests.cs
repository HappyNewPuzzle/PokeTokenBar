using System.Windows;
using PokeTokenBar.Windows.App.FloatingPet;
using Size = System.Windows.Size;

namespace PokeTokenBar.Windows.Tests;

public sealed class FloatingPetPositionerTests
{
    [Fact]
    public void DefaultPlacementMatchesSwiftBottomRightMargin()
    {
        var placement = FloatingPetPositioner.Calculate(
            new Rect(0, 0, 1920, 1040),
            new Size(96, 96));

        Assert.Equal(1800, placement.Left);
        Assert.Equal(920, placement.Top);
        Assert.Equal(96, placement.Width);
        Assert.Equal(96, placement.Height);
    }

    [Fact]
    public void PlacementUsesSuppliedMonitorWorkingAreaIncludingNegativeCoordinates()
    {
        var placement = FloatingPetPositioner.Calculate(
            new Rect(-1280, 40, 1280, 984),
            new Size(96, 96));

        Assert.Equal(-120, placement.Left);
        Assert.Equal(904, placement.Top);
        Assert.Equal(96, placement.Width);
        Assert.Equal(96, placement.Height);
    }

    [Fact]
    public void TinyWorkingAreaShrinksAndClampsWindowInsideBounds()
    {
        var workingArea = new Rect(10, 20, 60, 40);

        var placement = FloatingPetPositioner.Calculate(
            workingArea,
            new Size(96, 96));

        Assert.Equal(10, placement.Left);
        Assert.Equal(20, placement.Top);
        Assert.Equal(60, placement.Width);
        Assert.Equal(40, placement.Height);
        Assert.InRange(placement.Left, workingArea.Left, workingArea.Right);
        Assert.InRange(placement.Top, workingArea.Top, workingArea.Bottom);
        Assert.True(placement.Left + placement.Width <= workingArea.Right);
        Assert.True(placement.Top + placement.Height <= workingArea.Bottom);
    }

    [Fact]
    public void NegativeMarginIsNormalizedToZero()
    {
        var placement = FloatingPetPositioner.Calculate(
            new Rect(100, 200, 400, 300),
            new Size(96, 96),
            margin: -10);

        Assert.Equal(404, placement.Left);
        Assert.Equal(404, placement.Top);
    }

    [Fact]
    public void SavedPositionIsClampedInsideItsExistingMonitor()
    {
        var area = new Rect(-1280, 0, 1280, 1040);
        var placement = FloatingPetPositioner.Restore(
            [area], new Point(-40, 1000), new Size(96, 96), area);
        Assert.Equal(-96, placement.Left);
        Assert.Equal(944, placement.Top);
    }

    [Fact]
    public void PositionOutsideAllMonitorsFallsBackToCurrentMonitorDefault()
    {
        var primary = new Rect(0, 0, 1920, 1040);
        var placement = FloatingPetPositioner.Restore(
            [primary], new Point(9000, 9000), new Size(96, 96), primary);
        Assert.Equal(1800, placement.Left);
        Assert.Equal(920, placement.Top);
    }
}
