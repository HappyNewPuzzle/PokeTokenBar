namespace PokeTokenBar.Windows.Core;

public interface IAutoStartService
{
    bool IsAvailable { get; }

    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}
