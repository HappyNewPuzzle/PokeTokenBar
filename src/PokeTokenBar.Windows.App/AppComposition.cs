using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.App;

public static class AppComposition
{
    public static UsageViewModel CreateUsageViewModel()
    {
        IUsageProvider provider = new LocalCodexUsageProvider();
        var store = new UsageStore([provider]);
        return new UsageViewModel(store);
    }
}
