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
    public string? ChooseExportPath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export PokeTokenBar save",
            FileName = suggestedFileName,
            DefaultExt = ".json",
            Filter = "PokeTokenBar save (*.json)|*.json",
            AddExtension = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseImportPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import PokeTokenBar save",
            DefaultExt = ".json",
            Filter = "PokeTokenBar save (*.json)|*.json",
            Multiselect = false,
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public bool ConfirmImport(StateTransferPreview incoming, StateTransferSummary current) =>
        WpfMessageBox.Show(
            $"Replace the current progress?\n\nIncoming: {incoming.State.DexCount} Dex, {incoming.State.LifetimeTokens:N0} tokens\nCurrent: {current.DexCount} Dex, {current.LifetimeTokens:N0} tokens\n\nA local backup will be created first.",
            "Import PokeTokenBar save",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;

    public void ShowMessage(string title, string message, bool error = false) =>
        WpfMessageBox.Show(message, title, MessageBoxButton.OK,
            error ? MessageBoxImage.Warning : MessageBoxImage.Information);

    public void CopyText(string text) => WpfClipboard.SetText(text);

    public void OpenUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
