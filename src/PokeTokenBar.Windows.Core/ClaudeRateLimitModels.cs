namespace PokeTokenBar.Windows.Core;

public sealed record ClaudeOAuthCredential(
    string AccessToken,
    DateTimeOffset? ExpiresAt,
    string? SubscriptionType,
    string? RateLimitTier);

public sealed record ClaudeRateLimitWindow(
    double UsedPercent,
    DateTimeOffset? ResetsAt);

public sealed record ClaudeRateLimitStatus(
    ClaudeRateLimitWindow? FiveHour,
    ClaudeRateLimitWindow? SevenDay,
    string? SubscriptionType,
    string? RateLimitTier,
    string? AccountEmail,
    string? AccountOrganizationName)
{
    public bool HasVisibleLimit => FiveHour is not null || SevenDay is not null;

    public string? PlanDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SubscriptionType))
            {
                return null;
            }

            var plan = char.ToUpperInvariant(SubscriptionType[0]) + SubscriptionType[1..];
            var multiplier = RateLimitTier?
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(part =>
                    part.EndsWith('x') &&
                    part.Length > 1 &&
                    part[..^1].All(char.IsDigit));
            return multiplier is null ? plan : $"{plan} {multiplier}";
        }
    }

    public string? AccountDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AccountEmail))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(AccountOrganizationName) ||
                   AccountOrganizationName.Contains(AccountEmail, StringComparison.OrdinalIgnoreCase)
                ? AccountEmail
                : $"{AccountEmail} · {AccountOrganizationName}";
        }
    }
}
