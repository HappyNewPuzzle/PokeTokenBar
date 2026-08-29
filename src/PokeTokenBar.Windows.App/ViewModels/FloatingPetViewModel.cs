using System.ComponentModel;
using PokeTokenBar.Windows.App.Sprites;

namespace PokeTokenBar.Windows.App.ViewModels;

public sealed class FloatingPetViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CompanionViewModel _companion;
    private bool _disposed;

    public FloatingPetViewModel(CompanionViewModel companion)
    {
        _companion = companion ?? throw new ArgumentNullException(nameof(companion));
        _companion.PropertyChanged += OnCompanionPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PokemonSpritePresentation? Sprite => _companion.Sprite;

    public int? PokemonId => _companion.PokemonId;

    public bool IsShiny => _companion.IsShiny;

    public bool IsEgg => PokemonId is null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _companion.PropertyChanged -= OnCompanionPropertyChanged;
    }

    private void OnCompanionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(CompanionViewModel.Sprite):
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Sprite)));
                break;
            case nameof(CompanionViewModel.PokemonId):
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PokemonId)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEgg)));
                break;
            case nameof(CompanionViewModel.IsShiny):
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShiny)));
                break;
        }
    }
}
