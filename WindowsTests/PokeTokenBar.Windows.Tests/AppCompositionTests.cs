using System.Reflection;
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
    public void ProductionComposition_CreatesLocalCodexStoreAndUsageViewModel()
    {
        var viewModel = AppComposition.CreateUsageViewModel();
        var store = GetPrivateField<UsageStore>(viewModel, "_store");
        var providers = GetPrivateField<IReadOnlyList<IUsageProvider>>(store, "_providers");

        var provider = Assert.Single(providers);
        Assert.IsType<LocalCodexUsageProvider>(provider);
        Assert.Equal("codex", provider.Id);
        Assert.Empty(viewModel.Providers);
    }

    [Fact]
    public void AppStartup_IsAnExplicitCompositionRootWithoutStartupUri()
    {
        var appXaml = ReadRepositoryFile("src", "PokeTokenBar.Windows.App", "App.xaml");
        var appCode = ReadRepositoryFile("src", "PokeTokenBar.Windows.App", "App.xaml.cs");

        Assert.DoesNotContain("StartupUri", appXaml, StringComparison.Ordinal);
        Assert.Contains("AppComposition.CreateUsageViewModel()", appCode, StringComparison.Ordinal);
        Assert.Contains("new MainWindow(viewModel)", appCode, StringComparison.Ordinal);
        Assert.Contains("MainWindow.Show()", appCode, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_AssignsInjectedUsageViewModelAsDataContext()
    {
        var source = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "MainWindow.xaml.cs");

        Assert.Contains("MainWindow(UsageViewModel viewModel)", source, StringComparison.Ordinal);
        Assert.Contains("DataContext = viewModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalCodexUsageProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CodexLocalUsageService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryMainWindowBindingPath_ExistsOnUsageViewModel()
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
            Assert.NotNull(typeof(UsageViewModel).GetProperty(path)));
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
        ];

        Assert.All(expected, property =>
            Assert.Contains($"Binding {property}", xaml, StringComparison.Ordinal));
        Assert.Contains("Command=\"{Binding RefreshCommand}\"", xaml, StringComparison.Ordinal);
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
            if (path is nameof(UsageViewModel.SelectedProviderId) or nameof(UsageViewModel.RefreshCommand))
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

    [GeneratedRegex(@"\{Binding\s+([A-Za-z][A-Za-z0-9]*)")]
    private static partial Regex BindingPathRegex();

    [GeneratedRegex(@"\{Binding\s+([A-Za-z][A-Za-z0-9]*)[^}]*\}")]
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
}
