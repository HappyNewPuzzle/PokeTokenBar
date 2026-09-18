namespace PokeTokenBar.Windows.Core;

public readonly record struct CostCoverage(
    bool Reported = false,
    bool Estimated = false,
    bool Unknown = false)
{
    public static CostCoverage Source => new(Reported: true);
    public static CostCoverage Estimate => new(Estimated: true);
    public static CostCoverage Unavailable => new(Unknown: true);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasKnown => Reported || Estimated;

    public CostCoverage Merge(CostCoverage other) => new(
        Reported || other.Reported,
        Estimated || other.Estimated,
        Unknown || other.Unknown);

    public static CostCoverage FromLegacy(long totalTokens, double totalCost) =>
        totalTokens == 0 ? default : totalCost > 0 ? Estimate : Unavailable;
}

public readonly record struct UsageCost(double Amount, CostCoverage Coverage)
{
    public UsageCost Add(UsageCost other) =>
        new(Amount + other.Amount, Coverage.Merge(other.Coverage));
}
