using PokeTokenBar.Windows.App;
using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class Phase5ExperienceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"PokeTokenBar-Phase5-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(49, 0, false)]
    [InlineData(79.9, 0, false)]
    [InlineData(80, 1, false)]
    [InlineData(81, 1, false)]
    [InlineData(94.9, 1, false)]
    [InlineData(95, 1, true)]
    [InlineData(100, 1, true)]
    [InlineData(120, 1, true)]
    [InlineData(double.NaN, 0, false)]
    public void LimitAlert_UsesUpstreamThresholdTiers(
        double used, int expectedCount, bool critical)
    {
        var tiers = new Dictionary<string, int>();
        var alerts = LimitNotificationEvaluator.Evaluate(
            [new LimitNotificationWindow("w", "Window", used)], 80, 95, tiers);
        Assert.Equal(expectedCount, alerts.Count);
        if (expectedCount > 0) Assert.Equal(critical, alerts[0].IsCritical);
    }

    [Fact]
    public void LimitAlert_DoesNotRepeatSameTier()
    {
        var tiers = new Dictionary<string, int>();
        Assert.Single(Evaluate(80, tiers));
        Assert.Empty(Evaluate(90, tiers));
    }

    [Fact]
    public void LimitAlert_CanEscalateWarningToCriticalOnce()
    {
        var tiers = new Dictionary<string, int>();
        Assert.False(Assert.Single(Evaluate(80, tiers)).IsCritical);
        Assert.True(Assert.Single(Evaluate(95, tiers)).IsCritical);
        Assert.Empty(Evaluate(100, tiers));
    }

    [Fact]
    public void LimitAlert_RearmsBelowWarning()
    {
        var tiers = new Dictionary<string, int>();
        Assert.Single(Evaluate(80, tiers));
        Assert.Empty(Evaluate(20, tiers));
        Assert.DoesNotContain("w", tiers);
        Assert.Single(Evaluate(80, tiers));
    }

    [Theory]
    [InlineData(AppLanguage.Ko)]
    [InlineData(AppLanguage.En)]
    [InlineData(AppLanguage.Ja)]
    [InlineData(AppLanguage.Es)]
    [InlineData(AppLanguage.Fr)]
    [InlineData(AppLanguage.Pt)]
    [InlineData(AppLanguage.De)]
    public void Localization_AllUpstreamLanguagesHaveMajorKeys(AppLanguage language)
    {
        var text = new LocalizationService(language);
        Assert.All(new[]
        {
            text.Home, text.Shop, text.Bag, text.Collection, text.Settings,
            text.Notifications, text.ProviderRoots, text.ShowFloating,
        }, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    [Fact]
    public void Localization_RuntimeSwitchRaisesRefreshAndChangesText()
    {
        var text = new LocalizationService(AppLanguage.En);
        var changed = 0;
        text.PropertyChanged += (_, _) => changed++;
        text.Language = AppLanguage.Ko;
        Assert.Equal("홈", text.Home);
        Assert.Equal(1, changed);
    }

    [Theory]
    [InlineData(0, LimitDisplayMode.Used, 0)]
    [InlineData(0, LimitDisplayMode.Remaining, 100)]
    [InlineData(14, LimitDisplayMode.Used, 14)]
    [InlineData(14, LimitDisplayMode.Remaining, 86)]
    [InlineData(100, LimitDisplayMode.Used, 100)]
    [InlineData(100, LimitDisplayMode.Remaining, 0)]
    [InlineData(-1, LimitDisplayMode.Used, 0)]
    [InlineData(150, LimitDisplayMode.Remaining, 0)]
    public void LimitDisplay_ClampsAndSupportsUsedOrRemaining(
        double used, LimitDisplayMode mode, int expected) =>
        Assert.Equal(expected, UsageViewModel.DisplayPercent(used, mode));

    [Theory]
    [InlineData(0, 48)]
    [InlineData(47, 48)]
    [InlineData(52, 48)]
    [InlineData(101, 104)]
    [InlineData(191, 192)]
    [InlineData(500, 192)]
    public void FloatingSize_UsesUpstreamRangeAndEightPixelSteps(double value, double expected)
    {
        var viewModel = Settings();
        viewModel.FloatingPetSize = value;
        Assert.Equal(expected, viewModel.FloatingPetSize);
    }

    [Theory]
    [InlineData(AnimationQuality.PowerSaver, 400)]
    [InlineData(AnimationQuality.Balanced, 200)]
    [InlineData(AnimationQuality.Smooth, 100)]
    public void AnimationQuality_PreservesUpstreamFrameFloor(
        AnimationQuality quality, int milliseconds)
    {
        var settings = Settings();
        settings.SelectedAnimationQuality = quality;
        using var floating = new FloatingPetViewModel(Companion(), settings);
        Assert.Equal(milliseconds, floating.MinimumFrameDuration.TotalMilliseconds);
    }

    [Fact]
    public void Settings_DefaultsPreserveWindowsAndUpstreamContracts()
    {
        var defaults = AppSettings.Default;
        Assert.True(defaults.LimitNotificationsEnabled);
        Assert.True(defaults.CompanionNotificationsEnabled);
        Assert.True(defaults.FloatingBubbleAlertsEnabled);
        Assert.Equal(80, defaults.WarningThreshold);
        Assert.Equal(95, defaults.CriticalThreshold);
        Assert.Equal(96, defaults.FloatingPetSize);
        Assert.Equal(LimitDisplayMode.Remaining, defaults.LimitDisplayMode);
    }

    [Fact]
    public void Settings_AllPhase5ValuesRoundTripProductionJson()
    {
        var path = Path.Combine(_directory, "settings.json");
        var persistence = new JsonAppSettingsPersistence(path);
        var settings = AppSettings.Default with
        {
            Language = AppLanguage.De,
            LimitNotificationsEnabled = false,
            CompanionNotificationsEnabled = false,
            WarningThreshold = 70,
            CriticalThreshold = 90,
            LimitDisplayMode = LimitDisplayMode.Used,
            FloatingPetSize = 160,
            AnimationQuality = AnimationQuality.Smooth,
            FloatingBubbleAlertsEnabled = false,
            CustomProviderRoots = new Dictionary<string, string> { ["codex"] = "C:\\logs" },
            NotificationTiers = new Dictionary<string, int> { ["codex.primary"] = 2 },
            SelectedProviderId = "codex",
        };
        persistence.Save(settings);
        Assert.Equivalent(settings, persistence.Load(), strict: true);
    }

    [Fact]
    public void Settings_OldJsonReceivesBackwardCompatibleDefaults()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{\"floatingPetEnabled\":true}");
        var loaded = Assert.IsType<AppSettings>(new JsonAppSettingsPersistence(path).Load());
        Assert.True(loaded.LimitNotificationsEnabled);
        Assert.Equal(96, loaded.FloatingPetSize);
        Assert.Equal(LimitDisplayMode.Remaining, loaded.LimitDisplayMode);
    }

    [Fact]
    public void Settings_LanguageSwitchPersistsAndUpdatesResourceImmediately()
    {
        var persistence = new MemorySettings();
        var settings = new SettingsViewModel(persistence, new AutoStart());
        settings.SelectedLanguage = AppLanguage.Ja;
        Assert.Equal(AppLanguage.Ja, persistence.State?.Language);
        Assert.Equal("設定", settings.Localization.Settings);
    }

    [Theory]
    [InlineData(50, 55)]
    [InlineData(80, 85)]
    [InlineData(95, 100)]
    [InlineData(100, 100)]
    public void Settings_ThresholdsRemainOrdered(double warning, double expectedCritical)
    {
        var settings = Settings();
        settings.CriticalThreshold = 55;
        settings.WarningThreshold = warning;
        Assert.True(settings.WarningThreshold < settings.CriticalThreshold);
        Assert.Equal(expectedCritical, settings.CriticalThreshold);
    }

    [Fact]
    public void CustomRoots_OnlyReturnsExistingDirectories()
    {
        var valid = Directory.CreateDirectory(Path.Combine(_directory, "logs")).FullName;
        var roots = SettingsViewModel.ParseRoots($"{valid}{Environment.NewLine}Z:\\missing-phase5");
        Assert.Equal(Path.GetFullPath(valid), Assert.Single(roots));
    }

    [Fact]
    public void CustomRoots_AreProviderSpecificAndPersisted()
    {
        var valid = Directory.CreateDirectory(Path.Combine(_directory, "codex")).FullName;
        var persistence = new MemorySettings();
        var settings = new SettingsViewModel(persistence, new AutoStart())
        {
            SelectedRootProviderId = "codex",
            CustomRootText = valid,
        };
        Assert.Equal(valid, Assert.Single(settings.CustomRoots("codex")));
        Assert.Empty(settings.CustomRoots("claude_code"));
        Assert.Equal(valid, persistence.State?.CustomProviderRoots?["codex"]);
    }

    [Fact]
    public void NotificationTierLedgerPersistsAcrossSettingsRestart()
    {
        var persistence = new MemorySettings();
        SettingsViewModel first = new(persistence, new AutoStart());
        first.SaveNotificationTiers(new Dictionary<string, int> { ["w"] = 2 });
        SettingsViewModel second = new(persistence, new AutoStart());
        Assert.Equal(2, second.NotificationTiers["w"]);
    }

    [Fact]
    public void MostRecentProviderPersistsAcrossSettingsRestart()
    {
        var persistence = new MemorySettings();
        SettingsViewModel first = new(persistence, new AutoStart());
        first.SaveSelectedProvider("codex");
        SettingsViewModel second = new(persistence, new AutoStart());
        Assert.Equal("codex", second.SelectedProviderId);
    }

    [Fact]
    public async Task CompanionHatchRaisesPostMutationEventOnce()
    {
        var store = Store(new CompanionState { EggUsage = PokemonBalance.EggHatchThreshold });
        var events = new List<CompanionGameEvent>();
        store.GameEventOccurred += (_, value) => events.Add(value);
        Assert.True(await store.HatchAsync(1));
        Assert.Equal(CompanionGameEventKind.Hatch, Assert.Single(events).Kind);
    }

    [Fact]
    public async Task CompanionNotificationSubscriberFailureCannotFailMutation()
    {
        var store = Store(new CompanionState { EggUsage = PokemonBalance.EggHatchThreshold });
        store.GameEventOccurred += (_, _) => throw new InvalidOperationException("notification failure");
        Assert.True(await store.HatchAsync(1));
        Assert.NotNull(store.State.Active);
    }

    [Fact]
    public async Task NotificationIntegration_RefreshPostsOneNativeEquivalentAlert()
    {
        var (usage, settings, floating, companion) = NotificationFixture(enabled: true);
        var sink = new NotificationSink();
        using (floating)
        using (companion)
        using (var controller = new NotificationController(usage, settings, floating, sink))
        {
            await usage.RefreshAsync();
            await controller.LastEvaluation;
            Assert.Equal(NotificationKind.LimitWarning, Assert.Single(sink.Messages).Kind);
        }
    }

    [Fact]
    public async Task NotificationIntegration_DisabledAdvancesLedgerWithoutPosting()
    {
        var (usage, settings, floating, companion) = NotificationFixture(enabled: false);
        var sink = new NotificationSink();
        using (floating)
        using (companion)
        using (var controller = new NotificationController(usage, settings, floating, sink))
        {
            await usage.RefreshAsync();
            await controller.LastEvaluation;
            Assert.Empty(sink.Messages);
            Assert.Equal(1, settings.NotificationTiers["claude.fiveHour"]);
        }
    }

    [Fact]
    public async Task NotificationIntegration_SinkFailureDoesNotFailRefresh()
    {
        var (usage, settings, floating, companion) = NotificationFixture(enabled: true);
        using (floating)
        using (companion)
        using (var controller = new NotificationController(
                   usage, settings, floating, new NotificationSink(throws: true)))
        {
            await usage.RefreshAsync();
            await controller.LastEvaluation;
            Assert.Null(usage.ErrorMessage);
        }
    }

    private static IReadOnlyList<LimitNotificationAlert> Evaluate(
        double used, IDictionary<string, int> tiers) =>
        LimitNotificationEvaluator.Evaluate(
            [new LimitNotificationWindow("w", "Window", used)], 80, 95, tiers);

    private static SettingsViewModel Settings() =>
        new(new MemorySettings(), new AutoStart());

    private static CompanionViewModel Companion() => new(
        Store(new CompanionState()),
        (_, _, _) => Task.FromResult<PokemonSpriteAsset?>(null),
        new NullDecoder());

    private static CompanionStore Store(CompanionState state) =>
        new(new PokeApi(), new CompanionPersistence(state), new Random(1));

    private static (UsageViewModel Usage, SettingsViewModel Settings,
        FloatingPetViewModel Floating, CompanionViewModel Companion) NotificationFixture(bool enabled)
    {
        var usage = new UsageViewModel(new UsageStore(
            [new UsageProvider()], claudeRateLimitsProvider: new ClaudeLimits()));
        var settings = new SettingsViewModel(
            new MemorySettings(AppSettings.Default with { LimitNotificationsEnabled = enabled }),
            new AutoStart());
        var companion = Companion();
        return (usage, settings, new FloatingPetViewModel(companion, settings, usage), companion);
    }

    private sealed class MemorySettings(AppSettings? initial = null) : IAppSettingsPersistence
    {
        public AppSettings? State { get; private set; } = initial ?? AppSettings.Default;
        public AppSettings? Load() => State;
        public void Save(AppSettings settings) => State = settings;
    }

    private sealed class AutoStart : IAutoStartService
    {
        public bool IsAvailable => true;
        public bool IsEnabled { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }

    private sealed class CompanionPersistence(CompanionState state) : ICompanionPersistence
    {
        private CompanionState _state = state;
        public CompanionState? Load() => _state;
        public void Save(CompanionState state) => _state = state;
        public void Delete() => _state = new CompanionState();
    }

    private sealed class PokeApi : IPokeApiClient
    {
        public Task<EvoLine> GetLineAsync(int baseSpeciesId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EvoLine(1, new EvoNode(1, []), PokemonRarity.Common,
                new Dictionary<int, IReadOnlyDictionary<string, string>>()));
        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaseSpecies>>([new BaseSpecies(1, 255)]);
        public Task<BaseSpecies?> GetBaseSpeciesAsync(int speciesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<BaseSpecies?>(new BaseSpecies(1, 255));
    }

    private sealed class NullDecoder : PokeTokenBar.Windows.App.Sprites.IPokemonSpriteDecoder
    {
        public PokeTokenBar.Windows.App.Sprites.PokemonSpritePresentation? Decode(PokemonSpriteAsset asset) => null;
    }

    private sealed class UsageProvider : IUsageProvider
    {
        public string Id => "claude_code";
        public string DisplayName => "Claude Code";
        public bool ReportsCost => true;
        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<DailyUsage?>(new DailyUsage("2026-09-01", 1, 0, 0, 0, 0, 0));
        public Task<ProviderEnrichment> FetchEnrichmentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderEnrichment());
    }

    private sealed class ClaudeLimits : IClaudeRateLimitsProvider
    {
        public Task<ClaudeRateLimitStatus?> FetchAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ClaudeRateLimitStatus?>(new(
                new ClaudeRateLimitWindow(80, null), null, null, null, null, null));
    }

    private sealed class NotificationSink(bool throws = false) : INotificationService
    {
        public List<NotificationMessage> Messages { get; } = [];
        public Task ShowAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            if (throws) throw new IOException("notification unavailable");
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
