using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class CodexLocalRolloutPipelineTests : IDisposable
{
    private static readonly DateTimeOffset Cutoff =
        new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OldFileTime = Cutoff.AddDays(-1);
    private static readonly DateTimeOffset RecentFileTime = Cutoff.AddDays(1);

    private readonly string _temporaryHome;
    private readonly string _sessionsRoot;
    private readonly string _archivedRoot;

    public CodexLocalRolloutPipelineTests()
    {
        _temporaryHome = Path.Combine(
            Path.GetTempPath(),
            $"PokeTokenBar-CodexLocalRolloutPipelineTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temporaryHome);

        var roots = CodexSessionLocator.GetDefaultRoots(_temporaryHome);
        _sessionsRoot = roots[0];
        _archivedRoot = roots[1];
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryHome))
        {
            Directory.Delete(_temporaryHome, recursive: true);
        }
    }

    [Fact]
    public void LoadFromRoots_CustomRootDiscoversJsonlFile()
    {
        var path = WriteRollout(
            _sessionsRoot,
            "custom.jsonl",
            RecentFileTime,
            SessionMeta("session"),
            StateLine("2026-07-30T01:00:01.000Z", 100, 10, 100, 10));

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        Assert.Equal([path], Paths(result.PrimaryRollouts));
        Assert.Single(result.ResolvedPrimaryRollouts);
    }

    [Fact]
    public void LoadFromRoots_RecursivelyDiscoversNestedJsonlFiles()
    {
        var first = WriteRollout(
            _sessionsRoot,
            Path.Combine("2026", "07", "first.jsonl"),
            RecentFileTime,
            SessionMeta("first"));
        var second = WriteRollout(
            _sessionsRoot,
            Path.Combine("2026", "08", "nested", "second.jsonl"),
            RecentFileTime,
            SessionMeta("second"));

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        Assert.Equal(OrderPaths(first, second), Paths(result.PrimaryRollouts));
    }

    [Fact]
    public void LoadFromRoots_CombinesSessionsAndArchivedRoots()
    {
        var active = WriteRollout(
            _sessionsRoot,
            "active.jsonl",
            RecentFileTime,
            SessionMeta("active"));
        var archived = WriteRollout(
            _archivedRoot,
            "archived.jsonl",
            RecentFileTime,
            SessionMeta("archived"));

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot, _archivedRoot],
            Cutoff);

        Assert.Equal(OrderPaths(active, archived), Paths(result.PrimaryRollouts));
    }

    [Fact]
    public void LoadFromRoots_MalformedLineDoesNotDiscardValidTokenEvent()
    {
        WriteRollout(
            _sessionsRoot,
            "malformed-middle.jsonl",
            RecentFileTime,
            SessionMeta("session"),
            "{not-json",
            StateLine("2026-07-30T01:00:01.000Z", 100, 10, 100, 10));

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        Assert.Single(Assert.Single(result.PrimaryRollouts).TokenEvents);
    }

    [Fact]
    public void LoadFromRoots_TokenlessPrimaryRolloutIsPreserved()
    {
        var path = WriteRollout(
            _sessionsRoot,
            "tokenless.jsonl",
            RecentFileTime,
            SessionMeta("tokenless"));

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        var rollout = Assert.Single(result.PrimaryRollouts);
        Assert.Equal(path, rollout.FilePath);
        Assert.Equal("tokenless", rollout.RolloutMetadata?.SessionId);
        Assert.Empty(rollout.TokenEvents);
        Assert.Empty(Assert.Single(result.ResolvedPrimaryRollouts).ResolvedRollout.TokenEvents);
    }

    [Fact]
    public void LoadFromRoots_PrimaryParentAndChildResolveStructurally()
    {
        var parentPath = WriteParent(
            _sessionsRoot,
            "parent.jsonl",
            RecentFileTime,
            "parent",
            StateA,
            StateB);
        var childPath = WriteFork(
            _sessionsRoot,
            "child.jsonl",
            RecentFileTime,
            "child",
            "parent",
            StateA,
            StateB,
            StateC);

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);
        var child = ResolvedByPath(result, childPath);

        Assert.Empty(result.DependencyRollouts);
        Assert.Equal(parentPath, child.SelectedParent?.FilePath);
        Assert.Equal(2, child.ReplayCount);
        Assert.Single(child.ResolvedRollout.TokenEvents);
    }

    [Fact]
    public void LoadFromRoots_AddsOldParentAsDependencyOnly()
    {
        var parentPath = WriteParent(
            _sessionsRoot,
            "old-parent.jsonl",
            OldFileTime,
            "parent",
            StateA,
            StateB);
        var childPath = WriteFork(
            _sessionsRoot,
            "recent-child.jsonl",
            RecentFileTime,
            "child",
            "parent",
            StateA,
            StateB,
            StateC);

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        Assert.Equal([childPath], Paths(result.PrimaryRollouts));
        Assert.Equal([parentPath], Paths(result.DependencyRollouts));
        Assert.Equal(parentPath, ResolvedByPath(result, childPath).SelectedParent?.FilePath);
    }

    [Fact]
    public void LoadFromRoots_DependencyResultIsExcludedFromResolvedPrimaryRollouts()
    {
        var parentPath = WriteParent(
            _sessionsRoot,
            "old-parent.jsonl",
            OldFileTime,
            "parent",
            StateA);
        var childPath = WriteFork(
            _sessionsRoot,
            "recent-child.jsonl",
            RecentFileTime,
            "child",
            "parent",
            StateA,
            StateB);

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        Assert.Contains(result.ResolutionResults, item => item.FilePath == parentPath);
        Assert.DoesNotContain(result.ResolvedPrimaryRollouts, item => item.FilePath == parentPath);
        Assert.Equal([childPath], result.ResolvedPrimaryRollouts.Select(item => item.FilePath));
    }

    [Fact]
    public void LoadFromRoots_ForkOfForkLoadsOldParentClosure()
    {
        var ancestorPath = WriteParent(
            _sessionsRoot,
            "a.jsonl",
            OldFileTime,
            "a",
            StateA);
        var parentPath = WriteFork(
            _sessionsRoot,
            "b.jsonl",
            OldFileTime,
            "b",
            "a",
            StateA,
            StateB);
        var childPath = WriteFork(
            _sessionsRoot,
            "c.jsonl",
            RecentFileTime,
            "c",
            "b",
            StateA,
            StateB,
            StateC);

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);
        var child = ResolvedByPath(result, childPath);

        Assert.Equal(OrderPaths(ancestorPath, parentPath), Paths(result.DependencyRollouts));
        Assert.Equal(2, child.ReplayCount);
        Assert.Single(child.ResolvedRollout.TokenEvents);
    }

    [Fact]
    public void LoadFromRoots_ArchivedParentCanResolveActiveChild()
    {
        var parentPath = WriteParent(
            _archivedRoot,
            "parent.jsonl",
            OldFileTime,
            "parent",
            StateA);
        var childPath = WriteFork(
            _sessionsRoot,
            "child.jsonl",
            RecentFileTime,
            "child",
            "parent",
            StateA,
            StateB);

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot, _archivedRoot],
            Cutoff);

        Assert.Equal([parentPath], Paths(result.DependencyRollouts));
        Assert.Equal(parentPath, ResolvedByPath(result, childPath).SelectedParent?.FilePath);
    }

    [Fact]
    public void LoadFromRoots_ActiveParentCanResolveArchivedChild()
    {
        var parentPath = WriteParent(
            _sessionsRoot,
            "parent.jsonl",
            OldFileTime,
            "parent",
            StateA);
        var childPath = WriteFork(
            _archivedRoot,
            "child.jsonl",
            RecentFileTime,
            "child",
            "parent",
            StateA,
            StateB);

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_archivedRoot, _sessionsRoot],
            Cutoff);

        Assert.Equal([parentPath], Paths(result.DependencyRollouts));
        Assert.Equal(parentPath, ResolvedByPath(result, childPath).SelectedParent?.FilePath);
    }

    [Fact]
    public void LoadFromRoots_MultipleOldParentCandidatesRemainAvailableForLongestMatch()
    {
        var shortParentPath = WriteParent(
            _sessionsRoot,
            "parent-short.jsonl",
            OldFileTime,
            "parent",
            StateA);
        var longParentPath = WriteParent(
            _archivedRoot,
            "parent-long.jsonl",
            OldFileTime,
            "parent",
            StateA,
            StateB);
        var childPath = WriteFork(
            _sessionsRoot,
            "child.jsonl",
            RecentFileTime,
            "child",
            "parent",
            StateA,
            StateB,
            StateC);

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot, _archivedRoot],
            Cutoff);
        var child = ResolvedByPath(result, childPath);

        Assert.Equal(
            OrderPaths(shortParentPath, longParentPath),
            Paths(result.DependencyRollouts));
        Assert.Equal(longParentPath, child.SelectedParent?.FilePath);
        Assert.Equal(2, child.ReplayCount);
    }

    [Fact]
    public void LoadFromRoots_ReturnsOrdinalFilePathOrder()
    {
        var lower = WriteRollout(
            _sessionsRoot,
            "a.jsonl",
            RecentFileTime,
            SessionMeta("lower"));
        var upper = WriteRollout(
            _sessionsRoot,
            "B.jsonl",
            RecentFileTime,
            SessionMeta("upper"));
        var last = WriteRollout(
            _sessionsRoot,
            "z.jsonl",
            RecentFileTime,
            SessionMeta("last"));

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        Assert.Equal(OrderPaths(upper, lower, last), Paths(result.PrimaryRollouts));
        Assert.Equal(OrderPaths(upper, lower, last), result.ResolvedPrimaryRollouts.Select(item => item.FilePath));
    }

    [Fact]
    public void LoadFromRoots_RootOrderDoesNotChangeResults()
    {
        WriteParent(
            _archivedRoot,
            "parent.jsonl",
            OldFileTime,
            "parent",
            StateA);
        WriteFork(
            _sessionsRoot,
            "child.jsonl",
            RecentFileTime,
            "child",
            "parent",
            StateA,
            StateB);

        var forward = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot, _archivedRoot],
            Cutoff);
        var reverse = CodexLocalRolloutPipeline.LoadFromRoots(
            [_archivedRoot, _sessionsRoot],
            Cutoff);

        Assert.Equal(Paths(forward.PrimaryRollouts), Paths(reverse.PrimaryRollouts));
        Assert.Equal(Paths(forward.DependencyRollouts), Paths(reverse.DependencyRollouts));
        Assert.Equal(
            forward.ResolutionResults.Select(item => item.FilePath),
            reverse.ResolutionResults.Select(item => item.FilePath));
        Assert.Equal(
            forward.ResolvedPrimaryRollouts.Select(item => item.FilePath),
            reverse.ResolvedPrimaryRollouts.Select(item => item.FilePath));
    }

    [Fact]
    public void LoadFromRoots_MissingParentUsesResolverFallback()
    {
        var childPath = WriteFork(
            _sessionsRoot,
            "child.jsonl",
            RecentFileTime,
            "child",
            "missing",
            StateAt("2026-07-30T01:00:00.010Z", 100, 10, 100, 10),
            StateAt("2026-07-30T01:00:03.000Z", 300, 30, 200, 20));

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);
        var child = ResolvedByPath(result, childPath);

        Assert.Empty(result.DependencyRollouts);
        Assert.Null(child.SelectedParent);
        Assert.Equal(1, child.ReplayCount);
        Assert.Single(child.ResolvedRollout.TokenEvents);
    }

    [Fact]
    public void LoadFromRoots_SubagentParentIsLoadedWithoutFallbackTrimming()
    {
        var parentPath = WriteParent(
            _sessionsRoot,
            "parent.jsonl",
            OldFileTime,
            "parent",
            StateA);
        var childPath = WriteFork(
            _sessionsRoot,
            "subagent.jsonl",
            RecentFileTime,
            "subagent",
            "parent",
            isSubagent: true,
            StateB,
            StateC);

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);
        var child = ResolvedByPath(result, childPath);

        Assert.Equal([parentPath], Paths(result.DependencyRollouts));
        Assert.Null(child.SelectedParent);
        Assert.Equal(0, child.ReplayCount);
        Assert.Equal(2, child.ResolvedRollout.TokenEvents.Count);
    }

    [Fact]
    public void LoadFromRoots_StructuralReplayTakesPriorityOverTimestampFallback()
    {
        var parentPath = WriteParent(
            _sessionsRoot,
            "parent.jsonl",
            OldFileTime,
            "parent",
            StateA,
            StateB);
        var childPath = WriteFork(
            _sessionsRoot,
            "child.jsonl",
            RecentFileTime,
            "child",
            "parent",
            StateAt("2026-07-30T01:00:00.010Z", 100, 10, 100, 10),
            StateAt("2026-07-30T01:00:05.000Z", 300, 30, 200, 20),
            StateAt("2026-07-30T01:00:05.010Z", 600, 60, 300, 30));

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);
        var child = ResolvedByPath(result, childPath);

        Assert.Equal(parentPath, child.SelectedParent?.FilePath);
        Assert.Equal(2, child.ReplayCount);
        Assert.Single(child.ResolvedRollout.TokenEvents);
    }

    [Fact]
    public void LoadFromRoots_FallbackTrimsOnlyLeadingRapidBurst()
    {
        var childPath = WriteFork(
            _sessionsRoot,
            "child.jsonl",
            RecentFileTime,
            "child",
            "missing",
            StateAt("2026-07-30T01:00:00.010Z", 100, 10, 100, 10),
            StateAt("2026-07-30T01:00:00.020Z", 300, 30, 200, 20),
            StateAt("2026-07-30T01:00:03.000Z", 600, 60, 300, 30));

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);
        var child = ResolvedByPath(result, childPath);

        Assert.Equal(2, child.ReplayCount);
        Assert.Equal(600, Assert.Single(child.ResolvedRollout.TokenEvents)
            .TokenEvent.TokenCount.CumulativeUsageVector?.InputTokens);
    }

    [Fact]
    public void LoadFromRoots_AppliesConsecutiveDuplicateFilterBeforeEpochAssignment()
    {
        WriteRollout(
            _sessionsRoot,
            "duplicates.jsonl",
            RecentFileTime,
            SessionMeta("session"),
            StateLine(StateA),
            StateLine(StateAt("2026-07-30T01:00:02.000Z", 100, 10, 100, 10)));

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        var tokenEvent = Assert.Single(Assert.Single(result.PrimaryRollouts).TokenEvents);
        Assert.Equal(0, tokenEvent.Epoch);
    }

    [Fact]
    public void LoadFromRoots_AssignsNewEpochWhenCumulativeUsageDecreases()
    {
        WriteRollout(
            _sessionsRoot,
            "reset.jsonl",
            RecentFileTime,
            SessionMeta("session"),
            StateLine(StateB),
            StateLine(StateAt("2026-07-30T01:00:02.000Z", 50, 5, 50, 5)));

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        Assert.Equal(
            new int?[] { 0, 1 },
            Assert.Single(result.PrimaryRollouts).TokenEvents.Select(item => item.Epoch));
    }

    [Fact]
    public void LoadFromRoots_EmptyRootsReturnEmptyResult()
    {
        var result = CodexLocalRolloutPipeline.LoadFromRoots([], Cutoff);

        Assert.Empty(result.PrimaryRollouts);
        Assert.Empty(result.DependencyRollouts);
        Assert.Empty(result.ResolutionRollouts);
        Assert.Empty(result.ResolutionResults);
        Assert.Empty(result.ResolvedPrimaryRollouts);
    }

    [Fact]
    public void LoadFromRoots_MissingRootIsIgnored()
    {
        var missing = Path.Combine(_temporaryHome, "missing");

        var result = CodexLocalRolloutPipeline.LoadFromRoots([missing], Cutoff);

        Assert.Empty(result.PrimaryRollouts);
        Assert.Empty(result.ResolutionResults);
    }

    [Fact]
    public void LoadFiles_UnreadableFileIsPreservedAsEmptyPrimaryRollout()
    {
        var path = WriteRollout(
            _sessionsRoot,
            "locked.jsonl",
            RecentFileTime,
            SessionMeta("session"),
            StateLine(StateA));

        using var locked = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
        var result = CodexLocalRolloutPipeline.LoadFiles([path], Cutoff);

        var rollout = Assert.Single(result.PrimaryRollouts);
        Assert.Equal(path, rollout.FilePath);
        Assert.Null(rollout.RolloutMetadata);
        Assert.Empty(rollout.TokenEvents);
    }

    [Fact]
    public void LoadFromRoots_ResultKeepsPrimaryDependencyResolutionAndResolvedPrimarySeparate()
    {
        var parentPath = WriteParent(
            _sessionsRoot,
            "parent.jsonl",
            OldFileTime,
            "parent",
            StateA);
        var childPath = WriteFork(
            _sessionsRoot,
            "child.jsonl",
            RecentFileTime,
            "child",
            "parent",
            StateA,
            StateB);

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        Assert.Equal([childPath], Paths(result.PrimaryRollouts));
        Assert.Equal([parentPath], Paths(result.DependencyRollouts));
        Assert.Equal(OrderPaths(parentPath, childPath), Paths(result.ResolutionRollouts));
        Assert.Equal(OrderPaths(parentPath, childPath), result.ResolutionResults.Select(item => item.FilePath));
        Assert.Equal([childPath], result.ResolvedPrimaryRollouts.Select(item => item.FilePath));
    }

    [Fact]
    public void LoadFromRoots_DependencyOnlyFileNeverBecomesResolvedPrimary()
    {
        var parentPath = WriteParent(
            _sessionsRoot,
            "parent.jsonl",
            OldFileTime,
            "parent",
            StateA);
        WriteFork(
            _sessionsRoot,
            "child.jsonl",
            RecentFileTime,
            "child",
            "parent",
            StateA,
            StateB);

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        Assert.Contains(result.ResolutionResults, item => item.FilePath == parentPath);
        Assert.DoesNotContain(result.ResolvedPrimaryRollouts, item => item.FilePath == parentPath);
    }

    [Fact]
    public void LoadFromRoots_PrimarySelectionUsesFileMtimeNotEventTimestamp()
    {
        WriteRollout(
            _sessionsRoot,
            "old-file-future-event.jsonl",
            OldFileTime,
            SessionMeta("old"),
            StateLine("2099-01-01T00:00:00.000Z", 100, 10, 100, 10));
        var recentPath = WriteRollout(
            _sessionsRoot,
            "recent-file-old-event.jsonl",
            RecentFileTime,
            SessionMeta("recent"),
            StateLine("2000-01-01T00:00:00.000Z", 100, 10, 100, 10));

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        Assert.Equal([recentPath], Paths(result.PrimaryRollouts));
    }

    [Fact]
    public void LoadFromRoots_FileAtModifiedSinceBoundaryIsPrimary()
    {
        var path = WriteRollout(
            _sessionsRoot,
            "boundary.jsonl",
            Cutoff,
            SessionMeta("boundary"));

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        Assert.Equal([path], Paths(result.PrimaryRollouts));
    }

    [Fact]
    public void LoadFromRoots_UnreferencedOldFileIsNotAddedAsDependency()
    {
        var recentPath = WriteRollout(
            _sessionsRoot,
            "recent.jsonl",
            RecentFileTime,
            SessionMeta("recent"));
        WriteRollout(
            _sessionsRoot,
            "old-unreferenced.jsonl",
            OldFileTime,
            SessionMeta("old"));

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        Assert.Equal([recentPath], Paths(result.PrimaryRollouts));
        Assert.Empty(result.DependencyRollouts);
        Assert.Equal([recentPath], Paths(result.ResolutionRollouts));
    }

    [Fact]
    public void LoadFromRoots_TokenlessRolloutWithSessionIdCanBeDependencyCandidate()
    {
        var parentPath = WriteRollout(
            _sessionsRoot,
            "tokenless-parent.jsonl",
            OldFileTime,
            SessionMeta("parent"));
        WriteFork(
            _sessionsRoot,
            "child.jsonl",
            RecentFileTime,
            "child",
            "parent",
            StateA,
            StateB);

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        Assert.Equal([parentPath], Paths(result.DependencyRollouts));
        Assert.Empty(Assert.Single(result.DependencyRollouts).TokenEvents);
    }

    [Fact]
    public void LoadFromRoots_SessionlessOldRolloutCannotSatisfyParentDependency()
    {
        WriteRollout(
            _sessionsRoot,
            "sessionless.jsonl",
            OldFileTime,
            """{"type":"session_meta","payload":{}}""");
        var childPath = WriteFork(
            _sessionsRoot,
            "child.jsonl",
            RecentFileTime,
            "child",
            "parent",
            StateA,
            StateB);

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot],
            Cutoff);

        Assert.Empty(result.DependencyRollouts);
        Assert.Equal(1, ResolvedByPath(result, childPath).ReplayCount);
    }

    [Fact]
    public void LoadFromRoots_ActiveAndArchivedPrimaryCopiesRemainUntilCanonicalDedupStage()
    {
        var lines = new[]
        {
            SessionMeta("same-session"),
            StateLine(StateA),
        };
        var activePath = WriteRollout(
            _sessionsRoot,
            "copy.jsonl",
            RecentFileTime,
            lines);
        var archivedPath = WriteRollout(
            _archivedRoot,
            "copy.jsonl",
            RecentFileTime,
            lines);

        var result = CodexLocalRolloutPipeline.LoadFromRoots(
            [_sessionsRoot, _archivedRoot],
            Cutoff);

        Assert.Equal(OrderPaths(activePath, archivedPath), Paths(result.PrimaryRollouts));
        Assert.Equal(2, result.ResolvedPrimaryRollouts.Count);
    }

    private static readonly State StateA =
        StateAt("2026-07-30T01:00:01.000Z", 100, 10, 100, 10);
    private static readonly State StateB =
        StateAt("2026-07-30T01:00:03.000Z", 300, 30, 200, 20);
    private static readonly State StateC =
        StateAt("2026-07-30T01:00:05.000Z", 600, 60, 300, 30);

    private string WriteParent(
        string root,
        string relativePath,
        DateTimeOffset mtime,
        string sessionId,
        params State[] states) =>
        WriteRollout(
            root,
            relativePath,
            mtime,
            new[] { SessionMeta(sessionId) }
                .Concat(states.Select(StateLine))
                .ToArray());

    private string WriteFork(
        string root,
        string relativePath,
        DateTimeOffset mtime,
        string sessionId,
        string parentId,
        params State[] states) =>
        WriteFork(
            root,
            relativePath,
            mtime,
            sessionId,
            parentId,
            isSubagent: false,
            states);

    private string WriteFork(
        string root,
        string relativePath,
        DateTimeOffset mtime,
        string sessionId,
        string parentId,
        bool isSubagent,
        params State[] states) =>
        WriteRollout(
            root,
            relativePath,
            mtime,
            new[] { SessionMeta(sessionId, parentId, isSubagent) }
                .Concat(states.Select(StateLine))
                .ToArray());

    private static string WriteRollout(
        string root,
        string relativePath,
        DateTimeOffset mtime,
        params string[] lines)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join(Environment.NewLine, lines));
        File.SetLastWriteTimeUtc(path, mtime.UtcDateTime);
        return path;
    }

    private static CodexInMemoryResolvedRollout ResolvedByPath(
        CodexLocalRolloutPipelineResult result,
        string path) =>
        Assert.Single(result.ResolvedPrimaryRollouts, item =>
            string.Equals(item.FilePath, path, StringComparison.Ordinal));

    private static string[] Paths(IEnumerable<CodexEpochRollout> rollouts) =>
        rollouts.Select(item => item.FilePath).ToArray();

    private static string[] OrderPaths(params string[] paths) =>
        paths.Order(StringComparer.Ordinal).ToArray();

    private static string SessionMeta(
        string sessionId,
        string? parentId = null,
        bool isSubagent = false)
    {
        var parentFields = parentId is null
            ? string.Empty
            : $",\"forked_from_id\":\"{parentId}\",\"parent_thread_id\":\"{parentId}\"";
        var threadSource = isSubagent ? "subagent" : "user";
        return "{\"type\":\"session_meta\",\"timestamp\":\"2026-07-30T01:00:00.000Z\","
            + $"\"payload\":{{\"id\":\"{sessionId}\"{parentFields},\"thread_source\":\"{threadSource}\"}}}}";
    }

    private static string StateLine(State state) =>
        StateLine(
            state.Timestamp,
            state.CumulativeInput,
            state.CumulativeOutput,
            state.LastInput,
            state.LastOutput);

    private static string StateLine(
        string timestamp,
        long cumulativeInput,
        long cumulativeOutput,
        long lastInput,
        long lastOutput) =>
        "{\"type\":\"event_msg\",\"timestamp\":\"" + timestamp
        + "\",\"payload\":{\"type\":\"token_count\",\"info\":{"
        + "\"total_token_usage\":{"
        + $"\"input_tokens\":{cumulativeInput},\"cached_input_tokens\":0,"
        + $"\"cache_write_input_tokens\":0,\"output_tokens\":{cumulativeOutput},"
        + $"\"reasoning_output_tokens\":0,\"total_tokens\":{cumulativeInput + cumulativeOutput}}},"
        + "\"last_token_usage\":{"
        + $"\"input_tokens\":{lastInput},\"cached_input_tokens\":0,"
        + $"\"cache_write_input_tokens\":0,\"output_tokens\":{lastOutput},"
        + $"\"reasoning_output_tokens\":0,\"total_tokens\":{lastInput + lastOutput}}}}}}}}}";

    private static State StateAt(
        string timestamp,
        long cumulativeInput,
        long cumulativeOutput,
        long lastInput,
        long lastOutput) =>
        new(timestamp, cumulativeInput, cumulativeOutput, lastInput, lastOutput);

    private readonly record struct State(
        string Timestamp,
        long CumulativeInput,
        long CumulativeOutput,
        long LastInput,
        long LastOutput);
}
