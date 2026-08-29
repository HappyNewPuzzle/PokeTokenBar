using System.Windows;
using System.Windows.Media;
using PokeTokenBar.Windows.App.ViewModels;
using Forms = System.Windows.Forms;
using Size = System.Windows.Size;

namespace PokeTokenBar.Windows.App.FloatingPet;

public partial class FloatingPokemonWindow : Window, IFloatingPetWindow
{
    public const double SpriteSize = 96;
    private bool _disposed;

    public FloatingPokemonWindow(FloatingPetViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public void ShowAtDefaultPosition()
    {
        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor);
        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1;
        var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;
        var area = screen.WorkingArea;
        var workingArea = new Rect(
            area.Left / scaleX,
            area.Top / scaleY,
            area.Width / scaleX,
            area.Height / scaleY);
        var placement = FloatingPetPositioner.Calculate(
            workingArea,
            new Size(SpriteSize, SpriteSize));

        Width = placement.Width;
        Height = placement.Height;
        Left = placement.Left;
        Top = placement.Top;
        Show();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        FloatingSprite.Dispose();
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }
}
