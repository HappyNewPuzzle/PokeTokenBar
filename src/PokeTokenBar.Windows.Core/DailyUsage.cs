namespace PokeTokenBar.Windows.Core;

public sealed record DailyUsage(
    string Date,
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens,
    long TotalTokens,
    double TotalCost,
    CostCoverage CostCoverage = default)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public UsageCost UsageCost => new(TotalCost, CostCoverage);
}
