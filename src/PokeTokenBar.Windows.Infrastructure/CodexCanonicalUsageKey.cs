using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public readonly record struct CodexCanonicalUsageKey(
    string OwnerSessionId,
    int Epoch,
    CodexUsageVector CumulativeUsageVector,
    CodexUsageVector LastUsageVector)
{
    public const string ProviderName = "codex";

    public string Provider => ProviderName;
}
