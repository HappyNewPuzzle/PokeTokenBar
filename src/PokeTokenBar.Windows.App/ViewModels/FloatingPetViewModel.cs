using System.ComponentModel;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App.ViewModels;

public sealed class FloatingPetViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CompanionViewModel _companion;
    private readonly SettingsViewModel? _settings;
    private readonly UsageViewModel? _usage;
    private CancellationTokenSource? _bubbleCancellation;
    private string? _bubbleTitle;
    private string? _bubbleBody;
    private bool _disposed;

    public FloatingPetViewModel(
        CompanionViewModel companion,
        SettingsViewModel? settings = null,
        UsageViewModel? usage = null)
    {
        _companion = companion ?? throw new ArgumentNullException(nameof(companion));
        _settings = settings;
        _usage = usage;
        _companion.PropertyChanged += OnCompanionPropertyChanged;
        if (_settings is not null)
        {
            _settings.PropertyChanged += OnSettingsPropertyChanged;
            _settings.Localization.PropertyChanged += OnLocalizationChanged;
        }
        if (_usage is not null) _usage.PropertyChanged += OnUsagePropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PokemonSpritePresentation? Sprite => _companion.Sprite;

    public int? PokemonId => _companion.PokemonId;

    public bool IsShiny => _companion.IsShiny;

    public bool IsEgg => PokemonId is null;

    public double Size => _settings?.FloatingPetSize ?? 96;

    public TimeSpan MinimumFrameDuration => (_settings?.SelectedAnimationQuality ?? AnimationQuality.PowerSaver) switch
    {
        AnimationQuality.Smooth => TimeSpan.FromMilliseconds(100),
        AnimationQuality.Balanced => TimeSpan.FromMilliseconds(200),
        _ => TimeSpan.FromMilliseconds(400),
    };

    public string HoverText => _usage is null
        ? "PokeTokenBar"
        : $"{_settings?.Localization.Today ?? "Today"}: {_usage.TotalTodayTokensGroupedText}" +
          (_usage.FiveHourRemainingText is { } limit ? $" · {limit}" : "");

    public string OpenText => _settings?.Localization.Open ?? "Open";

    public string HideText => _settings?.Localization.HideFloating ?? "Hide Floating Pokémon";

    public string? BubbleTitle
    {
        get => _bubbleTitle;
        private set { if (_bubbleTitle != value) { _bubbleTitle = value; Changed(nameof(BubbleTitle)); Changed(nameof(IsBubbleVisible)); } }
    }

    public string? BubbleBody
    {
        get => _bubbleBody;
        private set { if (_bubbleBody != value) { _bubbleBody = value; Changed(nameof(BubbleBody)); } }
    }

    public bool IsBubbleVisible => !string.IsNullOrWhiteSpace(BubbleTitle);

    internal async Task ShowBubbleAsync(string title, string body, TimeSpan? duration = null)
    {
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _bubbleCancellation, cancellation)?.Cancel();
        BubbleTitle = title;
        BubbleBody = body;
        try
        {
            await Task.Delay(duration ?? TimeSpan.FromSeconds(6), cancellation.Token);
            if (ReferenceEquals(_bubbleCancellation, cancellation)) BubbleTitle = null;
        }
        catch (OperationCanceledException) { }
        finally { cancellation.Dispose(); }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Exchange(ref _bubbleCancellation, null)?.Cancel();
        _companion.PropertyChanged -= OnCompanionPropertyChanged;
        if (_settings is not null)
        {
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
            _settings.Localization.PropertyChanged -= OnLocalizationChanged;
        }
        if (_usage is not null) _usage.PropertyChanged -= OnUsagePropertyChanged;
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

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsViewModel.FloatingPetSize)) Changed(nameof(Size));
        if (args.PropertyName == nameof(SettingsViewModel.SelectedAnimationQuality)) Changed(nameof(MinimumFrameDuration));
        if (args.PropertyName == nameof(SettingsViewModel.SelectedLanguage))
        {
            Changed(nameof(HoverText));
            Changed(nameof(OpenText));
            Changed(nameof(HideText));
        }
    }

    private void OnUsagePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(UsageViewModel.TotalTodayTokens) or
            nameof(UsageViewModel.FiveHourRemainingText)) Changed(nameof(HoverText));
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs args)
    {
        Changed(nameof(HoverText));
        Changed(nameof(OpenText));
        Changed(nameof(HideText));
        BubbleTitle = null;
    }

    private void Changed(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
