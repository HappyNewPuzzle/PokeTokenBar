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

        var api = new PokeApiClient(httpClient);
        var companionStore = new CompanionStore(api, persistence);
        var settings = new SettingsViewModel(
            settingsPersistence, autoStartService, companionStore.State.Language);
        var usage = CreateUsageViewModel(httpClient, settings.CustomRoots, settings.Localization);
        usage.SelectedProviderId = settings.SelectedProviderId;
        settings.UpdateProviderStatuses(usage.ProviderStatuses);
        usage.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(UsageViewModel.SelectedProviderId))
            {
                settings.SaveSelectedProvider(usage.SelectedProviderId);
            }
            else if (args.PropertyName == nameof(UsageViewModel.ProviderStatuses))
            {
                settings.UpdateProviderStatuses(usage.ProviderStatuses);
            }
        };
        var spriteLoader = new PokemonSpriteLoader(httpClient);
        var companion = new CompanionViewModel(
            companionStore,
            spriteLoader,
            new WpfPokemonSpriteDecoder());
        var economy = new EconomyViewModel(
            companionStore, companion.RefreshAsync, settings.Localization);
        var usageCompanion = new UsageCompanionController(
            usage,
            companionStore,
            async cancellationToken =>
            {
                await companion.RefreshAsync(cancellationToken);
                economy.Refresh();
            });
        var floatingPet = new FloatingPetViewModel(companion, settings, usage);
        settings.LanguageChanged += language =>
        {
            companionStore.SetLanguage(language);
            usage.RefreshPresentation();
            _ = RefreshLanguageAsync(companion, economy);
        };
        settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.SelectedLimitDisplayMode))
            {
                usage.SetLimitDisplayMode(settings.SelectedLimitDisplayMode);
            }
        };
        usage.SetLimitDisplayMode(settings.SelectedLimitDisplayMode);
        var usagePolling = new UsagePollingController(
            usage,
            settings,
            dispatchAsync: usageRefreshDispatcher);
        SupportViewModel? support = null;
        if (settingsPersistence is JsonAppSettingsPersistence jsonSettings &&
            persistence is JsonCompanionPersistence jsonCompanion)
        {
            support = new SupportViewModel(
                new GitHubReleaseUpdateChecker(httpClient, ApplicationVersion.Current),
                new StateTransferService(jsonSettings, jsonCompanion, ApplicationVersion.Current),
                settings,
                usage,
                new WindowsUserInteraction(settings.Localization));
        }
        return new ApplicationComposition(
            new MainViewModel(usage, companion, economy, settings, support),
            floatingPet,
            usagePolling,
            usageCompanion,
            companionStore,
            httpClient);
    }

    public static UsageViewModel CreateUsageViewModel(HttpClient? httpClient = null) =>
        CreateUsageViewModel(httpClient, null, null);

    internal static UsageViewModel CreateUsageViewModel(
        HttpClient? httpClient,
        Func<string, IReadOnlyList<string>>? customRoots,
        LocalizationService? localization = null)
    {
        IUsageProvider[] providers = customRoots is null
            ?
            [
                new LocalCodexUsageProvider(), new LocalClaudeUsageProvider(),
                new LocalGeminiUsageProvider(), new LocalAntigravityUsageProvider(),
                httpClient is null ? new LocalCursorUsageProvider() : new LocalCursorUsageProvider(httpClient),
                new LocalOpenCodeUsageProvider(), new LocalHermesUsageProvider(),
                new LocalGrokUsageProvider(), new LocalCopilotUsageProvider(),
                new LocalKiroUsageProvider(), new LocalPiUsageProvider(), new LocalOmpUsageProvider(),
            ]
            : CreateConfiguredProviders(httpClient ?? new HttpClient(), customRoots);
        ICodexRateLimitsProvider codexRateLimitsProvider = new CodexRateLimitsProvider();
        IClaudeRateLimitsProvider claudeRateLimitsProvider = new ClaudeRateLimitsProvider();
        IAntigravityRateLimitsProvider antigravityRateLimitsProvider =
            new AntigravityRateLimitsProvider();
        var store = new UsageStore(
            providers,
            codexRateLimitsProvider: codexRateLimitsProvider,
            claudeRateLimitsProvider: claudeRateLimitsProvider,
            antigravityRateLimitsProvider: antigravityRateLimitsProvider);
        return new UsageViewModel(store, localization: localization);
    }

    private static IUsageProvider[] CreateConfiguredProviders(
        HttpClient httpClient,
        Func<string, IReadOnlyList<string>> customRoots) =>
    [
        Configured("codex", "Codex", true, CodexSessionLocator.GetDefaultRoots, customRoots,
            roots => new LocalCodexUsageProvider(roots)),
        Configured("claude_code", "Claude Code", true, () => LocalClaudeUsageProvider.GetDefaultRoots(), customRoots,
            roots => new LocalClaudeUsageProvider(roots)),
        Configured("gemini", "Gemini", true, () => LocalGeminiUsageProvider.GetDefaultRoots(), customRoots,
            roots => new LocalGeminiUsageProvider(roots)),
        Configured("antigravity", "Antigravity", false, () => LocalAntigravityUsageProvider.GetDefaultRoots(), customRoots,
            roots => new LocalAntigravityUsageProvider(roots)),
        Configured("cursor", "Cursor", false, () => LocalCursorUsageProvider.GetDefaultRoots(), customRoots,
            roots => new LocalCursorUsageProvider(httpClient, roots)),
        Configured("opencode", "OpenCode", true, () => LocalOpenCodeUsageProvider.GetDefaultRoots(), customRoots,
            roots => new LocalOpenCodeUsageProvider(roots)),
        Configured("hermes", "Hermes Agent", true, () => LocalHermesUsageProvider.GetDefaultRoots(), customRoots,
            roots => new LocalHermesUsageProvider(roots)),
        Configured("grok", "Grok", true, () => LocalGrokUsageProvider.GetDefaultRoots(), customRoots,
            roots => new LocalGrokUsageProvider(roots)),
        Configured("copilot", "Copilot", false, () => LocalCopilotUsageProvider.GetDefaultRoots(), customRoots,
            roots => new LocalCopilotUsageProvider(roots)),
        Configured("kiro", "Kiro", false, () => LocalKiroUsageProvider.GetDefaultRoots(), customRoots,
            roots => new LocalKiroUsageProvider(roots)),
        Configured("pi", "Pi", false, () => LocalPiUsageProvider.GetDefaultRoots(), customRoots,
            roots => new LocalPiUsageProvider(roots)),
        Configured("omp", "omp", true, () => LocalOmpUsageProvider.GetDefaultRoots(), customRoots,
            roots => new LocalOmpUsageProvider(roots)),
    ];

    private static IUsageProvider Configured(
        string id,
        string name,
        bool reportsCost,
        Func<IReadOnlyList<string>> defaults,
        Func<string, IReadOnlyList<string>> customRoots,
        Func<IReadOnlyList<string>, IUsageProvider> factory) =>
        new ConfigurableUsageProvider(
            id, name, reportsCost, defaults,
            () => customRoots(id), factory);

    private static async Task RefreshLanguageAsync(
        CompanionViewModel companion,
        EconomyViewModel economy)
    {
        await companion.RefreshAsync();
        economy.Refresh();
    }
}
