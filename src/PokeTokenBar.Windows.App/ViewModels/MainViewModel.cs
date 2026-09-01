namespace PokeTokenBar.Windows.App.ViewModels;

public sealed class MainViewModel : IDisposable
{
    private bool _disposed;

    public MainViewModel(
        UsageViewModel usage,
        CompanionViewModel companion,
        EconomyViewModel economy,
        SettingsViewModel settings,
        SupportViewModel? support = null)
    {
        Usage = usage ?? throw new ArgumentNullException(nameof(usage));
        Companion = companion ?? throw new ArgumentNullException(nameof(companion));
        Economy = economy ?? throw new ArgumentNullException(nameof(economy));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Support = support;
    }

    public UsageViewModel Usage { get; }

    public CompanionViewModel Companion { get; }

    public EconomyViewModel Economy { get; }

    public SettingsViewModel Settings { get; }

    public SupportViewModel? Support { get; }

    public LocalizationService Texts => Settings.Localization;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Support?.Dispose();
        Companion.Dispose();
    }
}
