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

        var usage = CreateUsageViewModel();
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

    public static UsageViewModel CreateUsageViewModel()
    {
        IUsageProvider provider = new LocalCodexUsageProvider();
        ICodexRateLimitsProvider rateLimitsProvider = new CodexRateLimitsProvider();
        var store = new UsageStore(
            [provider],
            codexRateLimitsProvider: rateLimitsProvider);
        return new UsageViewModel(store);
    }
}
