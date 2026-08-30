using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using Forms = System.Windows.Forms;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace PokeTokenBar.Windows.App.FloatingPet;

public partial class FloatingPokemonWindow : Window, IFloatingPetWindow
{
    public const double SpriteSize = 96;
    public const double ClickThreshold = 4;
    private bool _disposed;
    private System.Drawing.Point? _mouseDownScreen;
    private Point _originAtMouseDown;
    private bool _didDrag;

    public FloatingPokemonWindow(FloatingPetViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ContextMenu = CreateContextMenu();
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? HideRequested;

    public event EventHandler<FloatingPetPositionEventArgs>? PositionCommitted;

    public void ShowAtPosition(FloatingPetPosition? position)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1;
        var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;
        var cursorScreen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var fallbackArea = ToDips(cursorScreen.WorkingArea, scaleX, scaleY);
        var workingAreas = Forms.Screen.AllScreens
            .Select(screen => ToDips(screen.WorkingArea, scaleX, scaleY))
            .ToArray();
        var preferredSize = new Size(SpriteSize, SpriteSize);
        var placement = position is null
            ? FloatingPetPositioner.Calculate(fallbackArea, preferredSize)
            : FloatingPetPositioner.Restore(
                workingAreas,
                new Point(position.Left, position.Top),
                preferredSize,
                fallbackArea);

        ApplyPlacement(placement);
        Show();

        if (position is not null &&
            (position.Left != placement.Left || position.Top != placement.Top))
        {
            CommitPosition();
        }
    }

    public void ResetToDefaultPosition()
    {
        ShowAtPosition(null);
        CommitPosition();
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

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _mouseDownScreen = Forms.Cursor.Position;
        _originAtMouseDown = new Point(Left, Top);
        _didDrag = false;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_mouseDownScreen is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1;
        var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;
        var current = Forms.Cursor.Position;
        var dx = (current.X - start.X) / scaleX;
        var dy = (current.Y - start.Y) / scaleY;
        if ((dx * dx) + (dy * dy) >= ClickThreshold * ClickThreshold)
        {
            _didDrag = true;
        }

        if (_didDrag)
        {
            Left = _originAtMouseDown.X + dx;
            Top = _originAtMouseDown.Y + dy;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_mouseDownScreen is null)
        {
            return;
        }

        ReleaseMouseCapture();
        if (_didDrag)
        {
            ClampToCurrentMonitor();
            CommitPosition();
        }
        else
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }

        _mouseDownScreen = null;
        _didDrag = false;
        e.Handled = true;
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open Token Bar" };
        var hide = new MenuItem { Header = "Hide Floating Pokémon" };
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        hide.Click += (_, _) => HideRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(open);
        menu.Items.Add(hide);
        return menu;
    }

    private void ClampToCurrentMonitor()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1;
        var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var area = ToDips(screen.WorkingArea, scaleX, scaleY);
        ApplyPlacement(FloatingPetPositioner.Clamp(
            area,
            new Point(Left, Top),
            new Size(SpriteSize, SpriteSize)));
    }

    private void ApplyPlacement(FloatingPetPlacement placement)
    {
        Width = placement.Width;
        Height = placement.Height;
        Left = placement.Left;
        Top = placement.Top;
    }

    private void CommitPosition() =>
        PositionCommitted?.Invoke(
            this,
            new FloatingPetPositionEventArgs(new FloatingPetPosition(Left, Top)));

    private static Rect ToDips(
        System.Drawing.Rectangle area,
        double scaleX,
        double scaleY) =>
        new(
            area.Left / scaleX,
            area.Top / scaleY,
            area.Width / scaleX,
            area.Height / scaleY);
}
