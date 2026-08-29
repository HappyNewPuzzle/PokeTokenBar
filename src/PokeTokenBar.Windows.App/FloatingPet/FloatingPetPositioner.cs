using System.Windows;
using Size = System.Windows.Size;

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
}
