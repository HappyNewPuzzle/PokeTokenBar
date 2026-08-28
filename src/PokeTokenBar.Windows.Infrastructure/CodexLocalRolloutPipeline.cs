namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexLocalRolloutPipeline
{
    private static readonly StringComparer DiscoveryPathComparer =
        StringComparer.OrdinalIgnoreCase;

    public static CodexLocalRolloutPipelineResult LoadDefault(
        DateTimeOffset modifiedSince) =>
        LoadFromRoots(CodexSessionLocator.GetDefaultRoots(), modifiedSince);

    public static CodexLocalRolloutPipelineResult LoadFromRoots(
        IEnumerable<string> roots,
        DateTimeOffset modifiedSince)
    {
        ArgumentNullException.ThrowIfNull(roots);

        return LoadFiles(
            CodexSessionLocator.FindJsonlFiles(roots),
            modifiedSince);
    }

    public static CodexLocalRolloutPipelineResult LoadFiles(
        IEnumerable<string> filePaths,
        DateTimeOffset modifiedSince)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var discoveredFiles = DiscoverExistingFiles(filePaths);
        var availableRollouts = discoveredFiles
            .Select(static file => LoadRollout(file.FilePath))
            .ToArray();
        var primaryPaths = discoveredFiles
            .Where(file => file.LastWriteTimeUtc >= modifiedSince.UtcDateTime)
            .Select(static file => file.FilePath)
            .ToHashSet(DiscoveryPathComparer);
        var primaryRollouts = availableRollouts
            .Where(rollout => primaryPaths.Contains(rollout.FilePath))
            .ToArray();

        var expansion = CodexForkDependencyExpander.Expand(
            primaryRollouts,
            availableRollouts);
        var resolutionResults = CodexInMemoryForkResolver.Resolve(
            expansion.ResolutionRollouts);
        var resolvedPrimaryRollouts = resolutionResults
            .Where(result => primaryPaths.Contains(result.FilePath))
            .ToArray();

        return new CodexLocalRolloutPipelineResult(
            expansion,
            resolutionResults,
            resolvedPrimaryRollouts);
    }

    private static IReadOnlyList<DiscoveredRolloutFile> DiscoverExistingFiles(
        IEnumerable<string> filePaths)
    {
        var normalizedPaths = new HashSet<string>(DiscoveryPathComparer);
        foreach (var filePath in filePaths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            normalizedPaths.Add(Path.GetFullPath(filePath));
        }

        var discoveredFiles = new List<DiscoveredRolloutFile>(normalizedPaths.Count);
        foreach (var filePath in normalizedPaths.Order(DiscoveryPathComparer))
        {
            try
            {
                var file = new FileInfo(filePath);
                file.Refresh();
                if (!file.Exists)
                {
                    continue;
                }

                discoveredFiles.Add(new DiscoveredRolloutFile(
                    filePath,
                    file.LastWriteTimeUtc));
            }
            catch (IOException)
            {
                // Swift skips files whose resource metadata cannot be read.
            }
            catch (UnauthorizedAccessException)
            {
                // Swift skips files whose resource metadata cannot be read.
            }
        }

        return discoveredFiles;
    }

    private static CodexEpochRollout LoadRollout(string filePath)
    {
        CodexParsedRollout parsedRollout;
        try
        {
            parsedRollout = CodexRolloutReader.Read(filePath);
        }
        catch (IOException)
        {
            parsedRollout = EmptyRollout(filePath);
        }
        catch (UnauthorizedAccessException)
        {
            parsedRollout = EmptyRollout(filePath);
        }

        var filteredRollout = CodexConsecutiveDuplicateFilter.Filter(parsedRollout);
        return CodexCumulativeEpochAssigner.Assign(filteredRollout);
    }

    private static CodexParsedRollout EmptyRollout(string filePath) =>
        new(
            filePath,
            RolloutMetadata: null,
            Array.Empty<CodexRolloutTokenEvent>());

    private sealed record DiscoveredRolloutFile(
        string FilePath,
        DateTime LastWriteTimeUtc);
}
