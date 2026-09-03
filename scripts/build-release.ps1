param(
    [switch]$SkipTests,
    [switch]$BuildInstaller,
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
$publishDir = Join-Path $artifactRoot 'publish\win-x64'
$releaseRoot = Join-Path $artifactRoot 'release'
$signingEnabled = -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)
$signToolPath = $null

function Find-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $roots = @()
    if (${env:ProgramFiles(x86)}) { $roots += Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin' }
    if ($env:ProgramFiles) { $roots += Join-Path $env:ProgramFiles 'Windows Kits\10\bin' }
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $candidate = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($candidate) { return $candidate }
    }
    return $null
}

function Invoke-AuthenticodeSign([string]$Path) {
    $arguments = @('sign', '/fd', 'SHA256', '/sha1', $script:CertificateThumbprint, '/s', 'My')
    if ($script:CertificateStoreLocation -eq 'LocalMachine') { $arguments += '/sm' }
    if ($script:TimestampUrl) { $arguments += @('/tr', $script:TimestampUrl, '/td', 'SHA256') }
    $arguments += $Path
    & $script:signToolPath @arguments
    if ($LASTEXITCODE -ne 0) { throw "signing failed for $Path with exit code $LASTEXITCODE" }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate) {
        throw "signature verification failed for ${Path}: $($signature.Status)"
    }
    if ($script:TimestampUrl -and -not $signature.TimeStamperCertificate) {
        throw "timestamp verification failed for $Path"
    }
}

if ($signingEnabled) {
    $CertificateThumbprint = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
    if ($CertificateThumbprint -notmatch '^[0-9A-F]{40}$') { throw 'CertificateThumbprint must be a 40-character hexadecimal thumbprint.' }
    if ($TimestampUrl) {
        $timestampUri = $null
        if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestampUri) -or
            $timestampUri.Scheme -notin @('http', 'https')) {
            throw 'TimestampUrl must be an absolute HTTP or HTTPS URI.'
        }
    }
    $certificatePath = "Cert:\$CertificateStoreLocation\My\$CertificateThumbprint"
    $certificate = Get-Item -LiteralPath $certificatePath -ErrorAction SilentlyContinue
    if (-not $certificate -or -not $certificate.HasPrivateKey) {
        throw "A signing certificate with a private key was not found at $certificatePath."
    }
    $signToolPath = Find-SignTool
    if (-not $signToolPath) { throw 'signtool.exe was not found on PATH or under Windows Kits 10.' }
}

foreach ($target in @($publishDir, $releaseRoot)) {
    $resolved = [IO.Path]::GetFullPath($target)
    if (-not $resolved.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe release target: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw "solution restore failed with exit code $LASTEXITCODE" }
dotnet restore $project -r win-x64
if ($LASTEXITCODE -ne 0) { throw "win-x64 restore failed with exit code $LASTEXITCODE" }
dotnet clean $solution -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet clean failed with exit code $LASTEXITCODE" }
if (-not $SkipTests) {
    dotnet test $solution -c Release
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }
}
dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

if ($signingEnabled) {
    Invoke-AuthenticodeSign (Join-Path $publishDir 'PokeTokenBar.exe')
}

$version = (dotnet msbuild $project -nologo -getProperty:Version | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0) { throw "version lookup failed with exit code $LASTEXITCODE" }
if ($version -notmatch '^\d+\.\d+\.\d+([-.+][0-9A-Za-z.-]+)?$') {
    throw "Invalid project version: $version"
}

$portableName = "PokeTokenBar-$version-win-x64"
$portableDir = Join-Path $releaseRoot $portableName
New-Item -ItemType Directory -Path $portableDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $portableDir -Recurse
$zipPath = Join-Path $releaseRoot "$portableName.zip"
Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if (-not ($archive.Entries | Where-Object FullName -eq 'PokeTokenBar.exe')) {
        throw 'Portable archive does not contain PokeTokenBar.exe.'
    }
} finally { $archive.Dispose() }

if ($BuildInstaller) {
    $iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $isccPath = if ($iscc) { $iscc.Source } else { $null }
    if (-not $isccPath) {
        $candidates = @()
        if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
        if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe' }
        if ($env:LOCALAPPDATA) { $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }

        $isccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if ($isccPath) {
        & $isccPath "/DMyAppVersion=$version" "/DSourceDir=$publishDir" "/DOutputDir=$releaseRoot" (Join-Path $repoRoot 'installer\PokeTokenBar.iss')
        if ($LASTEXITCODE -ne 0) { throw "installer compilation failed with exit code $LASTEXITCODE" }
        $installerPath = Join-Path $releaseRoot "PokeTokenBar-Setup-$version.exe"
        if (-not (Test-Path -LiteralPath $installerPath)) { throw "installer output was not found: $installerPath" }
        if ($signingEnabled) { Invoke-AuthenticodeSign $installerPath }
    } else {
        Write-Warning 'Inno Setup 6 was not found; portable artifacts are complete and installer compilation was skipped.'
    }
}

Write-Host "Release ready: $releaseRoot"
