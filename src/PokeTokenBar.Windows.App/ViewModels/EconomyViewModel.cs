using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PokeTokenBar.Windows.App.Commands;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App.ViewModels;

public sealed class EconomyViewModel : INotifyPropertyChanged
{
    private readonly CompanionStore _store;
    private readonly Func<CancellationToken, Task> _refreshCompanion;
    private readonly LocalizationService _localization;
    private IReadOnlyList<ShopProductViewModel> _shopProducts = [];
    private IReadOnlyList<BagItemViewModel> _bagItems = [];
    private IReadOnlyList<CollectionEntryViewModel> _collectionEntries = [];
    private string? _resultMessage;
    private Func<LocalizationService, string>? _resultText;

    public EconomyViewModel(
        CompanionStore store,
        Func<CancellationToken, Task> refreshCompanion,
        LocalizationService? localization = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _refreshCompanion = refreshCompanion ?? throw new ArgumentNullException(nameof(refreshCompanion));
        _localization = localization ?? new LocalizationService(AppLanguage.En);
        ClearRepresentativeCommand = new AsyncCommand(ClearRepresentativeAsync);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string BalanceText => _localization.Tokens(_store.AvailableTokens);

    public IReadOnlyList<ShopProductViewModel> ShopProducts
    {
        get => _shopProducts;
        private set => SetField(ref _shopProducts, value);
    }

    public IReadOnlyList<BagItemViewModel> BagItems
    {
        get => _bagItems;
        private set => SetField(ref _bagItems, value);
    }

    public IReadOnlyList<CollectionEntryViewModel> CollectionEntries
    {
        get => _collectionEntries;
        private set => SetField(ref _collectionEntries, value);
    }

    public string? ResultMessage
    {
        get => _resultMessage;
        private set => SetField(ref _resultMessage, value);
    }

    public AsyncCommand ClearRepresentativeCommand { get; }

    public void Refresh()
    {
        ShopProducts = new ReadOnlyCollection<ShopProductViewModel>(
            _store.ShopProducts.Select(product => new ShopProductViewModel(
                product,
                ProductName(product),
                CanPurchase(product),
                _localization.Tokens(product.Price),
                _localization.Buy,
                token => PurchaseAsync(product, token))).ToArray());
        BagItems = new ReadOnlyCollection<BagItemViewModel>(
            _store.OwnedItems.Select(item => new BagItemViewModel(
                item.Kind,
                ItemName(item.Kind),
                item.Count,
                CanUse(item.Kind),
                item.Kind.IsPassive(),
                item.Kind.IsPassive() ? _localization.Active : $"×{item.Count}",
                _localization.Use,
                token => UseAsync(item.Kind, token))).ToArray());
        CollectionEntries = new ReadOnlyCollection<CollectionEntryViewModel>(BuildCollection().ToArray());
        OnPropertyChanged(nameof(BalanceText));
        if (_resultText is not null) ResultMessage = _resultText(_localization);
        ClearRepresentativeCommand.RaiseCanExecuteChanged();
    }

    private bool CanPurchase(ShopProduct product)
    {
        if (_store.AvailableTokens < product.Price)
        {
            return false;
        }

        if (product.ProductKind == ShopProductKind.Egg)
        {
            return _store.State.Active is not null;
        }

        return product.ItemKind is not CompanionItemKind item ||
               !item.IsPassive() || _store.ItemCount(item) == 0;
    }

    private bool CanUse(CompanionItemKind kind) => kind switch
    {
        CompanionItemKind.RareCandy =>
            _store.State.Active is not null && _store.CurrentLine is not null,
        CompanionItemKind.Mint => _store.State.Active is not null,
        _ => false,
    };

    private async Task PurchaseAsync(ShopProduct product, CancellationToken cancellationToken)
    {
        var result = await _store.PurchaseAsync(product.Id, cancellationToken);
        SetResult(result switch
        {
            PurchaseResult.Success => text => text.Purchased(ProductName(product)),
            PurchaseResult.InsufficientFunds => text => text.NotEnoughTokens,
            PurchaseResult.AlreadyOwned => text => text.AlreadyOwned,
            PurchaseResult.NotAllowed => text => text.PurchaseUnavailable,
            PurchaseResult.PersistenceFailed => text => text.PurchaseSaveFailed,
            _ => text => text.UnknownProduct,
        });
        await _refreshCompanion(cancellationToken);
        Refresh();
    }

    private async Task UseAsync(CompanionItemKind kind, CancellationToken cancellationToken)
    {
        var outcome = await _store.UseItemAsync(kind, cancellationToken);
        SetResult(outcome.Result switch
        {
            ItemUseResult.Progressed => text => text.ProgressIncreased,
            ItemUseResult.Evolved => text => text.PokemonEvolved,
            ItemUseResult.Graduated => text => text.PokemonGraduated,
            ItemUseResult.NatureChanged => text => text.NatureChanged(
                outcome.Nature is PokemonNature nature
                    ? PokemonNatureDisplayNames.GetName(nature, text.Language)
                    : text.UnknownNature),
            ItemUseResult.PersistenceFailed => text => text.ItemSaveFailed,
            _ => text => text.ItemUnavailable,
        });
        await _refreshCompanion(cancellationToken);
        Refresh();
    }

    private async Task SelectRepresentativeAsync(int speciesId, CancellationToken cancellationToken)
    {
        SetResult(_store.SetRepresentativeSpeciesId(speciesId)
            ? text => text.RepresentativeUpdated
            : text => text.SpeciesNotInCollection);
        await _refreshCompanion(cancellationToken);
        Refresh();
    }

    private async Task ClearRepresentativeAsync(CancellationToken cancellationToken)
    {
        _store.SetRepresentativeSpeciesId(null);
        SetResult(text => text.RepresentativeFollowsCurrent);
        await _refreshCompanion(cancellationToken);
        Refresh();
    }

    private IEnumerable<CollectionEntryViewModel> BuildCollection()
    {
        if (_store.State.Active is { } active)
        {
            foreach (var speciesId in active.PathIds.Take(Math.Min(active.StageIndex + 1, active.PathIds.Count)))
            {
                yield return Collection(
                    speciesId,
                    _store.CurrentLine?.LocalizedName(speciesId, _store.State.Language) ?? $"#{speciesId}",
                    active.Rarity,
                    active.IsShiny,
                    active.Nature,
                    speciesId == active.CurrentId,
                    null);
            }
        }

        foreach (var entry in _store.State.Dex.OrderByDescending(entry => entry.CaughtAt))
        {
            foreach (var speciesId in entry.ChainOrder)
            {
                var name = entry.Names is not null && entry.Names.TryGetValue(speciesId, out var names)
                    ? _store.State.Language.ResolveName(names) ?? $"#{speciesId}"
                    : $"#{speciesId}";
                yield return Collection(
                    speciesId, name, entry.Rarity, entry.IsShiny, entry.Nature, false, entry.CaughtAt);
            }
        }
    }

    private CollectionEntryViewModel Collection(
        int speciesId,
        string name,
        PokemonRarity rarity,
        bool shiny,
        PokemonNature? nature,
        bool current,
        DateTimeOffset? caughtAt) => new(
            speciesId,
            name,
            CompanionDisplayTexts.Rarity(rarity, _localization.Language),
            shiny,
            nature is PokemonNature value
                ? PokemonNatureDisplayNames.GetName(value, _localization.Language)
                : _localization.UnknownNature,
            current,
            _store.State.RepresentativeSpeciesId == speciesId,
            caughtAt,
            shiny ? _localization.Shiny : _localization.Normal,
            current ? _localization.Current :
                _store.State.RepresentativeSpeciesId == speciesId
                    ? _localization.Representative : _localization.Caught,
            caughtAt is DateTimeOffset caught ? _localization.LocalDate(caught) : null,
            _localization.Represent,
            token => SelectRepresentativeAsync(speciesId, token));

    private string ProductName(ShopProduct product) => product.ProductKind switch
    {
        ShopProductKind.Item => ItemName(product.ItemKind!.Value),
        _ when product.GuaranteedRarity is PokemonRarity rarity => _localization.RarityEgg(rarity),
        _ => _localization.FreshEgg,
    };

    private string ItemName(CompanionItemKind kind) => kind switch
    {
        CompanionItemKind.Mint => _localization.Mint,
        CompanionItemKind.RareCandy => _localization.RareCandy,
        CompanionItemKind.ShinyCharm => _localization.ShinyCharm,
        _ => kind.ToString(),
    };

    private void SetResult(Func<LocalizationService, string> text)
    {
        _resultText = text;
        ResultMessage = text(_localization);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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

public sealed class ShopProductViewModel
{
    public ShopProductViewModel(
        ShopProduct product,
        string name,
        bool canPurchase,
        string priceText,
        string buyText,
        Func<CancellationToken, Task> purchase)
    {
        Product = product;
        Name = name;
        CanPurchase = canPurchase;
        PriceText = priceText;
        BuyText = buyText;
        PurchaseCommand = new AsyncCommand(purchase, () => CanPurchase);
    }

    public ShopProduct Product { get; }
    public string Name { get; }
    public string PriceText { get; }
    public bool CanPurchase { get; }
    public string BuyText { get; }
    public AsyncCommand PurchaseCommand { get; }
}

public sealed class BagItemViewModel
{
    public BagItemViewModel(
        CompanionItemKind kind,
        string name,
        int count,
        bool canUse,
        bool isPassive,
        string statusText,
        string useText,
        Func<CancellationToken, Task> use)
    {
        Kind = kind;
        Name = name;
        Count = count;
        CanUse = canUse;
        IsPassive = isPassive;
        StatusText = statusText;
        UseText = useText;
        UseCommand = new AsyncCommand(use, () => CanUse);
    }

    public CompanionItemKind Kind { get; }
    public string Name { get; }
    public int Count { get; }
    public bool CanUse { get; }
    public bool IsPassive { get; }
    public string UseText { get; }
    public string StatusText { get; }
    public AsyncCommand UseCommand { get; }
}

public sealed class CollectionEntryViewModel
{
    public CollectionEntryViewModel(
        int speciesId,
        string name,
        string rarity,
        bool isShiny,
        string? nature,
        bool isCurrent,
        bool isRepresentative,
        DateTimeOffset? caughtAt,
        string shinyText,
        string roleText,
        string? caughtText,
        string representText,
        Func<CancellationToken, Task> selectRepresentative)
    {
        SpeciesId = speciesId;
        Name = name;
        Rarity = rarity;
        IsShiny = isShiny;
        Nature = nature;
        IsCurrent = isCurrent;
        IsRepresentative = isRepresentative;
        CaughtAt = caughtAt;
        ShinyText = shinyText;
        RoleText = roleText;
        CaughtText = caughtText;
        RepresentText = representText;
        SelectRepresentativeCommand = new AsyncCommand(selectRepresentative);
    }

    public int SpeciesId { get; }
    public string Name { get; }
    public string Rarity { get; }
    public bool IsShiny { get; }
    public string ShinyText { get; }
    public string? Nature { get; }
    public bool IsCurrent { get; }
    public bool IsRepresentative { get; }
    public DateTimeOffset? CaughtAt { get; }
    public string RepresentText { get; }
    public string RoleText { get; }
    public string? CaughtText { get; }
    public AsyncCommand SelectRepresentativeCommand { get; }
}
