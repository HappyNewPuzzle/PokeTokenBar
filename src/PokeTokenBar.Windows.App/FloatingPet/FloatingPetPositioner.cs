using System.Windows;
using Size = System.Windows.Size;
using Point = System.Windows.Point;

namespace PokeTokenBar.Windows.App.FloatingPet;

internal readonly record struct FloatingPetPlacement(
    double Left,
    double Top,
    double Width,
    double Height);

internal static class FloatingPetPositioner
{
    public const double DefaultMargin = 24;

    public static FloatingPetPlacement Calculate(
        Rect workingArea,
        Size preferredSize,
        double margin = DefaultMargin)
    {
        var safeMargin = Math.Max(0, margin);
        var width = Math.Min(Math.Max(0, preferredSize.Width), workingArea.Width);
        var height = Math.Min(Math.Max(0, preferredSize.Height), workingArea.Height);
        var horizontalMargin = Math.Min(safeMargin, Math.Max(0, workingArea.Width - width));
        var verticalMargin = Math.Min(safeMargin, Math.Max(0, workingArea.Height - height));
        var left = workingArea.Right - horizontalMargin - width;
        var top = workingArea.Bottom - verticalMargin - height;

        return new FloatingPetPlacement(
            Math.Clamp(left, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - width)),
            Math.Clamp(top, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - height)),
            width,
            height);
    }

    public static FloatingPetPlacement Restore(
        IReadOnlyList<Rect> workingAreas,
        Point desiredOrigin,
        Size preferredSize,
        Rect fallbackWorkingArea,
        double margin = DefaultMargin)
    {
        var desired = new Rect(desiredOrigin, preferredSize);
        Rect? matchingArea = null;
        foreach (var area in workingAreas)
        {
            if (area.IntersectsWith(desired))
            {
                matchingArea = area;
                break;
            }
        }

        if (matchingArea is not Rect availableArea)
        {
            return Calculate(fallbackWorkingArea, preferredSize, margin);
        }

        return Clamp(availableArea, desiredOrigin, preferredSize);
    }

    public static FloatingPetPlacement Clamp(
        Rect workingArea,
        Point desiredOrigin,
        Size preferredSize)
    {
        var width = Math.Min(Math.Max(0, preferredSize.Width), workingArea.Width);
        var height = Math.Min(Math.Max(0, preferredSize.Height), workingArea.Height);
        return new FloatingPetPlacement(
            Math.Clamp(
                desiredOrigin.X,
                workingArea.Left,
                Math.Max(workingArea.Left, workingArea.Right - width)),
            Math.Clamp(
                desiredOrigin.Y,
                workingArea.Top,
                Math.Max(workingArea.Top, workingArea.Bottom - height)),
            width,
            height);
    }
}
