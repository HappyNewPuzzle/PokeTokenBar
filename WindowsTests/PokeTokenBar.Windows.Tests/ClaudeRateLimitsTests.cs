using System.Net;
using System.Text;
using System.Text.Json;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class ClaudeRateLimitsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-Claude-Credentials-{Guid.NewGuid():N}");

    public ClaudeRateLimitsTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task CredentialFileParsesOAuthTokenPlanAndMillisecondExpiry()
    {
        var path = CredentialFile("""
            {"claudeAiOauth":{"accessToken":"fixture-token","expiresAt":1788094800000,"subscriptionType":"max","rateLimitTier":"default_claude_max_20x"}}
            """);
        var provider = new ClaudeCredentialProvider(path, new FixedTimeProvider(Now));

        var credential = await provider.GetCredentialAsync();

        Assert.NotNull(credential);
        Assert.Equal("fixture-token", credential.AccessToken);
        Assert.Equal("max", credential.SubscriptionType);
        Assert.Equal("default_claude_max_20x", credential.RateLimitTier);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1788094800000), credential.ExpiresAt);
    }

    [Fact]
    public async Task CredentialFileAcceptsSecondsAndNoExpiry()
    {
        var seconds = CredentialFile("""
            {"claudeAiOauth":{"accessToken":"a","expiresAt":"1788094800"}}
            """);
        var noExpiry = CredentialFile("""
            {"claudeAiOauth":{"accessToken":"b"}}
            """, "no-expiry.json");

        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1788094800),
            (await new ClaudeCredentialProvider(seconds, new FixedTimeProvider(Now)).GetCredentialAsync())!.ExpiresAt);
        Assert.Null((await new ClaudeCredentialProvider(noExpiry).GetCredentialAsync())!.ExpiresAt);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"mcpOAuth\":{}}")]
    [InlineData("{\"claudeAiOauth\":null}")]
    [InlineData("{\"claudeAiOauth\":{\"accessToken\":\"\"}}")]
    [InlineData("not-json")]
    public async Task MissingOrMalformedAccountCredentialIsUnavailable(string json)
    {
        var provider = new ClaudeCredentialProvider(CredentialFile(json));

        Assert.Null(await provider.GetCredentialAsync());
    }

    [Fact]
    public async Task ExpiredCredentialIsUnavailable()
    {
        var provider = new ClaudeCredentialProvider(
            CredentialFile("""
                {"claudeAiOauth":{"accessToken":"old","expiresAt":1788087600000}}
                """),
            new FixedTimeProvider(Now));

        Assert.Null(await provider.GetCredentialAsync());
    }

    [Fact]
    public async Task DisabledCredentialAccessDoesNotReadClaudeCredentialFile()
    {
        var provider = new ClaudeCredentialProvider(
            CredentialFile("{\"claudeAiOauth\":{\"accessToken\":\"secret\"}}"),
            credentialAccessEnabled: () => false);

        Assert.Null(await provider.GetCredentialAsync());
    }

    [Fact]
    public void DefaultCredentialPathUsesWindowsUserProfile()
    {
        var profile = Path.Combine(Path.GetPathRoot(_directory)!, "Users", "tester");

        Assert.Equal(
            Path.Combine(profile, ".claude", ".credentials.json"),
            ClaudeCredentialProvider.GetDefaultFilePath(profile));
    }

    [Fact]
    public void LegacyUsageResponseParsesLimitsResetsAndPlan()
    {
        using var document = JsonDocument.Parse("""
            {"five_hour":{"utilization":14,"resets_at":"2026-08-30T14:00:00Z"},"seven_day":{"utilization":61.5,"resets_at":"2026-09-02T00:00:00Z"}}
            """);

        var status = ClaudeRateLimitsProvider.Parse(document.RootElement, Credential());

        Assert.Equal(14, status.FiveHour!.UsedPercent);
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 14, 0, 0, TimeSpan.Zero), status.FiveHour.ResetsAt);
        Assert.Equal(61.5, status.SevenDay!.UsedPercent);
        Assert.Equal("Max 20x", status.PlanDisplay);
    }

    [Fact]
    public void NewLimitsArrayFallsBackToSessionAndWeeklyAll()
    {
        using var document = JsonDocument.Parse("""
            {"limits":[{"kind":"weekly_scoped","percent":90},{"kind":"session","percent":12,"resets_at":"2026-08-30T13:00:00Z"},{"kind":"weekly_all","percent":34}]}
            """);

        var status = ClaudeRateLimitsProvider.Parse(document.RootElement, Credential());

        Assert.Equal(12, status.FiveHour!.UsedPercent);
        Assert.Equal(34, status.SevenDay!.UsedPercent);
    }

    [Theory]
    [InlineData("person@example.com", "person@example.com's Organization", "person@example.com")]
    [InlineData("person@example.com", "Team", "person@example.com · Team")]
    public void AccountDisplayAvoidsRedundantPersonalOrganization(
        string email,
        string organization,
        string expected)
    {
        var status = Status(10, 20) with
        {
            AccountEmail = email,
            AccountOrganizationName = organization,
        };

        Assert.Equal(expected, status.AccountDisplay);
    }

    [Fact]
    public async Task MissingCredentialMakesNoNetworkRequest()
    {
        var handler = new QueueHandler();
        var provider = new ClaudeRateLimitsProvider(
            new HttpClient(handler),
            new QueueCredentials((ClaudeOAuthCredential?)null));

        Assert.Null(await provider.FetchAsync());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ValidCredentialFetchesUsageAndBestEffortProfileWithRequiredHeaders()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, "{\"five_hour\":{\"utilization\":14},\"seven_day\":{\"utilization\":61}}"),
            Json(HttpStatusCode.OK, "{\"account\":{\"email\":\"person@example.com\"},\"organization\":{\"name\":\"Team\"}}"));
        var provider = new ClaudeRateLimitsProvider(
            new HttpClient(handler),
            new QueueCredentials(Credential()));

        var status = await provider.FetchAsync();

        Assert.Equal(14, status!.FiveHour!.UsedPercent);
        Assert.Equal("person@example.com · Team", status.AccountDisplay);
        Assert.Equal(
            ["https://api.anthropic.com/api/oauth/usage", "https://api.anthropic.com/api/oauth/profile"],
            handler.Requests.Select(request => request.Uri).ToArray());
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer fixture-token", request.Authorization);
            Assert.Equal("oauth-2025-04-20", request.Beta);
        });
    }

    [Fact]
    public async Task UnauthorizedUnchangedCredentialFailsWithoutLooping()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.Unauthorized, "{}"));
        var credentials = new QueueCredentials(Credential(), Credential());
        var provider = new ClaudeRateLimitsProvider(new HttpClient(handler), credentials);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.FetchAsync());
        Assert.Single(handler.Requests);
        Assert.Equal(2, credentials.Calls);
    }

    [Fact]
    public async Task UnauthorizedChangedCredentialRetriesOnce()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.Unauthorized, "{}"),
            Json(HttpStatusCode.OK, "{\"five_hour\":{\"utilization\":9}}"),
            Json(HttpStatusCode.NotFound, "{}"));
        var credentials = new QueueCredentials(
            Credential("old-token"),
            Credential("new-token"));
        var provider = new ClaudeRateLimitsProvider(new HttpClient(handler), credentials);

        var status = await provider.FetchAsync();

        Assert.Equal(9, status!.FiveHour!.UsedPercent);
        Assert.Equal("Bearer new-token", handler.Requests[1].Authorization);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task HttpAndMalformedUsageFailuresAreReportedToStoreBoundary()
    {
        var failed = new ClaudeRateLimitsProvider(
            new HttpClient(new QueueHandler(Json(HttpStatusCode.InternalServerError, "{}"))),
            new QueueCredentials(Credential()));
        var malformed = new ClaudeRateLimitsProvider(
            new HttpClient(new QueueHandler(Json(HttpStatusCode.OK, "not-json"))),
            new QueueCredentials(Credential()));

        await Assert.ThrowsAsync<HttpRequestException>(() => failed.FetchAsync());
        await Assert.ThrowsAnyAsync<JsonException>(() => malformed.FetchAsync());
    }

    [Fact]
    public async Task ProfileFailureDoesNotHideValidLimits()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, "{\"five_hour\":{\"utilization\":7}}"),
            Json(HttpStatusCode.InternalServerError, "{}"));
        var provider = new ClaudeRateLimitsProvider(
            new HttpClient(handler),
            new QueueCredentials(Credential()));

        Assert.Equal(7, (await provider.FetchAsync())!.FiveHour!.UsedPercent);
    }

    [Fact]
    public async Task LimitsFailurePreservesClaudeLocalUsageAndPreviousLimits()
    {
        var local = new FakeUsageProvider("claude_code", Daily(25));
        var limits = new FakeLimitsProvider { Value = Status(10, 20) };
        var store = new UsageStore([local], claudeRateLimitsProvider: limits);
        await store.RefreshAsync();
        limits.Value = null;
        limits.Error = new HttpRequestException("offline");

        await store.RefreshAsync();

        Assert.Equal(25, store.Snapshot("claude_code")!.TodayTotalTokens);
        Assert.Equal(10, store.ClaudeRateLimits!.FiveHour!.UsedPercent);
    }

    [Fact]
    public async Task OfficialLimitsKeepClaudeProviderVisibleWithoutLocalUsage()
    {
        var store = new UsageStore(
            [new FakeUsageProvider("claude_code", null)],
            claudeRateLimitsProvider: new FakeLimitsProvider { Value = Status(14, 0) });

        await store.RefreshAsync();

        Assert.Equal("claude_code", Assert.Single(store.Snapshots).ProviderId);
        Assert.Null(store.Snapshots[0].Today);
    }

    [Fact]
    public async Task ViewModelShowsClaudeRemainingPercentPlanAndAccount()
    {
        var limits = new FakeLimitsProvider
        {
            Value = Status(14, 0) with
            {
                AccountEmail = "person@example.com",
                AccountOrganizationName = "Team",
            },
        };
        var viewModel = new UsageViewModel(new UsageStore(
            [new FakeUsageProvider("claude_code", Daily(25))],
            new FixedTimeProvider(Now),
            claudeRateLimitsProvider: limits));

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasCodexRateLimits);
        Assert.Equal(86, viewModel.FiveHourRemainingPercent);
        Assert.Equal("86% remaining", viewModel.FiveHourRemainingText);
        Assert.Equal(100, viewModel.WeeklyRemainingPercent);
        Assert.Equal("Max 20x · person@example.com · Team", viewModel.OfficialLimitsMetadataText);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string CredentialFile(string json, string name = ".credentials.json")
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, json);
        return path;
    }

    private static ClaudeOAuthCredential Credential(string token = "fixture-token") =>
        new(token, Now.AddHours(1), "max", "default_claude_max_20x");

    private static ClaudeRateLimitStatus Status(double fiveHour, double weekly) =>
        new(
            new ClaudeRateLimitWindow(fiveHour, Now.AddHours(2)),
            new ClaudeRateLimitWindow(weekly, Now.AddDays(2)),
            "max",
            "default_claude_max_20x",
            null,
            null);

    private static DailyUsage Daily(long total) =>
        new(Now.ToString("yyyy-MM-dd"), total, 0, 0, 0, total, 0);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class QueueCredentials(params ClaudeOAuthCredential?[] values)
        : IClaudeCredentialProvider
    {
        private readonly Queue<ClaudeOAuthCredential?> _values = new(values);
        public int Calls { get; private set; }

        public Task<ClaudeOAuthCredential?> GetCredentialAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_values.Count == 0 ? null : _values.Dequeue());
        }
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<Request> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new Request(
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("anthropic-beta", out var values)
                    ? values.Single()
                    : null));
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed record Request(string Uri, string? Authorization, string? Beta);

    private sealed class FakeLimitsProvider : IClaudeRateLimitsProvider
    {
        public ClaudeRateLimitStatus? Value { get; set; }
        public Exception? Error { get; set; }

        public Task<ClaudeRateLimitStatus?> FetchAsync(CancellationToken cancellationToken = default) =>
            Error is null
                ? Task.FromResult(Value)
                : Task.FromException<ClaudeRateLimitStatus?>(Error);
    }

    private sealed class FakeUsageProvider(string id, DailyUsage? daily) : IUsageProvider
    {
        public string Id => id;
        public string DisplayName => id == "claude_code" ? "Claude Code" : id;
        public bool ReportsCost => true;
        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(daily);
        public Task<ProviderEnrichment> FetchEnrichmentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderEnrichment());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
