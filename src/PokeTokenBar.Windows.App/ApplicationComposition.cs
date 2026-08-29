using System.Net.Http;
using PokeTokenBar.Windows.App.ViewModels;

namespace PokeTokenBar.Windows.App;

public sealed class ApplicationComposition : IDisposable
{
    private readonly HttpClient _httpClient;
    private bool _disposed;

    internal ApplicationComposition(MainViewModel viewModel, HttpClient httpClient)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public MainViewModel ViewModel { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ViewModel.Dispose();
        _httpClient.Dispose();
    }
}
