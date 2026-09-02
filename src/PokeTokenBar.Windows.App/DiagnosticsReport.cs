using System.Runtime.InteropServices;
using System.Text;
using PokeTokenBar.Windows.App.ViewModels;

namespace PokeTokenBar.Windows.App;

internal static class DiagnosticsReport
{
    public static string Create(
        string version,
        SettingsViewModel settings,
        UsageViewModel usage)
    {
        var builder = new StringBuilder()
            .AppendLine("PokeTokenBar diagnostics")
            .AppendLine($"appVersion={version}")
            .AppendLine($"windowsVersion={Environment.OSVersion.Version}")
            .AppendLine($"runtime={RuntimeInformation.FrameworkDescription}")
            .AppendLine($"processArchitecture={RuntimeInformation.ProcessArchitecture}")
            .AppendLine("settingsFormat=1")
            .AppendLine("companionStateFormat=compatible")
            .AppendLine("dataPath=%LOCALAPPDATA%\\PokeTokenBar")
            .AppendLine("spriteCache=%LOCALAPPDATA%\\PokeTokenBar\\sprites")
            .AppendLine($"usageCache={usage.UsageCacheStatus.ToString().ToLowerInvariant()}")
            .AppendLine($"refreshStatus={(usage.LastUpdated is null ? "never" : usage.HasRefreshError ? "error" : "ok")}");
        var active = usage.Providers.Select(provider => provider.ProviderId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in usage.RegisteredProviderIds)
        {
            builder.AppendLine($"provider.{id}.available={active.Contains(id).ToString().ToLowerInvariant()}");
            builder.AppendLine($"provider.{id}.customRootConfigured={settings.HasConfiguredCustomRoot(id).ToString().ToLowerInvariant()}");
        }
        return builder.ToString();
    }
}
