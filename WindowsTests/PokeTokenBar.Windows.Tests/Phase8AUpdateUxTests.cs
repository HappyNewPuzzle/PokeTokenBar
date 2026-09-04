using System.Net;
using System.Text.Json;
using PokeTokenBar.Windows.App;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class Phase8AUpdateUxTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"PokeTokenBar-Phase8A-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Theory]
    [InlineData("2.5.3", "2.5.4", UpdateCheckStatus.UpdateAvailable)]
    [InlineData("2.5.3", "2.5.3", UpdateCheckStatus.UpToDate)]
    [InlineData("2.5.4", "2.5.3", UpdateCheckStatus.UpToDate)]
    public async Task Checker_ComparesCurrentAndLatestNumerically(
        string current, string latest, UpdateCheckStatus expected)
    {
        var result = await Checker(current, [Release($"windows-v{latest}")]).CheckAsync();

        Assert.Equal(expected, result.Status);
        Assert.Equal(latest, result.LatestVersion);
    }

    [Fact]
    public async Task Checker_ChoosesNumericLatestInsteadOfLexicalOrApiOrder()
    {
        var result = await Checker("2.5.9",
            [Release("windows-v2.5.9"), Release("windows-v2.5.10"), Release("windows-v2.5.4")])
            .CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("2.5.10", result.LatestVersion);
        Assert.Equal("PokeTokenBar windows-v2.5.10", result.ReleaseName);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), result.PublishedAt);
    }

    [Fact]
    public async Task Checker_CanonicalizesCurrentBuildMetadata()
    {
        var checker = Checker("2.5.3+fd87e82", [Release("windows-v2.5.3")]);

        Assert.Equal("2.5.3", checker.CurrentVersion);
        Assert.Equal(UpdateCheckStatus.UpToDate, (await checker.CheckAsync()).Status);
    }

    [Fact]
    public async Task Checker_IgnoresMacPartialAndMalformedTags()
    {
        var result = await Checker("2.5.3",
        [
            Release("v2.9.0"), Release("foo-v2.9.0"), Release("windows-v2.6"),
            Release("windows-v2.06.0"), Release("windows-v2.5.4"),
        ]).CheckAsync();

        Assert.Equal("2.5.4", result.LatestVersion);
    }

    [Fact]
    public async Task Checker_IgnoresDraftAndPrereleaseWindowsReleases()
    {
        var result = await Checker("2.5.3",
        [
            Release("windows-v2.7.0", draft: true),
            Release("windows-v2.6.0", prerelease: true),
            Release("windows-v2.5.3"),
        ]).CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Equal("2.5.3", result.LatestVersion);
    }

    [Fact]
    public async Task Checker_ReturnsFailedWhenNoValidWindowsReleaseExists()
    {
        var result = await Checker("2.5.3", [Release("v2.5.4")]).CheckAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task SupportViewModel_RejectsConcurrentDuplicateChecks()
    {
        var checker = new BlockingUpdateChecker();
        using var support = Support(checker);

        var first = support.CheckAsync(TimeSpan.Zero, manual: true);
        await checker.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(UpdateCheckStatus.Checking, support.UpdateState);
        Assert.True(support.IsChecking);

        await support.CheckAsync(TimeSpan.Zero, manual: true);
        Assert.Equal(1, checker.Calls);

        checker.Complete(new(
            UpdateCheckStatus.UpdateAvailable,
            "2.5.3",
            "2.5.4",
            new Uri("https://github.com/HappyNewPuzzle/PokeTokenBar/releases/tag/windows-v2.5.4")));
        await first;
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, support.UpdateState);
        Assert.False(support.IsChecking);
        Assert.True(support.HasUpdate);
        Assert.Contains("2.5.3", support.UpdateStatus);
        Assert.Contains("2.5.4", support.UpdateStatus);
    }

    [Theory]
    [InlineData("https://github.com/HappyNewPuzzle/PokeTokenBar/releases/tag/windows-v2.5.4", true)]
    [InlineData("http://github.com/HappyNewPuzzle/PokeTokenBar/releases/tag/windows-v2.5.4", false)]
    [InlineData("https://evil.example/HappyNewPuzzle/PokeTokenBar/releases/tag/windows-v2.5.4", false)]
    [InlineData("https://github.com/other/PokeTokenBar/releases/tag/windows-v2.5.4", false)]
    [InlineData("https://github.com/HappyNewPuzzle/PokeTokenBar/releases/tag/v2.5.4", false)]
    [InlineData("https://github.com/HappyNewPuzzle/PokeTokenBar/releases/tag/windows-v2.5.4?asset=1", false)]
    public void ReleaseUrl_AllowsOnlyTrustedWindowsRelease(string value, bool expected) =>
        Assert.Equal(expected, ReleaseVersion.IsTrustedWindowsReleaseUri(new Uri(value)));

    public static TheoryData<AppLanguage> Languages => new(
        AppLanguage.Ko, AppLanguage.En, AppLanguage.Ja, AppLanguage.Es,
        AppLanguage.Fr, AppLanguage.Pt, AppLanguage.De);

    [Theory]
    [MemberData(nameof(Languages))]
    public void UpdateUx_IsLocalizedForEverySupportedLanguage(AppLanguage language)
    {
        var text = new LocalizationService(language);

        Assert.False(string.IsNullOrWhiteSpace(text.CheckingForUpdates));
        var details = text.UpdateAvailableDetails("2.5.3", "2.5.4");
        Assert.Contains("2.5.3", details);
        Assert.Contains("2.5.4", details);
        Assert.False(string.IsNullOrWhiteSpace(text.DownloadUpdate));
        Assert.False(string.IsNullOrWhiteSpace(text.UpdateCheckFailed));
    }

    [Fact]
    public void AboutXaml_BindsUpdateStatusAndCommandsWithWrapping()
    {
        var xaml = File.ReadAllText(Path.Combine(Root(), "src", "PokeTokenBar.Windows.App", "MainWindow.xaml"));

        Assert.Contains("Support.UpdateStatus", xaml);
        Assert.Contains("TextWrapping=\"Wrap\"", xaml);
        Assert.Contains("Support.CheckForUpdatesCommand", xaml);
        Assert.Contains("Support.OpenUpdateCommand", xaml);
    }

    private SupportViewModel Support(IUpdateChecker checker)
    {
        var settingsPersistence = new JsonAppSettingsPersistence(Path.Combine(_directory, "settings.json"));
        var companionPersistence = new JsonCompanionPersistence(Path.Combine(_directory, "companion-state.json"));
        var settings = new SettingsViewModel(new MemorySettings(), new AutoStart());
        var usage = new UsageViewModel(new UsageStore([new EmptyProvider()]));
        return new SupportViewModel(
            checker,
            new StateTransferService(settingsPersistence, companionPersistence, checker.CurrentVersion),
            settings,
            usage,
            new Interaction());
    }

    private static GitHubReleaseUpdateChecker Checker(string current, object[] releases) =>
        new(new HttpClient(new JsonHandler(JsonSerializer.Serialize(releases))), current);

    private static object Release(string tag, bool draft = false, bool prerelease = false) => new
    {
        tag_name = tag,
        html_url = $"https://github.com/HappyNewPuzzle/PokeTokenBar/releases/tag/{tag}",
        name = $"PokeTokenBar {tag}",
        published_at = "2026-09-01T00:00:00Z",
        body = "notes",
        draft,
        prerelease,
    };

    private static string Root() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class BlockingUpdateChecker : IUpdateChecker
    {
        private readonly TaskCompletionSource<UpdateCheckResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public string CurrentVersion => "2.5.3";

        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            Started.TrySetResult();
            return _result.Task.WaitAsync(cancellationToken);
        }

        public void Complete(UpdateCheckResult result) => _result.TrySetResult(result);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    private sealed class MemorySettings : IAppSettingsPersistence
    {
        private AppSettings _settings = AppSettings.Default;
        public AppSettings? Load() => _settings;
        public void Save(AppSettings settings) => _settings = settings;
    }

    private sealed class AutoStart : IAutoStartService
    {
        public bool IsAvailable => false;
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

    private sealed class Interaction : IUserInteraction
    {
        public string? ChooseExportPath(string suggestedFileName) => null;
        public string? ChooseImportPath() => null;
        public bool ConfirmImport(StateTransferPreview incoming, StateTransferSummary current) => false;
        public void ShowMessage(string title, string message, bool error = false) { }
        public void CopyText(string text) { }
        public void OpenUri(Uri uri) { }
    }
}
