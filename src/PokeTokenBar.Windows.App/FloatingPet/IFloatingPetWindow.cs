namespace PokeTokenBar.Windows.App.FloatingPet;

internal interface IFloatingPetWindow : IDisposable
{
    bool IsVisible { get; }

    void ShowAtDefaultPosition();

    void Close();
}
