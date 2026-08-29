using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Tests;

public sealed class CompanionStoreTests
{
    [Fact]
    public void Constructor_RestoresSavedCompanionWithoutStartingNetworkWork()
    {
        var api = new FakeApi();
        var persistence = new FakePersistence { Loaded = StateWithActive(1) };

        var store = new CompanionStore(api, persistence);

        Assert.Equal(1, store.CurrentSpeciesId);
        Assert.Equal(CompanionStateKind.Idle, store.DisplayState);
        Assert.Equal(0, api.LineCalls);
    }

    [Fact]
    public void Constructor_WithoutSavedStateStartsAsEgg()
    {
        var store = new CompanionStore(new FakeApi(), new FakePersistence());

        Assert.Null(store.State.Active);
        Assert.Equal(CompanionStateKind.Egg, store.DisplayState);
    }

    [Fact]
    public async Task HatchSuccess_SelectsPokemonAndPersistsImmediately()
    {
        var persistence = new FakePersistence();
        var store = new CompanionStore(
            new FakeApi { Line = Line(1, 2) },
            persistence,
            new FixedRandom(1, 1));

        Assert.True(await store.HatchAsync(1));

        Assert.Equal(1, store.CurrentSpeciesId);
        Assert.Equal([1], store.State.Active!.PathIds);
        Assert.Equal([1, 2], store.State.Active.PlannedPathIds);
        Assert.Equal(CompanionStateKind.LevelUp, store.DisplayState);
        Assert.NotNull(persistence.Saved);
        Assert.False(store.IsHatching);
    }

    [Fact]
    public async Task NetworkFailure_PreservesPreviousCompanionAndDoesNotSave()
    {
        var original = StateWithActive(4);
        var persistence = new FakePersistence { Loaded = original };
        var store = new CompanionStore(
            new FakeApi { Error = new HttpRequestException("offline") },
            persistence);

        Assert.False(await store.HatchAsync(1));

        Assert.Equal(original.Active, store.State.Active);
        Assert.Equal(4, store.CurrentSpeciesId);
        Assert.Null(persistence.Saved);
    }

    [Fact]
    public async Task PersistenceFailure_DoesNotUndoSuccessfulSelection()
    {
        var persistence = new FakePersistence { SaveError = new IOException("disk") };
        var store = new CompanionStore(new FakeApi { Line = Line(1) }, persistence);

        Assert.True(await store.HatchAsync(1));
        Assert.Equal(1, store.CurrentSpeciesId);
    }

    [Fact]
    public async Task Cancellation_IsPropagatedAndHatchingFlagIsCleared()
    {
        var api = new FakeApi { WaitForCancellation = true };
        var store = new CompanionStore(api, new FakePersistence());
        using var cancellation = new CancellationTokenSource();
        var task = store.HatchAsync(1, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(store.IsHatching);
    }

    [Fact]
    public async Task RepeatedSelection_ReplacesCompanionAndPersistsEachSuccess()
    {
        var persistence = new FakePersistence();
        var api = new FakeApi { Line = Line(1) };
        var store = new CompanionStore(api, persistence);
        await store.HatchAsync(1);
        api.Line = Line(4);

        Assert.True(await store.HatchAsync(4));

        Assert.Equal(4, store.CurrentSpeciesId);
        Assert.Equal(2, persistence.SaveCount);
    }

    [Fact]
    public async Task ConcurrentSelection_IsRejectedInsteadOfStartingAnotherRequest()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<EvoLine>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeApi { LineTask = release.Task, Started = started };
        var store = new CompanionStore(api, new FakePersistence());
        var first = store.HatchAsync(1);
        await started.Task;

        Assert.False(await store.HatchAsync(4));
        release.SetResult(Line(1));
        Assert.True(await first);
        Assert.Equal(1, api.LineCalls);
    }

    [Fact]
    public async Task RandomSelection_UsesBaseIndexAndExcludesDittoAndUnsupportedSpecies()
    {
        var api = new FakeApi
        {
            Index =
            [
                new BaseSpecies(PokemonOdds.DittoSpeciesId, 255),
                new BaseSpecies(650, 255),
                new BaseSpecies(1, 45),
            ],
            Line = Line(1),
        };
        var store = new CompanionStore(api, new FakePersistence(), new FixedRandom(0, 1));

        Assert.True(await store.HatchRandomAsync());

        Assert.Equal(1, store.CurrentSpeciesId);
        Assert.Equal(1, api.IndexCalls);
    }

    [Fact]
    public async Task RandomSelection_IndexFailureUsesSixteenAttemptRestFallbackContract()
    {
        var api = new FakeApi
        {
            IndexError = new HttpRequestException("graphql unavailable"),
            BaseSpecies = new BaseSpecies(25, 190),
            Line = Line(25),
        };
        var store = new CompanionStore(api, new FakePersistence(), new FixedRandom(24, 0, 0));

        Assert.True(await store.HatchRandomAsync());

        Assert.Equal(25, store.CurrentSpeciesId);
        Assert.Equal(1, api.BaseSpeciesCalls);
    }

    [Fact]
    public void RepresentativeSelection_MustBeOwnedAndSavesImmediately()
    {
        var persistence = new FakePersistence { Loaded = StateWithActive(1) };
        var store = new CompanionStore(new FakeApi(), persistence);

        Assert.False(store.SetRepresentativeSpeciesId(999));
        Assert.True(store.SetRepresentativeSpeciesId(1));

        Assert.Equal(1, store.RepresentativeSubject.SpeciesId);
        Assert.Equal(1, persistence.SaveCount);
    }

    [Fact]
    public void Reset_ClearsMemoryAndDeletesPersistence()
    {
        var persistence = new FakePersistence { Loaded = StateWithActive(1) };
        var store = new CompanionStore(new FakeApi(), persistence);

        store.Reset();

        Assert.Null(store.State.Active);
        Assert.Equal(CompanionStateKind.Egg, store.DisplayState);
        Assert.Equal(1, persistence.DeleteCount);
    }

    [Fact]
    public void LoadFailure_FallsBackToFreshEgg()
    {
        var persistence = new FakePersistence { LoadError = new IOException("disk") };

        var store = new CompanionStore(new FakeApi(), persistence);

        Assert.Null(store.State.Active);
        Assert.Equal(CompanionStateKind.Egg, store.DisplayState);
    }

    [Fact]
    public void CoreAssembly_DoesNotReferenceInfrastructureOrWpf()
    {
        var references = typeof(CompanionStore).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference =>
            reference.Name is "PokeTokenBar.Windows.Infrastructure" or "PresentationFramework");
    }

    private static CompanionState StateWithActive(int id) =>
        new()
        {
            Active = new MonState
            {
                BaseId = id,
                PathIds = [id],
                PlannedPathIds = [id],
                StageIndex = 0,
                Rarity = PokemonRarity.Common,
                TotalForms = 1,
            },
        };

    private static EvoLine Line(params int[] ids)
    {
        var node = new EvoNode(ids[^1], []);
        for (var index = ids.Length - 2; index >= 0; index--)
        {
            node = new EvoNode(ids[index], [node]);
        }

        return new EvoLine(
            ids[0],
            node,
            PokemonRarity.Common,
            new Dictionary<int, IReadOnlyDictionary<string, string>>());
    }

    private sealed class FakeApi : IPokeApiClient
    {
        public EvoLine? Line { get; set; }
        public Exception? Error { get; set; }
        public bool WaitForCancellation { get; set; }
        public Task<EvoLine>? LineTask { get; set; }
        public TaskCompletionSource? Started { get; set; }
        public IReadOnlyList<BaseSpecies> Index { get; set; } = [];
        public Exception? IndexError { get; set; }
        public BaseSpecies? BaseSpecies { get; set; }
        public int LineCalls { get; private set; }
        public int IndexCalls { get; private set; }
        public int BaseSpeciesCalls { get; private set; }

        public async Task<EvoLine> GetLineAsync(int baseSpeciesId, CancellationToken cancellationToken = default)
        {
            LineCalls++;
            Started?.SetResult();
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (LineTask is not null)
            {
                return await LineTask.WaitAsync(cancellationToken);
            }

            if (Error is not null)
            {
                throw Error;
            }

            return Line ?? CompanionStoreTests.Line(baseSpeciesId);
        }

        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(
            CancellationToken cancellationToken = default)
        {
            IndexCalls++;
            if (IndexError is not null)
            {
                throw IndexError;
            }

            return Task.FromResult(Index);
        }

        public Task<BaseSpecies?> GetBaseSpeciesAsync(int id, CancellationToken cancellationToken = default)
        {
            BaseSpeciesCalls++;
            return Task.FromResult(BaseSpecies);
        }
    }

    private sealed class FakePersistence : ICompanionPersistence
    {
        public CompanionState? Loaded { get; set; }
        public CompanionState? Saved { get; private set; }
        public Exception? LoadError { get; set; }
        public Exception? SaveError { get; set; }
        public int SaveCount { get; private set; }
        public int DeleteCount { get; private set; }

        public CompanionState? Load() =>
            LoadError is null ? Loaded : throw LoadError;

        public void Save(CompanionState state)
        {
            if (SaveError is not null)
            {
                throw SaveError;
            }

            SaveCount++;
            Saved = state;
        }

        public void Delete() => DeleteCount++;
    }

    private sealed class FixedRandom(params int[] values) : Random
    {
        private readonly Queue<int> _values = new(values);

        public override int Next(int maxValue)
        {
            var value = _values.Count == 0 ? 0 : _values.Dequeue();
            return Math.Clamp(value, 0, maxValue - 1);
        }

        public override int Next(int minValue, int maxValue) =>
            minValue + Next(maxValue - minValue);
    }
}
