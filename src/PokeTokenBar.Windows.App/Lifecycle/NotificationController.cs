using System.ComponentModel;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App.Lifecycle;

internal sealed class NotificationController : IDisposable
{
    private readonly UsageViewModel _usage;
    private readonly SettingsViewModel _settings;
    private readonly FloatingPetViewModel _floatingPet;
    private readonly INotificationService _notifications;
    private readonly CompanionStore? _companion;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _cancellation = new();
    private bool _disposed;

    public NotificationController(
        UsageViewModel usage,
        SettingsViewModel settings,
        FloatingPetViewModel floatingPet,
        INotificationService notifications,
        CompanionStore? companion = null)
    {
        _usage = usage;
        _settings = settings;
        _floatingPet = floatingPet;
        _notifications = notifications;
        _companion = companion;
        _usage.PropertyChanged += OnUsageChanged;
        if (_companion is not null) _companion.GameEventOccurred += OnCompanionEvent;
    }

    internal Task LastEvaluation { get; private set; } = Task.CompletedTask;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _usage.PropertyChanged -= OnUsageChanged;
        if (_companion is not null) _companion.GameEventOccurred -= OnCompanionEvent;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private void OnCompanionEvent(object? sender, CompanionGameEvent gameEvent)
    {
        if (_disposed || !_settings.CompanionNotificationsEnabled) return;
        AppReliability.Run(NotifyCompanionAsync(gameEvent, _cancellation.Token));
    }

    private async Task NotifyCompanionAsync(
        CompanionGameEvent gameEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            var text = _settings.Localization;
            var (kind, title, body) = gameEvent.Kind switch
            {
                CompanionGameEventKind.Hatch =>
                    (NotificationKind.Hatch, text.HatchTitle, text.CompanionEventBody(gameEvent.SpeciesId)),
                CompanionGameEventKind.Evolution =>
                    (NotificationKind.Evolution, text.EvolutionTitle, text.CompanionEventBody(gameEvent.SpeciesId)),
                CompanionGameEventKind.DittoReveal =>
                    (NotificationKind.DittoReveal,
                        gameEvent.IsShiny ? text.ShinyDittoRevealTitle : text.DittoRevealTitle,
                        text.DittoRevealBody(gameEvent.PreviousSpeciesId)),
                CompanionGameEventKind.Graduation =>
                    (NotificationKind.Graduation, text.GraduationTitle, text.CompanionEventBody(gameEvent.SpeciesId)),
                _ => (NotificationKind.Reward, text.RewardTitle, text.RewardBody(gameEvent.Count)),
            };
            await _notifications.ShowAsync(new NotificationMessage(
                $"companion.{gameEvent.Kind}.{Guid.NewGuid():N}", kind, title, body), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { }
    }

    private void OnUsageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_disposed && args.PropertyName == nameof(UsageViewModel.IsRefreshing) && !_usage.IsRefreshing)
        {
            LastEvaluation = EvaluateAsync(_cancellation.Token);
        }
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tiers = new Dictionary<string, int>(_settings.NotificationTiers, StringComparer.Ordinal);
            var alerts = LimitNotificationEvaluator.Evaluate(
                _usage.NotificationWindows,
                _settings.WarningThreshold,
                _settings.CriticalThreshold,
                tiers);
            _settings.SaveNotificationTiers(tiers);
            var alert = alerts.OrderByDescending(value => value.IsCritical)
                .ThenByDescending(value => value.UsedPercent)
                .FirstOrDefault();
            if (alert is null) return;

            var text = _settings.Localization;
            var title = alert.IsCritical
                ? text.NotificationCriticalTitle
                : text.NotificationWarningTitle;
            var body = text.PercentUsed(alert.WindowName, alert.UsedPercent);
            if (_settings.LimitNotificationsEnabled)
            {
                await _notifications.ShowAsync(new NotificationMessage(
                    $"{alert.Key}.{(alert.IsCritical ? "critical" : "warning")}",
                    alert.IsCritical ? NotificationKind.LimitCritical : NotificationKind.LimitWarning,
                    title,
                    body), cancellationToken);
            }

            if (_settings.IsFloatingPetEnabled && _settings.FloatingBubbleAlertsEnabled)
            {
                AppReliability.Run(_floatingPet.ShowBubbleAsync(title, body));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { }
        finally { _gate.Release(); }
    }
}
