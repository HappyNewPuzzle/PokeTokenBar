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
    private IReadOnlyList<ShopProductViewModel> _shopProducts = [];
    private IReadOnlyList<BagItemViewModel> _bagItems = [];
    private IReadOnlyList<CollectionEntryViewModel> _collectionEntries = [];
    private string? _resultMessage;

    public EconomyViewModel(
        CompanionStore store,
        Func<CancellationToken, Task> refreshCompanion)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _refreshCompanion = refreshCompanion ?? throw new ArgumentNullException(nameof(refreshCompanion));
        ClearRepresentativeCommand = new AsyncCommand(ClearRepresentativeAsync);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string BalanceText => $"{_store.AvailableTokens:N0} tokens";

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
                token => PurchaseAsync(product, token))).ToArray());
        BagItems = new ReadOnlyCollection<BagItemViewModel>(
            _store.OwnedItems.Select(item => new BagItemViewModel(
                item.Kind,
                ItemName(item.Kind),
                item.Count,
                CanUse(item.Kind),
                item.Kind.IsPassive(),
                token => UseAsync(item.Kind, token))).ToArray());
        CollectionEntries = new ReadOnlyCollection<CollectionEntryViewModel>(BuildCollection().ToArray());
        OnPropertyChanged(nameof(BalanceText));
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
        ResultMessage = result switch
        {
            PurchaseResult.Success => $"Purchased {ProductName(product)}.",
            PurchaseResult.InsufficientFunds => "Not enough tokens.",
            PurchaseResult.AlreadyOwned => "Already owned.",
            PurchaseResult.NotAllowed => "This purchase is not available now.",
            PurchaseResult.PersistenceFailed => "Could not save the purchase.",
            _ => "Unknown product.",
        };
        await _refreshCompanion(cancellationToken);
        Refresh();
    }

    private async Task UseAsync(CompanionItemKind kind, CancellationToken cancellationToken)
    {
        var outcome = await _store.UseItemAsync(kind, cancellationToken);
        ResultMessage = outcome.Result switch
        {
            ItemUseResult.Progressed => "Progress increased.",
            ItemUseResult.Evolved => "Your Pokémon evolved.",
            ItemUseResult.Graduated => "Your Pokémon graduated.",
            ItemUseResult.NatureChanged => $"Nature changed to {outcome.Nature}.",
            ItemUseResult.PersistenceFailed => "Could not save the item use.",
            _ => "This item cannot be used now.",
        };
        await _refreshCompanion(cancellationToken);
        Refresh();
    }

    private async Task SelectRepresentativeAsync(int speciesId, CancellationToken cancellationToken)
    {
        ResultMessage = _store.SetRepresentativeSpeciesId(speciesId)
            ? "Representative updated."
            : "That species is not in the collection.";
        await _refreshCompanion(cancellationToken);
        Refresh();
    }

    private async Task ClearRepresentativeAsync(CancellationToken cancellationToken)
    {
        _store.SetRepresentativeSpeciesId(null);
        ResultMessage = "Representative follows the current companion.";
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
            rarity.ToString(),
            shiny,
            nature?.ToString(),
            current,
            _store.State.RepresentativeSpeciesId == speciesId,
            caughtAt,
            token => SelectRepresentativeAsync(speciesId, token));

    private static string ProductName(ShopProduct product) => product.ProductKind switch
    {
        ShopProductKind.Item => ItemName(product.ItemKind!.Value),
        _ when product.GuaranteedRarity is PokemonRarity rarity => $"{rarity} Egg",
        _ => "Fresh Egg",
    };

    private static string ItemName(CompanionItemKind kind) => kind switch
    {
        CompanionItemKind.Mint => "Mint",
        CompanionItemKind.RareCandy => "Rare Candy",
        CompanionItemKind.ShinyCharm => "Shiny Charm",
        _ => kind.ToString(),
    };

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
        Func<CancellationToken, Task> purchase)
    {
        Product = product;
        Name = name;
        CanPurchase = canPurchase;
        PurchaseCommand = new AsyncCommand(purchase, () => CanPurchase);
    }

    public ShopProduct Product { get; }
    public string Name { get; }
    public string PriceText => $"{Product.Price:N0} tokens";
    public bool CanPurchase { get; }
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
        Func<CancellationToken, Task> use)
    {
        Kind = kind;
        Name = name;
        Count = count;
        CanUse = canUse;
        IsPassive = isPassive;
        UseCommand = new AsyncCommand(use, () => CanUse);
    }

    public CompanionItemKind Kind { get; }
    public string Name { get; }
    public int Count { get; }
    public bool CanUse { get; }
    public bool IsPassive { get; }
    public string StatusText => IsPassive ? "Active" : $"×{Count}";
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
        SelectRepresentativeCommand = new AsyncCommand(selectRepresentative);
    }

    public int SpeciesId { get; }
    public string Name { get; }
    public string Rarity { get; }
    public bool IsShiny { get; }
    public string ShinyText => IsShiny ? "Shiny" : "Normal";
    public string? Nature { get; }
    public bool IsCurrent { get; }
    public bool IsRepresentative { get; }
    public DateTimeOffset? CaughtAt { get; }
    public string RoleText => IsCurrent ? "Current" : IsRepresentative ? "Representative" : "Caught";
    public string? CaughtText => CaughtAt?.ToLocalTime().ToString("g");
    public AsyncCommand SelectRepresentativeCommand { get; }
}
