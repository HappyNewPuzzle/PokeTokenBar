using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App.FloatingPet;

public sealed class FloatingPetPositionEventArgs(FloatingPetPosition position) : EventArgs
{
    public FloatingPetPosition Position { get; } = position;
}

internal interface IFloatingPetWindow : IDisposable
{
    event EventHandler? OpenRequested;

    event EventHandler? HideRequested;

    event EventHandler<FloatingPetPositionEventArgs>? PositionCommitted;

    bool IsVisible { get; }

    void ShowAtPosition(FloatingPetPosition? position);

    void ResetToDefaultPosition();

    void Hide();

    void Close();
}
