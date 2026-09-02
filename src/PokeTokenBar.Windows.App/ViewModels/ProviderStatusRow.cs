namespace PokeTokenBar.Windows.App.ViewModels;

public sealed record ProviderStatusRow(
    string DisplayName,
    string StatusText,
    string AuthStatusText,
    string RootStatusText);
