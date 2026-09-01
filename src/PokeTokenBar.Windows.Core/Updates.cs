namespace PokeTokenBar.Windows.Core;

public enum UpdateCheckStatus
{
    UpToDate,
    Available,
    Failed,
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersion,
    string? LatestVersion = null,
    Uri? ReleaseUri = null,
    string? ReleaseNotes = null);

public interface IUpdateChecker
{
    string CurrentVersion { get; }

    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

public static class ReleaseVersion
{
    public static bool IsNewer(string candidate, string current)
    {
        if (!TryParse(candidate, out var candidateVersion, out var candidatePrerelease) ||
            !TryParse(current, out var currentVersion, out var currentPrerelease))
        {
            return false;
        }

        var comparison = candidateVersion.CompareTo(currentVersion);
        return comparison > 0 || comparison == 0 && currentPrerelease && !candidatePrerelease;
    }

    private static bool TryParse(string value, out Version version, out bool prerelease)
    {
        value = value.Trim().TrimStart('v', 'V');
        var separator = value.IndexOfAny(['-', '+']);
        prerelease = separator >= 0 && value[separator] == '-';
        if (separator >= 0) value = value[..separator];
        return Version.TryParse(value, out version!);
    }
}
