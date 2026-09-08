namespace PokeTokenBar.Windows.App.ViewModels;

public sealed record ProviderStatusRow(
    string DisplayName,
    string StatusText,
    string? AuthStatusText,
    string RootStatusText)
{
    public string DetailsText => string.Join(
        " · ", new[] { AuthStatusText, RootStatusText }
            .Where(static value => !string.IsNullOrWhiteSpace(value)));
}
