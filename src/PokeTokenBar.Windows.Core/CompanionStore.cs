namespace PokeTokenBar.Windows.Core;

public sealed class CompanionStore
{
    private readonly IPokeApiClient _provider;
    private readonly ICompanionPersistence _persistence;
    private readonly Random _random;
    private readonly TimeProvider _timeProvider;
    private readonly bool _dittoDisguiseRollingEnabled;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public event EventHandler<CompanionGameEvent>? GameEventOccurred;

    public CompanionStore(
        IPokeApiClient provider,
        ICompanionPersistence persistence,
        Random? random = null,
        TimeProvider? timeProvider = null,
        bool dittoDisguiseRollingEnabled = false)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _random = random ?? Random.Shared;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dittoDisguiseRollingEnabled = dittoDisguiseRollingEnabled;
        State = NormalizeState(TryLoad() ?? new CompanionState());
        DisplayState = State.Active is null
            ? CompanionStateKind.Egg
            : CompanionStateKind.Idle;
        RefreshRepresentativeSubject();
    }

    public CompanionState State { get; private set; }

    public CompanionStateKind DisplayState { get; private set; }

    public EvoLine? CurrentLine { get; private set; }

    public bool IsHatching { get; private set; }

    public RepresentativeSubject RepresentativeSubject { get; private set; }

    public int? CurrentSpeciesId => State.Active?.CurrentId;

    public bool CurrentIsShiny
    {
        get
        {
            var active = State.Active;
            if (active is null || active.DittoDisguise is not null && !active.DittoRevealed)
            {
                return false;
            }

            return active.IsShiny;
        }
    }

    public async Task<bool> HatchAsync(
        int baseSpeciesId,
        CancellationToken cancellationToken = default)
    {
        if (!await _mutationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var previous = State;
        IsHatching = true;
        try
        {
            var success = await HatchCoreAsync(baseSpeciesId, cancellationToken).ConfigureAwait(false);
            if (success) PublishCompanionChanges(previous, State);
            return success;
        }
        finally
        {
            IsHatching = false;
            _mutationGate.Release();
        }
    }

    public async Task<bool> HatchRandomAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _mutationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var previous = State;
        IsHatching = true;
        try
        {
            var success = await HatchRandomCoreAsync(cancellationToken).ConfigureAwait(false);
            if (success) PublishCompanionChanges(previous, State);
            return success;
        }
        finally
        {
            IsHatching = false;
            _mutationGate.Release();
        }
    }

    public async Task<bool> LoadCurrentLineAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _mutationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            var previous = State;
            var loaded = await LoadCurrentLineCoreAsync(cancellationToken).ConfigureAwait(false);
            if (loaded)
            {
                await ApplyUsageCoreAsync(0, cancellationToken).ConfigureAwait(false);
                PublishCompanionChanges(previous, State);
            }

            return loaded;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public long AvailableTokens => Math.Max(0, State.UsedSinceInstall - State.SpentTokens);

    public bool OwnsShinyCharm => ItemCount(CompanionItemKind.ShinyCharm) > 0;

    public int ItemCount(CompanionItemKind kind) =>
        State.Inventory.GetValueOrDefault(kind.Key());

    public IReadOnlyList<InventoryStack> OwnedItems =>
        Enum.GetValues<CompanionItemKind>()
            .Select(kind => new InventoryStack(kind, ItemCount(kind)))
            .Where(item => item.Count > 0)
            .ToArray();

    public IReadOnlyList<ShopProduct> ShopProducts
    {
        get
        {
            var products = Enum.GetValues<CompanionItemKind>()
                .Select(kind => new ShopProduct(
                    kind.Key(), ShopProductKind.Item, kind.Price(), kind))
                .ToList();
            if (State.Active is not null)
            {
                products.AddRange(CompanionEconomyRules.EggTiers.Select(tier => new ShopProduct(
                    tier is null ? "egg.basic" : $"egg.{tier.Value.ToString().ToLowerInvariant()}",
                    ShopProductKind.Egg,
                    CompanionEconomyRules.EggPrice(tier),
                    GuaranteedRarity: tier)));
            }

            return products
                .OrderBy(product => product.ItemKind is CompanionItemKind item &&
                                    item.IsPassive() && ItemCount(item) > 0)
                .ThenBy(product => product.Price)
                .ToArray();
        }
    }

    public async Task<PurchaseResult> PurchaseAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var product = AllShopProducts().FirstOrDefault(candidate =>
                candidate.Id.Equals(productId, StringComparison.Ordinal));
            if (product is null)
            {
                return PurchaseResult.InvalidProduct;
            }

            if (product.ProductKind == ShopProductKind.Egg && State.Active is null)
            {
                return PurchaseResult.NotAllowed;
            }

            if (product.ItemKind is CompanionItemKind passive &&
                passive.IsPassive() && ItemCount(passive) > 0)
            {
                return PurchaseResult.AlreadyOwned;
            }

            if (AvailableTokens < product.Price)
            {
                return PurchaseResult.InsufficientFunds;
            }

            var next = State with { SpentTokens = AddClamped(State.SpentTokens, product.Price) };
            if (product.ItemKind is CompanionItemKind item)
            {
                var inventory = new Dictionary<string, int>(State.Inventory, StringComparer.Ordinal)
                {
                    [item.Key()] = ItemCount(item) + 1,
                };
                next = next with { Inventory = inventory };
                return CommitEconomyState(next)
                    ? PurchaseResult.Success
                    : PurchaseResult.PersistenceFailed;
            }

            next = next with
            {
                Active = null,
                RepresentativeSpeciesId = State.RepresentativeSpeciesId is int selected &&
                                          State.Dex.Any(entry => entry.ChainOrder.Contains(selected))
                    ? selected
                    : null,
                EggUsage = 0,
                EggTier = product.GuaranteedRarity,
                PendingHatchId = null,
            };
            if (!CommitEconomyState(next))
            {
                return PurchaseResult.PersistenceFailed;
            }

            CurrentLine = null;
            DisplayState = CompanionStateKind.Egg;
            RefreshRepresentativeSubject();
            return PurchaseResult.Success;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<ItemUseOutcome> UseItemAsync(
        CompanionItemKind kind,
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (kind == CompanionItemKind.ShinyCharm || ItemCount(kind) <= 0 || State.Active is null)
            {
                return new ItemUseOutcome(ItemUseResult.Unavailable);
            }

            if (kind == CompanionItemKind.Mint)
            {
                var active = State.Active;
                var pool = Enum.GetValues<PokemonNature>()
                    .Where(nature => nature != active.Nature)
                    .ToArray();
                var nature = pool[_random.Next(pool.Length)];
                var inventory = DecrementedInventory(kind);
                var next = State with
                {
                    Active = active with { Nature = nature },
                    Inventory = inventory,
                };
                return CommitEconomyState(next)
                    ? new ItemUseOutcome(ItemUseResult.NatureChanged, nature)
                    : new ItemUseOutcome(ItemUseResult.PersistenceFailed);
            }

            if (CurrentLine is null)
            {
                return new ItemUseOutcome(ItemUseResult.Unavailable);
            }

            var previousState = State;
            var previousLine = CurrentLine;
            var previousDisplayState = DisplayState;
            var previousRepresentative = RepresentativeSubject;
            var beforeStage = State.Active.StageIndex;
            State = State with { Inventory = DecrementedInventory(kind) };
            await ApplyUsageCoreAsync(
                    CompanionEconomyRules.RareCandyExperience,
                    cancellationToken,
                    save: false)
                .ConfigureAwait(false);
            var result = State.Active is null
                ? ItemUseResult.Graduated
                : State.Active.StageIndex > beforeStage
                    ? ItemUseResult.Evolved
                    : ItemUseResult.Progressed;
            try
            {
                _persistence.Save(State);
                RefreshRepresentativeSubject();
                PublishCompanionChanges(previousState, State);
                return new ItemUseOutcome(result);
            }
            catch (Exception)
            {
                State = previousState;
                CurrentLine = previousLine;
                DisplayState = previousDisplayState;
                RepresentativeSubject = previousRepresentative;
                return new ItemUseOutcome(ItemUseResult.PersistenceFailed);
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public static IReadOnlyList<CandyGrant> EvaluateCandyGrants(
        IEnumerable<CandyWindow> windows,
        IDictionary<string, int> grantTier)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(grantTier);
        var grants = new List<CandyGrant>();
        foreach (var window in windows)
        {
            if (window.Utilization < 100)
            {
                grantTier.Remove(window.Key);
                continue;
            }

            if (grantTier.TryGetValue(window.Key, out var previous) && previous >= 1)
            {
                continue;
            }

            grantTier[window.Key] = 1;
            grants.Add(new CandyGrant(
                window.Key,
                window.Name,
                window.Kind == LimitWindowClass.Weekly
                    ? CompanionEconomyRules.WeeklyCandyGrant
                    : 1));
        }

        return grants;
    }

    public async Task<int> GrantCandiesAsync(
        IReadOnlyList<CandyWindow> windows,
        bool limitsReady,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (!limitsReady)
        {
            return 0;
        }

        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var ledger = new Dictionary<string, int>(State.CandyGrantTier, StringComparer.Ordinal);
            if (!State.CandyFeatureSeeded)
            {
                foreach (var window in windows.Where(window => window.Utilization >= 100))
                {
                    ledger[window.Key] = 1;
                }

                var seeded = State with { CandyGrantTier = ledger, CandyFeatureSeeded = true };
                CommitEconomyState(seeded);
                return 0;
            }

            var grants = EvaluateCandyGrants(windows, ledger);
            var total = grants.Sum(grant => grant.Count);
            var changed = total > 0 || !ledger.OrderBy(pair => pair.Key)
                .SequenceEqual(State.CandyGrantTier.OrderBy(pair => pair.Key));
            if (!changed)
            {
                return 0;
            }

            var inventory = new Dictionary<string, int>(State.Inventory, StringComparer.Ordinal);
            inventory[CompanionItemKind.RareCandy.Key()] =
                inventory.GetValueOrDefault(CompanionItemKind.RareCandy.Key()) + total;
            var next = State with { CandyGrantTier = ledger, Inventory = inventory };
            if (!CommitEconomyState(next)) return 0;
            if (total > 0)
            {
                PublishGameEvent(new CompanionGameEvent(
                    CompanionGameEventKind.Reward, Count: total));
            }
            return total;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task UpdateUsageAsync(
        IReadOnlyDictionary<string, long> todayTokensByProvider,
        string todayDate,
        bool hasUsageData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(todayTokensByProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(todayDate);

        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previousState = State;
            var current = todayTokensByProvider.ToDictionary(
                static pair => pair.Key,
                static pair => Math.Max(0, pair.Value),
                StringComparer.Ordinal);
            var hasCurrentProviderData = hasUsageData && current.Count > 0;
            var delta = 0L;

            if (!State.InstallBaselineSet)
            {
                if (!hasCurrentProviderData)
                {
                    await MaintainProgressionAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                State = State with
                {
                    InstallBaselineSet = true,
                    ClaimedTodayTokensByProvider = current,
                    LastDate = todayDate,
                };
            }
            else if (hasCurrentProviderData)
            {
                if (State.ClaimedTodayTokensByProvider is null)
                {
                    State = State with
                    {
                        ClaimedTodayTokensByProvider = current,
                        LastDate = todayDate,
                    };
                }
                else if (!string.Equals(State.LastDate, todayDate, StringComparison.Ordinal))
                {
                    var nextLedger = State.ClaimedTodayTokensByProvider.Keys.ToDictionary(
                        static id => id,
                        static _ => 0L,
                        StringComparer.Ordinal);
                    foreach (var pair in current)
                    {
                        nextLedger[pair.Key] = pair.Value;
                        delta = AddClamped(delta, pair.Value);
                    }

                    State = State with
                    {
                        ClaimedTodayTokensByProvider = nextLedger,
                        LastDate = todayDate,
                    };
                }
                else
                {
                    var ledger = new Dictionary<string, long>(
                        State.ClaimedTodayTokensByProvider,
                        StringComparer.Ordinal);
                    foreach (var pair in current)
                    {
                        if (!ledger.TryGetValue(pair.Key, out var previous) || pair.Value < previous)
                        {
                            ledger[pair.Key] = pair.Value;
                            continue;
                        }

                        delta = AddClamped(delta, pair.Value - previous);
                        ledger[pair.Key] = pair.Value;
                    }

                    State = State with { ClaimedTodayTokensByProvider = ledger };
                }
            }

            if (delta > 0)
            {
                State = State with
                {
                    UsedSinceInstall = AddClamped(State.UsedSinceInstall, delta),
                };
                if (State.Active is null)
                {
                    State = State with { EggUsage = AddClamped(State.EggUsage, delta) };
                }
                else
                {
                    await ApplyUsageCoreAsync(delta, cancellationToken).ConfigureAwait(false);
                }
            }

            TrySave();
            await MaintainProgressionAsync(cancellationToken).ConfigureAwait(false);
            PublishCompanionChanges(previousState, State);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public bool SetRepresentativeSpeciesId(int? speciesId)
    {
        if (speciesId is int selected && !State.OwnsSpecies(selected))
        {
            return false;
        }

        State = State with { RepresentativeSpeciesId = speciesId };
        RefreshRepresentativeSubject();
        TrySave();
        return true;
    }

    public void SetLanguage(AppLanguage language)
    {
        State = State with { Language = language };
        TrySave();
    }

    public void Reset()
    {
        State = new CompanionState();
        CurrentLine = null;
        DisplayState = CompanionStateKind.Egg;
        RefreshRepresentativeSubject();
        try
        {
            _persistence.Delete();
        }
        catch (Exception)
        {
            // Swift's save/delete boundary is best effort; memory state remains usable.
        }
    }

    private async Task<bool> HatchRandomCoreAsync(CancellationToken cancellationToken)
    {
        if (State.PendingHatchId is int pending)
        {
            return await HatchCoreAsync(pending, cancellationToken).ConfigureAwait(false);
        }

        int? selectedId;
        try
        {
            var all = await _provider
                .GetBaseSpeciesIndexAsync(cancellationToken)
                .ConfigureAwait(false);
            var candidates = all
                .Where(candidate =>
                    candidate.Id != PokemonOdds.DittoSpeciesId &&
                    PokemonAssets.HasAnimatedSprite(candidate.Id) &&
                    (State.EggTier is null || State.EggTier.Value.Includes(candidate.CaptureRate)))
                .ToArray();
            if (candidates.Length == 0)
            {
                return false;
            }

            var weights = candidates
                .Select(candidate => State.CollectedFinals.Any(value =>
                        value.StartsWith($"{candidate.Id}:", StringComparison.Ordinal))
                    ? Math.Max(1, candidate.CaptureRate / 2)
                    : Math.Max(1, candidate.CaptureRate))
                .ToArray();
            var roll = _random.Next(weights.Sum());
            var selected = candidates[^1];
            for (var index = 0; index < candidates.Length; index++)
            {
                if (roll < weights[index])
                {
                    selected = candidates[index];
                    break;
                }

                roll -= weights[index];
            }

            selectedId = selected.Id;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            selectedId = await ChooseBaseViaRestAsync(cancellationToken).ConfigureAwait(false);
        }

        if (selectedId is not int id)
        {
            return false;
        }

        State = State with { PendingHatchId = id };
        TrySave();
        return await HatchCoreAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task MaintainProgressionAsync(CancellationToken cancellationToken)
    {
        if (State.Active is null)
        {
            DisplayState = CompanionStateKind.Egg;
            if (!State.InstallBaselineSet || State.EggUsage < PokemonBalance.EggHatchThreshold)
            {
                return;
            }

            IsHatching = true;
            try
            {
                await HatchRandomCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                IsHatching = false;
            }

            return;
        }

        if (CurrentLine is null)
        {
            await LoadCurrentLineCoreAsync(cancellationToken).ConfigureAwait(false);
        }

        await ApplyUsageCoreAsync(0, cancellationToken).ConfigureAwait(false);
        if (DisplayState != CompanionStateKind.LevelUp)
        {
            DisplayState = State.Active is null
                ? CompanionStateKind.Egg
                : CompanionStateKind.Idle;
        }
    }

    private async Task<bool> LoadCurrentLineCoreAsync(CancellationToken cancellationToken)
    {
        var active = State.Active;
        if (active is null)
        {
            return false;
        }

        try
        {
            var line = await _provider
                .GetLineAsync(active.BaseId, cancellationToken)
                .ConfigureAwait(false);
            if (State.Active?.BaseId != active.BaseId)
            {
                return false;
            }

            CurrentLine = line;
            NormalizeEvolutionPlan(line);
            TrySave();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task ApplyUsageCoreAsync(
        long delta,
        CancellationToken cancellationToken,
        bool save = true)
    {
        if (State.Active is not MonState active)
        {
            return;
        }

        active = active with { UsedAtStage = AddClamped(active.UsedAtStage, delta) };
        State = State with { Active = active };
        if (CurrentLine is null)
        {
            TrySave();
            return;
        }

        for (var guard = 0; State.Active is MonState current && guard < 50; guard++)
        {
            var threshold = PokemonBalance.PhaseThreshold(
                current.Rarity,
                current.TotalForms,
                current.StageIndex);
            if (current.UsedAtStage < threshold)
            {
                break;
            }

            var node = CurrentLine.Tree.Find(current.CurrentId);
            if (node is null)
            {
                break;
            }

            if (current.DittoDisguise is not null && !current.DittoRevealed)
            {
                if (!await RevealDittoCoreAsync(current, threshold, cancellationToken)
                        .ConfigureAwait(false))
                {
                    break;
                }

                continue;
            }

            if (node.Children.Count == 0)
            {
                GraduateCore(current);
                break;
            }

            var nextIndex = current.StageIndex + 1;
            var next = nextIndex < current.PlannedPathIds.Count
                ? node.Children.FirstOrDefault(child =>
                    child.SpeciesId == current.PlannedPathIds[nextIndex])
                : null;
            IReadOnlyList<int> planned = current.PlannedPathIds;
            if (next is null)
            {
                next = PickEvolutionChild(node, current.BaseId);
                planned = current.PathIds
                    .Take(current.StageIndex + 1)
                    .Concat(BuildEvolutionPlan(next, current.BaseId))
                    .ToArray();
            }

            var path = current.PathIds
                .Take(current.StageIndex + 1)
                .Append(next.SpeciesId)
                .ToArray();
            State = State with
            {
                Active = current with
                {
                    PathIds = path,
                    PlannedPathIds = planned,
                    StageIndex = nextIndex,
                    UsedAtStage = current.UsedAtStage - threshold,
                    TotalForms = planned.Count,
                },
            };
            DisplayState = CompanionStateKind.LevelUp;
        }

        if (save)
        {
            TrySave();
        }
    }

    public static bool DittoDisguiseHit(PokemonRarity rarity, int totalForms, int roll) =>
        rarity == PokemonRarity.Common &&
        totalForms >= 2 &&
        roll % PokemonOdds.DittoDisguiseDenominator == 0;

    private async Task<bool> RevealDittoCoreAsync(
        MonState disguise,
        long firstEvolutionThreshold,
        CancellationToken cancellationToken)
    {
        EvoLine dittoLine;
        try
        {
            dittoLine = await _provider
                .GetLineAsync(PokemonOdds.DittoSpeciesId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }

        if (!ReferenceEquals(State.Active, disguise) ||
            disguise.DittoDisguise is null ||
            disguise.DittoRevealed ||
            disguise.UsedAtStage < firstEvolutionThreshold)
        {
            return false;
        }

        var plan = BuildEvolutionPlan(dittoLine.Tree, dittoLine.BaseId);
        State = State with
        {
            Active = disguise with
            {
                BaseId = dittoLine.BaseId,
                PathIds = [dittoLine.BaseId],
                PlannedPathIds = plan,
                StageIndex = 0,
                UsedAtStage = Math.Max(0, disguise.UsedAtStage - firstEvolutionThreshold),
                Rarity = dittoLine.Rarity,
                TotalForms = plan.Count,
                DittoRevealed = true,
            },
        };
        ReconcileRepresentativeSelection();
        CurrentLine = dittoLine;
        DisplayState = CompanionStateKind.LevelUp;
        RefreshRepresentativeSubject();
        return true;
    }

    private EvoNode PickEvolutionChild(EvoNode node, int baseId)
    {
        var fresh = node.Children
            .Where(child => child.FinalIds.Any(finalId =>
                !State.CollectedFinals.Contains($"{baseId}:{finalId}")))
            .ToArray();
        var pool = fresh.Length == 0 ? node.Children : fresh;
        return pool[_random.Next(pool.Count)];
    }

    private IReadOnlyList<int> BuildEvolutionPlan(EvoNode root, int baseId)
    {
        var result = new List<int>();
        var current = root;
        while (true)
        {
            result.Add(current.SpeciesId);
            if (current.Children.Count == 0)
            {
                return result;
            }

            current = PickEvolutionChild(current, baseId);
        }
    }

    private void NormalizeEvolutionPlan(EvoLine line)
    {
        if (State.Active is not MonState active)
        {
            return;
        }

        var realized = new List<int> { line.Tree.SpeciesId };
        var node = line.Tree;
        if (active.PathIds.FirstOrDefault() == line.Tree.SpeciesId)
        {
            foreach (var id in active.PathIds.Take(active.StageIndex + 1).Skip(1))
            {
                var child = node.Children.FirstOrDefault(candidate => candidate.SpeciesId == id);
                if (child is null)
                {
                    break;
                }

                realized.Add(id);
                node = child;
            }
        }

        var planValid = active.PlannedPathIds.Count >= realized.Count &&
            active.PlannedPathIds.Take(realized.Count).SequenceEqual(realized) &&
            IsCompletePath(line.Tree, active.PlannedPathIds);
        var plan = planValid
            ? active.PlannedPathIds
            : realized.Concat(BuildEvolutionPlan(node, active.BaseId).Skip(1)).ToArray();
        State = State with
        {
            Active = active with
            {
                PathIds = realized,
                PlannedPathIds = plan,
                StageIndex = realized.Count - 1,
                TotalForms = plan.Count,
            },
        };
    }

    private static bool IsCompletePath(EvoNode root, IReadOnlyList<int> path)
    {
        if (path.Count == 0 || path[0] != root.SpeciesId)
        {
            return false;
        }

        EvoNode? node = root;
        foreach (var id in path.Skip(1))
        {
            node = node.Children.FirstOrDefault(child => child.SpeciesId == id);
            if (node is null)
            {
                return false;
            }
        }

        return node.Children.Count == 0;
    }

    private void GraduateCore(MonState active)
    {
        var finalId = active.CurrentId;
        var collected = new HashSet<string>(State.CollectedFinals, StringComparer.Ordinal)
        {
            $"{active.BaseId}:{finalId}",
        };
        var names = CurrentLine is null
            ? null
            : active.PathIds
                .Where(CurrentLine.Names.ContainsKey)
                .ToDictionary(id => id, id => CurrentLine.Names[id]);
        var dex = State.Dex.Append(new DexEntry
        {
            BaseId = active.BaseId,
            FinalId = finalId,
            ChainOrder = active.PathIds,
            Rarity = active.Rarity,
            CaughtAt = _timeProvider.GetUtcNow(),
            IsShiny = active.IsShiny,
            Nature = active.Nature,
            Names = names,
        }).ToArray();
        State = State with
        {
            Active = null,
            Dex = dex,
            CollectedFinals = collected,
            EggUsage = 0,
            EggTier = null,
            PendingHatchId = null,
        };
        CurrentLine = null;
        DisplayState = CompanionStateKind.Egg;
        RefreshRepresentativeSubject();
    }

    private static long AddClamped(long value, long delta) =>
        delta > long.MaxValue - value ? long.MaxValue : value + delta;

    private async Task<bool> HatchCoreAsync(
        int baseSpeciesId,
        CancellationToken cancellationToken)
    {
        EvoLine line;
        try
        {
            line = await _provider
                .GetLineAsync(baseSpeciesId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }

        if (State.EggTier is PokemonRarity guaranteed &&
            line.Rarity.SortRank() < guaranteed.SortRank())
        {
            State = State with { PendingHatchId = null };
            TrySave();
            return false;
        }

        var overflow = Math.Max(0, State.EggUsage - PokemonBalance.EggHatchThreshold);
        var natures = Enum.GetValues<PokemonNature>();
        var isShiny = CompanionEconomyRules.RollsShiny(
            _random.Next(OwnsShinyCharm
                ? CompanionEconomyRules.ShinyCharmDenominator
                : PokemonOdds.ShinyDenominator),
            OwnsShinyCharm);
        var nature = natures[_random.Next(natures.Length)];
        var dittoDisguise = _dittoDisguiseRollingEnabled &&
                            DittoDisguiseHit(
                                line.Rarity,
                                line.TotalForms,
                                _random.Next(PokemonOdds.DittoDisguiseDenominator))
            ? line.BaseId
            : (int?)null;
        var plan = BuildEvolutionPlan(line.Tree, line.BaseId);
        var active = new MonState
        {
            BaseId = line.BaseId,
            PathIds = [line.BaseId],
            PlannedPathIds = plan,
            StageIndex = 0,
            UsedAtStage = 0,
            Rarity = line.Rarity,
            TotalForms = plan.Count,
            IsShiny = isShiny,
            Nature = nature,
            DittoDisguise = dittoDisguise,
        };

        State = State with
        {
            Active = active,
            EggUsage = 0,
            EggTier = null,
            PendingHatchId = null,
        };
        CurrentLine = line;
        DisplayState = CompanionStateKind.LevelUp;
        RefreshRepresentativeSubject();
        if (overflow > 0)
        {
            await ApplyUsageCoreAsync(overflow, cancellationToken).ConfigureAwait(false);
        }

        TrySave();
        return true;
    }

    private async Task<int?> ChooseBaseViaRestAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var id = _random.Next(
                PokemonAssets.FirstAnimatedSpeciesId,
                PokemonAssets.LastAnimatedSpeciesId + 1);
            BaseSpecies? candidate;
            try
            {
                candidate = await _provider
                    .GetBaseSpeciesAsync(id, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }

            if (candidate is not null &&
                candidate.Id != PokemonOdds.DittoSpeciesId &&
                (State.EggTier is null || State.EggTier.Value.Includes(candidate.CaptureRate)))
            {
                return id;
            }
        }

        return null;
    }

    private void RefreshRepresentativeSubject()
    {
        RepresentativeSubject = State.RepresentativeSpeciesId is int selected
            ? new RepresentativeSubject(selected, State.OwnsShinySpecies(selected))
            : new RepresentativeSubject(CurrentSpeciesId, CurrentIsShiny);
    }

    private void ReconcileRepresentativeSelection()
    {
        if (State.RepresentativeSpeciesId is int selected && !State.OwnsSpecies(selected))
        {
            State = State with { RepresentativeSpeciesId = null };
        }
    }

    private CompanionState? TryLoad()
    {
        try
        {
            return _persistence.Load();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void TrySave()
    {
        RefreshRepresentativeSubject();
        try
        {
            _persistence.Save(State);
        }
        catch (Exception)
        {
            // Persistence failure does not discard a successfully selected companion.
        }
    }

    private IReadOnlyList<ShopProduct> AllShopProducts() =>
        Enum.GetValues<CompanionItemKind>()
            .Select(kind => new ShopProduct(
                kind.Key(), ShopProductKind.Item, kind.Price(), kind))
            .Concat(CompanionEconomyRules.EggTiers.Select(tier => new ShopProduct(
                tier is null ? "egg.basic" : $"egg.{tier.Value.ToString().ToLowerInvariant()}",
                ShopProductKind.Egg,
                CompanionEconomyRules.EggPrice(tier),
                GuaranteedRarity: tier)))
            .ToArray();

    private IReadOnlyDictionary<string, int> DecrementedInventory(CompanionItemKind kind)
    {
        var inventory = new Dictionary<string, int>(State.Inventory, StringComparer.Ordinal)
        {
            [kind.Key()] = Math.Max(0, ItemCount(kind) - 1),
        };
        return inventory;
    }

    private bool CommitEconomyState(CompanionState next)
    {
        try
        {
            _persistence.Save(next);
            State = next;
            RefreshRepresentativeSubject();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void PublishCompanionChanges(CompanionState previous, CompanionState current)
    {
        if (previous.Active is null &&
            current.Active is { DittoRevealed: true, DittoDisguise: int disguise } revealed)
        {
            PublishGameEvent(new CompanionGameEvent(CompanionGameEventKind.Hatch, disguise));
            PublishGameEvent(new CompanionGameEvent(
                CompanionGameEventKind.DittoReveal,
                revealed.CurrentId,
                PreviousSpeciesId: disguise,
                IsShiny: revealed.IsShiny));
            return;
        }

        if (previous.Active is
                { DittoRevealed: false, DittoDisguise: int previousDisguise } hidden &&
            current.Active is null &&
            current.Dex.Count > previous.Dex.Count &&
            current.Dex[^1] is { BaseId: PokemonOdds.DittoSpeciesId } ditto)
        {
            PublishGameEvent(new CompanionGameEvent(
                CompanionGameEventKind.DittoReveal,
                PokemonOdds.DittoSpeciesId,
                PreviousSpeciesId: previousDisguise,
                IsShiny: hidden.IsShiny));
            PublishGameEvent(new CompanionGameEvent(
                CompanionGameEventKind.Graduation,
                ditto.FinalId));
            return;
        }

        CompanionGameEvent? gameEvent = null;
        if (previous.Active is null && current.Active is { } hatched)
        {
            gameEvent = new CompanionGameEvent(
                CompanionGameEventKind.Hatch,
                hatched.CurrentId,
                IsShiny: hatched.IsShiny &&
                    (hatched.DittoDisguise is null || hatched.DittoRevealed));
        }
        else if (previous.Active is { } graduated && current.Active is null &&
                 current.Dex.Count > previous.Dex.Count)
        {
            gameEvent = new CompanionGameEvent(CompanionGameEventKind.Graduation, graduated.CurrentId);
        }
        else if (previous.Active is { } before && current.Active is { } after &&
                 before.CurrentId != after.CurrentId)
        {
            gameEvent = before.DittoDisguise is not null &&
                        !before.DittoRevealed &&
                        after.DittoRevealed
                ? new CompanionGameEvent(
                    CompanionGameEventKind.DittoReveal,
                    after.CurrentId,
                    PreviousSpeciesId: before.DittoDisguise,
                    IsShiny: after.IsShiny)
                : new CompanionGameEvent(CompanionGameEventKind.Evolution, after.CurrentId);
        }

        if (gameEvent is not null) PublishGameEvent(gameEvent);
    }

    private void PublishGameEvent(CompanionGameEvent gameEvent)
    {
        if (GameEventOccurred is not { } handlers) return;
        foreach (var subscriber in handlers.GetInvocationList())
        {
            try { ((EventHandler<CompanionGameEvent>)subscriber)(this, gameEvent); }
            catch (Exception) { }
        }
    }

    private static CompanionState NormalizeState(CompanionState state)
    {
        var active = state.Active;
        if (active is not null)
        {
            if (active.PathIds is null || active.PathIds.Count == 0)
            {
                active = null;
            }
            else
            {
                active = active with
                {
                    PlannedPathIds = active.PlannedPathIds is { Count: > 0 }
                        ? active.PlannedPathIds
                        : active.PathIds,
                    StageIndex = Math.Clamp(active.StageIndex, 0, active.PathIds.Count - 1),
                    UsedAtStage = Math.Max(0, active.UsedAtStage),
                    TotalForms = Math.Max(1, active.TotalForms),
                };
            }
        }

        var hasActive = active is not null;
        var normalized = state with
        {
            UsedSinceInstall = Math.Max(0, state.UsedSinceInstall),
            SpentTokens = Math.Max(0, state.SpentTokens),
            EggUsage = Math.Max(0, state.EggUsage),
            LastDate = state.LastDate ?? string.Empty,
            Active = active,
            Dex = state.Dex ?? Array.Empty<DexEntry>(),
            CollectedFinals = state.CollectedFinals ?? new HashSet<string>(),
            Inventory = state.Inventory ?? new Dictionary<string, int>(),
            CandyGrantTier = state.CandyGrantTier ?? new Dictionary<string, int>(),
            EggTier = !hasActive && state.EggTier is PokemonRarity.Uncommon or PokemonRarity.Rare
                ? state.EggTier
                : null,
            PendingHatchId = hasActive ? null : state.PendingHatchId,
        };

        if (normalized.RepresentativeSpeciesId is int selected &&
            !normalized.OwnsSpecies(selected))
        {
            normalized = normalized with { RepresentativeSpeciesId = null };
        }

        return normalized;
    }
}
