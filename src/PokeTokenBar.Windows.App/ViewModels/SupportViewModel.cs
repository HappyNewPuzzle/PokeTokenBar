using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using PokeTokenBar.Windows.App.Commands;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.App.ViewModels;

public sealed class SupportViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IUpdateChecker _updateChecker;
    private readonly StateTransferService _transfer;
    private readonly SettingsViewModel _settings;
    private readonly UsageViewModel _usage;
    private readonly IUserInteraction _interaction;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private DateTimeOffset? _lastChecked;
    private UpdateCheckResult? _update;
    private string _updateStatus = "Not checked";
    private bool _isChecking;
    private bool _disposed;

    internal SupportViewModel(
        IUpdateChecker updateChecker,
        StateTransferService transfer,
        SettingsViewModel settings,
        UsageViewModel usage,
        IUserInteraction interaction,
        TimeProvider? timeProvider = null)
    {
        _updateChecker = updateChecker;
        _transfer = transfer;
        _settings = settings;
        _usage = usage;
        _interaction = interaction;
        _timeProvider = timeProvider ?? TimeProvider.System;
        CheckForUpdatesCommand = new AsyncCommand(
            token => CheckAsync(TimeSpan.Zero, true, token), onException: ShowError);
        OpenUpdateCommand = new AsyncCommand(token =>
        {
            if (_update?.ReleaseUri is { } uri) _interaction.OpenUri(uri);
            return Task.CompletedTask;
        }, () => HasUpdate, ShowError);
        SkipUpdateCommand = new AsyncCommand(token =>
        {
            _settings.SkipUpdateVersion(_update?.LatestVersion);
            _update = null;
            UpdateStatus = "This version will not be shown again.";
            RaiseUpdateState();
            return Task.CompletedTask;
        }, () => HasUpdate, ShowError);
        ExportCommand = new AsyncCommand(ExportAsync, onException: ShowError);
        ImportCommand = new AsyncCommand(ImportAsync, onException: ShowError);
        CopyDiagnosticsCommand = new AsyncCommand(token =>
        {
            _interaction.CopyText(DiagnosticsReport.Create(CurrentVersion, _settings, _usage));
            UpdateStatus = "Diagnostics copied.";
            return Task.CompletedTask;
        }, onException: ShowError);
        _settings.PropertyChanged += OnSettingsChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AppName => "PokeTokenBar";
    public string CurrentVersion => _updateChecker.CurrentVersion;
    public string VersionText => $"Version {CurrentVersion}";
    public bool IsChecking { get => _isChecking; private set => SetField(ref _isChecking, value); }
    public string UpdateStatus { get => _updateStatus; private set => SetField(ref _updateStatus, value); }
    public bool HasUpdate => _update?.Status == UpdateCheckStatus.Available;
    public bool ShowUpdateBanner => HasUpdate && _settings.UpdateNotificationsEnabled;
    public string UpdateBannerText => HasUpdate ? $"PokeTokenBar {_update!.LatestVersion} is available." : "";

    public AsyncCommand CheckForUpdatesCommand { get; }
    public AsyncCommand OpenUpdateCommand { get; }
    public AsyncCommand SkipUpdateCommand { get; }
    public AsyncCommand ExportCommand { get; }
    public AsyncCommand ImportCommand { get; }
    public AsyncCommand CopyDiagnosticsCommand { get; }

    internal async Task CheckAsync(
        TimeSpan minimumInterval,
        bool manual = false,
        CancellationToken cancellationToken = default)
    {
        if (_lastChecked is { } last && _timeProvider.GetUtcNow() - last < minimumInterval) return;
        if (!await _checkGate.WaitAsync(0, cancellationToken)) return;
        try
        {
            _lastChecked = _timeProvider.GetUtcNow();
            IsChecking = true;
            var result = await _updateChecker.CheckAsync(cancellationToken);
            _update = result.Status == UpdateCheckStatus.Available &&
                      !string.Equals(result.LatestVersion, _settings.SkippedUpdateVersion, StringComparison.Ordinal)
                ? result : null;
            UpdateStatus = result.Status switch
            {
                UpdateCheckStatus.Available when _update is not null => $"Version {result.LatestVersion} is available.",
                UpdateCheckStatus.Failed => "Update check failed. Try again later.",
                _ => $"PokeTokenBar {CurrentVersion} is up to date.",
            };
            if (!manual && result.Status == UpdateCheckStatus.Failed) UpdateStatus = "Not checked";
            RaiseUpdateState();
        }
        finally
        {
            IsChecking = false;
            _checkGate.Release();
        }
    }

    private Task ExportAsync(CancellationToken cancellationToken)
    {
        var path = _interaction.ChooseExportPath(_transfer.SuggestedFileName);
        if (path is null) return Task.CompletedTask;
        _transfer.ExportTo(path);
        _interaction.ShowMessage("Export save", "The PokeTokenBar save was exported.");
        return Task.CompletedTask;
    }

    private Task ImportAsync(CancellationToken cancellationToken)
    {
        var path = _interaction.ChooseImportPath();
        if (path is null) return Task.CompletedTask;
        var data = File.ReadAllBytes(path);
        var preview = _transfer.Preview(data);
        if (!_interaction.ConfirmImport(preview, _transfer.CurrentSummary)) return Task.CompletedTask;
        _transfer.Import(data, _usage.TodayTokensByProvider, _usage.TodayDate, _usage.HasUsageData);
        _interaction.ShowMessage("Import save",
            "Import completed and a pre-import backup was saved. Restart PokeTokenBar to load the imported state.");
        return Task.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsViewModel.UpdateNotificationsEnabled))
            OnPropertyChanged(nameof(ShowUpdateBanner));
    }

    private void ShowError(Exception exception)
    {
        UpdateStatus = exception is StateTransferException transfer
            ? transfer.Message : "The operation could not be completed.";
        _interaction.ShowMessage("PokeTokenBar", UpdateStatus, true);
    }

    private void RaiseUpdateState()
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(ShowUpdateBanner));
        OnPropertyChanged(nameof(UpdateBannerText));
        OpenUpdateCommand.RaiseCanExecuteChanged();
        SkipUpdateCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.PropertyChanged -= OnSettingsChanged;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
