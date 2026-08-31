using System.Net.Http;
using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.App;

public static class AppComposition
{
    public static ApplicationComposition CreateApplication()
    {
        var httpClient = new HttpClient();
        try
        {
            return CreateApplication(
                httpClient,
                new JsonCompanionPersistence(),
                new JsonAppSettingsPersistence(),
                new WindowsAutoStartService(),
                operation => System.Windows.Application.Current.Dispatcher
                    .InvokeAsync(operation).Task.Unwrap());
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    internal static ApplicationComposition CreateApplication(
        HttpClient httpClient,
        ICompanionPersistence persistence,
        IAppSettingsPersistence settingsPersistence,
        IAutoStartService autoStartService,
        Func<Func<Task>, Task>? usageRefreshDispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(settingsPersistence);
        ArgumentNullException.ThrowIfNull(autoStartService);

        var usage = CreateUsageViewModel(httpClient);
        var api = new PokeApiClient(httpClient);
        var companionStore = new CompanionStore(api, persistence);
        var spriteLoader = new PokemonSpriteLoader(httpClient);
        var companion = new CompanionViewModel(
            companionStore,
            spriteLoader,
            new WpfPokemonSpriteDecoder());
        var usageCompanion = new UsageCompanionController(
            usage,
            companionStore,
            companion.RefreshAsync);
        var floatingPet = new FloatingPetViewModel(companion);
        var settings = new SettingsViewModel(settingsPersistence, autoStartService);
        var usagePolling = new UsagePollingController(
            usage,
            settings,
            dispatchAsync: usageRefreshDispatcher);
        return new ApplicationComposition(
            new MainViewModel(usage, companion, settings),
            floatingPet,
            usagePolling,
            usageCompanion,
            httpClient);
    }

    public static UsageViewModel CreateUsageViewModel(HttpClient? httpClient = null)
    {
        IUsageProvider[] providers =
        [
            new LocalCodexUsageProvider(),
            new LocalClaudeUsageProvider(),
            new LocalGeminiUsageProvider(),
            new LocalAntigravityUsageProvider(),
            httpClient is null
                ? new LocalCursorUsageProvider()
                : new LocalCursorUsageProvider(httpClient),
            new LocalOpenCodeUsageProvider(),
            new LocalHermesUsageProvider(),
            new LocalGrokUsageProvider(),
            new LocalCopilotUsageProvider(),
            new LocalKiroUsageProvider(),
            new LocalPiUsageProvider(),
            new LocalOmpUsageProvider(),
        ];
        ICodexRateLimitsProvider codexRateLimitsProvider = new CodexRateLimitsProvider();
        IClaudeRateLimitsProvider claudeRateLimitsProvider = new ClaudeRateLimitsProvider();
        IAntigravityRateLimitsProvider antigravityRateLimitsProvider =
            new AntigravityRateLimitsProvider();
        var store = new UsageStore(
            providers,
            codexRateLimitsProvider: codexRateLimitsProvider,
            claudeRateLimitsProvider: claudeRateLimitsProvider,
            antigravityRateLimitsProvider: antigravityRateLimitsProvider);
        return new UsageViewModel(store);
    }
}
