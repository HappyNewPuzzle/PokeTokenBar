# PokeTokenBar for Windows

PokeTokenBar is published as a self-contained Windows x64 application. No separate .NET runtime is required.

## Portable build

Extract `PokeTokenBar-<version>-win-x64.zip` to a writable folder and run `PokeTokenBar.exe`. Keep the complete extracted folder together. Updating the portable build means replacing the application folder while PokeTokenBar is closed.

## Installer

`PokeTokenBar-Setup-<version>.exe` is built from `installer/PokeTokenBar.iss` with Inno Setup 6. It installs per-user, creates a Start Menu shortcut, optionally creates a desktop shortcut, and upgrades the same installation in place without administrator elevation.

Uninstall removes application files, shortcuts, and PokeTokenBar's HKCU startup value. It deliberately preserves `%LOCALAPPDATA%\PokeTokenBar`, including settings, companion/economy/collection progress, notification state, and sprite cache.

## Data and startup

Application state is independent of the executable location and lives under `%LOCALAPPDATA%\PokeTokenBar`. “Launch at startup” uses the current executable's quoted path in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

Save export contains only PokeTokenBar settings and companion/economy/collection state. Provider credentials, access tokens, and raw session logs are never exported. Import validates the file, creates a pre-import backup, rolls back a partial write, and requires an app restart before the imported state is loaded.

## Updates and diagnostics

PokeTokenBar checks the latest stable GitHub release at startup and when the popup is reopened, with a 30-minute minimum interval. “Check for updates” performs an immediate check. PokeTokenBar never replaces its running executable or starts an installer automatically; the release page opens only after the user selects it.

“Copy diagnostics” copies a sanitized support report containing versions, architectures, provider availability, and boolean custom-root status. It excludes credentials, raw paths, session contents, prompts, and user identifiers.

## Building a release

Run `powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1`. Add `-BuildInstaller` to compile the Inno Setup source when Inno Setup 6 is installed. Portable artifacts are still produced when the installer compiler is unavailable.

Code signing is not automated because no signing identity is stored in the repository. Actual installer install/uninstall and Windows trust-prompt QA remain release-environment checks.
