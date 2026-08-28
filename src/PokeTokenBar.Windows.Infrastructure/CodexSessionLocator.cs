namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexSessionLocator
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.Hidden,
    };

    public static IReadOnlyList<string> GetDefaultRoots()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException("The current user profile directory is unavailable.");
        }

        return GetDefaultRoots(userProfile);
    }

    public static IReadOnlyList<string> GetDefaultRoots(string homeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);

        var codexDirectory = Path.Combine(Path.GetFullPath(homeDirectory), ".codex");
        return
        [
            Path.Combine(codexDirectory, "sessions"),
            Path.Combine(codexDirectory, "archived_sessions"),
        ];
    }

    public static IReadOnlyList<string> FindDefaultJsonlFiles() =>
        FindJsonlFiles(GetDefaultRoots());

    public static IReadOnlyList<string> FindJsonlFiles(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var normalizedRoots = new HashSet<string>(PathComparer);
        var files = new HashSet<string>(PathComparer);

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var normalizedRoot = Path.GetFullPath(root);
            if (!normalizedRoots.Add(normalizedRoot) || !Directory.Exists(normalizedRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(
                         normalizedRoot,
                         "*",
                         EnumerationOptions))
            {
                if (string.Equals(Path.GetExtension(file), ".jsonl", StringComparison.Ordinal))
                {
                    files.Add(Path.GetFullPath(file));
                }
            }
        }

        return files.Order(PathComparer).ToArray();
    }
}
