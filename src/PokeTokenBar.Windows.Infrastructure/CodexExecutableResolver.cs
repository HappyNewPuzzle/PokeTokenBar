namespace PokeTokenBar.Windows.Infrastructure;

public enum CodexExecutableKind
{
    Direct,
    CommandScript,
    PowerShellScript,
}

public sealed record CodexExecutable(string Path, CodexExecutableKind Kind);

public sealed class CodexExecutableResolver
{
    private static readonly string[] CandidateNames =
        ["codex.exe", "codex.cmd", "codex.ps1", "codex"];

    private readonly string? _path;
    private readonly string? _userProfile;
    private readonly string? _appData;
    private readonly string _baseDirectory;

    public CodexExecutableResolver(
        string? path = null,
        string? userProfile = null,
        string? appData = null,
        string? baseDirectory = null)
    {
        _path = path ?? Environment.GetEnvironmentVariable("PATH");
        _userProfile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _appData = appData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
    }

    public CodexExecutable? Resolve()
    {
        foreach (var directory in CandidateDirectories())
        {
            foreach (var name in CandidateNames)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return new CodexExecutable(
                        Path.GetFullPath(candidate),
                        KindFor(candidate));
                }
            }
        }

        return null;
    }

    private IEnumerable<string> CandidateDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in new[]
                 {
                     _baseDirectory,
                     NonEmptyCombine(_userProfile, ".codex", "bin"),
                     NonEmptyCombine(_appData, "npm"),
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        foreach (var item in (_path ?? string.Empty).Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = item.Trim('"');
            if (directory.Length > 0 && seen.Add(directory))
            {
                yield return directory;
            }
        }
    }

    private static string? NonEmptyCombine(string? root, params string[] parts) =>
        string.IsNullOrWhiteSpace(root) ? null : Path.Combine([root, .. parts]);

    private static CodexExecutableKind KindFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cmd" or ".bat" => CodexExecutableKind.CommandScript,
            ".ps1" => CodexExecutableKind.PowerShellScript,
            _ => CodexExecutableKind.Direct,
        };
}
