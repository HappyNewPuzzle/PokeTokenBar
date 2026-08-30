namespace PokeTokenBar.Windows.App.ViewModels;

public sealed record OfficialLimitRow(
    string Label,
    int RemainingPercent,
    string RemainingText,
    string? ResetText);
