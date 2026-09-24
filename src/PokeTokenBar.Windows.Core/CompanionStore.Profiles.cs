namespace PokeTokenBar.Windows.Core;

// Read-only presentation snapshot; active individuals are never inserted into Dex.
public sealed record PokemonIndividual(PokemonProfile Profile, PokemonRarity Rarity,
    PokemonNature? Nature, bool IsShiny, bool IsCurrent, bool IsReleased);

public sealed partial class CompanionStore
{
    public IReadOnlyList<PokemonIndividual> GetPokemonIndividuals(int speciesId)
    {
        var state = State;
        var individuals = new List<PokemonIndividual>();
        if (state.Active is { Profile: { } profile } active && active.CurrentId == speciesId)
            individuals.Add(new(profile, active.Rarity, active.Nature,
                active.IsShiny && (active.DittoDisguise is null || active.DittoRevealed), true, false));
        individuals.AddRange(state.Dex.Where(entry => entry.FinalId == speciesId && entry.Profile is not null)
            .OrderByDescending(entry => entry.CaughtAt ?? entry.ReleasedAt)
            .Select(entry => new PokemonIndividual(entry.Profile!, entry.Rarity, entry.Nature,
                entry.IsShiny, false, entry.IsReleased)));
        return individuals;
    }

    private readonly IPokemonDetailProvider? _detailProvider;
    private readonly object _detailsLock = new();
    private readonly Dictionary<int, PokemonDetails> _details = [];
    private readonly Dictionary<int, Task> _detailLoads = [];
    private readonly HashSet<int> _failedDetails = [];
    private readonly CancellationTokenSource _detailsLifetime = new();
    private bool _profilesDisposed;

    public IReadOnlyDictionary<int, PokemonDetails> PokemonDetailsById
    {
        get { lock (_detailsLock) return new Dictionary<int, PokemonDetails>(_details); }
    }
    public IReadOnlySet<int> LoadingPokemonDetailIds
    {
        get { lock (_detailsLock) return _detailLoads.Keys.ToHashSet(); }
    }
    public IReadOnlySet<int> FailedPokemonDetailIds
    {
        get { lock (_detailsLock) return _failedDetails.ToHashSet(); }
    }

    public Task PreparePokemonProfilesAsync() => State.Active is { } active && PokemonAssets.HasAnimatedSprite(active.CurrentId)
        ? LoadPokemonDetailsAsync(active.CurrentId) : Task.CompletedTask;

    public Task LoadPokemonDetailsAsync(int speciesId)
    {
        if (!PokemonAssets.HasAnimatedSprite(speciesId)) throw new ArgumentOutOfRangeException(nameof(speciesId));
        lock (_detailsLock)
        {
            if (_profilesDisposed || _detailProvider is null) return Task.CompletedTask;
            if (_detailLoads.TryGetValue(speciesId, out var pending)) return pending;
            _failedDetails.Remove(speciesId);
            var token = _detailsLifetime.Token;
            var task = Task.Run(() => LoadDetailsCoreAsync(speciesId, token));
            _detailLoads[speciesId] = task;
            return task;
        }
    }

    private async Task LoadDetailsCoreAsync(int speciesId, CancellationToken token)
    {
        try
        {
            PokemonDetails? details;
            lock (_detailsLock) _details.TryGetValue(speciesId, out details);
            // Metadata HTTP is deliberately outside the progression gate.
            details ??= await _detailProvider!.GetPokemonDetailsAsync(speciesId, token).ConfigureAwait(false);
            if (details.SpeciesId != speciesId) throw new InvalidDataException("Unexpected Pokémon species.");
            await _mutationGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                lock (_detailsLock)
                {
                    if (_profilesDisposed) return;
                    _details[speciesId] = details;
                    var before = State;
                    var active = before.Active;
                    if (active?.CurrentId == speciesId && active.Profile is { } profile)
                        active = active with { Profile = profile.Enrich(details) };
                    var dex = before.Dex.Select(entry => entry.FinalId == speciesId && entry.Profile is { } saved
                        ? entry with { Profile = saved.Enrich(details) } : entry).ToArray();
                    if (active != before.Active || !dex.SequenceEqual(before.Dex))
                    {
                        State = before with { Active = active, Dex = dex };
                        TrySave();
                    }
                }
            }
            finally { _mutationGate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            lock (_detailsLock) _failedDetails.Add(speciesId);
        }
        finally
        {
            lock (_detailsLock) _detailLoads.Remove(speciesId);
        }
    }

    private void QueueActiveDetails()
    {
        if (State.Active is not { } active || _detailProvider is null || !PokemonAssets.HasAnimatedSprite(active.CurrentId)) return;
        lock (_detailsLock)
        {
            if (_details.ContainsKey(active.CurrentId) || _profilesDisposed) return;
        }
        _ = LoadPokemonDetailsAsync(active.CurrentId);
    }

    private PokemonProfile? EnrichCached(int speciesId, PokemonProfile? profile)
    {
        lock (_detailsLock)
            return profile is not null && _details.TryGetValue(speciesId, out var details) ? profile.Enrich(details) : profile;
    }

    private void ReconcileProfileGrowth()
    {
        if (State.Active is not { Profile: { } profile } active) return;
        State = State with { Active = active with { Profile = EnrichCached(active.CurrentId,
            profile.AdvanceGrowth(PokemonProfileMigration.ReconstructedGrowth(active, GrowthDifficulty), active.Rarity)) } };
    }

    public void Dispose()
    {
        lock (_detailsLock)
        {
            if (_profilesDisposed) return;
            _profilesDisposed = true;
        }
        _detailsLifetime.Cancel();
        _detailsLifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
