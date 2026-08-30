namespace PokeTokenBar.Windows.Core;

/// <summary>Floating window origin stored in WPF device-independent pixels (DIPs).</summary>
public sealed record FloatingPetPosition(double Left, double Top);

public enum RefreshIntervalMode
{
    Manual = 0,
    OneMinute = 60,
    TwoMinutes = 120,
    FiveMinutes = 300,
    FifteenMinutes = 900,
}

public sealed record AppSettings(
    bool FloatingPetEnabled = false,
    FloatingPetPosition? FloatingPetPosition = null,
    bool LaunchAtStartup = false,
    RefreshIntervalMode RefreshInterval = RefreshIntervalMode.TwoMinutes)
{
    public static AppSettings Default { get; } = new();
}
