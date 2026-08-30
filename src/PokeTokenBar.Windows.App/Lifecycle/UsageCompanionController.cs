using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App.Lifecycle;

internal sealed class UsageCompanionController : IDisposable
{
    private readonly UsageViewModel _usage;
    private readonly CompanionStore _companion;
    private readonly Func<CancellationToken, Task> _refreshPresentation;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private Task _lastUpdate = Task.CompletedTask;
    private bool _disposed;

    public UsageCompanionController(
        UsageViewModel usage,
        CompanionStore companion,
        Func<CancellationToken, Task> refreshPresentation)
    {
        _usage = usage ?? throw new ArgumentNullException(nameof(usage));
        _companion = companion ?? throw new ArgumentNullException(nameof(companion));
        _refreshPresentation = refreshPresentation ??
            throw new ArgumentNullException(nameof(refreshPresentation));
        _usage.RefreshCompleted += OnRefreshCompleted;
    }

    internal Task LastUpdate
    {
        get
        {
            lock (_sync)
            {
                return _lastUpdate;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _usage.RefreshCompleted -= OnRefreshCompleted;
        _cancellation.Cancel();
    }

    private void OnRefreshCompleted(object? sender, RefreshCompletedEventArgs args)
    {
        if (!args.Succeeded)
        {
            return;
        }

        var date = _usage.TodayDate;
        var tokens = new Dictionary<string, long>(
            _usage.TodayTokensByProvider,
            StringComparer.Ordinal);
        var hasUsageData = _usage.HasUsageData;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _lastUpdate = ApplyAsync(tokens, date, hasUsageData, _cancellation.Token);
        }
    }

    private async Task ApplyAsync(
        IReadOnlyDictionary<string, long> tokens,
        string date,
        bool hasUsageData,
        CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                await _companion.UpdateUsageAsync(
                    tokens,
                    date,
                    hasUsageData,
                    cancellationToken);
                await _refreshPresentation(cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // Companion progression is best effort and must not fail usage refresh.
        }
    }
}
