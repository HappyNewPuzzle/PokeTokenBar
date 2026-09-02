using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PokeTokenBar.Windows.App;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class Phase6ProductionizationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"PokeTokenBar-Phase6-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Theory]
    [InlineData("2.5.3", "2.5.2", true)]
    [InlineData("2.5.2", "2.5.2", false)]
    [InlineData("2.5.1", "2.5.2", false)]
    [InlineData("2.6.0", "2.5.9", true)]
    [InlineData("3.0.0", "2.9.9", true)]
    [InlineData("2.0.10", "2.0.9", true)]
    [InlineData("2.0", "2.0.0", false)]
    [InlineData("v2.5.3", "2.5.2", true)]
    [InlineData("2.5.3", "2.5.3-beta.1", true)]
    [InlineData("bad", "2.5.2", false)]
    public void ReleaseVersion_UsesVersionSafeComparison(string candidate, string current, bool expected) =>
        Assert.Equal(expected, ReleaseVersion.IsNewer(candidate, current));

    [Fact] public async Task UpdateChecker_ReturnsNewerStableRelease() =>
        Assert.Equal(UpdateCheckStatus.Available, (await Checker("2.5.3").CheckAsync()).Status);

    [Theory]
    [InlineData("2.5.2")]
    [InlineData("2.5.1")]
    public async Task UpdateChecker_SameOrOlderIsUpToDate(string latest) =>
        Assert.Equal(UpdateCheckStatus.UpToDate, (await Checker(latest).CheckAsync()).Status);

    [Fact] public async Task UpdateChecker_RejectsPrerelease() =>
        Assert.Equal(UpdateCheckStatus.Failed, (await Checker("2.6.0-beta", prerelease: true).CheckAsync()).Status);

    [Fact] public async Task UpdateChecker_NetworkFailureIsContained() =>
        Assert.Equal(UpdateCheckStatus.Failed,
            (await new GitHubReleaseUpdateChecker(new HttpClient(new ThrowingHandler()), "2.5.2").CheckAsync()).Status);

    [Fact] public async Task UpdateChecker_MalformedResponseIsContained() =>
        Assert.Equal(UpdateCheckStatus.Failed,
            (await new GitHubReleaseUpdateChecker(new HttpClient(new JsonHandler("{")), "2.5.2").CheckAsync()).Status);

    [Fact] public async Task UpdateChecker_RejectsNonGithubDownloadPage() =>
        Assert.Equal(UpdateCheckStatus.Failed,
            (await Checker("2.5.3", url: "https://evil.example/release").CheckAsync()).Status);

    [Fact]
    public async Task UpdateChecker_SendsGitHubMediaTypeAndUserAgent()
    {
        var handler = new CapturingHandler(ReleaseJson("2.5.3"));
        await new GitHubReleaseUpdateChecker(new HttpClient(handler), "2.5.2").CheckAsync();
        Assert.Contains("application/vnd.github+json", handler.Accept);
        Assert.Contains("PokeTokenBar/2.5.2", handler.UserAgent);
    }

    [Fact]
    public void StateTransfer_RoundTripsSettingsAndCompanionState()
    {
        var service = Transfer(out var settings, out var companion);
        settings.Save(AppSettings.Default with { Language = AppLanguage.De, SelectedProviderId = "codex" });
        companion.Save(State(42, 7));
        var data = service.Export();
        var json = Encoding.UTF8.GetString(data);
        Assert.Contains("\"schema\": 1", json);
        Assert.Contains("\"exportedAt\"", json);
        settings.Save(AppSettings.Default);
        companion.Save(new CompanionState());
        service.Import(data);
        Assert.Equal(AppLanguage.De, settings.Load()!.Language);
        Assert.Equal(42, companion.Load()!.UsedSinceInstall);
        Assert.Equal(7, companion.Load()!.SpentTokens);
    }

    [Fact] public void StateTransfer_SuggestedNameUsesExportClock() =>
        Assert.Equal("PokeTokenBar-Save-2026-09-01.json", Transfer(out _, out _).SuggestedFileName);

    [Fact]
    public void StateTransfer_PreviewSummarizesIncomingSave()
    {
        var service = Transfer(out _, out var companion);
        companion.Save(State(99, dexCount: 2));
        var preview = service.Preview(service.Export());
        Assert.Equal(99, preview.State.LifetimeTokens);
        Assert.Equal(2, preview.State.DexCount);
    }

    [Fact]
    public void StateTransfer_AcceptsMacEnvelopeWithoutSettings()
    {
        var service = Transfer(out var settings, out _);
        settings.Save(AppSettings.Default with { Language = AppLanguage.Fr });
        var node = JsonNode.Parse(service.Export())!.AsObject();
        node.Remove("settings");
        service.Import(Encoding.UTF8.GetBytes(node.ToJsonString()));
        Assert.Equal(AppLanguage.Fr, settings.Load()!.Language);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"another.app\",\"formatVersion\":1,\"state\":{}}")]
    public void StateTransfer_RejectsForeignOrCorruptContent(string content) =>
        Assert.Equal(StateTransferError.NotASaveFile,
            Assert.Throws<StateTransferException>(() => Transfer(out _, out _).Preview(Encoding.UTF8.GetBytes(content))).Reason);

    [Fact]
    public void StateTransfer_RejectsFutureFormat()
    {
        var service = Transfer(out _, out _);
        var data = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(service.Export()).Replace(
            "\"schema\": 1", "\"schema\": 99"));
        Assert.Equal(StateTransferError.NewerFormat,
            Assert.Throws<StateTransferException>(() => service.Preview(data)).Reason);
    }

    [Fact]
    public void StateTransfer_RejectsOversizedFile() =>
        Assert.Equal(StateTransferError.FileTooLarge,
            Assert.Throws<StateTransferException>(() =>
                Transfer(out _, out _).Preview(new byte[StateTransferService.MaxFileBytes + 1])).Reason);

    [Fact]
    public void StateTransfer_RejectsInvalidSettingsCandidate()
    {
        var service = Transfer(out _, out _);
        var node = JsonNode.Parse(service.Export())!.AsObject();
        node["settings"]!["warningThreshold"] = 100;
        Assert.Equal(StateTransferError.NotASaveFile,
            Assert.Throws<StateTransferException>(() => service.Preview(
                Encoding.UTF8.GetBytes(node.ToJsonString()))).Reason);
    }

    [Fact]
    public void StateTransfer_ExportsNoCredentialOrSessionFields()
    {
        var json = Encoding.UTF8.GetString(Transfer(out _, out _).Export());
        Assert.DoesNotContain("Authorization", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("oauth", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StateTransfer_RestoresEconomyCollectionAndRepresentative()
    {
        var service = Transfer(out _, out var companion);
        var state = State(500, 20) with
        {
            Inventory = new Dictionary<string, int> { ["rareCandy"] = 3 },
            Dex = [Dex(25)],
            RepresentativeSpeciesId = 25,
        };
        companion.Save(state);
        var data = service.Export();
        companion.Save(new CompanionState());
        service.Import(data);
        var restored = companion.Load()!;
        Assert.Equal(3, restored.Inventory["rareCandy"]);
        Assert.Equal(25, restored.RepresentativeSpeciesId);
        Assert.Single(restored.Dex);
    }

    [Fact]
    public void StateTransfer_RebasesUsageLedgerToCurrentDevice()
    {
        var service = Transfer(out _, out var companion);
        companion.Save(State(1) with { ClaimedTodayTokensByProvider = new Dictionary<string, long> { ["old"] = 9 } });
        var data = service.Export();
        service.Import(data, new Dictionary<string, long> { ["codex"] = 12 }, "2026-09-01", true);
        var state = companion.Load()!;
        Assert.True(state.InstallBaselineSet);
        Assert.Equal(12, state.ClaimedTodayTokensByProvider!["codex"]);
        Assert.Equal("2026-09-01", state.LastDate);
    }

    [Fact]
    public void StateTransfer_LeavesBaselineUnseededWithoutUsage()
    {
        var service = Transfer(out _, out var companion);
        var data = service.Export();
        service.Import(data, hasUsageData: false);
        Assert.False(companion.Load()!.InstallBaselineSet);
        Assert.Null(companion.Load()!.ClaimedTodayTokensByProvider);
    }

    [Fact]
    public void StateTransfer_PreservesCurrentDeviceLanguageAndCandyGrantTier()
    {
        var service = Transfer(out _, out var companion);
        companion.Save(State(1) with { Language = AppLanguage.Ja, CandyGrantTier = new Dictionary<string, int> { ["w"] = 1 } });
        var data = service.Export();
        companion.Save(State(2) with { Language = AppLanguage.Ko, CandyGrantTier = new Dictionary<string, int> { ["w"] = 2 } });
        service.Import(data);
        Assert.Equal(AppLanguage.Ko, companion.Load()!.Language);
        Assert.Equal(2, companion.Load()!.CandyGrantTier["w"]);
    }

    [Fact]
    public void StateTransfer_ClampsUntrustedArithmeticValues()
    {
        var service = Transfer(out _, out var companion);
        companion.Save(State(long.MaxValue, long.MinValue));
        var data = service.Export();
        service.Import(data);
        Assert.Equal(StateTransferService.MaxTokenValue, companion.Load()!.UsedSinceInstall);
        Assert.Equal(0, companion.Load()!.SpentTokens);
    }

    [Fact]
    public void StateTransfer_ClearsUnownedRepresentative()
    {
        var service = Transfer(out _, out var companion);
        companion.Save(State(1) with { RepresentativeSpeciesId = 999 });
        var data = service.Export();
        service.Import(data);
        Assert.Null(companion.Load()!.RepresentativeSpeciesId);
    }

    [Fact]
    public void StateTransfer_RollsBackBothFilesOnCommitFailure()
    {
        var source = Transfer(out var settings, out var companion);
        settings.Save(AppSettings.Default with { Language = AppLanguage.De });
        companion.Save(State(99));
        var import = source.Export();
        settings.Save(AppSettings.Default with { Language = AppLanguage.En });
        companion.Save(State(7));
        var failing = new StateTransferService(settings, companion, "2.5.2", Clock(),
            step => { if (step == 2) throw new IOException("fixture failure"); });
        Assert.Equal(StateTransferError.CommitFailed,
            Assert.Throws<StateTransferException>(() => failing.Import(import)).Reason);
        Assert.Equal(AppLanguage.En, settings.Load()!.Language);
        Assert.Equal(7, companion.Load()!.UsedSinceInstall);
    }

    [Fact]
    public void StateTransfer_CreatesPreImportBackup()
    {
        var service = Transfer(out _, out _);
        service.Import(service.Export());
        Assert.Single(Directory.GetFiles(Path.Combine(_directory, "backups"), "PokeTokenBar-PreImport-*.json"));
    }

    [Fact]
    public void Diagnostics_ContainsVersionsProvidersAndSymbolicPaths()
    {
        var (settings, usage) = DiagnosticsFixture();
        var report = DiagnosticsReport.Create("2.5.2", settings, usage);
        Assert.Contains("appVersion=2.5.2", report);
        Assert.Contains("runtime=.NET", report);
        Assert.Contains("provider.codex.available=false", report);
        Assert.Contains("%LOCALAPPDATA%\\PokeTokenBar", report);
        Assert.Contains("usageCache=missing", report);
    }

    [Fact]
    public void Diagnostics_DoesNotExposeCustomPathCredentialOrRawError()
    {
        var (settings, usage) = DiagnosticsFixture();
        settings.SelectedRootProviderId = "codex";
        settings.CustomRootText = @"C:\Users\alice\Bearer-fake-secret";
        var report = DiagnosticsReport.Create("2.5.2", settings, usage);
        Assert.DoesNotContain("alice", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer-fake-secret", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("oauth", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("customRootConfigured=true", report);
    }

    [Fact]
    public void ReleaseContracts_UseAssemblyVersionAndLocationIndependentDataPath()
    {
        var project = File.ReadAllText(Path.Combine(Root(), "src", "PokeTokenBar.Windows.App", "PokeTokenBar.Windows.App.csproj"));
        Assert.Contains("<Version>2.5.2</Version>", project);
        Assert.Contains("PokeTokenBarDataPaths.Root", File.ReadAllText(Path.Combine(Root(), "src", "PokeTokenBar.Windows.Infrastructure", "JsonAppSettingsPersistence.cs")));
        Assert.DoesNotContain("Environment.ProcessPath", JsonAppSettingsPersistence.GetDefaultFilePath());
    }

    [Fact]
    public void ReleaseContracts_TemporaryProfileRootIsInjectable()
    {
        var root = Path.Combine(_directory, "isolated-profile");
        Assert.Equal(Path.GetFullPath(root), PokeTokenBarDataPaths.Resolve(root));
        Assert.Equal("POKETOKENBAR_DATA_ROOT", PokeTokenBarDataPaths.RootEnvironmentVariable);
    }

    [Fact]
    public void ReleaseContracts_InstallerPreservesDataAndProvidesUpgradeShortcuts()
    {
        var script = File.ReadAllText(Path.Combine(Root(), "installer", "PokeTokenBar.iss"));
        Assert.Contains("PrivilegesRequired=lowest", script);
        Assert.Contains("{group}\\PokeTokenBar", script);
        Assert.Contains("desktopicon", script);
        Assert.Contains("RegDeleteValue", script);
        Assert.DoesNotContain("UninstallDelete", script);
        Assert.DoesNotContain("companion-state.json", script);
    }

    [Fact]
    public void ReleaseContracts_ScriptBuildsTestsPublishZipAndOptionalInstaller()
    {
        var script = File.ReadAllText(Path.Combine(Root(), "scripts", "build-release.ps1"));
        Assert.Contains("dotnet test", script);
        Assert.Contains("dotnet publish", script);
        Assert.Contains("Compress-Archive", script);
        Assert.Contains("OpenRead", script);
        Assert.Contains("BuildInstaller", script);
    }

    [Fact]
    public void ReleaseContracts_XamlExposesUpdateTransferAndDiagnostics()
    {
        var xaml = File.ReadAllText(Path.Combine(Root(), "src", "PokeTokenBar.Windows.App", "MainWindow.xaml"));
        Assert.Contains("Support.CheckForUpdatesCommand", xaml);
        Assert.Contains("Support.ExportCommand", xaml);
        Assert.Contains("Support.ImportCommand", xaml);
        Assert.Contains("Support.CopyDiagnosticsCommand", xaml);
    }

    private StateTransferService Transfer(
        out JsonAppSettingsPersistence settings,
        out JsonCompanionPersistence companion)
    {
        settings = new(Path.Combine(_directory, "settings.json"));
        companion = new(Path.Combine(_directory, "companion-state.json"));
        return new(settings, companion, "2.5.2", Clock());
    }

    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

    private static CompanionState State(long used, long spent = 0, int dexCount = 0) => new()
    {
        UsedSinceInstall = used,
        SpentTokens = spent,
        Dex = Enumerable.Range(1, dexCount).Select(Dex).ToArray(),
    };

    private static DexEntry Dex(int id) => new()
    {
        BaseId = id, FinalId = id, ChainOrder = [id], Rarity = PokemonRarity.Common,
    };

    private static GitHubReleaseUpdateChecker Checker(
        string version, bool prerelease = false, string url = "https://github.com/chattymin/PokeTokenBar/releases/tag/v2.5.3") =>
        new(new HttpClient(new JsonHandler(ReleaseJson(version, prerelease, url))), "2.5.2");

    private static string ReleaseJson(string version, bool prerelease = false,
        string url = "https://github.com/chattymin/PokeTokenBar/releases/tag/v2.5.3") =>
        JsonSerializer.Serialize(new { tag_name = version, html_url = url, draft = false, prerelease, body = "notes" });

    private static (SettingsViewModel, UsageViewModel) DiagnosticsFixture() =>
        (new SettingsViewModel(new MemorySettings(), new AutoStart()),
         new UsageViewModel(new UsageStore([new EmptyProvider()])));

    private static string Root() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    private sealed class CapturingHandler(string json) : HttpMessageHandler
    {
        public string Accept { get; private set; } = "";
        public string UserAgent { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Accept = request.Headers.Accept.ToString();
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MemorySettings : IAppSettingsPersistence
    {
        private AppSettings _settings = AppSettings.Default;
        public AppSettings? Load() => _settings;
        public void Save(AppSettings settings) => _settings = settings;
    }

    private sealed class AutoStart : IAutoStartService
    {
        public bool IsAvailable => true;
        public bool IsEnabled => false;
        public void SetEnabled(bool enabled) { }
    }

    private sealed class EmptyProvider : IUsageProvider
    {
        public string Id => "codex";
        public string DisplayName => "Codex";
        public bool ReportsCost => true;
        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) => Task.FromResult<DailyUsage?>(null);
        public Task<ProviderEnrichment> FetchEnrichmentAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ProviderEnrichment());
    }
}
