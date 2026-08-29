namespace PokeTokenBar.Windows.Core;

public sealed class CompanionStore
{
    private readonly IPokeApiClient _provider;
    private readonly ICompanionPersistence _persistence;
    private readonly Random _random;
    private readonly SemaphoreSlim _selectionGate = new(1, 1);

    public CompanionStore(
        IPokeApiClient provider,
        ICompanionPersistence persistence,
        Random? random = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _random = random ?? Random.Shared;
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
        if (!await _selectionGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
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
            _selectionGate.Release();
        }
    }

    public async Task<bool> HatchRandomAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _selectionGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        IsHatching = true;
        try
        {
            IReadOnlyList<BaseSpecies> all;
            try
            {
                all = await _provider
                    .GetBaseSpeciesIndexAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                var fallbackId = await ChooseBaseViaRestAsync(cancellationToken).ConfigureAwait(false);
                return fallbackId is int selectedId &&
                       await HatchCoreAsync(selectedId, cancellationToken).ConfigureAwait(false);
            }

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

            return await HatchCoreAsync(selected.Id, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            IsHatching = false;
            _selectionGate.Release();
        }
    }

    public async Task<bool> LoadCurrentLineAsync(
        CancellationToken cancellationToken = default)
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

        var plan = BuildEvolutionPlan(line.Tree);
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
                (State.EggTier is null || State.EggTier.Value.Includes(candidate.CaptureRate)))
            {
                return id;
            }
        }

        return null;
    }

    private IReadOnlyList<int> BuildEvolutionPlan(EvoNode root)
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

            current = current.Children[_random.Next(current.Children.Count)];
        }
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
