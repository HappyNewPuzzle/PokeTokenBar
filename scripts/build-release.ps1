param(
    [switch]$SkipTests,
    [switch]$BuildInstaller,
    [switch]$RequireSigning,
    [string]$CertificateThumbprint,
    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$CertificateStoreLocation = 'CurrentUser',
    [string]$TimestampUrl
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot '.git'))) {
    throw 'Repository root could not be verified.'
}

$project = Join-Path $repoRoot 'src\PokeTokenBar.Windows.App\PokeTokenBar.Windows.App.csproj'
$solution = Join-Path $repoRoot 'PokeTokenBar.Windows.sln'
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$publishRoot = Join-Path $artifactRoot 'publish'
$releaseRoot = Join-Path $artifactRoot 'release'
$stagingRoot = Join-Path $artifactRoot ('.release-staging-' + [guid]::NewGuid().ToString('N'))
$publishDir = Join-Path $stagingRoot 'publish\win-x64'
$stagingReleaseRoot = Join-Path $stagingRoot 'release'
$signingEnabled = $false
$signToolPath = $null
$isccPath = $null
$releaseSucceeded = $false

function Assert-SafeArtifactTarget([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe release target: $resolved"
    }
    return $resolved
}

function Find-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $roots = @()
    if (${env:ProgramFiles(x86)}) { $roots += Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin' }
    if ($env:ProgramFiles) { $roots += Join-Path $env:ProgramFiles 'Windows Kits\10\bin' }
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $candidate = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object { try { [version]$_.Name } catch { [version]'0.0' } } -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
            Select-Object -First 1
        if ($candidate) { return $candidate }
    }
    return $null
}

function Find-InnoCompiler {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidates = @()
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
    if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe' }
    if ($env:LOCALAPPDATA) { $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
    return $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}

function Invoke-AuthenticodeSign([string]$Path) {
    $arguments = @('sign', '/fd', 'SHA256', '/sha1', $script:CertificateThumbprint, '/s', 'My')
    if ($script:CertificateStoreLocation -eq 'LocalMachine') { $arguments += '/sm' }
    if ($script:TimestampUrl) { $arguments += @('/tr', $script:TimestampUrl, '/td', 'SHA256') }
    $arguments += $Path
    & $script:signToolPath @arguments
    if ($LASTEXITCODE -ne 0) { throw "Signing failed for $Path with exit code $LASTEXITCODE" }
    Assert-AuthenticodeSignature $Path $script:CertificateThumbprint ([bool]$script:TimestampUrl) $script:signToolPath
}

function Invoke-CheckedCommand([string]$FailureMessage, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$FailureMessage with exit code $LASTEXITCODE" }
}

$publishRoot = Assert-SafeArtifactTarget $publishRoot
$releaseRoot = Assert-SafeArtifactTarget $releaseRoot
$stagingRoot = Assert-SafeArtifactTarget $stagingRoot
[IO.Directory]::CreateDirectory($artifactRoot) | Out-Null

# A run owns these generated locations. Clearing them before preflight prevents a failed
# attempt from leaving an older release that can be mistaken for the current result.
foreach ($target in @($publishRoot, $releaseRoot)) {
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}

Import-Module (Join-Path $PSScriptRoot 'ReleaseSigning.psm1') -Force

try {
    $configuration = Assert-ReleaseSigningConfiguration `
        ([bool]$RequireSigning) ([bool]$BuildInstaller) $CertificateThumbprint $TimestampUrl
    $signingEnabled = $configuration.SigningEnabled
    $CertificateThumbprint = $configuration.Thumbprint
    $TimestampUrl = $configuration.TimestampUrl

    if ($BuildInstaller) {
        $isccPath = Find-InnoCompiler
        if (-not $isccPath) { throw 'BuildInstaller was requested, but Inno Setup 6 was not found.' }
    }

    if ($signingEnabled) {
        $certificate = Assert-CodeSigningCertificate $CertificateStoreLocation $CertificateThumbprint
        $signToolPath = Find-SignTool
        if (-not $signToolPath) { throw 'signtool.exe was not found on PATH or under Windows Kits 10.' }
    }

    [IO.Directory]::CreateDirectory($publishDir) | Out-Null
    [IO.Directory]::CreateDirectory($stagingReleaseRoot) | Out-Null

    Invoke-CheckedCommand 'Solution restore failed' { dotnet restore $solution }
    Invoke-CheckedCommand 'win-x64 restore failed' { dotnet restore $project -r win-x64 }
    Invoke-CheckedCommand 'dotnet clean failed' { dotnet clean $solution -c Release }
    if (-not $SkipTests) {
        Invoke-CheckedCommand 'dotnet test failed' { dotnet test $solution -c Release }
    }
    Invoke-CheckedCommand 'dotnet publish failed' {
        dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishDir
    }

    Assert-NoReleaseSigningMaterial $publishDir
    $ownedPeNames = @(Get-ProjectOwnedPeNames)
    $ownedPePaths = @(Get-ProjectOwnedPePaths $publishDir)
    if ($signingEnabled) {
        foreach ($path in $ownedPePaths) { Invoke-AuthenticodeSign $path }
    }

    $version = (dotnet msbuild $project -nologo -getProperty:Version | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Version lookup failed with exit code $LASTEXITCODE" }
    if ($version -notmatch '^\d+\.\d+\.\d+([-.+][0-9A-Za-z.-]+)?$') {
        throw "Invalid project version: $version"
    }

    $portableName = "PokeTokenBar-$version-win-x64"
    $portableDir = Join-Path $stagingReleaseRoot $portableName
    [IO.Directory]::CreateDirectory($portableDir) | Out-Null
    Copy-Item -Path (Join-Path $publishDir '*') -Destination $portableDir -Recurse
    Assert-NoReleaseSigningMaterial $portableDir

    $zipPath = Join-Path $stagingReleaseRoot "$portableName.zip"
    Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Assert-ZipPayload $zipPath $publishDir $ownedPeNames $CertificateThumbprint ([bool]$TimestampUrl) $signToolPath

    $finalArtifacts = @($zipPath)
    if ($BuildInstaller) {
        $innoArguments = @(
            "/DMyAppVersion=$version"
            "/DSourceDir=$publishDir"
            "/DOutputDir=$stagingReleaseRoot"
        )
        if ($RequireSigning) {
            $innoArguments += '/DProductionSigning=1'
            $innoArguments += New-InnoSignToolArgument `
                $signToolPath $CertificateThumbprint $CertificateStoreLocation $TimestampUrl
        }
        $innoArguments += (Join-Path $repoRoot 'installer\PokeTokenBar.iss')
        Invoke-CheckedCommand 'Installer compilation failed' { & $isccPath @innoArguments }

        $installerPath = Join-Path $stagingReleaseRoot "PokeTokenBar-Setup-$version.exe"
        if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
            throw "Installer output was not found: $installerPath"
        }
        if ($RequireSigning) {
            Assert-AuthenticodeSignature $installerPath $CertificateThumbprint $true $signToolPath
        } elseif ($signingEnabled) {
            Invoke-AuthenticodeSign $installerPath
        }
        $finalArtifacts += $installerPath
    }

    $manifestPath = Join-Path $stagingReleaseRoot 'SHA256SUMS.txt'
    $hashLines = @(New-Sha256Manifest $manifestPath $finalArtifacts)

    Move-Item -LiteralPath $stagingReleaseRoot -Destination $releaseRoot
    Move-Item -LiteralPath (Join-Path $stagingRoot 'publish') -Destination $publishRoot
    [IO.Directory]::Delete($stagingRoot)
    $releaseSucceeded = $true
    foreach ($line in $hashLines) { Write-Host $line }
} catch {
    if (-not $releaseSucceeded -and (Test-Path -LiteralPath $releaseRoot)) {
        Remove-Item -LiteralPath $releaseRoot -Recurse -Force
    }
    if (-not $releaseSucceeded -and (Test-Path -LiteralPath $publishRoot)) {
        Remove-Item -LiteralPath $publishRoot -Recurse -Force
    }
    throw
} finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

if (-not $releaseSucceeded) { throw 'Release did not complete.' }
Write-Host "Release ready: $releaseRoot"
