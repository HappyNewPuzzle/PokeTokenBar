using System.ComponentModel;
using System.Globalization;
using PokeTokenBar.Windows.App.Commands;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.App.ViewModels;

public sealed record PokemonIndividualOption(PokemonIndividual Individual, int Number)
{
    public string Label => $"#{Number} · Lv. {Individual.Profile.Level}";
}
public sealed record PokemonDetailStatRow(string StatName, int StatValue, int ScaleMaximum, string? IvText);
public sealed record PokemonDetailMoveRow(string MoveName, string MethodText);

public sealed class PokemonDetailViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CompanionStore _store;
    private readonly LocalizationService _texts;
    private readonly Func<int, bool, CancellationToken, Task<PokemonSpriteAsset?>> _loadSprite;
    private readonly IPokemonSpriteDecoder _decoder;
    private PokemonDetails? _details;
    private PokemonIndividualOption? _selected;
    private string? _preferredInstanceId;
    private PokemonRarity _fallbackRarity;
    private bool _fallbackShiny;
    private long _generation;
    private long _spriteGeneration;
    private CancellationTokenSource? _spriteCancellation;
    private (int Id, bool Shiny)? _spriteIdentity;
    private bool _disposed;

    public PokemonDetailViewModel(CompanionStore store, LocalizationService texts,
        Func<int, bool, CancellationToken, Task<PokemonSpriteAsset?>> loadSprite,
        IPokemonSpriteDecoder decoder, Func<int, CancellationToken, Task> represent)
    {
        _store = store;
        _texts = texts;
        _loadSprite = loadSprite;
        _decoder = decoder;
        BackCommand = new AsyncCommand(_ => { Close(); return Task.CompletedTask; });
        RetryCommand = new AsyncCommand(_ => LoadDetailsAsync(_generation), () => IsOpen && !IsLoading);
        RepresentCommand = new AsyncCommand(async token =>
        {
            await represent(SpeciesId, token);
            Notify();
        }, () => IsOpen);
        _texts.PropertyChanged += OnLanguageChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsOpen { get; private set; }
    public bool IsCollectionVisible => !IsOpen;
    public bool IsLoading { get; private set; }
    public bool HasError { get; private set; }
    public bool HasDetails => _details is not null;
    public int SpeciesId { get; private set; }
    public string Name { get; private set; } = "";
    public PokemonSpritePresentation? Sprite { get; private set; }
    public IReadOnlyList<PokemonIndividualOption> Individuals { get; private set; } = [];
    public bool HasMultipleIndividuals => Individuals.Count > 1;
    public bool HasIndividual => SelectedIndividual is not null;
    public PokemonIndividualOption? SelectedIndividual
    {
        get => _selected;
        set
        {
            if (value is null || !Individuals.Contains(value) || value == _selected) return;
            _selected = value;
            Notify();
            SpriteLoadTask = EnsureSpriteAsync();
        }
    }
    private PokemonIndividual? Individual => _selected?.Individual;
    private PokemonProfile? Profile => Individual?.Profile;
    public string RarityText => CompanionDisplayTexts.Rarity(Individual?.Rarity ?? _fallbackRarity, _texts.Language);
    public bool IsShiny => Individual?.IsShiny ?? _fallbackShiny;
    public string ShinyText => IsShiny ? _texts.Shiny : _texts.Normal;
    public string RoleText => Individual is { IsCurrent: true } ? _texts.Current
        : Individual is { IsReleased: true } ? _texts.Released : HasIndividual ? _texts.Caught : "";
    public bool IsRepresentative => _store.State.RepresentativeSpeciesId == SpeciesId;
    public string RepresentText => IsRepresentative ? _texts.Representative : _texts.Represent;
    public string LevelText => Profile?.Level.ToString(CultureInfo.InvariantCulture) ?? "—";
    public string GenderText => Profile?.Gender switch
    {
        PokemonGender.Male => _texts.Male, PokemonGender.Female => _texts.Female,
        PokemonGender.Genderless => _texts.Genderless, _ => "—",
    };
    public string NatureText => Individual?.Nature is { } nature
        ? PokemonNatureDisplayNames.GetName(nature, _texts.Language) : "—";
    public string AbilityText => Profile?.AbilityName is { } name ? DisplayIdentifier(name) : "—";
    public bool HasHiddenAbility => Profile?.AbilityIsHidden == true;
    public string StatsTitle => HasIndividual ? _texts.ActualStats : _texts.BaseStats;
    public IReadOnlyList<PokemonDetailStatRow> Stats
    {
        get
        {
            if (_details is null) return [];
            if (Profile is { } profile)
            {
                var stats = PokemonStatCalculator.Calculate(_details, profile, Individual!.Nature);
                var scale = Math.Max(300, (int)Math.Ceiling(stats.Select(s => s.Value).DefaultIfEmpty().Max() / 100d) * 100);
                return stats.Select(s => new PokemonDetailStatRow(StatName(s.Name), s.Value, scale, $"IV {s.Iv}")).ToArray();
            }
            return PokemonStatCalculator.Order.Where(_details.BaseStats.ContainsKey)
                .Select(s => new PokemonDetailStatRow(StatName(s), _details.BaseStats[s], 300, null)).ToArray();
        }
    }
    public IReadOnlyList<PokemonDetailMoveRow> KnownMoves => Profile?.Moves.Take(4)
        .Select(m => new PokemonDetailMoveRow(DisplayIdentifier(m.Name), $"Lv. {m.LearnedAtLevel}")).ToArray() ?? [];
    public bool HasNoKnownMoves => HasIndividual && KnownMoves.Count == 0;
    public IReadOnlyList<PokemonDetailMoveRow> CompleteMoves => _details?.Moves.Select(m =>
        new PokemonDetailMoveRow(DisplayIdentifier(m.Name), string.Join(" · ", m.LearnMethods.Select(MoveMethod).Distinct()))).ToArray() ?? [];
    public string CompleteMovesTitle => _texts.CompleteMoveList(_details?.Moves.Count ?? 0);
    public string TypesText => string.Join(" · ", _details?.Types.Select(DisplayIdentifier) ?? []);
    public string HeightText => _details is null ? "—" : (_details.Height / 10d).ToString("F1", CultureInfo.InvariantCulture) + " m";
    public string WeightText => _details is null ? "—" : (_details.Weight / 10d).ToString("F1", CultureInfo.InvariantCulture) + " kg";
    public int BaseStatTotal => _details is null ? 0 : PokemonStatCalculator.Order.Where(_details.BaseStats.ContainsKey).Sum(s => _details.BaseStats[s]);
    public string PossibleAbilitiesText => string.Join(" · ", _details?.Abilities.Select(a =>
        DisplayIdentifier(a.Name) + (a.IsHidden ? $" ({_texts.Hidden})" : "")) ?? []);
    public AsyncCommand BackCommand { get; }
    public AsyncCommand RetryCommand { get; }
    public AsyncCommand RepresentCommand { get; }
    internal Task SpriteLoadTask { get; private set; } = Task.CompletedTask;

    public async Task OpenAsync(int speciesId, string name, PokemonRarity rarity, bool shiny, string? preferredInstanceId = null)
    {
        if (_disposed) return;
        Close();
        IsOpen = true;
        SpeciesId = speciesId;
        Name = name;
        _fallbackRarity = rarity;
        _fallbackShiny = shiny;
        _preferredInstanceId = preferredInstanceId;
        RefreshIndividuals();
        Notify();
        SpriteLoadTask = EnsureSpriteAsync();
        await Task.WhenAll(LoadDetailsAsync(_generation), SpriteLoadTask);
    }

    private async Task LoadDetailsAsync(long generation)
    {
        if (!IsCurrent(generation)) return;
        var species = SpeciesId;
        IsLoading = true;
        HasError = false;
        Notify();
        await _store.LoadPokemonDetailsAsync(species);
        if (!IsCurrent(generation)) return;
        _store.PokemonDetailsById.TryGetValue(species, out _details);
        HasError = _details is null;
        IsLoading = false;
        RefreshPresentation();
    }

    public void RefreshPresentation(string? name = null)
    {
        if (_disposed || !IsOpen) return;
        if (name is not null) Name = name;
        if (Name == $"#{SpeciesId}" && _details is not null) Name = DisplayIdentifier(_details.Name);
        RefreshIndividuals();
        Notify();
        SpriteLoadTask = EnsureSpriteAsync();
    }

    private void RefreshIndividuals()
    {
        var selectedId = Profile?.InstanceId;
        Individuals = _store.GetPokemonIndividuals(SpeciesId).Select((individual, index) => new PokemonIndividualOption(individual, index + 1)).ToArray();
        _selected = Individuals.FirstOrDefault(i => i.Individual.Profile.InstanceId == selectedId)
            ?? Individuals.FirstOrDefault(i => i.Individual.Profile.InstanceId == _preferredInstanceId)
            ?? Individuals.FirstOrDefault();
    }

    private async Task EnsureSpriteAsync()
    {
        if (_disposed || !IsOpen) return;
        var identity = (SpeciesId, IsShiny);
        if (_spriteIdentity == identity) return;
        CancelSprite();
        _spriteIdentity = identity;
        Sprite = null;
        Notify();
        var cancellation = new CancellationTokenSource();
        _spriteCancellation = cancellation;
        var generation = _spriteGeneration;
        try
        {
            var asset = await _loadSprite(identity.SpeciesId, identity.IsShiny, cancellation.Token);
            if (_disposed || !IsOpen || generation != _spriteGeneration || cancellation.IsCancellationRequested) return;
            Sprite = asset is null ? null : _decoder.Decode(asset);
            Notify();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Sprite failure leaves metadata and navigation available. */ }
        finally
        {
            if (ReferenceEquals(_spriteCancellation, cancellation)) _spriteCancellation = null;
            cancellation.Dispose();
        }
    }

    public void Close()
    {
        _generation++;
        CancelSprite();
        _spriteIdentity = null;
        IsOpen = false;
        IsLoading = HasError = false;
        _details = null;
        _selected = null;
        _preferredInstanceId = null;
        Individuals = [];
        Sprite = null;
        Notify();
    }
    private void CancelSprite()
    {
        _spriteGeneration++;
        var cancellation = _spriteCancellation;
        _spriteCancellation = null;
        cancellation?.Cancel();
    }
    private bool IsCurrent(long generation) => !_disposed && IsOpen && generation == _generation;
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args) => RefreshPresentation();
    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        RetryCommand.RaiseCanExecuteChanged();
        RepresentCommand.RaiseCanExecuteChanged();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _texts.PropertyChanged -= OnLanguageChanged;
        Close();
    }
    public static string DisplayIdentifier(string value) => string.Join(" ", value.Split('-', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    private string MoveMethod(PokemonMoveLearnMethod method) => method.Method switch
    {
        "level-up" => method.Level > 0 ? $"Lv. {method.Level}" : _texts.StartMove,
        "machine" => "TM", "egg" => _texts.EggMove, "tutor" => _texts.Tutor,
        _ => DisplayIdentifier(method.Method),
    };
    private string StatName(string name) => name switch
    {
        "hp" => "HP", "attack" => _texts.Attack, "defense" => _texts.Defense,
        "special-attack" => _texts.SpecialAttack, "special-defense" => _texts.SpecialDefense,
        "speed" => _texts.Speed, _ => DisplayIdentifier(name),
    };
}
