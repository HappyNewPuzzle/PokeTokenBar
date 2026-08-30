using Microsoft.Win32;
using System.Runtime.Versioning;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public interface IUserRunKey
{
    string? Read(string valueName);

    void Write(string valueName, string value);

    void Delete(string valueName);
}

[SupportedOSPlatform("windows")]
public sealed class CurrentUserRunKey : IUserRunKey
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void Write(string valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the current-user Run key.");
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void Delete(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsAutoStartService : IAutoStartService
{
    public const string RegistryValueName = "PokeTokenBar";

    private readonly IUserRunKey _runKey;
    private readonly string? _launchCommand;

    public WindowsAutoStartService()
        : this(new CurrentUserRunKey(), Environment.ProcessPath)
    {
    }

    public WindowsAutoStartService(IUserRunKey runKey, string? executablePath)
    {
        _runKey = runKey ?? throw new ArgumentNullException(nameof(runKey));
        _launchCommand = CreateLaunchCommand(executablePath);
    }

    public bool IsAvailable => _launchCommand is not null;

    public bool IsEnabled =>
        _launchCommand is not null &&
        string.Equals(
            _runKey.Read(RegistryValueName),
            _launchCommand,
            StringComparison.OrdinalIgnoreCase);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (_launchCommand is null)
            {
                throw new InvalidOperationException(
                    "Launch at startup is available only when PokeTokenBar runs from its executable.");
            }

            _runKey.Write(RegistryValueName, _launchCommand);
        }
        else
        {
            _runKey.Delete(RegistryValueName);
        }
    }

    public static string? CreateLaunchCommand(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(executablePath), "dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(executablePath))
        {
            return null;
        }

        return $"\"{Path.GetFullPath(executablePath)}\"";
    }
}
