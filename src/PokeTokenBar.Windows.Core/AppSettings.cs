namespace PokeTokenBar.Windows.Core;

/// <summary>Floating window origin stored in WPF device-independent pixels (DIPs).</summary>
public sealed record FloatingPetPosition(double Left, double Top);

public sealed record AppSettings(
    bool FloatingPetEnabled = false,
    FloatingPetPosition? FloatingPetPosition = null,
    bool LaunchAtStartup = false)
{
    public static AppSettings Default { get; } = new();
}
