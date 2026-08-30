using System.Text.Json;

namespace PokeTokenBar.Windows.Infrastructure;

public interface ICodexAppServerProcess
{
    Task<JsonElement> SendAsync(
        CodexExecutable executable,
        IReadOnlyList<string> inputLines,
        int responseId,
        CancellationToken cancellationToken = default);
}
