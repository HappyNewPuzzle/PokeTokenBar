using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace PokeTokenBar.Windows.Tests;

public sealed class Phase11AProductionSigningTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PokeTokenBar-Phase11A-" + Guid.NewGuid().ToString("N"));

    public Phase11AProductionSigningTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("$true", "$true", "$null", "'https://tsa.example'", "CertificateThumbprint")]
    [InlineData("$true", "$true", "'0000000000000000000000000000000000000000'", "$null", "TimestampUrl")]
    [InlineData("$true", "$false", "'0000000000000000000000000000000000000000'", "'https://tsa.example'", "BuildInstaller")]
    [InlineData("$false", "$false", "'not-a-thumbprint'", "$null", "40-character")]
    public void SigningConfigurationRejectsInvalidProductionInputs(
        string requireSigning,
        string buildInstaller,
        string thumbprint,
        string timestampUrl,
        string expectedError)
    {
        var result = RunPowerShell(
            $"Assert-ReleaseSigningConfiguration {requireSigning} {buildInstaller} {thumbprint} {timestampUrl} | Out-Null");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expectedError, result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalUnsignedConfigurationRemainsAvailable()
    {
        var result = RunPowerShell(
            "$result = Assert-ReleaseSigningConfiguration $false $false $null $null; " +
            "if ($result.SigningEnabled -or $result.Thumbprint -or $result.TimestampUrl) { throw 'Unsigned configuration changed.' }");

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ExplicitCertificateEnablesSigningConfiguration()
    {
        var thumbprint = new string('A', 40);
        var result = RunPowerShell(
            $"$result = Assert-ReleaseSigningConfiguration $false $false '{thumbprint}' $null; " +
            "if (-not $result.SigningEnabled) { throw 'Signing was not enabled.' }");

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void InnoSignToolArgumentUsesInnoQuotePlaceholdersForSpacedPath()
    {
        const string signTool = @"C:\Program Files (x86)\Windows Kits\10\bin\signtool.exe";
        var thumbprint = new string('A', 40);
        var result = RunPowerShell(
            $"Write-Output ('ARG=' + (New-InnoSignToolArgument '{Quote(signTool)}' '{thumbprint}' CurrentUser 'https://tsa.example/'))");

        Assert.Equal(0, result.ExitCode);
        var argument = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("ARG=", StringComparison.Ordinal))[4..];
        Assert.Equal(
            $"/Sptbsign=$q{signTool}$q sign /fd SHA256 /sha1 {thumbprint} /s My /tr $qhttps://tsa.example/$q /td SHA256 $f",
            argument);
        Assert.DoesNotContain('"', argument);
    }

    [Theory]
    [InlineData("https://user:do-not-print@tsa.example/")]
    [InlineData("https://tsa.example/?token=do-not-print")]
    [InlineData("https://tsa.example/#do-not-print")]
    public void TimestampUrlRejectsCredentialsQueryAndFragmentWithoutEchoingInput(string timestampUrl)
    {
        var result = RunPowerShell(
            $"Assert-ReleaseSigningConfiguration $true $true '{new string('A', 40)}' '{Quote(timestampUrl)}' | Out-Null");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("without credentials, query strings, fragments", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("do-not-print", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SigningAllowlistContainsOnlyProjectOwnedBinaries()
    {
        var result = RunPowerShell("Get-ProjectOwnedPeNames | ConvertTo-Json -Compress");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            new[]
            {
                "PokeTokenBar.exe",
                "PokeTokenBar.dll",
                "PokeTokenBar.Windows.Core.dll",
                "PokeTokenBar.Windows.Infrastructure.dll",
            },
            System.Text.Json.JsonSerializer.Deserialize<string[]>(
                result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Single(line => line.StartsWith("[\"PokeTokenBar", StringComparison.Ordinal))));
        Assert.DoesNotContain("createdump.exe", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignatureVerifierRejectsUnsignedFile()
    {
        var unsignedFile = typeof(Phase11AProductionSigningTests).Assembly.Location;
        var result = RunPowerShell(
            $"Assert-AuthenticodeSignature '{Quote(unsignedFile)}' '{new string('0', 40)}' $true $null");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Authenticode verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleasePayloadRejectsSigningMaterial()
    {
        var payload = Path.Combine(_directory, "payload");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "do-not-log.pfx"), "private-content-do-not-log");

        var result = RunPowerShell($"Assert-NoReleaseSigningMaterial '{Quote(payload)}'");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("forbidden", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("do-not-log.pfx", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-content-do-not-log", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_directory, result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HashManifestUsesFinalBytesAndDeterministicNames()
    {
        var zip = Path.Combine(_directory, "PokeTokenBar-2.5.6-win-x64.zip");
        var installer = Path.Combine(_directory, "PokeTokenBar-Setup-2.5.6.exe");
        var manifest = Path.Combine(_directory, "SHA256SUMS.txt");
        File.WriteAllText(zip, "portable-final");
        File.WriteAllText(installer, "installer-final");

        var result = RunPowerShell(
            $"New-Sha256Manifest '{Quote(manifest)}' @('{Quote(installer)}','{Quote(zip)}') | Out-Null");

        Assert.Equal(0, result.ExitCode);
        var lines = File.ReadAllLines(manifest);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith(" *PokeTokenBar-2.5.6-win-x64.zip", lines[0]);
        Assert.EndsWith(" *PokeTokenBar-Setup-2.5.6.exe", lines[1]);
        Assert.All(lines, line => Assert.Matches("^[0-9A-F]{64} \\*[^\\\\/]+$", line));
    }

    [Fact]
    public void ZipVerifierChecksHashesRatherThanNamesOnly()
    {
        var source = Path.Combine(_directory, "source");
        Directory.CreateDirectory(source);
        foreach (var name in OwnedPeNames()) File.WriteAllText(Path.Combine(source, name), "signed-source:" + name);
        var zip = Path.Combine(_directory, "portable.zip");
        ZipFile.CreateFromDirectory(source, zip);

        var valid = RunPowerShell(
            $"Assert-ZipPayload '{Quote(zip)}' '{Quote(source)}' {PowerShellArray(OwnedPeNames())} $null $false $null");
        Assert.Equal(0, valid.ExitCode);

        File.WriteAllText(Path.Combine(source, "PokeTokenBar.dll"), "changed-after-packaging");
        var invalid = RunPowerShell(
            $"Assert-ZipPayload '{Quote(zip)}' '{Quote(source)}' {PowerShellArray(OwnedPeNames())} $null $false $null");
        Assert.NotEqual(0, invalid.ExitCode);
        Assert.Contains("hash mismatch", invalid.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingRequestedInstallerFailsAndRemovesStaleRelease()
    {
        var (root, release, script) = CreateHarness("missing-inno");

        var result = RunScript(
            script,
            "-BuildInstaller",
            new Dictionary<string, string?>
            {
                ["PATH"] = "",
                ["ProgramFiles"] = Path.Combine(root, "missing-program-files"),
                ["ProgramFiles(x86)"] = Path.Combine(root, "missing-program-files-x86"),
                ["LOCALAPPDATA"] = Path.Combine(root, "missing-local-app-data"),
            });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Inno Setup 6 was not found", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Release ready", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(release));
        Assert.Empty(Directory.GetDirectories(Path.Combine(root, "artifacts"), ".release-staging-*"));
    }

    [Fact]
    public void UnsignedReleaseCompletesWithoutSigningPreflightAndRetainsPublishDirectory()
    {
        var (root, release, script) = CreateHarness("unsigned-release");
        var fakeBin = Path.Combine(root, "fake-bin");
        Directory.CreateDirectory(fakeBin);
        File.WriteAllText(Path.Combine(fakeBin, "dotnet.cmd"), FakeDotnetScript());

        var result = RunScript(script, "-SkipTests", new Dictionary<string, string?> { ["PATH"] = fakeBin });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Release ready", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signing certificate", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(release, "PokeTokenBar-2.5.7-win-x64.zip")));
        Assert.True(File.Exists(Path.Combine(release, "SHA256SUMS.txt")));
        Assert.True(File.Exists(Path.Combine(root, "artifacts", "publish", "win-x64", "PokeTokenBar.exe")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(root, "artifacts"), ".release-staging-*"));
    }

    [Fact]
    public void FailedProductionPreflightRemovesStaleRelease()
    {
        var (root, release, script) = CreateHarness("production-preflight");

        var result = RunScript(
            script,
            "-RequireSigning -BuildInstaller -TimestampUrl 'https://tsa.example'",
            new Dictionary<string, string?>());

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("requires CertificateThumbprint", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Release ready", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(release));
        Assert.Empty(Directory.GetDirectories(Path.Combine(root, "artifacts"), ".release-staging-*"));
    }

    [Fact]
    public void InstallerAndReleaseOrderingAreProductionGated()
    {
        var script = File.ReadAllText(Path.Combine(Root(), "scripts", "build-release.ps1"));
        var installer = File.ReadAllText(Path.Combine(Root(), "installer", "PokeTokenBar.iss"));

        Assert.Contains("#ifdef ProductionSigning", installer);
        Assert.Contains("SignTool=ptbsign", installer);
        Assert.Contains("SignedUninstaller=yes", installer);
        Assert.True(script.IndexOf("foreach ($path in $ownedPePaths)", StringComparison.Ordinal) <
                    script.IndexOf("Compress-Archive", StringComparison.Ordinal));
        Assert.True(script.IndexOf("Assert-ZipPayload", StringComparison.Ordinal) <
                    script.IndexOf("Installer compilation failed", StringComparison.Ordinal));
        Assert.True(script.IndexOf("Assert-AuthenticodeSignature $installerPath", StringComparison.Ordinal) <
                    script.IndexOf("New-Sha256Manifest", StringComparison.Ordinal));
        Assert.True(script.IndexOf("Move-Item -LiteralPath $stagingReleaseRoot", StringComparison.Ordinal) <
                    script.LastIndexOf("Release ready", StringComparison.Ordinal));
        Assert.Contains("Move-Item -LiteralPath (Join-Path $stagingRoot 'publish') -Destination $publishRoot", script);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static string[] OwnedPeNames() =>
    [
        "PokeTokenBar.exe",
        "PokeTokenBar.dll",
        "PokeTokenBar.Windows.Core.dll",
        "PokeTokenBar.Windows.Infrastructure.dll",
    ];

    private static string PowerShellArray(IEnumerable<string> values) =>
        "@(" + string.Join(",", values.Select(value => $"'{Quote(value)}'")) + ")";

    private static string FakeDotnetScript() => """
        @echo off
        if "%~1"=="msbuild" (
          echo 2.5.7
          exit /b 0
        )
        if not "%~1"=="publish" exit /b 0
        :find_output
        if "%~1"=="-o" goto create_output
        shift
        goto find_output
        :create_output
        set "output=%~2"
        if not exist "%output%" mkdir "%output%"
        echo unsigned>"%output%\PokeTokenBar.exe"
        echo unsigned>"%output%\PokeTokenBar.dll"
        echo unsigned>"%output%\PokeTokenBar.Windows.Core.dll"
        echo unsigned>"%output%\PokeTokenBar.Windows.Infrastructure.dll"
        exit /b 0
        """;

    private (string Root, string Release, string Script) CreateHarness(string name)
    {
        var root = Path.Combine(_directory, name);
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        var release = Path.Combine(root, "artifacts", "release");
        Directory.CreateDirectory(release);
        File.WriteAllText(Path.Combine(release, "stale.zip"), "stale");
        var script = Path.Combine(root, "scripts", "build-release.ps1");
        File.Copy(Path.Combine(Root(), "scripts", "build-release.ps1"), script);
        File.Copy(Path.Combine(Root(), "scripts", "ReleaseSigning.psm1"),
            Path.Combine(root, "scripts", "ReleaseSigning.psm1"));
        return (root, release, script);
    }

    private static CommandResult RunPowerShell(string command)
    {
        var module = Path.Combine(Root(), "scripts", "ReleaseSigning.psm1");
        return RunEncodedCommand(
            $"$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; " +
            $"Import-Module '{Quote(module)}' -Force; {command}");
    }

    private static CommandResult RunScript(
        string script,
        string arguments,
        IReadOnlyDictionary<string, string?> environment)
    {
        var command = $"& '{Quote(script)}' {arguments}";
        return RunEncodedCommand(command, environment);
    }

    private static CommandResult RunEncodedCommand(
        string command,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var startInfo = new ProcessStartInfo(powerShell)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
        if (environment is not null)
        {
            foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new(process.ExitCode, standardOutput.GetAwaiter().GetResult() + standardError.GetAwaiter().GetResult());
    }

    private static string Quote(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string Root() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed record CommandResult(int ExitCode, string Output);
}
