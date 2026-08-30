using System.Collections.ObjectModel;

namespace PokeTokenBar.Windows.Core;

public sealed record CodexRateLimitWindow(
    int UsedPercent,
    int? WindowDurationMinutes,
    DateTimeOffset? ResetsAt);

public sealed record CodexCreditsSnapshot(
    string? Balance,
    bool HasCredits,
    bool Unlimited);

public sealed record CodexSpendControlLimit(
    string Limit,
    int RemainingPercent,
    DateTimeOffset ResetsAt,
    string Used)
{
    public int UsedPercent => Math.Clamp(100 - RemainingPercent, 0, 100);
}

public sealed record CodexRateLimitSnapshot(
    string? LimitId,
    string? LimitName,
    CodexRateLimitWindow? Primary,
    CodexRateLimitWindow? Secondary,
    CodexCreditsSnapshot? Credits,
    CodexSpendControlLimit? IndividualLimit,
    string? PlanType,
    string? RateLimitReachedType)
{
    public bool HasVisibleLimit =>
        Primary is not null || Secondary is not null || IndividualLimit is not null;
}

public sealed class CodexRateLimitStatus
{
    private readonly IReadOnlyList<CodexRateLimitSnapshot> _snapshots;
    private readonly IReadOnlyList<CodexRateLimitSnapshot> _visibleSnapshots;

    public CodexRateLimitStatus(
        CodexRateLimitSnapshot rateLimits,
        IReadOnlyDictionary<string, CodexRateLimitSnapshot>? rateLimitsByLimitId = null)
    {
        RateLimits = rateLimits ?? throw new ArgumentNullException(nameof(rateLimits));
        RateLimitsByLimitId = rateLimitsByLimitId is null
            ? null
            : new ReadOnlyDictionary<string, CodexRateLimitSnapshot>(
                new Dictionary<string, CodexRateLimitSnapshot>(
                    rateLimitsByLimitId,
                    StringComparer.Ordinal));

        var snapshots = new List<CodexRateLimitSnapshot> { RateLimits };
        if (RateLimitsByLimitId is not null)
        {
            var primaryKey = RateLimits.LimitId ?? "codex";
            foreach (var pair in RateLimitsByLimitId.OrderBy(
                         static pair => pair.Key,
                         StringComparer.Ordinal))
            {
                if (pair.Key == primaryKey ||
                    (pair.Value.LimitId is not null &&
                     pair.Value.LimitId == RateLimits.LimitId))
                {
                    continue;
                }

                snapshots.Add(pair.Value);
            }
        }

        _snapshots = new ReadOnlyCollection<CodexRateLimitSnapshot>(snapshots);
        _visibleSnapshots = new ReadOnlyCollection<CodexRateLimitSnapshot>(
            snapshots.Where(static snapshot => snapshot.HasVisibleLimit).ToArray());
    }

    public CodexRateLimitSnapshot RateLimits { get; }

    public IReadOnlyDictionary<string, CodexRateLimitSnapshot>? RateLimitsByLimitId { get; }

    public IReadOnlyList<CodexRateLimitSnapshot> Snapshots => _snapshots;

    public IReadOnlyList<CodexRateLimitSnapshot> VisibleSnapshots => _visibleSnapshots;

    public bool HasVisibleLimit => _visibleSnapshots.Count > 0;

    public int? MaxPrimaryUsedPercent =>
        _visibleSnapshots
            .Select(static snapshot => (int?)snapshot.Primary?.UsedPercent)
            .Max();
}
