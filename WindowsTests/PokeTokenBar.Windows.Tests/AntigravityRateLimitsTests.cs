using System.Net;
using System.Text;
using System.Text.Json;
using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class AntigravityRateLimitsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri Endpoint = new("https://quota.test/v1internal:retrieveUserQuotaSummary");
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-Antigravity-Credentials-{Guid.NewGuid():N}");

    public AntigravityRateLimitsTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task PrimaryCredentialFileIsReadOnlyTokenSource()
    {
        var path = CredentialFile("primary.json", "fixture-token");
        var provider = new AntigravityCredentialProvider([path]);

        Assert.Equal("fixture-token", await provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task AlternateCredentialIsUsedWhenPrimaryIsMissingOrMalformed()
    {
        var missing = Path.Combine(_directory, "missing.json");
        var malformed = Path.Combine(_directory, "malformed.json");
        File.WriteAllText(malformed, "not-json");
        var valid = CredentialFile("alternate.json", "alternate-token");
        var provider = new AntigravityCredentialProvider([missing, malformed, valid]);

        Assert.Equal("alternate-token", await provider.GetAccessTokenAsync());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"token\":null}")]
    [InlineData("{\"token\":\"\"}")]
    [InlineData("not-json")]
    public async Task MissingOrMalformedCredentialIsUnavailable(string json)
    {
        var path = Path.Combine(_directory, $"credential-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);

        Assert.Null(await new AntigravityCredentialProvider([path]).GetAccessTokenAsync());
    }

    [Fact]
    public void DefaultCredentialPathsUseWindowsUserProfile()
    {
        var profile = Path.Combine(Path.GetPathRoot(_directory)!, "Users", "tester");

        Assert.Equal(
            [
                Path.Combine(profile, ".gemini", "jetski-standalone-oauth-token"),
                Path.Combine(profile, ".gemini", "antigravity", "jetski-standalone-oauth-token"),
            ],
            AntigravityCredentialProvider.GetDefaultFilePaths(profile));
    }

    [Fact]
    public async Task MissingCredentialMakesNoNetworkRequest()
    {
        var handler = new QueueHandler();
        var provider = Provider(handler, new QueueCredentials((string?)null));

        Assert.Null(await provider.FetchAsync());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void QuotaResponseParsesAllGroupsBucketsAndUsedSemantics()
    {
        using var document = JsonDocument.Parse(SampleJson);

        var status = AntigravityRateLimitsProvider.Parse(document.RootElement);

        Assert.True(status.HasVisibleLimit);
        Assert.Equal(2, status.Groups.Count);
        Assert.Equal(15, status.Groups[0].FiveHour!.UsedPercent, precision: 6);
        Assert.Equal(6, status.Groups[0].Weekly!.UsedPercent, precision: 6);
        Assert.Equal(50, status.MaxPrimaryUsedPercent);
    }

    [Theory]
    [InlineData(-0.2, 100, 0)]
    [InlineData(0, 100, 0)]
    [InlineData(0.14, 86, 14)]
    [InlineData(1, 0, 100)]
    [InlineData(1.2, 0, 100)]
    public void RemainingFractionClampsUsedAndRemaining(
        double fraction,
        double expectedUsed,
        int expectedRemaining)
    {
        var bucket = new AntigravityQuotaBucket("id", "name", "5h", null, null, fraction);

        Assert.Equal(expectedUsed, bucket.UsedPercent, precision: 6);
        Assert.Equal(expectedRemaining, bucket.RemainingPercent);
    }

    [Fact]
    public void BucketWindowAndResetSemanticsMatchMacOS()
    {
        var bucket = new AntigravityQuotaBucket(
            "gemini-weekly",
            "Weekly",
            null,
            "2026-09-02T00:00:00Z",
            null,
            0.5);

        Assert.True(bucket.IsWeekly);
        Assert.False(bucket.IsFiveHour);
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero), bucket.ResetsAt);
    }

    [Fact]
    public async Task ValidRequestUsesPostBearerJsonAndAntigravityUserAgent()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, SampleJson));
        var provider = Provider(handler, new QueueCredentials("fixture-token"));

        var status = await provider.FetchAsync();

        Assert.True(status!.HasVisibleLimit);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("Bearer fixture-token", request.Authorization);
        Assert.Equal("antigravity/2.9.1", request.UserAgent);
        Assert.Equal("application/json; charset=utf-8", request.ContentType);
        Assert.Equal("{}", request.Body);
    }

    [Fact]
    public async Task NonAuthFailureFallsBackToNextQuotaEndpoint()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.InternalServerError, "{}"),
            Json(HttpStatusCode.OK, SampleJson));
        var provider = new AntigravityRateLimitsProvider(
            new HttpClient(handler),
            new QueueCredentials("token"),
            [new Uri("https://first.test/"), new Uri("https://second.test/")]);

        Assert.True((await provider.FetchAsync())!.HasVisibleLimit);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void CloudCodeEnvironmentEndpointPrecedesDailyAndPrimary()
    {
        var endpoints = AntigravityRateLimitsProvider.GetDefaultEndpoints("https://custom.test/base/");

        Assert.Equal("https://custom.test/base/v1internal:retrieveUserQuotaSummary", endpoints[0].AbsoluteUri);
        Assert.Equal(AntigravityRateLimitsProvider.DailyUri, endpoints[1]);
        Assert.Equal(AntigravityRateLimitsProvider.PrimaryUri, endpoints[2]);
    }

    [Fact]
    public async Task UnauthorizedUnchangedCredentialFailsWithoutLooping()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.Unauthorized, "{}"));
        var credentials = new QueueCredentials("token", "token");
        var provider = Provider(handler, credentials);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.FetchAsync());
        Assert.Single(handler.Requests);
        Assert.Equal(2, credentials.Calls);
    }

    [Fact]
    public async Task UnauthorizedChangedCredentialRetriesOnce()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.Unauthorized, "{}"),
            Json(HttpStatusCode.OK, SampleJson));
        var provider = Provider(handler, new QueueCredentials("old", "new"));

        Assert.True((await provider.FetchAsync())!.HasVisibleLimit);
        Assert.Equal("Bearer new", handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task RateLimitStopsEndpointFallback()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.TooManyRequests, "{}"));
        var provider = new AntigravityRateLimitsProvider(
            new HttpClient(handler),
            new QueueCredentials("token"),
            [new Uri("https://first.test/"), new Uri("https://second.test/")]);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => provider.FetchAsync());
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MalformedAndHttpFailuresReachStoreBoundary()
    {
        var malformed = Provider(
            new QueueHandler(Json(HttpStatusCode.OK, "not-json")),
            new QueueCredentials("token"));
        var failed = Provider(
            new QueueHandler(Json(HttpStatusCode.InternalServerError, "{}")),
            new QueueCredentials("token"));

        await Assert.ThrowsAnyAsync<JsonException>(() => malformed.FetchAsync());
        await Assert.ThrowsAsync<HttpRequestException>(() => failed.FetchAsync());
    }

    [Fact]
    public async Task QuotaFailurePreservesLocalUsageAndPreviousQuota()
    {
        var limits = new FakeLimits { Value = Status() };
        var store = new UsageStore(
            [new FakeUsage("antigravity", Daily(25), reportsCost: false)],
            antigravityRateLimitsProvider: limits);
        await store.RefreshAsync();
        limits.Error = new HttpRequestException("offline");

        await store.RefreshAsync();

        Assert.Equal(25, store.Snapshot("antigravity")!.TodayTotalTokens);
        Assert.Equal(2, store.AntigravityRateLimits!.Groups.Count);
    }

    [Fact]
    public async Task OfficialQuotaKeepsAntigravityVisibleWithoutLocalUsage()
    {
        var store = new UsageStore(
            [new FakeUsage("antigravity", null, reportsCost: false)],
            antigravityRateLimitsProvider: new FakeLimits { Value = Status() });

        await store.RefreshAsync();

        Assert.Equal("antigravity", Assert.Single(store.Snapshots).ProviderId);
        Assert.Null(store.Snapshots[0].Today);
    }

    [Fact]
    public async Task ViewModelDisplaysEveryQuotaBucketWithoutTwoRowLoss()
    {
        var viewModel = new UsageViewModel(
            new UsageStore(
                [new FakeUsage("antigravity", Daily(1), reportsCost: false)],
                new FixedTimeProvider(Now),
                antigravityRateLimitsProvider: new FakeLimits { Value = Status() }));

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasCodexRateLimits);
        Assert.True(viewModel.HasAntigravityLimitRows);
        Assert.Equal(4, viewModel.AntigravityLimitRows.Count);
        Assert.Contains(viewModel.AntigravityLimitRows, row =>
            row.Label == "Gemini Models · Five Hour Limit Remaining" &&
            row.RemainingPercent == 85 &&
            row.RemainingText == "85% remaining");
        Assert.Contains(viewModel.AntigravityLimitRows, row => row.Label.StartsWith("Claude and GPT models"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string CredentialFile(string name, string token)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, JsonSerializer.Serialize(new { token }));
        return path;
    }

    private static AntigravityRateLimitsProvider Provider(
        QueueHandler handler,
        IAntigravityCredentialProvider credentials) =>
        new(new HttpClient(handler), credentials, [Endpoint]);

    private static AntigravityRateLimitStatus Status()
    {
        using var document = JsonDocument.Parse(SampleJson);
        return AntigravityRateLimitsProvider.Parse(document.RootElement);
    }

    private static DailyUsage Daily(long total) =>
        new(Now.ToString("yyyy-MM-dd"), total, 0, 0, 0, total, 0);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class QueueCredentials(params string?[] values) : IAntigravityCredentialProvider
    {
        private readonly Queue<string?> _values = new(values);
        public int Calls { get; private set; }

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_values.Count == 0 ? null : _values.Dequeue());
        }
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<Request> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new Request(
                request.Method,
                request.Headers.Authorization?.ToString(),
                request.Headers.UserAgent.ToString(),
                request.Content?.Headers.ContentType?.ToString(),
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)));
            return _responses.Dequeue();
        }
    }

    private sealed record Request(
        HttpMethod Method,
        string? Authorization,
        string UserAgent,
        string? ContentType,
        string? Body);

    private sealed class FakeLimits : IAntigravityRateLimitsProvider
    {
        public AntigravityRateLimitStatus? Value { get; set; }
        public Exception? Error { get; set; }

        public Task<AntigravityRateLimitStatus?> FetchAsync(CancellationToken cancellationToken = default) =>
            Error is null
                ? Task.FromResult(Value)
                : Task.FromException<AntigravityRateLimitStatus?>(Error);
    }

    private sealed class FakeUsage(string id, DailyUsage? daily, bool reportsCost) : IUsageProvider
    {
        public string Id => id;
        public string DisplayName => "Antigravity";
        public bool ReportsCost => reportsCost;
        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(daily);
        public Task<ProviderEnrichment> FetchEnrichmentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderEnrichment());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private const string SampleJson = """
        {
          "groups": [
            {
              "displayName": "Gemini Models",
              "description": "Gemini group",
              "buckets": [
                {"bucketId":"gemini-weekly","displayName":"Weekly Limit Remaining","window":"weekly","resetTime":"2026-09-02T00:00:00Z","remainingFraction":0.94},
                {"bucketId":"gemini-5h","displayName":"Five Hour Limit Remaining","window":"5h","resetTime":"2026-08-30T14:00:00Z","remainingFraction":0.85}
              ]
            },
            {
              "displayName": "Claude and GPT models",
              "buckets": [
                {"bucketId":"3p-weekly","displayName":"Weekly Limit Remaining","window":"weekly","resetTime":"2026-09-03T00:00:00Z","remainingFraction":1.0},
                {"bucketId":"3p-5h","displayName":"Five Hour Limit Remaining","window":"5h","resetTime":"2026-08-30T15:00:00Z","remainingFraction":0.50}
              ]
            }
          ],
          "description": "Quota summary"
        }
        """;
}
