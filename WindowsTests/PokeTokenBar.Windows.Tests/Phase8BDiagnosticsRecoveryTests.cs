using System.Text;
using System.Text.Json;
using PokeTokenBar.Windows.App;
using PokeTokenBar.Windows.App.Lifecycle;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class Phase8BDiagnosticsRecoveryTests : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"PokeTokenBar-Phase8B-{Guid.NewGuid():N}");

    [Fact]
    public void Settings_InvalidValuesFallbackWithoutDiscardingValidFields()
    {
        Write("settings.json", """
            {
              "floatingPetEnabled": true,
              "refreshInterval": 999,
              "language": 999,
              "warningThreshold": -1,
              "criticalThreshold": 95,
              "floatingPetSize": 999,
              "animationQuality": 999,
              "credentialAccessEnabled": false
            }
            """);

        var settings = new JsonAppSettingsPersistence(Path("settings.json")).Load()!;

        Assert.True(settings.FloatingPetEnabled);
        Assert.False(settings.CredentialAccessEnabled);
        Assert.Equal(AppSettings.Default.RefreshInterval, settings.RefreshInterval);
        Assert.Equal(AppSettings.Default.Language, settings.Language);
        Assert.Equal(AppSettings.Default.WarningThreshold, settings.WarningThreshold);
        Assert.Equal(AppSettings.Default.FloatingPetSize, settings.FloatingPetSize);
        Assert.Equal(AppSettings.Default.AnimationQuality, settings.AnimationQuality);
    }

    [Fact]
    public void Settings_MalformedFieldUsesDefaultAndKeepsSiblingFields()
    {
        Write("settings.json", """
            {"floatingPetEnabled":true,"refreshInterval":"bad","credentialAccessEnabled":false}
            """);

        var settings = new JsonAppSettingsPersistence(Path("settings.json")).Load()!;

        Assert.True(settings.FloatingPetEnabled);
        Assert.False(settings.CredentialAccessEnabled);
        Assert.Equal(RefreshIntervalMode.TwoMinutes, settings.RefreshInterval);
    }

    [Theory]
    [InlineData("""{"floatingPetEnabled":true}""")]
    [InlineData("""{"floatingPetEnabled":true,"refreshInterval":300,"language":0}""")]
    public void Settings_OlderFixturesRemainReadable(string json)
    {
        Write("settings.json", json);

        Assert.True(new JsonAppSettingsPersistence(Path("settings.json")).Load()!.FloatingPetEnabled);
    }

    [Fact]
    public void Settings_InvalidSavePreservesPreviousValidFile()
    {
        var persistence = new JsonAppSettingsPersistence(Path("settings.json"));
        persistence.Save(AppSettings.Default with { Language = AppLanguage.De });

        Assert.Throws<ArgumentException>(() =>
            persistence.Save(AppSettings.Default with { WarningThreshold = -1 }));

        Assert.Equal(AppLanguage.De, persistence.Load()!.Language);
    }

    [Fact]
    public void AtomicWrite_FailureKeepsPreviousFileAndCleansTemporaryFile()
    {
        var path = Path("atomic.json");
        AtomicFile.WriteBytes(path, Encoding.UTF8.GetBytes("""{"value":1}"""));

        Assert.Throws<IOException>(() => AtomicFile.Write(path, stream =>
        {
            stream.Write(Encoding.UTF8.GetBytes("""{"value":2"""));
            throw new IOException("fixture");
        }));

        Assert.Equal("""{"value":1}""", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void AtomicWrite_ConcurrentWritersLeaveOneCompleteDocument()
    {
        var path = Path("concurrent.json");

        Parallel.For(0, 20, value =>
            AtomicFile.WriteBytes(path, Encoding.UTF8.GetBytes($$"""{"value":{{value}}}""")));

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.InRange(document.RootElement.GetProperty("value").GetInt32(), 0, 19);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void AtomicWrite_TemporaryHandleAllowsRenameBeforeDispose()
    {
        var path = Path("rename.json");

        AtomicFile.Write(path, stream =>
        {
            stream.Write(Encoding.UTF8.GetBytes("fixture"));
            var temporary = Assert.IsType<FileStream>(stream).Name;
            var probe = $"{temporary}.probe";
            File.Move(temporary, probe);
            File.Move(probe, temporary);
        });

        Assert.Equal("fixture", File.ReadAllText(path));
    }

    [Fact]
    public void Persistence_ReplaceFailurePreservesPreviousValidFile()
    {
        var persistence = new JsonAppSettingsPersistence(Path("settings.json"));
        persistence.Save(AppSettings.Default with { Language = AppLanguage.De });
        using (new FileStream(persistence.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = Record.Exception(() =>
                persistence.Save(AppSettings.Default with { Language = AppLanguage.En }));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        }

        Assert.Equal(AppLanguage.De, persistence.Load()!.Language);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void Settings_UnavailableStartupReadCannotOverwriteExistingFile()
    {
        var path = Path("settings.json");
        var writer = new JsonAppSettingsPersistence(path);
        writer.Save(AppSettings.Default with { Language = AppLanguage.De });
        var reader = new JsonAppSettingsPersistence(path);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Null(reader.Load());

        Assert.Equal(AppLanguage.De, reader.Load()!.Language);
        Assert.Throws<IOException>(() =>
            reader.Save(AppSettings.Default with { Language = AppLanguage.En }));
        Assert.Equal(AppLanguage.De, writer.Load()!.Language);
    }

    [Fact]
    public void Companion_UnavailablePrimaryUsesBackupWithoutOverwritingNewerState()
    {
        var path = Path("companion-state.json");
        var writer = new JsonCompanionPersistence(path);
        writer.Save(State(10));
        writer.Save(State(20));
        var reader = new JsonCompanionPersistence(path);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(10, reader.Load()!.UsedSinceInstall);

        Assert.Equal(20, reader.Load()!.UsedSinceInstall);
        Assert.Throws<IOException>(() => reader.Save(State(30)));
        Assert.Equal(20, writer.Load()!.UsedSinceInstall);
    }

    [Fact]
    public void Companion_CorruptPrimaryRestoresLastKnownGoodWithoutReplayingProgression()
    {
        var persistence = new JsonCompanionPersistence(Path("companion-state.json"));
        persistence.Save(State(10, ditto: true));
        persistence.Save(State(20));
        File.WriteAllText(persistence.FilePath, "{broken");

        var restored = persistence.Load()!;

        Assert.Equal(10, restored.UsedSinceInstall);
        Assert.Equal(7, restored.Active!.UsedAtStage);
        Assert.True(restored.Active.DittoRevealed);
        Assert.Equal(132, restored.Active.CurrentId);
        Assert.NotNull(new JsonCompanionPersistence(persistence.FilePath).Load());
        Assert.Single(Directory.GetFiles(_directory, "companion-state.corrupt-*.json"));
    }

    [Fact]
    public void Companion_DeleteRemovesLastKnownGoodBackup()
    {
        var persistence = new JsonCompanionPersistence(Path("companion-state.json"));
        persistence.Save(State(1));
        persistence.Save(State(2));
        Assert.True(File.Exists(persistence.BackupPath));

        persistence.Delete();

        Assert.False(File.Exists(persistence.FilePath));
        Assert.False(File.Exists(persistence.BackupPath));
    }

    [Fact]
    public void Companion_MissingPrimaryRestoresExistingLastKnownGood()
    {
        var persistence = new JsonCompanionPersistence(Path("companion-state.json"));
        persistence.Save(State(10));
        persistence.Save(State(20));
        File.Delete(persistence.FilePath);

        var restored = new JsonCompanionPersistence(persistence.FilePath).Load()!;

        Assert.Equal(10, restored.UsedSinceInstall);
        Assert.True(File.Exists(persistence.FilePath));
    }

    [Fact]
    public void Companion_BothPrimaryAndBackupCorruptAreIsolatedIndependently()
    {
        var persistence = new JsonCompanionPersistence(Path("companion-state.json"));
        Write("companion-state.json", "{broken-primary");
        File.WriteAllText(persistence.BackupPath, "{broken-backup");

        Assert.Null(persistence.Load());

        Assert.False(File.Exists(persistence.FilePath));
        Assert.False(File.Exists(persistence.BackupPath));
        Assert.Single(Directory.GetFiles(_directory, "companion-state.corrupt-*.json"));
        Assert.Single(Directory.GetFiles(_directory, "companion-state.json.corrupt-*.bak"));
    }

    [Theory]
    [InlineData(-1, "2026-09-04")]
    [InlineData(50, "not-a-date")]
    public async Task Companion_InvalidUsageLedgerBecomesBaselineWithoutDuplicateReward(
        long claimedTokens,
        string lastDate)
    {
        Write("companion-state.json", $$"""
            {
              "installBaselineSet": true,
              "usedSinceInstall": 10,
              "claimedTodayTokensByProvider": { "codex": {{claimedTokens}} },
              "lastDate": "{{lastDate}}"
            }
            """);
        var store = new CompanionStore(
            new UnusedPokeApi(),
            new JsonCompanionPersistence(Path("companion-state.json")));

        await store.UpdateUsageAsync(
            new Dictionary<string, long> { ["codex"] = 100 },
            "2026-09-04",
            hasUsageData: true);

        Assert.Equal(10, store.State.UsedSinceInstall);
        Assert.Equal(0, store.State.EggUsage);
        Assert.Equal(100, store.State.ClaimedTodayTokensByProvider!["codex"]);
    }

    [Fact]
    public void Companion_InvalidNestedValuesDoNotDiscardValidSiblingState()
    {
        Write("companion-state.json", """
            {
              "usedSinceInstall": 42,
              "active": {
                "baseID": 1, "pathIDs": null, "stageIndex": 0,
                "usedAtStage": 0, "rarity": "common", "totalForms": 1
              },
              "dex": [
                { "baseID": 1, "finalID": 3, "chainOrder": null, "rarity": "common" },
                { "baseID": 4, "finalID": 6, "chainOrder": [4,5,6], "rarity": "common", "caughtAt": 1e100 },
                { "baseID": 7, "finalID": 9, "chainOrder": [7,8,9], "rarity": "common" }
              ]
            }
            """);

        var state = new JsonCompanionPersistence(Path("companion-state.json")).Load()!;

        Assert.Equal(42, state.UsedSinceInstall);
        Assert.Null(state.Active);
        Assert.Equal(7, Assert.Single(state.Dex).BaseId);
    }

    [Fact]
    public void CorruptIsolation_IsBoundedAndDoesNotDeleteUnrelatedFiles()
    {
        var path = Path("settings.json");
        var unrelated = Path("keep.txt");
        File.WriteAllText(unrelated, "keep");
        for (var index = 0; index < 5; index++)
        {
            File.WriteAllText(path, "{broken");
            new JsonAppSettingsPersistence(path).Load();
            Thread.Sleep(2);
        }

        Assert.True(File.Exists(unrelated));
        Assert.InRange(Directory.GetFiles(_directory, "settings.corrupt-*.json").Length, 1, 3);
    }

    [Fact]
    public async Task CorruptUsageCacheIsIgnoredWithoutDuplicateCompanionReward()
    {
        Write("usage-cache.json", "{broken");
        var provider = new UsageProvider(100);
        var usage = new UsageViewModel(new UsageStore(
            [provider], snapshotPersistence: new JsonUsageSnapshotPersistence(Path("usage-cache.json"))));
        var state = new CompanionState
        {
            InstallBaselineSet = true,
            LastDate = DateTimeOffset.Now.ToString("yyyy-MM-dd"),
            ClaimedTodayTokensByProvider = new Dictionary<string, long> { ["codex"] = 100 },
        };
        var companion = new CompanionStore(new UnusedPokeApi(), new MemoryCompanionPersistence(state));
        using var controller = new UsageCompanionController(usage, companion, _ => Task.CompletedTask);
        Assert.Equal(UsageCacheLoadStatus.Corrupt, usage.UsageCacheStatus);

        await usage.RefreshAsync();
        await controller.LastUpdate;

        Assert.Equal(UsageCacheLoadStatus.Available, usage.UsageCacheStatus);
        Assert.Equal(0, companion.State.EggUsage);
        Assert.Equal(100, companion.State.ClaimedTodayTokensByProvider!["codex"]);
    }

    [Fact]
    public void UsageCache_InvalidSavePreservesPreviousValidFile()
    {
        var persistence = new JsonUsageSnapshotPersistence(Path("usage-cache.json"));
        persistence.Save(Cache(10));

        Assert.Throws<ArgumentException>(() =>
            persistence.Save(Cache(-1)));

        Assert.Equal(10, persistence.Load().Cache!.Providers[0].Today!.TotalTokens);
    }

    [Fact]
    public void Import_RollbackFailureRemainsContainedAndOtherRestoreStillRuns()
    {
        var settings = new JsonAppSettingsPersistence(Path("settings.json"));
        var companion = new JsonCompanionPersistence(Path("companion-state.json"));
        settings.Save(AppSettings.Default with { Language = AppLanguage.De });
        companion.Save(State(10));
        var import = new StateTransferService(settings, companion, "2.5.3").Export();
        settings.Save(AppSettings.Default with { Language = AppLanguage.En });
        companion.Save(State(20));
        FileStream? settingsLock = null;
        var service = new StateTransferService(
            settings,
            companion,
            "2.5.3",
            beforeCommitStep: step =>
            {
                if (step != 2) return;
                companion.Save(State(30));
                settingsLock = new FileStream(
                    settings.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
                throw new IOException("fixture");
            });

        StateTransferException error;
        try { error = Assert.Throws<StateTransferException>(() => service.Import(import)); }
        finally { settingsLock?.Dispose(); }

        Assert.Equal(StateTransferError.CommitFailed, error.Reason);
        Assert.Equal(20, companion.Load()!.UsedSinceInstall);
        Assert.Contains(ReliabilityEventLog.Snapshot(), entry =>
            entry.Component == "import-rollback" && entry.Kind == ReliabilityEventKind.Error);
        Assert.Single(Directory.GetFiles(Path("backups"), "PokeTokenBar-PreImport-*.json"));
    }

    [Fact]
    public void Import_SuccessBlocksStaleLiveWritersUntilRestart()
    {
        var settings = new JsonAppSettingsPersistence(Path("settings.json"));
        var companion = new JsonCompanionPersistence(Path("companion-state.json"));
        settings.Save(AppSettings.Default with { Language = AppLanguage.De });
        companion.Save(State(10));
        var service = new StateTransferService(settings, companion, "2.5.3");
        var import = service.Export();
        settings.Save(AppSettings.Default with { Language = AppLanguage.En });
        companion.Save(State(20));

        service.Import(import);

        Assert.Throws<IOException>(() =>
            settings.Save(AppSettings.Default with { Language = AppLanguage.En }));
        Assert.Throws<IOException>(() => companion.Save(State(30)));
        Assert.Throws<IOException>(companion.Delete);
        Assert.Equal(
            AppLanguage.De,
            new JsonAppSettingsPersistence(settings.FilePath).Load()!.Language);
        Assert.Equal(
            10,
            new JsonCompanionPersistence(companion.FilePath).Load()!.UsedSinceInstall);
    }

    [Fact]
    public void Diagnostics_ContinuesWhenOnePersistenceProbeFails()
    {
        var (settings, usage) = DiagnosticsFixture();

        var report = DiagnosticsReport.Create(
            "2.5.3", settings, usage, UpdateCheckStatus.UpToDate,
            path => path.EndsWith("usage-cache.json", StringComparison.OrdinalIgnoreCase)
                ? throw new IOException("Bearer fixture-secret")
                : new DiagnosticsReport.FileState(false, null, null));

        Assert.Contains("appVersion=2.5.3", report);
        Assert.Contains("fileVersion=2.5.5.0", report);
        Assert.Contains("runtime=.NET", report);
        Assert.Contains("language=", report);
        Assert.Contains("updateStatus=UpToDate", report);
        Assert.Contains("persistence.settings.exists=false", report);
        Assert.Contains("persistence.usageCache=unavailable", report);
        Assert.DoesNotContain("fixture-secret", report, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsAndRecoveryEventsNeverExposeRepresentativeSecrets()
    {
        const string secret = "Phase8B-Unique-Secret";
        foreach (var value in new[]
                 {
                     $"Authorization: Bearer {secret}", $"Bearer {secret}",
                     $"ApI_KeY={secret}", $"apikey={secret}",
                     $"access_token={secret}", $"refresh_token={secret}",
                     $"token={secret}", $"cookie={secret}", $"credential={secret}",
                 })
            ReliabilityEventLog.RecordRecovery("diagnostics", value);
        ReliabilityEventLog.RecordError("diagnostics", new InvalidOperationException($"api-key={secret}"));
        var (settings, usage) = DiagnosticsFixture();

        var report = DiagnosticsReport.Create("2.5.3", settings, usage);

        Assert.DoesNotContain(secret, report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recentError=diagnostics:InvalidOperationException", report);
    }

    [Fact]
    public async Task BackgroundFailureRecordsOnlySanitizedContextAndType()
    {
        const string secret = "Phase8B-Background-Secret";

        await AppReliability.ObserveAsync(
            Task.FromException(new IOException($"Bearer {secret}")),
            "startup");

        var entry = Assert.Single(ReliabilityEventLog.Snapshot(), candidate =>
            candidate.Component == "startup" && candidate.Summary == nameof(IOException));
        Assert.DoesNotContain(secret, $"{entry.Component}:{entry.Summary}", StringComparison.Ordinal);
    }

    [Fact]
    public void RecoverySummaryIsAvailableWithoutAStartupDialog()
    {
        ReliabilityEventLog.RecordRecovery("settings", "invalid-fields-defaulted");
        var (settings, usage) = DiagnosticsFixture();

        Assert.Contains(
            "recovery=settings:invalid-fields-defaulted",
            DiagnosticsReport.Create("2.5.3", settings, usage));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private string Path(string name)
    {
        Directory.CreateDirectory(_directory);
        return System.IO.Path.Combine(_directory, name);
    }

    private void Write(string name, string value) => File.WriteAllText(Path(name), value);

    private static CompanionState State(long used, bool ditto = false) => new()
    {
        UsedSinceInstall = used,
        Active = new MonState
        {
            BaseId = ditto ? 132 : 1,
            PathIds = [ditto ? 132 : 1],
            PlannedPathIds = [ditto ? 132 : 1],
            TotalForms = 1,
            UsedAtStage = 7,
            DittoDisguise = ditto ? 25 : null,
            DittoRevealed = ditto,
        },
    };

    private static UsageSnapshotCache Cache(long tokens)
    {
        var now = DateTimeOffset.Now;
        return new(now,
        [
            new CachedProviderUsage(
                "codex",
                new DailyUsage(now.ToString("yyyy-MM-dd"), tokens, 0, 0, 0, tokens, 0),
                null, null, null, now),
        ]);
    }

    private static (SettingsViewModel Settings, UsageViewModel Usage) DiagnosticsFixture() =>
        (new SettingsViewModel(new MemorySettings(), new AutoStart()),
         new UsageViewModel(new UsageStore([new UsageProvider(0)])));

    private sealed class UsageProvider(long tokens) : IUsageProvider
    {
        public string Id => "codex";
        public string DisplayName => "Codex";
        public bool ReportsCost => true;
        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<DailyUsage?>(new(
                DateTimeOffset.Now.ToString("yyyy-MM-dd"), tokens, 0, 0, 0, tokens, 0));
        public Task<ProviderEnrichment> FetchEnrichmentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderEnrichment());
    }

    private sealed class MemorySettings : IAppSettingsPersistence
    {
        public AppSettings? Load() => AppSettings.Default;
        public void Save(AppSettings settings) { }
    }

    private sealed class AutoStart : IAutoStartService
    {
        public bool IsAvailable => false;
        public bool IsEnabled => false;
        public void SetEnabled(bool enabled) { }
    }

    private sealed class MemoryCompanionPersistence(CompanionState state) : ICompanionPersistence
    {
        public CompanionState? Load() => state;
        public void Save(CompanionState value) => state = value;
        public void Delete() => state = new CompanionState();
    }

    private sealed class UnusedPokeApi : IPokeApiClient
    {
        public Task<EvoLine> GetLineAsync(int baseSpeciesId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<BaseSpecies?> GetBaseSpeciesAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
