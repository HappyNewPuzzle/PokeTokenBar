namespace PokeTokenBar.Windows.Core;

public sealed record PeriodUsage(
    string Period,
    long TotalTokens,
    double TotalCost,
    CostCoverage CostCoverage = default)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public UsageCost UsageCost => new(TotalCost, CostCoverage);
}
