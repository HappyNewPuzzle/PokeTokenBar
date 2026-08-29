using System.Globalization;

namespace PokeTokenBar.Windows.App.Formatting;

public static class UsageValueFormatter
{
    public static string Compact(long value)
    {
        var magnitude = value < 0 ? -(double)value : value;
        var sign = value < 0 ? "-" : string.Empty;

        return magnitude switch
        {
            < 1_000 => value.ToString(CultureInfo.InvariantCulture),
            < 1_000_000 => sign + Trim(magnitude / 1_000, 1) + "K",
            < 1_000_000_000 => sign + Trim(magnitude / 1_000_000, 1) + "M",
            _ => sign + Trim(magnitude / 1_000_000_000, 2) + "B",
        };
    }

    public static string Grouped(long value, CultureInfo? culture = null) =>
        value.ToString("N0", culture ?? CultureInfo.CurrentCulture);

    public static string Cost(double usd) =>
        string.Create(CultureInfo.InvariantCulture, $"${usd:F2}");

    public static string CompactCost(double usd) => usd switch
    {
        < 100 => string.Create(CultureInfo.InvariantCulture, $"${usd:F1}"),
        < 10_000 => string.Create(CultureInfo.InvariantCulture, $"${usd:F0}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"${usd / 1_000:F1}K"),
    };

    private static string Trim(double value, int decimals) =>
        value.ToString($"F{decimals}", CultureInfo.InvariantCulture)
            .TrimEnd('0')
            .TrimEnd('.');
}
