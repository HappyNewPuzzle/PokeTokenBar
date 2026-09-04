namespace PokeTokenBar.Windows.Core;

public enum UpdateCheckStatus
{
    Idle,
    Checking,
    UpToDate,
    UpdateAvailable,
    Failed,
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersion,
    string? LatestVersion = null,
    Uri? ReleaseUri = null,
    string? ReleaseNotes = null,
    string? ReleaseName = null,
    DateTimeOffset? PublishedAt = null);

public interface IUpdateChecker
{
    string CurrentVersion { get; }

    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

public static class ReleaseVersion
{
    private const string WindowsTagPrefix = "windows-v";

    public static string? Canonicalize(string? value)
    {
        return TryParseSemantic(value, out var version, out _) ? version.ToString(3) : null;
    }

    public static bool TryParseWindowsTag(string? tag, out string version)
    {
        version = "";
        if (tag is null || !tag.StartsWith(WindowsTagPrefix, StringComparison.Ordinal)) return false;
        var value = tag[WindowsTagPrefix.Length..];
        if (!TryParse(value, out var parsed)) return false;
        version = parsed.ToString(3);
        return true;
    }

    public static bool IsNewer(string candidate, string current)
    {
        if (!TryParseSemantic(candidate, out var candidateVersion, out var candidatePrerelease) ||
            !TryParseSemantic(current, out var currentVersion, out var currentPrerelease))
        {
            return false;
        }

        var comparison = candidateVersion.CompareTo(currentVersion);
        return comparison > 0 || comparison == 0 && currentPrerelease && !candidatePrerelease;
    }

    public static bool IsTrustedWindowsReleaseUri(Uri? uri, string? expectedTag = null)
    {
        const string pathPrefix = "/HappyNewPuzzle/PokeTokenBar/releases/tag/";
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.Equals(uri.IdnHost, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var tag = Uri.UnescapeDataString(uri.AbsolutePath[pathPrefix.Length..]);
        return TryParseWindowsTag(tag, out _) &&
               (expectedTag is null || string.Equals(tag, expectedTag, StringComparison.Ordinal));
    }

    private static bool TryParse(string? value, out Version version)
    {
        version = new Version();
        if (string.IsNullOrEmpty(value)) return false;
        var parts = value.Split('.');
        if (parts.Length != 3 || parts.Any(part => part.Length == 0 ||
            part.Length > 1 && part[0] == '0' || !int.TryParse(part, out _))) return false;
        return Version.TryParse(value, out version!);
    }

    private static bool TryParseSemantic(string? value, out Version version, out bool prerelease)
    {
        prerelease = false;
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return TryParse(value, out version);
        var metadata = value.IndexOf('+');
        if (metadata >= 0) value = value[..metadata];
        var suffix = value.IndexOf('-');
        prerelease = suffix >= 0;
        if (suffix >= 0) value = value[..suffix];
        return TryParse(value, out version);
    }
}
