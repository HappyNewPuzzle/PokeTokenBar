using System.Net.Http;
using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.ViewModels;

namespace PokeTokenBar.Windows.App;

public sealed class ApplicationComposition : IDisposable
{
    private readonly HttpClient _httpClient;
    private bool _disposed;

    internal ApplicationComposition(
        MainViewModel viewModel,
        FloatingPetViewModel floatingPet,
        UsagePollingController usagePolling,
        HttpClient httpClient)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        FloatingPet = floatingPet ?? throw new ArgumentNullException(nameof(floatingPet));
        UsagePolling = usagePolling ?? throw new ArgumentNullException(nameof(usagePolling));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public MainViewModel ViewModel { get; }

    public FloatingPetViewModel FloatingPet { get; }

    internal UsagePollingController UsagePolling { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UsagePolling.Dispose();
        FloatingPet.Dispose();
        ViewModel.Dispose();
        _httpClient.Dispose();
    }
}
