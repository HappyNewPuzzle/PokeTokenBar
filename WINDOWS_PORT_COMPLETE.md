# PokeTokenBar Windows Port — Complete

## Status

The macOS-to-Windows port is complete for the current upstream baseline.

- Windows stable release: `2.5.6`
- Windows release tag: `windows-v2.5.6`
- Windows release source commit: `af0f04314c64e3e366a57b357807b292375e419d`
- Upstream baseline: `upstream/main` at `b34673aa74f26375dffa3564caf730d0dfcbd171`
- Port status: **COMPLETE**
- Known required P0 gaps: **0**
- Known required P1 gaps: **0**
- Known required P2 gaps: **0**
- Known missing Windows-equivalent user-facing features: **0**

This document is the current completion record. Older parity-audit documents may describe intermediate states and should be treated as historical snapshots when they conflict with this file.

## What “complete” means

Completion means that the user-facing macOS functionality relevant to Windows has either:

- been implemented directly on Windows,
- been implemented through a Windows-native equivalent, or
- been classified as macOS-only where no literal Windows port is required.

The Windows implementation is not expected to be source-code-identical to the macOS implementation. Native platform mechanisms are intentionally different where appropriate, for example WPF and Windows tray behavior instead of AppKit and `NSStatusItem`.

## Implemented Windows scope

The Windows port includes the production paths required for normal use:

- C# / .NET 10 / WPF application
- Windows tray lifecycle and single-instance behavior
- startup, sleep/resume, refresh scheduling, and network-reconnect refresh
- provider usage collection for Codex, Claude Code, Gemini, Antigravity, Cursor, OpenCode, Hermes, Grok, GitHub Copilot, Kiro, Pi, and omp
- official provider limit/status surfaces where implemented by the upstream behavior
- localized reset countdowns and absolute local reset times
- floating Pokémon companion with animated rendering, persisted position, drag behavior, and size range up to 384 DIP
- companion progression, Egg lifecycle, evolution, graduation, Ditto lifecycle, rarity, nature, shiny behavior, and representative selection
- Shop, Bag, Collection, token economy, Rare Candy, Mint, Shiny Charm, and Egg products
- Egg-stage Shop cards remain visible while Egg purchases are locked
- seven-language UI: Korean, English, Japanese, Spanish, French, Portuguese, and German
- settings persistence, usage snapshot cache, diagnostics, export/import, backup, rollback, and recovery paths
- GitHub update checking and version/About UI
- portable Windows x64 ZIP packaging
- per-user Inno Setup installer
- release tests and manual native WPF QA

## 2.5.6 validation baseline

The `2.5.6` release was validated with:

- automated tests: `1,494 / 1,494` passed
- Release build: passed
- Portable launch: passed
- About/version `2.5.6`: passed
- Floating Pet maximum `384`: passed
- Floating Pet live resize, drag, restart persistence, aspect ratio, and rendering: passed
- Egg-stage Shop card visibility: passed
- Egg-stage purchase-button lock: passed
- localized Egg lock reason and card layout: passed
- ZIP internal executable integrity: passed
- public GitHub Release artifact SHA-256 verification: passed
- original companion state restoration after QA: passed

Public `2.5.6` artifact hashes:

- `PokeTokenBar-2.5.6-win-x64.zip`
  - SHA-256: `F0B3663488C963C93E2FC46918119B52D698773F811A6DB37EFD2BBA34DC3BE5`
- `PokeTokenBar-Setup-2.5.6.exe`
  - SHA-256: `3CF595320E3DE38F789D7E05A0CCAAB8F2DC1B5FEB378D825DD35CBC7E4E4379`

## Final upstream parity disposition

The last audited upstream changes through `b34673a` have been resolved as follows:

- `b34673a` — immediate refresh on network reconnection: implemented in Windows `2.5.5`
- `f2fe5ac` — absolute reset time next to limit countdowns: implemented in Windows `2.5.5`
- `cd3125a` — floating pet maximum 192 → 384: implemented in Windows `2.5.6`
- `56a3547` — keep Egg Shop cards visible while Egg-stage locked: implemented in Windows `2.5.6`
- `cb4fba7` — stale runtime-comment cleanup: no functional Windows gap
- `edc3b3c` — macOS menu-bar layer optimization: macOS-only
- `5f1ef52` — `/Applications/ChatGPT.app/Contents/Resources/codex` fallback: macOS-only for the current Windows evidence; no speculative Windows package-directory scanning is implemented

The `5f1ef52` audit found no stable or documented Windows ChatGPT bundled-Codex location that would justify a Windows fallback. Current Windows Codex discovery remains based on the application directory, `%USERPROFILE%\.codex\bin`, `%APPDATA%\npm`, and `PATH`.

## Code signing / SmartScreen

The current public Windows artifacts are intentionally Authenticode **NotSigned**.

This is not considered a functional parity gap for the current usage model because the application is intended for a single trusted user. A public production signing identity is therefore not required at this time.

If distribution expands to multiple external users, Authenticode productionization should be reopened before treating SmartScreen reputation as a release requirement. The existing signing-ready pipeline can be hardened and connected to a trusted signing identity at that time.

## Maintenance policy after completion

Do not create a new Windows version only to keep version numbers moving.

Create a new Windows release only when at least one of the following is true:

1. a real Windows bug is fixed,
2. an upstream commit introduces functionality relevant to Windows,
3. a Windows-specific feature is intentionally added,
4. a distribution/security requirement changes.

When upstream changes, audit only commits newer than the last known upstream baseline and classify them as:

- `COMPLETE`
- `WINDOWS EQUIVALENT`
- `PARTIAL`
- `MISSING`
- `MAC-ONLY`

with priority `P0`–`P3`.

If no relevant Windows gap exists, keep `windows-v2.5.6` as the stable completion baseline.

## Current maintenance baseline

- Windows branch: `windows-port`
- Windows stable tag: `windows-v2.5.6`
- Upstream reference: `b34673aa74f26375dffa3564caf730d0dfcbd171`
- Porting phase: **closed**
- Project phase: **maintenance / real-world use**
