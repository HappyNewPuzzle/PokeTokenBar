using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class CodexRateLimitsProvider : ICodexRateLimitsProvider
{
    private readonly CodexExecutableResolver _resolver;
    private readonly ICodexAppServerProcess _process;
    private readonly string _version;

    public CodexRateLimitsProvider()
        : this(
            new CodexExecutableResolver(),
            new CodexAppServerProcess(),
            typeof(CodexRateLimitsProvider).Assembly.GetName().Version?.ToString() ?? "0.1.0")
    {
    }

    public CodexRateLimitsProvider(
        CodexExecutableResolver resolver,
        ICodexAppServerProcess process,
        string version = "0.1.0")
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _version = string.IsNullOrWhiteSpace(version) ? "0.1.0" : version;
    }

    public async Task<CodexRateLimitStatus?> FetchAsync(
        CancellationToken cancellationToken = default)
    {
        var executable = _resolver.Resolve();
        if (executable is null)
        {
            return null;
        }

        var result = await _process.SendAsync(
                executable,
                CreateRequestLines(_version),
                responseId: 1,
                cancellationToken)
            .ConfigureAwait(false);
        return CodexRateLimitJsonParser.Parse(result);
    }

    public static IReadOnlyList<string> CreateRequestLines(string version)
    {
        var initialize = JsonSerializer.Serialize(new
        {
            method = "initialize",
            id = 0,
            @params = new
            {
                clientInfo = new
                {
                    name = "token_mac",
                    title = "PokeTokenBar",
                    version,
                },
                capabilities = new
                {
                    experimentalApi = true,
                },
            },
        });
        var initialized = JsonSerializer.Serialize(new
        {
            method = "initialized",
            @params = new { },
        });
        var read = JsonSerializer.Serialize(new
        {
            method = "account/rateLimits/read",
            id = 1,
            @params = new { },
        });
        return Array.AsReadOnly(new[] { initialize, initialized, read });
    }
}
