namespace PokeTokenBar.Windows.Core;

public sealed class CompanionStore
{
    private readonly IPokeApiClient _provider;
    private readonly ICompanionPersistence _persistence;
    private readonly Random _random;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public CompanionStore(
        IPokeApiClient provider,
        ICompanionPersistence persistence,
        Random? random = null,
        TimeProvider? timeProvider = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _random = random ?? Random.Shared;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        IsHatching = true;
        try
        {
            return await HatchCoreAsync(baseSpeciesId, cancellationToken).ConfigureAwait(false);
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

        IsHatching = true;
        try
        {
            return await HatchRandomCoreAsync(cancellationToken).ConfigureAwait(false);
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
            return await LoadCurrentLineCoreAsync(cancellationToken).ConfigureAwait(false);
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
                    ApplyUsageCore(delta);
                }
            }

            TrySave();
            await MaintainProgressionAsync(cancellationToken).ConfigureAwait(false);
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

        ApplyUsageCore(0);
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

    private void ApplyUsageCore(long delta)
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

        TrySave();
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
        var plan = BuildEvolutionPlan(line.Tree, line.BaseId);
        var natures = Enum.GetValues<PokemonNature>();
        var active = new MonState
        {
            BaseId = line.BaseId,
            PathIds = [line.BaseId],
            PlannedPathIds = plan,
            StageIndex = 0,
            UsedAtStage = 0,
            Rarity = line.Rarity,
            TotalForms = plan.Count,
            IsShiny = _random.Next(PokemonOdds.ShinyDenominator) == 0,
            Nature = natures[_random.Next(natures.Length)],
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
            ApplyUsageCore(overflow);
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
        };

        if (normalized.RepresentativeSpeciesId is int selected &&
            !normalized.OwnsSpecies(selected))
        {
            normalized = normalized with { RepresentativeSpeciesId = null };
        }

        return normalized;
    }
}
