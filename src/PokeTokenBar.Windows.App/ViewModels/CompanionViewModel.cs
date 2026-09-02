using System.ComponentModel;
using System.Runtime.CompilerServices;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.App.ViewModels;

public sealed class CompanionViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CompanionStore _store;
    private readonly Func<int, bool, CancellationToken, Task<PokemonSpriteAsset?>> _loadSprite;
    private readonly IPokemonSpriteDecoder _spriteDecoder;
    private CancellationTokenSource? _spriteLoadCancellation;
    private CancellationTokenSource? _companionSpriteLoadCancellation;
    private long _spriteGeneration;
    private long _companionSpriteGeneration;
    private bool _hasAttemptedSprite;
    private int? _attemptedPokemonId;
    private bool _attemptedShiny;
    private bool _hasAttemptedCompanionSprite;
    private int? _attemptedCompanionPokemonId;
    private bool _attemptedCompanionShiny;
    private bool _isEgg;
    private bool _hasCompanion;
    private bool _isHatching;
    private bool _isSpriteLoading;
    private int? _activePokemonId;
    private int? _pokemonId;
    private string _displayName = "Token Egg";
    private bool _isShiny;
    private bool _currentIsShiny;
    private PokemonNature? _nature;
    private PokemonRarity? _rarity;
    private int? _stageIndex;
    private int? _totalForms;
    private bool _isFinalStage;
    private CompanionStateKind _displayState;
    private AppLanguage _language;
    private PokemonSpritePresentation? _sprite;
    private PokemonSpritePresentation? _companionSprite;

    public CompanionViewModel(
        CompanionStore store,
        PokemonSpriteLoader spriteLoader,
        IPokemonSpriteDecoder? spriteDecoder = null)
        : this(
            store,
            CreateSpriteLoader(spriteLoader),
            spriteDecoder ?? new WpfPokemonSpriteDecoder())
    {
    }

    public CompanionViewModel(
        CompanionStore store,
        Func<int, bool, CancellationToken, Task<PokemonSpriteAsset?>> loadSprite,
        IPokemonSpriteDecoder spriteDecoder)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _loadSprite = loadSprite ?? throw new ArgumentNullException(nameof(loadSprite));
        _spriteDecoder = spriteDecoder ?? throw new ArgumentNullException(nameof(spriteDecoder));
        ApplyStoreState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsEgg
    {
        get => _isEgg;
        private set => SetField(ref _isEgg, value);
    }

    public bool HasCompanion
    {
        get => _hasCompanion;
        private set => SetField(ref _hasCompanion, value);
    }

    public bool IsHatching
    {
        get => _isHatching;
        private set => SetField(ref _isHatching, value);
    }

    public bool IsSpriteLoading
    {
        get => _isSpriteLoading;
        private set => SetField(ref _isSpriteLoading, value);
    }

    public int? ActivePokemonId
    {
        get => _activePokemonId;
        private set => SetField(ref _activePokemonId, value);
    }

    public int? PokemonId
    {
        get => _pokemonId;
        private set => SetField(ref _pokemonId, value);
    }

    public string DisplayName
    {
        get => _displayName;
        private set => SetField(ref _displayName, value);
    }

    public bool IsShiny
    {
        get => _isShiny;
        private set => SetField(ref _isShiny, value);
    }

    public bool CurrentIsShiny
    {
        get => _currentIsShiny;
        private set => SetField(ref _currentIsShiny, value);
    }

    public PokemonNature? Nature
    {
        get => _nature;
        private set
        {
            if (SetField(ref _nature, value))
            {
                OnPropertyChanged(nameof(Personality));
            }
        }
    }

    public string? Personality => Nature is PokemonNature nature
        ? PokemonNatureDisplayNames.GetName(nature, Language)
        : null;

    public string? RarityText => Rarity is PokemonRarity rarity
        ? CompanionDisplayTexts.Rarity(rarity, Language)
        : null;

    public string? StageText => StageIndex is int stageIndex && TotalForms is int totalForms
        ? CompanionDisplayTexts.Stage(stageIndex + 1, totalForms, IsFinalStage, Language)
        : null;

    public double Progress
    {
        get
        {
            if (_store.State.Active is not MonState active)
            {
                return Math.Clamp(
                    (double)_store.State.EggUsage / PokemonBalance.EggHatchThreshold,
                    0,
                    1);
            }

            var threshold = PokemonBalance.PhaseThreshold(
                active.Rarity,
                active.TotalForms,
                active.StageIndex);
            return threshold == 0
                ? 0
                : Math.Clamp((double)active.UsedAtStage / threshold, 0, 1);
        }
    }

    public string ProgressText
    {
        get
        {
            var active = _store.State.Active;
            var remaining = active is null
                ? Math.Max(0, PokemonBalance.EggHatchThreshold - _store.State.EggUsage)
                : Math.Max(
                    0,
                    PokemonBalance.PhaseThreshold(
                        active.Rarity,
                        active.TotalForms,
                        active.StageIndex) - active.UsedAtStage);
            return CompanionDisplayTexts.Progress(
                active is null,
                IsFinalStage,
                remaining,
                Language);
        }
    }

    public string StatusText => CompanionDisplayTexts.Status(DisplayState, Language);

    public string HatchingText => CompanionDisplayTexts.Hatching(Language);

    public PokemonRarity? Rarity
    {
        get => _rarity;
        private set => SetField(ref _rarity, value);
    }

    public int? StageIndex
    {
        get => _stageIndex;
        private set => SetField(ref _stageIndex, value);
    }

    public int? TotalForms
    {
        get => _totalForms;
        private set => SetField(ref _totalForms, value);
    }

    public bool IsFinalStage
    {
        get => _isFinalStage;
        private set => SetField(ref _isFinalStage, value);
    }

    public CompanionStateKind DisplayState
    {
        get => _displayState;
        private set => SetField(ref _displayState, value);
    }

    public AppLanguage Language
    {
        get => _language;
        private set
        {
            if (SetField(ref _language, value))
            {
                OnPropertyChanged(nameof(Personality));
            }
        }
    }

    public PokemonSpritePresentation? Sprite
    {
        get => _sprite;
        private set => SetField(ref _sprite, value);
    }

    public PokemonSpritePresentation? CompanionSprite
    {
        get => _companionSprite;
        private set => SetField(ref _companionSprite, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_store.State.Active is not null && _store.CurrentLine is null)
        {
            await _store.LoadCurrentLineAsync(cancellationToken);
        }

        ApplyStoreState();
        await EnsureSpriteAsync(cancellationToken);
        await EnsureCompanionSpriteAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_store.State.Active is not null && _store.CurrentLine is null)
        {
            await _store.LoadCurrentLineAsync(cancellationToken);
        }

        ApplyStoreState();
        await EnsureSpriteAsync(cancellationToken);
        await EnsureCompanionSpriteAsync(cancellationToken);
    }

    public async Task<bool> HatchRandomAsync(CancellationToken cancellationToken = default) =>
        await HatchAsync(
            token => _store.HatchRandomAsync(token),
            cancellationToken);

    public async Task<bool> HatchSpecificAsync(
        int baseSpeciesId,
        CancellationToken cancellationToken = default) =>
        await HatchAsync(
            token => _store.HatchAsync(baseSpeciesId, token),
            cancellationToken);

    public async Task<bool> SelectRepresentativeAsync(
        int? speciesId,
        CancellationToken cancellationToken = default)
    {
        var changed = _store.SetRepresentativeSpeciesId(speciesId);
        ApplyStoreState();
        if (changed)
        {
            await EnsureSpriteAsync(cancellationToken);
        }

        return changed;
    }

    public void Reset()
    {
        CancelSpriteLoad();
        CancelCompanionSpriteLoad();
        _store.Reset();
        _hasAttemptedSprite = false;
        _attemptedPokemonId = null;
        Sprite = null;
        _hasAttemptedCompanionSprite = false;
        _attemptedCompanionPokemonId = null;
        CompanionSprite = null;
        IsSpriteLoading = false;
        ApplyStoreState();
    }

    public void Dispose()
    {
        CancelSpriteLoad();
        CancelCompanionSpriteLoad();
        GC.SuppressFinalize(this);
    }

    private async Task<bool> HatchAsync(
        Func<CancellationToken, Task<bool>> hatch,
        CancellationToken cancellationToken)
    {
        IsHatching = true;
        bool success;
        try
        {
            success = await hatch(cancellationToken);
        }
        finally
        {
            IsHatching = _store.IsHatching;
            ApplyStoreState();
        }

        if (success)
        {
            await EnsureSpriteAsync(cancellationToken);
            await EnsureCompanionSpriteAsync(cancellationToken);
        }

        return success;
    }

    private async Task EnsureSpriteAsync(CancellationToken callerCancellationToken)
    {
        var id = PokemonId;
        var shiny = IsShiny;
        if (id is null)
        {
            CancelSpriteLoad();
            _spriteGeneration++;
            _hasAttemptedSprite = false;
            _attemptedPokemonId = null;
            Sprite = null;
            IsSpriteLoading = false;
            return;
        }

        if (_hasAttemptedSprite &&
            _attemptedPokemonId == id &&
            _attemptedShiny == shiny)
        {
            return;
        }

        CancelSpriteLoad();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken);
        _spriteLoadCancellation = cancellation;
        var generation = ++_spriteGeneration;
        _hasAttemptedSprite = true;
        _attemptedPokemonId = id;
        _attemptedShiny = shiny;
        IsSpriteLoading = true;

        try
        {
            var asset = await _loadSprite(id.Value, shiny, cancellation.Token);
            var presentation = asset is null ? null : _spriteDecoder.Decode(asset);
            if (generation != _spriteGeneration || cancellation.IsCancellationRequested)
            {
                return;
            }

            Sprite = presentation;
        }
        catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
        {
            // Superseded identity: the newer generation owns presentation state.
        }
        catch (OperationCanceledException)
        {
            if (generation == _spriteGeneration)
            {
                _hasAttemptedSprite = false;
            }

            throw;
        }
        catch (Exception)
        {
            if (generation == _spriteGeneration)
            {
                Sprite = null;
            }
        }
        finally
        {
            if (generation == _spriteGeneration)
            {
                IsSpriteLoading = false;
                if (ReferenceEquals(_spriteLoadCancellation, cancellation))
                {
                    _spriteLoadCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private async Task EnsureCompanionSpriteAsync(CancellationToken callerCancellationToken)
    {
        var id = ActivePokemonId;
        var shiny = CurrentIsShiny;
        if (id is null)
        {
            CancelCompanionSpriteLoad();
            _companionSpriteGeneration++;
            _hasAttemptedCompanionSprite = false;
            _attemptedCompanionPokemonId = null;
            CompanionSprite = null;
            return;
        }

        if (id == PokemonId && shiny == IsShiny)
        {
            CancelCompanionSpriteLoad();
            _companionSpriteGeneration++;
            _hasAttemptedCompanionSprite = true;
            _attemptedCompanionPokemonId = id;
            _attemptedCompanionShiny = shiny;
            CompanionSprite = Sprite;
            return;
        }

        if (_hasAttemptedCompanionSprite &&
            _attemptedCompanionPokemonId == id &&
            _attemptedCompanionShiny == shiny)
        {
            return;
        }

        CancelCompanionSpriteLoad();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken);
        _companionSpriteLoadCancellation = cancellation;
        var generation = ++_companionSpriteGeneration;
        _hasAttemptedCompanionSprite = true;
        _attemptedCompanionPokemonId = id;
        _attemptedCompanionShiny = shiny;
        IsSpriteLoading = true;

        try
        {
            var asset = await _loadSprite(id.Value, shiny, cancellation.Token);
            var presentation = asset is null ? null : _spriteDecoder.Decode(asset);
            if (generation != _companionSpriteGeneration || cancellation.IsCancellationRequested)
            {
                return;
            }

            CompanionSprite = presentation;
        }
        catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            if (generation == _companionSpriteGeneration)
            {
                _hasAttemptedCompanionSprite = false;
            }

            throw;
        }
        catch (Exception)
        {
            if (generation == _companionSpriteGeneration)
            {
                CompanionSprite = null;
            }
        }
        finally
        {
            if (generation == _companionSpriteGeneration)
            {
                IsSpriteLoading = false;
                if (ReferenceEquals(_companionSpriteLoadCancellation, cancellation))
                {
                    _companionSpriteLoadCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void ApplyStoreState()
    {
        var active = _store.State.Active;
        var subject = _store.RepresentativeSubject;
        IsEgg = active is null;
        ActivePokemonId = _store.CurrentSpeciesId;
        PokemonId = subject.SpeciesId;
        HasCompanion = subject.SpeciesId is not null;
        IsShiny = subject.IsShiny;
        CurrentIsShiny = _store.CurrentIsShiny;
        DisplayState = _store.DisplayState;
        Language = _store.State.Language;

        Nature = active?.Nature;
        Rarity = active?.Rarity;
        StageIndex = active?.StageIndex;
        TotalForms = active?.TotalForms;
        IsFinalStage = active is not null && IsCurrentFinalStage(active);

        DisplayName = active is null || _store.CurrentLine is null
            ? CompanionDisplayTexts.EggName(Language)
            : _store.CurrentLine.LocalizedName(active.CurrentId, Language);

        OnPropertyChanged(nameof(RarityText));
        OnPropertyChanged(nameof(StageText));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HatchingText));
    }

    private bool IsCurrentFinalStage(MonState active)
    {
        var node = _store.CurrentLine?.Tree.Find(active.CurrentId);
        return node?.Children.Count == 0;
    }

    private void CancelSpriteLoad()
    {
        var previous = Interlocked.Exchange(ref _spriteLoadCancellation, null);
        if (previous is null)
        {
            return;
        }

        previous.Cancel();
    }

    private void CancelCompanionSpriteLoad()
    {
        var previous = Interlocked.Exchange(ref _companionSpriteLoadCancellation, null);
        previous?.Cancel();
    }

    private static Func<int, bool, CancellationToken, Task<PokemonSpriteAsset?>> CreateSpriteLoader(
        PokemonSpriteLoader spriteLoader)
    {
        ArgumentNullException.ThrowIfNull(spriteLoader);
        return (id, shiny, cancellationToken) =>
            spriteLoader.LoadAsync(
                id,
                preferAnimated: true,
                shiny,
                cancellationToken);
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
