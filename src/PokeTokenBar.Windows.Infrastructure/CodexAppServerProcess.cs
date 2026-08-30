using System.Diagnostics;
using System.Text.Json;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class CodexAppServerProcess : ICodexAppServerProcess
{
    private readonly TimeSpan _timeout;

    public CodexAppServerProcess(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(20);
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public async Task<JsonElement> SendAsync(
        CodexExecutable executable,
        IReadOnlyList<string> inputLines,
        int responseId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(inputLines);
        cancellationToken.ThrowIfCancellationRequested();

        using var timeoutSource = new CancellationTokenSource(_timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        using var process = new Process { StartInfo = CreateStartInfo(executable) };

        try
        {
            if (!process.Start())
            {
                throw new CodexAppServerException($"Could not start {executable.Path}.");
            }

            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            foreach (var line in inputLines)
            {
                await process.StandardInput.WriteLineAsync(line.AsMemory(), linkedSource.Token)
                    .ConfigureAwait(false);
            }

            await process.StandardInput.FlushAsync(linkedSource.Token).ConfigureAwait(false);

            while (true)
            {
                var line = await process.StandardOutput
                    .ReadLineAsync(linkedSource.Token)
                    .ConfigureAwait(false);
                if (line is null)
                {
                    await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
                    var stderr = await stderrTask.ConfigureAwait(false);
                    if (process.ExitCode != 0)
                    {
                        throw new CodexAppServerException(
                            $"Codex app-server exited with code {process.ExitCode}: {Tail(stderr)}");
                    }

                    throw new CodexAppServerException(
                        "Codex app-server exited without the requested JSON-RPC response.");
                }

                if (TryReadResponse(line, responseId, out var result, out var rpcError))
                {
                    if (rpcError is not null)
                    {
                        throw new CodexAppServerException($"JSON-RPC error: {rpcError}");
                    }

                    return result!.Value;
                }
            }
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Codex app-server did not respond within {_timeout.TotalSeconds:0.#} seconds.");
        }
        finally
        {
            TryKill(process);
        }
    }

    public static bool TryReadResponse(
        string? line,
        int responseId,
        out JsonElement? result,
        out string? errorMessage)
    {
        result = null;
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.Number ||
                !id.TryGetInt32(out var parsedId) ||
                parsedId != responseId)
            {
                return false;
            }

            if (root.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object)
            {
                errorMessage = error.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String
                    ? message.GetString()
                    : error.GetRawText();
                return true;
            }

            if (!root.TryGetProperty("result", out var responseResult))
            {
                return false;
            }

            result = responseResult.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ProcessStartInfo CreateStartInfo(CodexExecutable executable)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        switch (executable.Kind)
        {
            case CodexExecutableKind.CommandScript:
                startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                startInfo.Arguments =
                    $"/d /s /c \"\"{executable.Path}\" app-server --stdio\"";
                break;
            case CodexExecutableKind.PowerShellScript:
                startInfo.FileName = "powershell.exe";
                startInfo.ArgumentList.Add("-NoLogo");
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-File");
                startInfo.ArgumentList.Add(executable.Path);
                startInfo.ArgumentList.Add("app-server");
                startInfo.ArgumentList.Add("--stdio");
                break;
            default:
                startInfo.FileName = executable.Path;
                startInfo.ArgumentList.Add("app-server");
                startInfo.ArgumentList.Add("--stdio");
                break;
        }

        return startInfo;
    }

    private static string Tail(string value) =>
        value.Length <= 300 ? value.Trim() : value[^300..].Trim();

    private static void TryKill(Process process)
    {
        try
        {
            if (process.Id > 0 && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2_000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
