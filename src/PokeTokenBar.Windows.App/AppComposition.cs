using System.Net.Http;
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
            return CreateApplication(httpClient, new JsonCompanionPersistence());
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    internal static ApplicationComposition CreateApplication(
        HttpClient httpClient,
        ICompanionPersistence persistence)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(persistence);

        var usage = CreateUsageViewModel();
        var api = new PokeApiClient(httpClient);
        var companionStore = new CompanionStore(api, persistence);
        var spriteLoader = new PokemonSpriteLoader(httpClient);
        var companion = new CompanionViewModel(
            companionStore,
            spriteLoader,
            new WpfPokemonSpriteDecoder());
        var floatingPet = new FloatingPetViewModel(companion);
        return new ApplicationComposition(
            new MainViewModel(usage, companion),
            floatingPet,
            httpClient);
    }

    public static UsageViewModel CreateUsageViewModel()
    {
        IUsageProvider provider = new LocalCodexUsageProvider();
        var store = new UsageStore([provider]);
        return new UsageViewModel(store);
    }
}
