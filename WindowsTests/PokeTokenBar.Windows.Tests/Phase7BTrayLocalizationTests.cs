using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokeTokenBar.Windows.App;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.App.Tray;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class Phase7BTrayLocalizationTests
{
    public static TheoryData<AppLanguage> Languages => new()
    {
        AppLanguage.Ko, AppLanguage.En, AppLanguage.Ja, AppLanguage.Es,
        AppLanguage.Fr, AppLanguage.Pt, AppLanguage.De,
    };

    [Fact]
    public void TrayEgg_UsesTwoFrameUpstreamBob()
    {
        var egg = SystemTrayController.EggPresentation;
        Assert.True(egg.IsAnimated);
        Assert.Equal(2, egg.Frames.Count);
        Assert.All(egg.Frames, frame => Assert.Equal(500, frame.Duration.TotalMilliseconds));
    }

    [Fact]
    public async Task TrayAnimatedRepresentative_StartsAtFirstFrame()
    {
        using var companion = await CompanionAsync(animated: true);
        var tray = new TrayIcon();
        var timers = new TimerFactory();
        using var controller = Controller(tray, companion, timers: timers);

        Assert.Equal(1, Marker(tray.Frames.Last()));
        Assert.Single(timers.Timers);
    }

    [Fact]
    public async Task TrayStaticRepresentative_DoesNotStartTimer()
    {
        using var companion = await CompanionAsync(animated: false);
        var tray = new TrayIcon();
        var timers = new TimerFactory();
        using var controller = Controller(tray, companion, timers: timers);

        Assert.Equal(1, Marker(tray.Frames.Last()));
        Assert.Empty(timers.Timers);
    }

    [Fact]
    public async Task TrayAnimation_AdvancesDecodedFullFrames()
    {
        using var companion = await CompanionAsync(animated: true);
        var tray = new TrayIcon();
        var timers = new TimerFactory();
        using var controller = Controller(tray, companion, timers: timers);

        timers.Timers.Single().Fire();

        Assert.Equal(11, Marker(tray.Frames.Last()));
    }

    [Fact]
    public async Task TrayRepresentativeChange_ReplacesFramesImmediately()
    {
        using var companion = await CompanionAsync(animated: true);
        var tray = new TrayIcon();
        using var controller = Controller(tray, companion);

        Assert.True(await companion.SelectRepresentativeAsync(2));

        Assert.Equal(2, Marker(tray.Frames.Last()));
    }

    [Fact]
    public async Task TrayEggTransition_ReplacesRepresentativeImmediately()
    {
        using var companion = await CompanionAsync(animated: true);
        var tray = new TrayIcon();
        using var controller = Controller(tray, companion);

        companion.Reset();

        Assert.Same(SystemTrayController.EggPresentation.Frames[0].Image, tray.Frames.Last());
    }

    [Fact]
    public async Task TrayPopupLifecycle_PausesAndResumesAnimation()
    {
        using var companion = await CompanionAsync(animated: true);
        var tray = new TrayIcon();
        var window = new TrayWindow();
        var timers = new TimerFactory();
        using var controller = Controller(tray, companion, window: window, timers: timers);
        var first = timers.Timers.Single();

        tray.RequestToggle();
        Assert.True(first.IsDisposed);
        window.RequestDeactivation();

        Assert.Equal(2, timers.Timers.Count);
    }

    [Fact]
    public async Task TrayExit_DisposesAnimationTimerAndIconOnce()
    {
        using var companion = await CompanionAsync(animated: true);
        var tray = new TrayIcon();
        var timers = new TimerFactory();
        var controller = Controller(tray, companion, timers: timers);

        controller.Dispose();
        controller.Dispose();

        Assert.True(timers.Timers.Single().IsDisposed);
        Assert.Equal(1, tray.DisposeCalls);
    }

    [Theory]
    [InlineData(AnimationQuality.PowerSaver, 400)]
    [InlineData(AnimationQuality.Balanced, 200)]
    [InlineData(AnimationQuality.Smooth, 100)]
    public async Task TrayAnimation_UsesSharedQualityFloor(AnimationQuality quality, int milliseconds)
    {
        using var companion = await CompanionAsync(animated: true);
        var settings = Settings(AppLanguage.En);
        settings.SelectedAnimationQuality = quality;
        var timers = new TimerFactory();
        using var controller = Controller(new TrayIcon(), companion, settings, timers: timers);

        Assert.Equal(milliseconds, timers.Timers.Single().Interval.TotalMilliseconds);
    }

    [Fact]
    public async Task TrayLanguageSwitch_UpdatesMenuAndTooltipImmediately()
    {
        using var companion = await CompanionAsync(animated: false);
        var settings = Settings(AppLanguage.En);
        var tray = new TrayIcon();
        using var controller = Controller(tray, companion, settings);
        Assert.Equal("Open", tray.OpenText);

        settings.SelectedLanguage = AppLanguage.Ko;

        Assert.Equal("열기", tray.OpenText);
        Assert.Contains("사용량 데이터 없음", tray.Text);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Localization_AllPhase7BKeysExist(AppLanguage language)
    {
        var text = new LocalizationService(language);
        Assert.All(new[]
        {
            text.LastUpdated, text.CacheWrite, text.CacheRead, text.UsagePeriods,
            text.ShopIntro, text.BagIntro, text.Active, text.Current, text.Representative,
            text.Caught, text.Shiny, text.Normal, text.UnknownNature, text.Mint,
            text.RareCandy, text.ShinyCharm, text.FreshEgg, text.NotChecked,
            text.ExportDialogTitle, text.ImportDialogTitle, text.UpdateCheckFailed,
        }, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Localization_EconomyFormattingExistsInEveryLanguage(AppLanguage language)
    {
        var text = new LocalizationService(language);
        Assert.All(new[]
        {
            text.Tokens(1234), text.RarityEgg(PokemonRarity.Rare), text.Purchased(text.Mint),
            text.NatureChanged(text.UnknownNature), text.InvalidPaths("X:\\bad"),
            text.ResetsIn("2h"), text.MinutesAgo(2),
        }, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Localization_CompanionDetailsExistInEveryLanguage(AppLanguage language)
    {
        Assert.All(Enum.GetValues<PokemonRarity>(), rarity =>
            Assert.False(string.IsNullOrWhiteSpace(CompanionDisplayTexts.Rarity(rarity, language))));
        Assert.All(Enum.GetValues<PokemonNature>(), nature =>
            Assert.False(string.IsNullOrWhiteSpace(PokemonNatureDisplayNames.GetName(nature, language))));
        Assert.False(string.IsNullOrWhiteSpace(
            CompanionDisplayTexts.Status(CompanionStateKind.Working, language)));
    }

    [Fact]
    public void Localization_GermanUsesUpstreamCompanionNames()
    {
        Assert.Equal("Legendär", CompanionDisplayTexts.Rarity(PokemonRarity.Legendary, AppLanguage.De));
        Assert.Equal("Mutig", PokemonNatureDisplayNames.GetName(PokemonNature.Brave, AppLanguage.De));
        Assert.Equal("Schläft gerade.", CompanionDisplayTexts.Status(CompanionStateKind.Sleep, AppLanguage.De));
    }

    [Fact]
    public void SettingsRuntimeLanguageSwitch_RebuildsLocalizedOptions()
    {
        var settings = Settings(AppLanguage.En);
        Assert.Equal("Manual", settings.RefreshIntervalOptions[0].Label);

        settings.SelectedLanguage = AppLanguage.Ja;

        Assert.Equal("手動", settings.RefreshIntervalOptions[0].Label);
        Assert.Equal("5分", settings.RefreshIntervalOptions[3].Label);
    }

    [Fact]
    public void ProductionViews_RouteKnownPhase7BStringsThroughLocalization()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PokeTokenBar.Windows.App", "MainWindow.xaml"));
        Assert.DoesNotContain("Spend the tokens earned", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Items persist across restarts", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetNullValue=Unknown nature", xaml, StringComparison.Ordinal);
        Assert.Contains("Texts.ShopIntro", xaml, StringComparison.Ordinal);
        Assert.Contains("Texts.BagIntro", xaml, StringComparison.Ordinal);
        Assert.Contains("Texts.LastUpdated", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayNativeIconContract_ReleasesEveryOwnedHandle()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "PokeTokenBar.Windows.App", "Tray", "NotifyIconTrayIcon.cs"));
        Assert.Contains("DestroyIcon(handle)", source, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _companionIcon, icon)?.Dispose()", source, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _companionIcon, null)?.Dispose()", source, StringComparison.Ordinal);
        Assert.Contains("Math.Min(value.Length, 63)", source, StringComparison.Ordinal);
    }

    private static SystemTrayController Controller(
        TrayIcon tray,
        CompanionViewModel companion,
        SettingsViewModel? settings = null,
        TrayWindow? window = null,
        TimerFactory? timers = null) => new(
            tray, window ?? new TrayWindow(),
            new UsageViewModel(new UsageStore([new UsageProvider()]), localization: settings?.Localization),
            () => { }, settings, companion, timers ?? new TimerFactory());

    private static async Task<CompanionViewModel> CompanionAsync(bool animated)
    {
        var state = new CompanionState
        {
            Active = new MonState
            {
                BaseId = 1, PathIds = [1], PlannedPathIds = [1],
                TotalForms = 1, Rarity = PokemonRarity.Common,
            },
            Dex = [new DexEntry { BaseId = 2, FinalId = 2, ChainOrder = [2] }],
            Language = AppLanguage.En,
        };
        var companion = new CompanionViewModel(
            new CompanionStore(new PokeApi(), new CompanionPersistence(state)),
            (id, _, _) => Task.FromResult<PokemonSpriteAsset?>(new(
                new byte[] { (byte)id }, new Uri("https://example.test/sprite"), "image/gif", true, false)),
            new Decoder(animated));
        await companion.InitializeAsync();
        return companion;
    }

    private static SettingsViewModel Settings(AppLanguage language) => new(
        new SettingsPersistence(AppSettings.Default with { Language = language }), new AutoStart());

    private static BitmapSource Image(byte marker)
    {
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { marker, marker, marker, 255 }, 4);
        image.Freeze();
        return image;
    }

    private static byte Marker(BitmapSource? image)
    {
        Assert.NotNull(image);
        var bytes = new byte[4];
        image.CopyPixels(bytes, 4, 0);
        return bytes[0];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PokeTokenBar.Windows.sln")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }

    private sealed class Decoder(bool animated) : IPokemonSpriteDecoder
    {
        public PokemonSpritePresentation Decode(PokemonSpriteAsset asset)
        {
            var marker = asset.Data.Span[0];
            if (!animated) return new(Image(marker), [], false);
            var frames = new[]
            {
                new AnimatedSpriteFrame(Image(marker), TimeSpan.FromMilliseconds(50)),
                new AnimatedSpriteFrame(Image((byte)(marker * 10 + marker)), TimeSpan.FromMilliseconds(50)),
            };
            return new(frames[0].Image, frames, true);
        }
    }

    private sealed class TimerFactory : ISpriteAnimationTimerFactory
    {
        public List<Timer> Timers { get; } = [];
        public ISpriteAnimationTimer Create(TimeSpan interval, EventHandler tick)
        {
            var timer = new Timer(interval, tick);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class Timer(TimeSpan interval, EventHandler tick) : ISpriteAnimationTimer
    {
        public TimeSpan Interval { get; set; } = interval;
        public bool IsDisposed { get; private set; }
        public void Start() { }
        public void Stop() { }
        public void Fire() { if (!IsDisposed) tick(this, EventArgs.Empty); }
        public void Dispose() => IsDisposed = true;
    }

    private sealed class TrayIcon : ITrayIcon
    {
        public event EventHandler? ToggleRequested;
        public event EventHandler? OpenRequested;
        public event EventHandler? RefreshRequested;
        public event EventHandler? ExitRequested;
        public bool Visible { get; set; }
        public string Text { get; set; } = "";
        public string OpenText { get; private set; } = "";
        public List<BitmapSource?> Frames { get; } = [];
        public int DisposeCalls { get; private set; }
        public void RequestToggle() => ToggleRequested?.Invoke(this, EventArgs.Empty);
        public void RequestOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);
        public void RequestRefresh() => RefreshRequested?.Invoke(this, EventArgs.Empty);
        public void RequestExit() => ExitRequested?.Invoke(this, EventArgs.Empty);
        public void SetMenuText(string open, string refresh, string exit) => OpenText = open;
        public void SetCompanionFrame(BitmapSource? frame) => Frames.Add(frame);
        public void ShowNotification(NotificationMessage message) { }
        public void Dispose() => DisposeCalls++;
    }

    private sealed class TrayWindow : ITrayWindow
    {
        public event CancelEventHandler? Closing;
        public event EventHandler? Deactivated;
        public bool IsVisible { get; private set; }
        public bool IsMinimized => false;
        public void ShowNearTray() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Activate() { }
        public void Restore() { }
        public void Close() { }
        public void RequestDeactivation()
        {
            IsVisible = false;
            Deactivated?.Invoke(this, EventArgs.Empty);
        }
        public void RequestClose() => Closing?.Invoke(this, new CancelEventArgs());
    }

    private sealed class UsageProvider : IUsageProvider
    {
        public string Id => "codex";
        public string DisplayName => "Codex";
        public bool ReportsCost => true;
        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<DailyUsage?>(null);
        public Task<ProviderEnrichment> FetchEnrichmentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderEnrichment());
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
                new Dictionary<int, IReadOnlyDictionary<string, string>>
                {
                    [1] = new Dictionary<string, string> { ["en"] = "Bulbasaur" },
                }));
        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaseSpecies>>([new BaseSpecies(1, 255)]);
        public Task<BaseSpecies?> GetBaseSpeciesAsync(int speciesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<BaseSpecies?>(new BaseSpecies(speciesId, 255));
    }

    private sealed class SettingsPersistence(AppSettings state) : IAppSettingsPersistence
    {
        private AppSettings _state = state;
        public AppSettings? Load() => _state;
        public void Save(AppSettings settings) => _state = settings;
    }

    private sealed class AutoStart : IAutoStartService
    {
        public bool IsAvailable => true;
        public bool IsEnabled { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }
}
