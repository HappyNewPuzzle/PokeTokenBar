using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using PokeTokenBar.Windows.App;
using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed partial class AppCompositionTests
{
    [Fact]
    public void ProductionComposition_RegistersPhase3DProvidersAndOfficialLimits()
    {
        var viewModel = AppComposition.CreateUsageViewModel();
        var store = GetPrivateField<UsageStore>(viewModel, "_store");
        var providers = GetPrivateField<IReadOnlyList<IUsageProvider>>(store, "_providers");
        var rateLimitsProvider = GetPrivateField<ICodexRateLimitsProvider>(
            store,
            "_codexRateLimitsProvider");
        var claudeRateLimitsProvider = GetPrivateField<IClaudeRateLimitsProvider>(
            store,
            "_claudeRateLimitsProvider");
        var antigravityRateLimitsProvider = GetPrivateField<IAntigravityRateLimitsProvider>(
            store,
            "_antigravityRateLimitsProvider");

        Assert.Collection(
            providers,
            provider => Assert.IsType<LocalCodexUsageProvider>(provider),
            provider => Assert.IsType<LocalClaudeUsageProvider>(provider),
            provider => Assert.IsType<LocalGeminiUsageProvider>(provider),
            provider => Assert.IsType<LocalAntigravityUsageProvider>(provider),
            provider => Assert.IsType<LocalCursorUsageProvider>(provider),
            provider => Assert.IsType<LocalOpenCodeUsageProvider>(provider),
            provider => Assert.IsType<LocalHermesUsageProvider>(provider),
            provider => Assert.IsType<LocalGrokUsageProvider>(provider),
            provider => Assert.IsType<LocalCopilotUsageProvider>(provider),
            provider => Assert.IsType<LocalKiroUsageProvider>(provider),
            provider => Assert.IsType<LocalPiUsageProvider>(provider),
            provider => Assert.IsType<LocalOmpUsageProvider>(provider));
        Assert.Equal(
            ["codex", "claude_code", "gemini", "antigravity", "cursor", "opencode", "hermes", "grok", "copilot", "kiro", "pi", "omp"],
            providers.Select(provider => provider.Id));
        Assert.IsType<CodexRateLimitsProvider>(rateLimitsProvider);
        Assert.IsType<ClaudeRateLimitsProvider>(claudeRateLimitsProvider);
        Assert.IsType<AntigravityRateLimitsProvider>(antigravityRateLimitsProvider);
        Assert.Empty(viewModel.Providers);
    }

    [Fact]
    public void CompanionComposition_UsesInjectedPersistenceAndSharedHttpClientWithoutIo()
    {
        var handler = new TrackingHttpHandler();
        var httpClient = new HttpClient(handler);
        var persistence = new FakeCompanionPersistence();

        using var composition = AppComposition.CreateApplication(
            httpClient,
            persistence,
            new FakeSettingsPersistence(),
            new FakeAutoStartService());

        Assert.NotNull(composition.ViewModel.Usage);
        Assert.NotNull(composition.ViewModel.Companion);
        Assert.NotNull(composition.ViewModel.Settings);
        Assert.NotNull(composition.FloatingPet);
        var companionStore = GetPrivateField<CompanionStore>(
            composition.ViewModel.Companion,
            "_store");
        Assert.IsType<PokeApiClient>(GetPrivateField<IPokeApiClient>(companionStore, "_provider"));
        Assert.Equal(1, persistence.LoadCalls);
        Assert.Equal(0, handler.RequestCalls);
    }

    [Fact]
    public void ApplicationComposition_DisposesSharedHttpClientExactlyOnce()
    {
        var handler = new TrackingHttpHandler();
        var composition = AppComposition.CreateApplication(
            new HttpClient(handler),
            new FakeCompanionPersistence(),
            new FakeSettingsPersistence(),
            new FakeAutoStartService());

        composition.Dispose();
        composition.Dispose();

        Assert.Equal(1, handler.DisposeCalls);
    }

    [Fact]
    public void SingleInstanceGuard_AcquiresOnceAndCanReacquireAfterDispose()
    {
        var name = $@"Local\PokeTokenBar.Tests.{Guid.NewGuid():N}";
        var first = Assert.IsType<SingleInstanceGuard>(SingleInstanceGuard.TryAcquire(name));

        Assert.Null(SingleInstanceGuard.TryAcquire(name));

        first.Dispose();
        first.Dispose();
        using var reacquired = Assert.IsType<SingleInstanceGuard>(
            SingleInstanceGuard.TryAcquire(name));
    }

    [Fact]
    public void AppStartup_IsAnExplicitCompositionRootWithoutStartupUri()
    {
        var appXaml = ReadRepositoryFile("src", "PokeTokenBar.Windows.App", "App.xaml");
        var appCode = ReadRepositoryFile("src", "PokeTokenBar.Windows.App", "App.xaml.cs");
        var compositionCode = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "AppComposition.cs");

        Assert.DoesNotContain("StartupUri", appXaml, StringComparison.Ordinal);
        Assert.Contains("AppComposition.CreateApplication()", appCode, StringComparison.Ordinal);
        Assert.Contains("new MainWindow(viewModel)", appCode, StringComparison.Ordinal);
        Assert.Contains("ShutdownMode.OnExplicitShutdown", appCode, StringComparison.Ordinal);
        Assert.Contains("new NotifyIconTrayIcon()", appCode, StringComparison.Ordinal);
        Assert.Contains("new SystemTrayController(", appCode, StringComparison.Ordinal);
        Assert.Contains("new InitialRefreshController(viewModel.Usage)", appCode, StringComparison.Ordinal);
        Assert.Contains("new InitialCompanionController(viewModel.Companion)", appCode, StringComparison.Ordinal);
        Assert.Contains("new UsageCompanionController(", compositionCode, StringComparison.Ordinal);
        Assert.Contains("companionStore,", compositionCode, StringComparison.Ordinal);
        Assert.Contains("companion.RefreshAsync", compositionCode, StringComparison.Ordinal);
        Assert.Contains("new FloatingPokemonWindow(_composition.FloatingPet)", appCode, StringComparison.Ordinal);
        Assert.Contains("_floatingPet.Start()", appCode, StringComparison.Ordinal);
        Assert.Contains("_initialRefresh.StartAsync()", appCode, StringComparison.Ordinal);
        Assert.Contains("_composition.UsagePolling.Start()", appCode, StringComparison.Ordinal);
        Assert.Contains("_initialCompanion.StartAsync()", appCode, StringComparison.Ordinal);
        Assert.Contains("_initialCompanion?.Dispose()", appCode, StringComparison.Ordinal);
        Assert.Contains("_floatingPet?.Dispose()", appCode, StringComparison.Ordinal);
        Assert.Contains("_composition?.Dispose()", appCode, StringComparison.Ordinal);
        Assert.Contains("ShutdownMode.OnMainWindowClose", appCode, StringComparison.Ordinal);
        Assert.Contains("mainWindow.Show()", appCode, StringComparison.Ordinal);
        Assert.Contains("if (_singleInstance is null)", appCode, StringComparison.Ordinal);
        var guardIndex = appCode.IndexOf("SingleInstanceGuard.TryAcquire()", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0);
        Assert.True(
            guardIndex < appCode.IndexOf("AppComposition.CreateApplication()", StringComparison.Ordinal));
    }

    [Fact]
    public void MainWindow_AssignsInjectedMainViewModelAsDataContext()
    {
        var source = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "MainWindow.xaml.cs");

        Assert.Contains("MainWindow(MainViewModel viewModel)", source, StringComparison.Ordinal);
        Assert.Contains("DataContext = viewModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalCodexUsageProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CodexLocalUsageService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialRefreshController", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_IsConfiguredAsACompactTransientToolWindow()
    {
        var xaml = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "MainWindow.xaml");

        Assert.Contains("ShowInTaskbar=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation=\"Manual\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"NoResize\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStyle=\"ToolWindow\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Topmost=\"True\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void NotifyIcon_KeepsLeftClickToggleAndOpenRefreshExitContextMenu()
    {
        var source = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "Tray", "NotifyIconTrayIcon.cs");

        Assert.Contains("Forms.MouseButtons.Left", source, StringComparison.Ordinal);
        Assert.Contains("ToggleRequested?.Invoke", source, StringComparison.Ordinal);
        Assert.Contains("new Forms.ContextMenuStrip()", source, StringComparison.Ordinal);
        Assert.Contains("ToolStripMenuItem(\"Open\")", source, StringComparison.Ordinal);
        Assert.Contains("ToolStripMenuItem(\"Refresh\")", source, StringComparison.Ordinal);
        Assert.Contains("ToolStripMenuItem(\"Exit\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfTrayWindow_UsesScreenWorkingAreaAndDpiConversion()
    {
        var source = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "Tray", "WpfTrayWindow.cs");

        Assert.Contains("Forms.Screen.FromPoint(cursor)", source, StringComparison.Ordinal);
        Assert.Contains("screen.WorkingArea", source, StringComparison.Ordinal);
        Assert.Contains("VisualTreeHelper.GetDpi(_window)", source, StringComparison.Ordinal);
        Assert.Contains("PopupPositioner.Calculate(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryMainWindowBindingPath_ExistsOnRootViewModelGraph()
    {
        var xaml = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "MainWindow.xaml");
        var bindingPaths = BindingPathRegex()
            .Matches(xaml)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(bindingPaths);
        Assert.All(bindingPaths, path =>
        {
            if (new[]
                {
                    typeof(OfficialLimitRow),
                    typeof(ShopProductViewModel),
                    typeof(BagItemViewModel),
                    typeof(CollectionEntryViewModel),
                }.All(type => type.GetProperty(path) is null))
            {
                AssertBindingPath(typeof(MainViewModel), path);
            }
        });
    }

    [Fact]
    public void MainWindow_EconomyTabsBindShopBagAndCollectionCommands()
    {
        var xaml = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "MainWindow.xaml");

        Assert.Contains("Header=\"{Binding Texts.Shop", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding Texts.Bag", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding Texts.Collection", xaml, StringComparison.Ordinal);
        Assert.Contains("Economy.BalanceText", xaml, StringComparison.Ordinal);
        Assert.Contains("Economy.ShopProducts", xaml, StringComparison.Ordinal);
        Assert.Contains("PurchaseCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Economy.BagItems", xaml, StringComparison.Ordinal);
        Assert.Contains("UseCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Economy.CollectionEntries", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectRepresentativeCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_UsesRefreshCommandAndExpectedUsageBindings()
    {
        var xaml = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "MainWindow.xaml");

        string[] expected =
        [
            "ProviderName",
            "TodayTokensText",
            "InputTokensText",
            "OutputTokensText",
            "CacheWriteTokensText",
            "CacheReadTokensText",
            "RecentFiveHourTokensText",
            "WeekTokensText",
            "MonthTokensText",
            "LastUpdatedText",
            "RefreshCommand",
            "ErrorMessage",
            "HasCodexRateLimits",
            "HasFiveHourLimit",
            "FiveHourRemainingPercent",
            "FiveHourRemainingText",
            "FiveHourResetText",
            "HasWeeklyLimit",
            "WeeklyRemainingPercent",
            "WeeklyRemainingText",
            "WeeklyResetText",
            "OfficialLimitsMetadataText",
            "AntigravityLimitRows",
        ];

        Assert.All(expected, property =>
            Assert.Contains($"Binding Usage.{property}", xaml, StringComparison.Ordinal));
        Assert.Contains("Command=\"{Binding Usage.RefreshCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding Settings.IsFloatingPetEnabled", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding Settings.IsLaunchAtStartupEnabled", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding Settings.ResetFloatingPetPositionCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding Settings.RefreshIntervalOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding Settings.SelectedRefreshInterval", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding Usage.FiveHourRemainingPercent", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Usage.FiveHourRemainingText", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding Usage.WeeklyRemainingPercent", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Usage.WeeklyRemainingText", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Usage.AntigravityLimitRows", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding RemainingPercent", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding RemainingText", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_ContainsCompanionStateAndSpriteBindings()
    {
        var xaml = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "MainWindow.xaml");

        string[] expected =
        [
            "DisplayName",
            "CompanionSprite",
            "CurrentIsShiny",
            "RarityText",
            "Personality",
            "StageText",
            "Progress",
            "ProgressText",
            "StatusText",
            "IsHatching",
            "HatchingText",
        ];

        Assert.All(expected, property =>
            Assert.Contains($"Binding Companion.{property}", xaml, StringComparison.Ordinal));
        Assert.Contains("AnimatedSpritePresenter", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"🥚\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_DoesNotAddManualHatchOrResetActionsAbsentFromSwiftHeader()
    {
        var xaml = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "MainWindow.xaml");

        Assert.DoesNotContain("HatchRandom", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlyDisplayBindings_AreExplicitlyOneWay()
    {
        var xaml = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "MainWindow.xaml");
        var bindings = FullBindingRegex().Matches(xaml);

        Assert.All(bindings, match =>
        {
            var path = match.Groups[1].Value;
            if (path is "Usage.SelectedProviderId" or
                "Usage.RefreshCommand" or
                "Settings.IsFloatingPetEnabled" or
                "Settings.IsLaunchAtStartupEnabled" or
                "Settings.SelectedRefreshInterval" or
                "Settings.SelectedLanguage" or
                "Settings.SelectedLimitDisplayMode" or
                "Settings.LimitNotificationsEnabled" or
                "Settings.CompanionNotificationsEnabled" or
                "Settings.WarningThreshold" or
                "Settings.CriticalThreshold" or
                "Settings.FloatingPetSize" or
                "Settings.SelectedAnimationQuality" or
                "Settings.FloatingBubbleAlertsEnabled" or
                "Settings.UpdateNotificationsEnabled" or
                "Settings.SelectedRootProviderId" or
                "Settings.CustomRootText" or
                "Settings.ResetFloatingPetPositionCommand")
            {
                return;
            }

            Assert.Contains("Mode=OneWay", match.Value, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task InitialRefreshController_StartsRefreshOnlyOnce()
    {
        var provider = new TestUsageProvider(Daily(10));
        var viewModel = new UsageViewModel(new UsageStore([provider]));
        var initialRefresh = new InitialRefreshController(viewModel);

        await Task.WhenAll(initialRefresh.StartAsync(), initialRefresh.StartAsync());

        Assert.True(initialRefresh.HasStarted);
        Assert.Equal(1, provider.DailyCalls);
        Assert.Equal(10, viewModel.TodayTokens);
    }

    [Fact]
    public async Task InitialRefreshFailure_DoesNotEscapeTheUiCommandBoundary()
    {
        var provider = new TestUsageProvider(
            daily: null,
            dailyError: new TestException("startup failed"));
        var viewModel = new UsageViewModel(new UsageStore([provider]));
        var initialRefresh = new InitialRefreshController(viewModel);

        var exception = await Record.ExceptionAsync(initialRefresh.StartAsync);

        Assert.Null(exception);
        Assert.Contains("startup failed", viewModel.ErrorMessage);
        Assert.False(viewModel.IsRefreshing);
    }

    [Fact]
    public async Task NoUsageData_RemainsAValidWindowBindingState()
    {
        var viewModel = new UsageViewModel(
            new UsageStore([new TestUsageProvider(daily: null)]));
        var initialRefresh = new InitialRefreshController(viewModel);

        await initialRefresh.StartAsync();

        Assert.Empty(viewModel.Providers);
        Assert.Null(viewModel.ProviderName);
        Assert.Null(viewModel.TodayTokensText);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task SelectedProviderId_IsWritableForTheXamlSelector()
    {
        var one = new TestUsageProvider(Daily(10), id: "one");
        var two = new TestUsageProvider(Daily(20), id: "two");
        var viewModel = new UsageViewModel(new UsageStore([one, two]));
        await viewModel.RefreshAsync();

        viewModel.SelectedProviderId = "two";

        Assert.Equal("two", viewModel.PreferredProviderId);
        Assert.Equal("two", viewModel.SelectedProviderId);
        Assert.Equal(20, viewModel.TodayTokens);
    }

    private static T GetPrivateField<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsAssignableFrom<T>(field?.GetValue(instance));
    }

    private static void AssertBindingPath(Type rootType, string path)
    {
        var current = rootType;
        foreach (var segment in path.Split('.'))
        {
            var property = current.GetProperty(segment) ??
                current.GetInterfaces()
                    .Select(type => type.GetProperty(segment))
                    .FirstOrDefault(candidate => candidate is not null);
            Assert.True(property is not null, $"{current.Name} does not expose binding segment '{segment}' from '{path}'.");
            current = property!.PropertyType;
        }
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PokeTokenBar.Windows.sln")))
            {
                return File.ReadAllText(
                    Path.Combine([directory.FullName, .. relativeParts]));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static DailyUsage Daily(long total) =>
        new(
            DateTimeOffset.Now.ToString("yyyy-MM-dd"),
            total,
            0,
            0,
            0,
            total,
            0);

    [GeneratedRegex(@"\{Binding\s+([A-Za-z][A-Za-z0-9.]*)")]
    private static partial Regex BindingPathRegex();

    [GeneratedRegex(@"\{Binding\s+([A-Za-z][A-Za-z0-9.]*)[^}]*\}")]
    private static partial Regex FullBindingRegex();

    private sealed class TestUsageProvider(
        DailyUsage? daily,
        string id = "test",
        Exception? dailyError = null) : IUsageProvider
    {
        public string Id { get; } = id;
        public string DisplayName => Id;
        public bool ReportsCost => true;
        public int DailyCalls { get; private set; }

        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default)
        {
            DailyCalls++;
            return dailyError is null
                ? Task.FromResult(daily)
                : Task.FromException<DailyUsage?>(dailyError);
        }

        public Task<ProviderEnrichment> FetchEnrichmentAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderEnrichment());
    }

    private sealed class TestException(string message) : Exception(message);

    private sealed class FakeCompanionPersistence : ICompanionPersistence
    {
        public int LoadCalls { get; private set; }

        public CompanionState? Load()
        {
            LoadCalls++;
            return null;
        }

        public void Save(CompanionState state) { }
        public void Delete() { }
    }

    private sealed class FakeSettingsPersistence : IAppSettingsPersistence
    {
        public AppSettings? Load() => null;
        public void Save(AppSettings settings) { }
    }

    private sealed class FakeAutoStartService : IAutoStartService
    {
        public bool IsAvailable => true;
        public bool IsEnabled { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }

    private sealed class TrackingHttpHandler : HttpMessageHandler
    {
        public int RequestCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCalls++;
            }

            base.Dispose(disposing);
        }
    }
}
