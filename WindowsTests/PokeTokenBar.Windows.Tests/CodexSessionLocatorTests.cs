using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexSessionLocatorTests : IDisposable
{
    private readonly string _temporaryHome;
    private readonly string _sessionsRoot;
    private readonly string _archivedSessionsRoot;

    public CodexSessionLocatorTests()
    {
        _temporaryHome = Path.Combine(
            Path.GetTempPath(),
            $"PokeTokenBar-CodexSessionLocatorTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temporaryHome);

        var roots = CodexSessionLocator.GetDefaultRoots(_temporaryHome);
        _sessionsRoot = roots[0];
        _archivedSessionsRoot = roots[1];
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryHome))
        {
            Directory.Delete(_temporaryHome, recursive: true);
        }
    }

    [Fact]
    public void FindJsonlFiles_FindsFileDirectlyUnderSessions()
    {
        var expected = CreateFile(_sessionsRoot, "rollout.jsonl");

        var files = CodexSessionLocator.FindJsonlFiles([_sessionsRoot]);

        Assert.Equal([expected], files);
    }

    [Fact]
    public void FindJsonlFiles_RecursivelyFindsFilesInMultipleSubdirectories()
    {
        var first = CreateFile(_sessionsRoot, Path.Combine("2026", "07", "first.jsonl"));
        var second = CreateFile(_sessionsRoot, Path.Combine("2026", "08", "nested", "second.jsonl"));

        var files = CodexSessionLocator.FindJsonlFiles([_sessionsRoot]);

        Assert.Equal(SortPaths(first, second), files);
    }

    [Fact]
    public void FindJsonlFiles_FindsFilesUnderArchivedSessions()
    {
        var expected = CreateFile(_archivedSessionsRoot, "archived.jsonl");

        var files = CodexSessionLocator.FindJsonlFiles([_archivedSessionsRoot]);

        Assert.Equal([expected], files);
    }

    [Fact]
    public void FindJsonlFiles_CombinesSessionsAndArchivedSessions()
    {
        var active = CreateFile(_sessionsRoot, "active.jsonl");
        var archived = CreateFile(_archivedSessionsRoot, "archived.jsonl");

        var files = CodexSessionLocator.FindJsonlFiles(
            [_sessionsRoot, _archivedSessionsRoot]);

        Assert.Equal(SortPaths(active, archived), files);
    }

    [Fact]
    public void FindJsonlFiles_ExcludesOtherExtensionsAndNonExactJsonlCasing()
    {
        Directory.CreateDirectory(_sessionsRoot);
        CreateFile(_sessionsRoot, "notes.txt");
        CreateFile(_sessionsRoot, "events.log");
        CreateFile(_sessionsRoot, "upper.JSONL");
        var expected = CreateFile(_sessionsRoot, "included.jsonl");

        var files = CodexSessionLocator.FindJsonlFiles([_sessionsRoot]);

        Assert.Equal([expected], files);
    }

    [Fact]
    public void FindJsonlFiles_IncludesJsonlWithoutRolloutFilenamePrefix()
    {
        var expected = CreateFile(_sessionsRoot, "custom-name.jsonl");

        var files = CodexSessionLocator.FindJsonlFiles([_sessionsRoot]);

        Assert.Equal([expected], files);
    }

    [Fact]
    public void FindJsonlFiles_IgnoresMissingRoot()
    {
        var missing = Path.Combine(_temporaryHome, "missing");
        var expected = CreateFile(_sessionsRoot, "existing.jsonl");

        var files = CodexSessionLocator.FindJsonlFiles([missing, _sessionsRoot]);

        Assert.Equal([expected], files);
    }

    [Fact]
    public void FindJsonlFiles_NoFiles_ReturnsEmptyResult()
    {
        Directory.CreateDirectory(_sessionsRoot);

        var files = CodexSessionLocator.FindJsonlFiles(
            [_sessionsRoot, _archivedSessionsRoot]);

        Assert.Empty(files);
    }

    [Fact]
    public void FindJsonlFiles_DeduplicatesDuplicateAndNestedRoots()
    {
        var nestedRoot = Path.Combine(_sessionsRoot, "nested");
        var expected = CreateFile(nestedRoot, "single.jsonl");

        var files = CodexSessionLocator.FindJsonlFiles(
            [_sessionsRoot, _sessionsRoot, nestedRoot]);

        Assert.Equal([expected], files);
    }

    [Fact]
    public void GetDefaultRoots_CombinesInjectedHomeWithBothCodexDirectories()
    {
        var roots = CodexSessionLocator.GetDefaultRoots(_temporaryHome);

        Assert.Equal(
            Path.Combine(_temporaryHome, ".codex", "sessions"),
            roots[0]);
        Assert.Equal(
            Path.Combine(_temporaryHome, ".codex", "archived_sessions"),
            roots[1]);
    }

    [Fact]
    public void FindJsonlFiles_ReturnsOrdinalIgnoreCaseSortedPaths()
    {
        var third = CreateFile(_sessionsRoot, "z.jsonl");
        var first = CreateFile(_sessionsRoot, "A.jsonl");
        var second = CreateFile(_sessionsRoot, "m.jsonl");

        var files = CodexSessionLocator.FindJsonlFiles([_sessionsRoot]);

        Assert.Equal([first, second, third], files);
    }

    private static string CreateFile(string root, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private static string[] SortPaths(params string[] paths) =>
        paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
}
