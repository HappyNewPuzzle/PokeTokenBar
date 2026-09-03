using PokeTokenBar.Windows.App;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Tests;

public sealed class Phase7EProductionizationTests
{
    [Fact]
    public void CredentialControlsAreBoundWithoutTokenInputOrRevealUi()
    {
        var xaml = File.ReadAllText(Path.Combine(Root(), "src", "PokeTokenBar.Windows.App", "MainWindow.xaml"));

        Assert.Contains("Settings.CredentialAccessEnabled", xaml);
        Assert.Contains("Texts.RefreshCredentials", xaml);
        Assert.Contains("Usage.RefreshCommand", xaml);
        Assert.DoesNotContain("PasswordBox", xaml);
        Assert.DoesNotContain("RevealCredential", xaml);
    }

    [Fact]
    public void CredentialControlTextExistsForEverySupportedLanguage()
    {
        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            var text = new LocalizationService(language);
            Assert.False(string.IsNullOrWhiteSpace(text.CredentialAccess));
            Assert.False(string.IsNullOrWhiteSpace(text.CredentialAccessHint));
            Assert.False(string.IsNullOrWhiteSpace(text.RefreshCredentials));
        }
    }

    [Fact]
    public void ProductionCredentialStoreIsReadOnlyAndNeverLogsCredentialMaterial()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src", "PokeTokenBar.Windows.Infrastructure", "WindowsCredentialStore.cs"));

        Assert.Contains("CredReadW", source);
        Assert.DoesNotContain("CredWrite", source);
        Assert.DoesNotContain("CredDelete", source);
        Assert.DoesNotContain("Console.", source);
    }

    [Fact]
    public void ReleaseSigningIsOptInDiscoveredAndVerifiedInArtifactOrder()
    {
        var script = File.ReadAllText(Path.Combine(Root(), "scripts", "build-release.ps1"));

        Assert.Contains("CertificateThumbprint", script);
        Assert.Contains("Get-Command signtool.exe", script);
        Assert.Contains("Windows Kits\\10\\bin", script);
        Assert.Contains("Get-AuthenticodeSignature", script);
        Assert.True(script.IndexOf("Invoke-AuthenticodeSign (Join-Path $publishDir 'PokeTokenBar.exe')", StringComparison.Ordinal) <
                    script.IndexOf("Compress-Archive", StringComparison.Ordinal));
        Assert.True(script.LastIndexOf("Invoke-AuthenticodeSign $installerPath", StringComparison.Ordinal) >
                    script.IndexOf("& $isccPath", StringComparison.Ordinal));
    }

    [Fact]
    public void UnsignedReleaseAndTimestampRemainUnconfiguredByDefault()
    {
        var script = File.ReadAllText(Path.Combine(Root(), "scripts", "build-release.ps1"));

        Assert.Contains("$signingEnabled = -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)", script);
        Assert.Contains("[string]$TimestampUrl", script);
        Assert.DoesNotContain("timestamp.digicert.com", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".pfx", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallerPolicyPreservesUserDataAndCredentials()
    {
        var installer = File.ReadAllText(Path.Combine(Root(), "installer", "PokeTokenBar.iss"));

        Assert.Contains("PrivilegesRequired=lowest", installer);
        Assert.Contains("DefaultDirName={localappdata}\\Programs\\PokeTokenBar", installer);
        Assert.DoesNotContain("UninstallDelete", installer);
        Assert.DoesNotContain("CredDelete", installer);
        Assert.DoesNotContain("PokeTokenBarDataPaths", installer);
    }

    private static string Root() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
