using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace PokeTokenBar.Windows.App.Tray;

internal readonly record struct PopupPlacement(
    double Left,
    double Top,
    double Width,
    double Height);

internal static class PopupPositioner
{
    public static PopupPlacement Calculate(
        Rect workingArea,
        Size preferredSize,
        Point anchor,
        double margin)
    {
        var safeMargin = Math.Max(0, margin);
        var horizontalMargin = Math.Min(safeMargin, workingArea.Width / 2);
        var verticalMargin = Math.Min(safeMargin, workingArea.Height / 2);
        var availableWidth = Math.Max(0, workingArea.Width - horizontalMargin * 2);
        var availableHeight = Math.Max(0, workingArea.Height - verticalMargin * 2);
        var width = Math.Min(Math.Max(0, preferredSize.Width), availableWidth);
        var height = Math.Min(Math.Max(0, preferredSize.Height), availableHeight);

        var placeRight = anchor.X >= workingArea.Left + workingArea.Width / 2;
        var placeBottom = anchor.Y >= workingArea.Top + workingArea.Height / 2;
        var left = placeRight
            ? workingArea.Right - horizontalMargin - width
            : workingArea.Left + horizontalMargin;
        var top = placeBottom
            ? workingArea.Bottom - verticalMargin - height
            : workingArea.Top + verticalMargin;

        var minLeft = workingArea.Left;
        var maxLeft = Math.Max(minLeft, workingArea.Right - width);
        var minTop = workingArea.Top;
        var maxTop = Math.Max(minTop, workingArea.Bottom - height);

        return new PopupPlacement(
            Math.Clamp(left, minLeft, maxLeft),
            Math.Clamp(top, minTop, maxTop),
            width,
            height);
    }
}
