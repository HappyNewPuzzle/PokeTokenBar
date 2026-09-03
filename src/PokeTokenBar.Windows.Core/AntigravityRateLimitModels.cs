using System.Globalization;
using System.Text.Json.Serialization;

namespace PokeTokenBar.Windows.Core;

public sealed record AntigravityOAuthCredential(
    string AccessToken,
    string? RefreshToken = null,
    DateTimeOffset? ExpiresAt = null)
{
    public bool IsExpired(TimeProvider? timeProvider = null) =>
        ExpiresAt is DateTimeOffset expiresAt &&
        expiresAt <= (timeProvider ?? TimeProvider.System).GetUtcNow().AddMinutes(1);
}

public sealed record AntigravityQuotaBucket(
    [property: JsonPropertyName("bucketId")] string BucketId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("window")] string? Window,
    [property: JsonPropertyName("resetTime")] string? ResetTime,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("remainingFraction")] double RemainingFraction)
{
    public double UsedPercent => Math.Clamp((1 - RemainingFraction) * 100, 0, 100);
    public int RemainingPercent => (int)Math.Round(
        Math.Clamp(RemainingFraction * 100, 0, 100),
        MidpointRounding.AwayFromZero);
    public DateTimeOffset? ResetsAt =>
        DateTimeOffset.TryParse(
            ResetTime,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
    public bool IsFiveHour =>
        Window == "5h" || BucketId.Contains("5h", StringComparison.OrdinalIgnoreCase);
    public bool IsWeekly =>
        Window == "weekly" || BucketId.Contains("weekly", StringComparison.OrdinalIgnoreCase);
}

public sealed record AntigravityQuotaGroup(
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("buckets")] IReadOnlyList<AntigravityQuotaBucket> Buckets)
{
    public AntigravityQuotaBucket? FiveHour => Buckets.FirstOrDefault(bucket => bucket.IsFiveHour);
    public AntigravityQuotaBucket? Weekly => Buckets.FirstOrDefault(bucket => bucket.IsWeekly);
}

public sealed record AntigravityRateLimitStatus(
    [property: JsonPropertyName("groups")] IReadOnlyList<AntigravityQuotaGroup> Groups,
    [property: JsonPropertyName("description")] string? Description)
{
    public bool HasVisibleLimit => Groups.Any(group => group.Buckets.Count > 0);
    public double? MaxPrimaryUsedPercent => Groups
        .Select(group => group.FiveHour?.UsedPercent)
        .Where(value => value is not null)
        .Max();
}
