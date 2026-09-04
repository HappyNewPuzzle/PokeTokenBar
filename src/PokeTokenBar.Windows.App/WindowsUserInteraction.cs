using System.Diagnostics;
using System.Windows;
using PokeTokenBar.Windows.Core;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;

namespace PokeTokenBar.Windows.App;

internal interface IUserInteraction
{
    string? ChooseExportPath(string suggestedFileName);
    string? ChooseImportPath();
    bool ConfirmImport(StateTransferPreview incoming, StateTransferSummary current);
    void ShowMessage(string title, string message, bool error = false);
    void CopyText(string text);
    void OpenUri(Uri uri);
}

internal sealed class WindowsUserInteraction : IUserInteraction
{
    private readonly LocalizationService _text;

    public WindowsUserInteraction(LocalizationService text) =>
        _text = text ?? throw new ArgumentNullException(nameof(text));

    public string? ChooseExportPath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = _text.ExportDialogTitle,
            FileName = suggestedFileName,
            DefaultExt = ".json",
            Filter = _text.SaveFileFilter,
            AddExtension = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseImportPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = _text.ImportDialogTitle,
            DefaultExt = ".json",
            Filter = _text.SaveFileFilter,
            Multiselect = false,
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public bool ConfirmImport(StateTransferPreview incoming, StateTransferSummary current) =>
        WpfMessageBox.Show(
            _text.ImportConfirm(incoming, current),
            _text.ImportDialogTitle,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;

    public void ShowMessage(string title, string message, bool error = false) =>
        WpfMessageBox.Show(message, title, MessageBoxButton.OK,
            error ? MessageBoxImage.Warning : MessageBoxImage.Information);

    public void CopyText(string text) => WpfClipboard.SetText(text);

    public void OpenUri(Uri uri)
    {
        if (!ReleaseVersion.IsTrustedWindowsReleaseUri(uri)) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
