using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed record CodexTokenCountParseResult(
    DateTimeOffset Timestamp,
    CodexUsageEntry Entry,
    CodexUsageVector LastUsageVector,
    CodexUsageVector? CumulativeUsageVector);
