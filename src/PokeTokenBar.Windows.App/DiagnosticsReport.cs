using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.App;

internal static class DiagnosticsReport
{
    internal sealed record FileState(bool Exists, long? Size, DateTimeOffset? LastWriteTime);

    public static string Create(
        string version,
        SettingsViewModel settings,
        UsageViewModel usage,
        UpdateCheckStatus updateStatus = UpdateCheckStatus.Idle,
        Func<string, FileState>? fileProbe = null)
    {
        var builder = new StringBuilder()
            .AppendLine("PokeTokenBar diagnostics")
            .AppendLine($"appVersion={version}")
            .AppendLine($"fileVersion={typeof(App).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "unknown"}")
            .AppendLine($"windowsVersion={Environment.OSVersion.Version}")
            .AppendLine($"runtime={RuntimeInformation.FrameworkDescription}")
            .AppendLine($"processArchitecture={RuntimeInformation.ProcessArchitecture}")
            .AppendLine($"culture={CultureInfo.CurrentCulture.Name}")
            .AppendLine($"language={settings.Localization.Language}")
            .AppendLine($"updateStatus={updateStatus}")
            .AppendLine("settingsFormat=1")
            .AppendLine("companionStateFormat=compatible")
            .AppendLine("dataPath=%LOCALAPPDATA%\\PokeTokenBar")
            .AppendLine("spriteCache=%LOCALAPPDATA%\\PokeTokenBar\\sprites")
            .AppendLine($"usageCache={usage.UsageCacheStatus.ToString().ToLowerInvariant()}")
            .AppendLine($"refreshStatus={(usage.LastUpdated is null ? "never" : usage.HasRefreshError ? "error" : "ok")}");

        try
        {
            var active = usage.Providers.Select(provider => provider.ProviderId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var id in usage.RegisteredProviderIds)
            {
                var status = usage.ProviderStatuses.FirstOrDefault(provider => provider.ProviderId == id);
                builder.AppendLine($"provider.{id}.available={active.Contains(id).ToString().ToLowerInvariant()}")
                    .AppendLine($"provider.{id}.status={status?.RuntimeStatus.ToString() ?? "Unknown"}")
                    .AppendLine($"provider.{id}.auth={status?.AuthStatus.ToString() ?? "Unknown"}")
                    .AppendLine($"provider.{id}.customRootConfigured={settings.HasConfiguredCustomRoot(id).ToString().ToLowerInvariant()}");
            }
        }
        catch (Exception exception)
        {
            ReliabilityEventLog.RecordError("diagnostics-provider", exception);
            builder.AppendLine("providers=unavailable");
        }

        try
        {
            var root = PokeTokenBarDataPaths.Root;
            AppendFile(builder, "settings", Path.Combine(root, "settings.json"), fileProbe);
            AppendFile(builder, "usageCache", Path.Combine(root, "usage-cache.json"), fileProbe, includeAge: true);
            AppendFile(builder, "companion", Path.Combine(root, "companion-state.json"), fileProbe);
            AppendFile(builder, "baseIndex", Path.Combine(root, "base-index.json"), fileProbe, includeAge: true);
        }
        catch (Exception exception)
        {
            ReliabilityEventLog.RecordError("diagnostics-persistence", exception);
            builder.AppendLine("persistenceFiles=unavailable");
        }

        foreach (var entry in ReliabilityEventLog.Snapshot())
        {
            builder.AppendLine(entry.Kind == ReliabilityEventKind.Recovery
                ? $"recovery={entry.Component}:{entry.Summary}"
                : $"recentError={entry.Component}:{entry.Summary}");
        }
        return builder.ToString();
    }

    private static void AppendFile(
        StringBuilder builder,
        string name,
        string path,
        Func<string, FileState>? probe,
        bool includeAge = false)
    {
        try
        {
            var state = (probe ?? Probe)(path);
            builder.AppendLine($"persistence.{name}.exists={state.Exists.ToString().ToLowerInvariant()}")
                .AppendLine($"persistence.{name}.size={state.Size?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}");
            if (includeAge)
            {
                var age = state.LastWriteTime is { } modified
                    ? Math.Max(0, (DateTimeOffset.UtcNow - modified).TotalMinutes)
                    : (double?)null;
                builder.AppendLine($"persistence.{name}.ageMinutes={age?.ToString("F0", CultureInfo.InvariantCulture) ?? "unavailable"}");
            }
        }
        catch (Exception exception)
        {
            ReliabilityEventLog.RecordError($"diagnostics-{name}", exception);
            builder.AppendLine($"persistence.{name}=unavailable");
        }
    }

    private static FileState Probe(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        return info.Exists
            ? new(true, info.Length, info.LastWriteTimeUtc)
            : new(false, null, null);
    }
}
