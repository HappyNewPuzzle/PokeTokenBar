namespace PokeTokenBar.Windows.Core;

public sealed record BlockUsage(
    string Id,
    string StartTime,
    string EndTime,
    bool IsActive,
    long TotalTokens,
    double CostUSD,
    double? TokensPerMinute,
    CostCoverage CostCoverage = default)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public UsageCost UsageCost => new(CostUSD, CostCoverage);
}
