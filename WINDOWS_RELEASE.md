# PokeTokenBar for Windows

PokeTokenBar is published as a self-contained Windows x64 application. No separate .NET runtime is required.

## Portable build

Extract `PokeTokenBar-<version>-win-x64.zip` to a writable folder and run `PokeTokenBar.exe`. Keep the complete extracted folder together. Updating the portable build means replacing the application folder while PokeTokenBar is closed.

## Installer

`PokeTokenBar-Setup-<version>.exe` is built from `installer/PokeTokenBar.iss` with Inno Setup 6. It installs per-user, creates a Start Menu shortcut, optionally creates a desktop shortcut, and upgrades the same installation in place without administrator elevation.

Uninstall removes application files, shortcuts, and PokeTokenBar's HKCU startup value. It deliberately preserves `%LOCALAPPDATA%\PokeTokenBar`, including settings, companion/economy/collection progress, notification state, and sprite cache. Provider CLI files and Windows Credential Manager entries are read-only inputs and are never removed by install, upgrade, or uninstall.

## Data and startup

Application state is independent of the executable location and lives under `%LOCALAPPDATA%\PokeTokenBar`. “Launch at startup” uses the current executable's quoted path in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

Save export contains only PokeTokenBar settings and companion/economy/collection state. Provider credentials, access tokens, and raw session logs are never exported. Import validates the file, creates a pre-import backup, rolls back a partial write, and requires an app restart before the imported state is loaded.

## Updates and diagnostics

PokeTokenBar checks the latest stable `windows-vX.Y.Z` GitHub release at startup and when the popup is reopened, with a 30-minute minimum interval. “Check for updates” performs an immediate check. PokeTokenBar never replaces its running executable or starts an installer automatically; the release page opens only after the user selects it.

“Copy diagnostics” copies a sanitized support report containing versions, runtime/culture, update and provider status, persistence-file health, and recent recovery/error categories. It excludes exception messages, credentials, raw paths, session contents, prompts, and user identifiers. Malformed settings and caches are isolated per file; companion progress prefers its validated last-known-good backup without replaying game events.

## Building a release

Run `powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1`. Add `-BuildInstaller` to compile the Inno Setup source when Inno Setup 6 is installed. If `-BuildInstaller` is requested and the compiler is unavailable, the build fails.

Unsigned release and development builds remain supported and are the default. They require no certificate. `-RequireSigning` is an optional stricter production gate:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1 -BuildInstaller
```

Use `-RequireSigning` for a production signed release. It requires `-BuildInstaller`, a trusted Code Signing certificate with an accessible private key in the selected Windows `My` certificate store, its exact SHA-1 thumbprint, an RFC 3161 timestamp URL, SignTool, and Inno Setup 6:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1 `
  -RequireSigning -BuildInstaller `
  -CertificateThumbprint <SHA1> `
  -CertificateStoreLocation CurrentUser `
  -TimestampUrl <provider-rfc3161-url>
```

The production gate signs only `PokeTokenBar.exe`, `PokeTokenBar.dll`, `PokeTokenBar.Windows.Core.dll`, `PokeTokenBar.Windows.Infrastructure.dll`, the Inno uninstaller, and the final installer. Microsoft and .NET runtime files are not re-signed. Application signatures are verified before ZIP creation; ZIP copies are extracted and checked for matching SHA-256 hashes, the expected signer, and timestamps. The final installer is verified before `SHA256SUMS.txt` is generated. A successful run retains `artifacts\publish\win-x64` and promotes final release files to `artifacts\release`; a failed run promotes neither.

Verify an extracted project binary or installer with:

```powershell
Get-AuthenticodeSignature -LiteralPath .\PokeTokenBar.exe | Format-List Status,SignerCertificate,TimeStamperCertificate
```

The repository stores no certificate, private key, or password. A trusted production certificate and timestamp service must be supplied by the release environment.

A valid Authenticode signature establishes publisher identity, but SmartScreen reputation is a separate service signal and is not guaranteed by signing alone. Actual signed-artifact, install/uninstall, and trust-prompt QA therefore remains a release-environment check.
