namespace PokeTokenBar.Windows.Core;

public enum ProviderRuntimeStatus
{
    Ready,
    NoSessions,
    LocalDataOnly,
    Error,
    Stale,
}

public enum ProviderAuthStatus
{
    NotApplicable,
    Authenticated,
    QuotaUnavailable,
}

public sealed record ProviderStatusSnapshot(
    string ProviderId,
    string DisplayName,
    ProviderRuntimeStatus RuntimeStatus,
    ProviderAuthStatus AuthStatus);
