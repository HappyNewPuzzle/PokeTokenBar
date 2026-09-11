Set-StrictMode -Version Latest

$script:ProjectOwnedPeNames = @(
    'PokeTokenBar.exe'
    'PokeTokenBar.dll'
    'PokeTokenBar.Windows.Core.dll'
    'PokeTokenBar.Windows.Infrastructure.dll'
)

function Get-ProjectOwnedPeNames {
    return $script:ProjectOwnedPeNames
}

function ConvertTo-NormalizedThumbprint([string]$Thumbprint) {
    if ([string]::IsNullOrWhiteSpace($Thumbprint)) { return $null }
    return ($Thumbprint -replace '\s', '').ToUpperInvariant()
}

function Assert-ReleaseSigningConfiguration(
    [bool]$RequireSigning,
    [bool]$BuildInstaller,
    [string]$CertificateThumbprint,
    [string]$TimestampUrl
) {
    $normalizedThumbprint = ConvertTo-NormalizedThumbprint $CertificateThumbprint
    if ($RequireSigning -and -not $BuildInstaller) {
        throw 'RequireSigning requires BuildInstaller so the complete signed release can be produced.'
    }
    if ($RequireSigning -and -not $normalizedThumbprint) {
        throw 'RequireSigning requires CertificateThumbprint.'
    }
    if ($normalizedThumbprint -and $normalizedThumbprint -notmatch '^[0-9A-F]{40}$') {
        throw 'CertificateThumbprint must be a 40-character hexadecimal thumbprint.'
    }
    if ($RequireSigning -and [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        throw 'RequireSigning requires TimestampUrl.'
    }
    if (-not $normalizedThumbprint -and -not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        throw 'TimestampUrl requires CertificateThumbprint.'
    }

    $normalizedTimestampUrl = $null
    if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $timestampUri = $null
        if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestampUri) -or
            $timestampUri.Scheme -notin @('http', 'https') -or
            -not [string]::IsNullOrEmpty($timestampUri.UserInfo) -or
            -not [string]::IsNullOrEmpty($timestampUri.Query) -or
            -not [string]::IsNullOrEmpty($timestampUri.Fragment) -or
            $TimestampUrl.IndexOfAny([char[]]@('"', "`r", "`n")) -ge 0) {
            throw 'TimestampUrl must be an absolute HTTP or HTTPS URI without credentials, query strings, fragments, quotes, or line breaks.'
        }
        $normalizedTimestampUrl = $timestampUri.AbsoluteUri
    }

    return [pscustomobject]@{
        SigningEnabled = [bool]$normalizedThumbprint
        Thumbprint = $normalizedThumbprint
        TimestampUrl = $normalizedTimestampUrl
    }
}

function New-InnoSignToolArgument(
    [string]$SignToolPath,
    [string]$CertificateThumbprint,
    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$CertificateStoreLocation,
    [string]$TimestampUrl
) {
    $storeArguments = if ($CertificateStoreLocation -eq 'LocalMachine') { '/s My /sm' } else { '/s My' }
    $command = '$q' + $SignToolPath + '$q sign /fd SHA256 /sha1 ' + $CertificateThumbprint +
        ' ' + $storeArguments + ' /tr $q' + $TimestampUrl + '$q /td SHA256 $f'
    return "/Sptbsign=$command"
}

function Assert-CodeSigningCertificate(
    [string]$StoreLocation,
    [string]$Thumbprint,
    [datetime]$Now = [datetime]::UtcNow
) {
    $certificatePath = "Cert:\$StoreLocation\My\$Thumbprint"
    $certificate = Get-Item -LiteralPath $certificatePath -ErrorAction SilentlyContinue
    if (-not $certificate) { throw "A signing certificate was not found at $certificatePath." }
    if (-not $certificate.HasPrivateKey) { throw "The signing certificate at $certificatePath has no accessible private key." }
    if ($Now -lt $certificate.NotBefore.ToUniversalTime() -or $Now -gt $certificate.NotAfter.ToUniversalTime()) {
        throw "The signing certificate at $certificatePath is not currently valid."
    }
    $codeSigningOid = '1.3.6.1.5.5.7.3.3'
    if (-not ($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId.Value -eq $codeSigningOid })) {
        throw "The certificate at $certificatePath is not valid for Code Signing."
    }
    return $certificate
}

function Get-ProjectOwnedPePaths([string]$PublishDirectory) {
    $paths = foreach ($name in $script:ProjectOwnedPeNames) {
        $path = Join-Path $PublishDirectory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Project-owned signing target is missing: $path"
        }
        $path
    }
    return $paths
}

function Assert-NoReleaseSigningMaterial([string]$Root) {
    $forbiddenExtensions = @('.pfx', '.p12', '.key', '.pem')
    $found = Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction Stop |
        Where-Object { $_.Extension.ToLowerInvariant() -in $forbiddenExtensions } |
        Select-Object -First 1
    if ($found) { throw 'Signing material is forbidden in release payloads.' }
}

function Assert-AuthenticodeSignature(
    [string]$Path,
    [string]$ExpectedThumbprint,
    [bool]$RequireTimestamp,
    [string]$SignToolPath
) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate) {
        throw "Authenticode verification failed for ${Path}: $($signature.Status)"
    }
    $actualThumbprint = ConvertTo-NormalizedThumbprint $signature.SignerCertificate.Thumbprint
    if ($actualThumbprint -ne (ConvertTo-NormalizedThumbprint $ExpectedThumbprint)) {
        throw "Authenticode signer mismatch for $Path."
    }
    if ($RequireTimestamp -and -not $signature.TimeStamperCertificate) {
        throw "Authenticode timestamp verification failed for $Path."
    }
    if ($SignToolPath) {
        & $SignToolPath verify /pa /all /v $Path
        if ($LASTEXITCODE -ne 0) { throw "SignTool verification failed for $Path with exit code $LASTEXITCODE" }
    }
}

function Assert-ZipPayload(
    [string]$ZipPath,
    [string]$SourceDirectory,
    [string[]]$ExpectedRelativePaths,
    [string]$ExpectedThumbprint,
    [bool]$RequireTimestamp,
    [string]$SignToolPath
) {
    $verificationDirectory = Join-Path ([IO.Path]::GetTempPath()) ("PokeTokenBar-zip-verify-" + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($verificationDirectory) | Out-Null
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($ZipPath, $verificationDirectory)
        Assert-NoReleaseSigningMaterial $verificationDirectory
        foreach ($relativePath in $ExpectedRelativePaths) {
            $sourcePath = Join-Path $SourceDirectory $relativePath
            $extractedPath = Join-Path $verificationDirectory $relativePath
            if (-not (Test-Path -LiteralPath $extractedPath -PathType Leaf)) {
                throw "Portable archive is missing project-owned file: $relativePath"
            }
            if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne
                (Get-FileHash -LiteralPath $extractedPath -Algorithm SHA256).Hash) {
                throw "Portable archive hash mismatch for project-owned file: $relativePath"
            }
            if ($ExpectedThumbprint) {
                Assert-AuthenticodeSignature $extractedPath $ExpectedThumbprint $RequireTimestamp $SignToolPath
            }
        }
    } finally {
        if (Test-Path -LiteralPath $verificationDirectory) {
            Remove-Item -LiteralPath $verificationDirectory -Recurse -Force
        }
    }
}

function New-Sha256Manifest([string]$OutputPath, [string[]]$ArtifactPaths) {
    $lines = foreach ($path in ($ArtifactPaths | Sort-Object { [IO.Path]::GetFileName($_) })) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Hash target is missing: $path" }
        '{0} *{1}' -f (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash, [IO.Path]::GetFileName($path)
    }
    [IO.File]::WriteAllLines($OutputPath, $lines, [Text.UTF8Encoding]::new($false))
    return $lines
}

Export-ModuleMember -Function @(
    'Get-ProjectOwnedPeNames'
    'ConvertTo-NormalizedThumbprint'
    'Assert-ReleaseSigningConfiguration'
    'New-InnoSignToolArgument'
    'Assert-CodeSigningCertificate'
    'Get-ProjectOwnedPePaths'
    'Assert-NoReleaseSigningMaterial'
    'Assert-AuthenticodeSignature'
    'Assert-ZipPayload'
    'New-Sha256Manifest'
)
