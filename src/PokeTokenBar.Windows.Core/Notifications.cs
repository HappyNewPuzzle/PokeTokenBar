namespace PokeTokenBar.Windows.Core;

public enum NotificationKind
{
    LimitWarning,
    LimitCritical,
    Hatch,
    Evolution,
    Graduation,
    Reward,
}

public enum CompanionGameEventKind
{
    Hatch,
    Evolution,
    Graduation,
    Reward,
}

public sealed record CompanionGameEvent(
    CompanionGameEventKind Kind,
    int? SpeciesId = null,
    int Count = 0);

public sealed record NotificationMessage(
    string Id,
    NotificationKind Kind,
    string Title,
    string Body);

public interface INotificationService
{
    Task ShowAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

public sealed record LimitNotificationWindow(string Key, string Name, double UsedPercent);

public sealed record LimitNotificationAlert(
    string Key,
    string WindowName,
    bool IsCritical,
    double UsedPercent);

public static class LimitNotificationEvaluator
{
    public static IReadOnlyList<LimitNotificationAlert> Evaluate(
        IEnumerable<LimitNotificationWindow> windows,
        double warningThreshold,
        double criticalThreshold,
        IDictionary<string, int> tiers)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(tiers);
        var alerts = new List<LimitNotificationAlert>();
        foreach (var window in windows)
        {
            var used = double.IsFinite(window.UsedPercent)
                ? Math.Clamp(window.UsedPercent, 0, 100)
                : 0;
            var tier = used >= criticalThreshold ? 2 : used >= warningThreshold ? 1 : 0;
            if (tier == 0)
            {
                tiers.Remove(window.Key);
                continue;
            }

            var previous = tiers.TryGetValue(window.Key, out var savedTier) ? savedTier : 0;
            if (tier <= previous)
            {
                continue;
            }

            tiers[window.Key] = tier;
            alerts.Add(new LimitNotificationAlert(
                window.Key, window.Name, tier == 2, used));
        }

        return alerts;
    }
}
