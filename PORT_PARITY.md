# PokeTokenBar Windows Port Parity Audit

## 1. Audit Baseline

This is a code-based parity audit, not an implementation plan disguised as completed work. README claims were used only for orientation; every finding below is grounded in source or tests.

| Item | Value |
|---|---|
| Audit date | 2026-09-01 |
| Windows branch | `windows-port` |
| Windows commit | `1e0b022` plus the Phase 6 working tree |
| macOS baseline | `upstream/main` at `37763d3c367068492c18f6e51b45977c2d27f6d5` |
| Merge base | `4c29ca0fa28c1fb67929517542d4e58d802171f8` |
| Initial `git status --short` | Existing unrelated `.gitignore` change and two untracked Korean documentation files |
| Scope | Product behavior, runtime integration, settings, persistence, distribution, and meaningful test coverage |

Status definitions:

- **COMPLETE**: behavior and integration are materially at parity.
- **PARTIAL**: useful behavior exists, but an important path, option, or integration is absent.
- **MISSING**: no production implementation of the macOS behavior exists.
- **WINDOWS EQUIVALENT**: different native mechanism, equivalent user outcome.
- **MAC-ONLY / N/A**: the exact behavior is inherent to the macOS shell and needs no literal Windows port.

Estimates are implementation effort after dependencies are available: **S** (up to 2 engineering days), **M** (3–5 days), **L** (1–2 weeks), **XL** (more than 2 weeks). They are comparative, not delivery commitments.

## 2. Executive Summary

The Windows port now has a complete P0 production path: twelve providers, limits, companion/economy UI, native lifecycle integration, update checks, safe save transfer, sanitized diagnostics, and reproducible self-contained portable packaging. Inno Setup source supplies the per-user installer path; installer compilation/signing and real Antigravity environment validation remain release-environment work.

The full matrix in section 18 contains **93 atomic feature rows**:

| Status | Count | Share |
|---|---:|---:|
| COMPLETE | 68 | 73.1% |
| PARTIAL | 8 | 8.6% |
| MISSING | 4 | 4.3% |
| WINDOWS EQUIVALENT | 12 | 12.9% |
| MAC-ONLY / N/A | 1 | 1.1% |

These counts deliberately do not award parity merely because a model type or dormant method exists. A feature must be reachable from the production composition and UI.

### P0 findings

**P0 = 0.** No known gap blocks normal production use or the portable release path.

## 3. Critical Missing Features

| Priority | Gap | macOS behavior | Windows state | Size | Dependencies | Principal files |
|---|---|---|---|---|---|---|
| P1 | Companion product UI | Home shows progression and celebrations; Shop, Bag, Collection, catch log, dex details, and representative selection are reachable. | Home, Shop, Bag, and Collection are reachable; celebrations and richer dex details remain absent. | M | Celebration/detail presentation | macOS `CompanionView.swift`, `ShopView.swift`, `BagView.swift`, `PopoverView.swift`; Windows `MainWindow.xaml` |
| P1 | Full settings surface | Language, refresh, animation quality, limit mode, menu content, floating size/bubbles, notifications, thresholds, provider roots/auth, updates, and save transfer are configurable. | User-facing runtime, update, transfer, and root controls are connected; provider auth/status and configurable tray-content controls remain. | M | Remaining product settings | macOS `SettingsView.swift`, `Core/UsageStore.swift`; Windows `MainWindow.xaml`, `SettingsViewModel.cs`, `SupportViewModel.cs` |
| P2 | Release signing and installer QA | macOS release workflow signs and packages releases. | Reproducible portable zip and Inno Setup source exist; a signing identity is intentionally absent and real installer compile/install QA is pending. | M | Release environment and signing identity | macOS release scripts; Windows `scripts/build-release.ps1`, `installer/PokeTokenBar.iss`, `WINDOWS_RELEASE.md` |

## 4. Usage Providers

macOS provider registration is explicit in `UsageStore.init` and provider implementations are in `LocalUsageProvider.swift` and `LocalAdditionalUsageProvider.swift`. Windows production registration is explicit in `AppComposition.CreateUsageViewModel`.

| Provider | macOS | Windows | Status | Evidence |
|---|---|---|---|---|
| Codex | Local session parsing, period enrichment, cost/token aggregates, official app-server limits. | Detailed JSONL rollout/fork/canonical-session pipeline, daily/5h/week/month enrichment, official limits. | COMPLETE | macOS `LocalCodexProvider` in `LocalUsageProvider.swift`; Windows `LocalCodexUsageProvider.cs`, `CodexLocalRolloutPipeline.cs`, `CodexRateLimitsProvider.cs` |
| Claude Code | Local JSONL usage plus OAuth limits/account metadata. | Recursive local JSONL usage/cost parsing plus read-only CLI OAuth limits/account metadata. | COMPLETE | macOS `LocalClaudeProvider`, `OAuthLimitsProvider.swift`; Windows `LocalClaudeUsageProvider.cs`, `ClaudeRateLimitsProvider.cs`, `ClaudeCredentialProvider.cs` |
| Gemini | Local JSON/JSONL usage, period enrichment, and model pricing. | Recursive Windows-profile JSON/JSONL parsing, matching token mapping/dedup, periods, and cost. | COMPLETE | macOS `LocalGeminiProvider`; Windows `LocalGeminiUsageProvider.cs` |
| Antigravity | SQLite/protobuf local usage plus Google quota limits. | Windows built-in SQLite/protobuf local usage plus read-only token-file quota integration. | COMPLETE | macOS `LocalAntigravityProvider`, `AntigravityRateLimitsProvider.swift`; Windows `LocalAntigravityUsageProvider.cs`, `AntigravityRateLimitsProvider.cs` |
| OpenCode | SQLite and legacy JSON local usage. | Registered SQLite/legacy JSON provider with token, cost, dedup, and period aggregation. | COMPLETE | macOS `LocalOpenCodeProvider`; Windows `LocalOpenCodeUsageProvider` |
| Hermes Agent | SQLite local session usage. | Registered SQLite provider with token, reasoning, actual/estimated cost, and periods. | COMPLETE | macOS `LocalHermesProvider`; Windows `LocalHermesUsageProvider` |
| Cursor | Dashboard API primary path with SQLite fallback, including the zero-local-token fix. | Matching dashboard pagination/auth path with read-only Windows `state.vscdb` fallback, periods, dedup, and stale preservation. | COMPLETE | macOS `LocalCursorProvider` in `LocalAdditionalUsageProvider.swift`; Windows `LocalCursorUsageProvider.cs` |
| Grok | Turn-completed local JSONL usage. | Registered JSONL provider with replay/subagent filtering, token normalization, server cost, dedup, and periods. | COMPLETE | macOS `LocalGrokProvider`; Windows `LocalGrokUsageProvider` |
| GitHub Copilot | SQLite local usage provider. | Registered token-only SQLite provider with cache normalization, dedup, and periods. | COMPLETE | macOS `LocalCopilotProvider`; Windows `LocalCopilotUsageProvider` |
| Kiro | Legacy SQLite plus CLI 2.20+ and current JSONL sessions. | Registered provider for all three formats with upstream byte estimation, compaction preservation, dedup, and periods. | COMPLETE | macOS `LocalKiroProvider`; Windows `LocalKiroUsageProvider` |
| Pi | Agent JSONL usage. | Registered token-only JSONL provider with message/compaction/branch-summary parsing, dedup, and periods. | COMPLETE | macOS `LocalPiProvider`; Windows `LocalPiUsageProvider` |
| omp | Agent JSONL usage. | Registered JSONL provider with bridge filtering, message/compaction parsing, source-or-model cost, dedup, and periods. | COMPLETE | macOS `LocalOmpProvider`; Windows `LocalOmpUsageProvider` |

Windows `IUsageProvider`, `UsageSnapshot`, and provider-selector plumbing are reusable foundations, but they are not provider parity by themselves.

## 5. Usage / Rate Limits

The Windows Codex data path is one of the most complete parts of the port. `UsageStore.RefreshAsync` coalesces refreshes, performs daily and enrichment work, preserves stale snapshots on failure, and retains a provider when daily usage is empty but another period or official limit exists. `UsageViewModel.ApplyOfficialLimits` converts provider-level used percentages into clamped remaining values for the UI. `MainWindow.xaml` displays Today, Recent 5 hours, This week, This month, and official 5-hour/weekly reset information.

Remaining gaps:

- Windows `UsagePollingController` matches the macOS Manual/1/2/5/15-minute schedule, defaults to two minutes, retries a truly empty successful refresh once after 20 seconds, and preserves `UsageStore` refresh coalescing.
- Windows exposes used/remaining display preference and warning thresholds. Burn-rate forecast, provider status checks, and configurable menu-bar summaries remain absent.
- macOS Codex UI can represent multiple buckets, plan metadata, credits/spend controls, and warnings. Windows parses richer app-server data, but `UsageViewModel.ApplyOfficialLimits` selects only primary/secondary rows.
- Antigravity quota groups are fetched and displayed without the existing two-row projection losing buckets. Windows reads the two known standalone token files, but OS credential-store discovery and refresh remain unverified because this machine has no Antigravity installation; quota parity is therefore partial.
- Claude Code and Gemini are production cost-reporting providers; calculated local cost flows through the existing snapshot and UI path. Antigravity intentionally reports no per-token cost.

## 6. Companion / Pokemon

Windows now connects every successful usage refresh to one provider-neutral companion seam. `CompanionStore` seeds the first valid daily observation, consumes independent provider deltas, handles date rollover/regression/disappearance, carries egg and evolution overflow, persists planned branches, graduates into the dex/catch history, and starts the next egg. Automatic hatch reuses the existing weighted PokeAPI selection and rarity/nature/shiny rules; the view model updates immediately and state survives restart.

The remaining companion gaps are Ditto disguise/reveal, richer celebrations, and richer dex detail presentation.

## 7. Economy / Shop / Items

Windows now matches the upstream token wallet (`usedSinceInstall - spentTokens`), persisted inventory and candy grant ledger, atomic purchase/item mutations, Rare Candy progression, Mint rerolls, permanent Shiny Charm odds, and fresh/premium egg guarantees. Home, Shop, Bag, and Collection are reachable from the popup without a separate economy timer or provider-specific companion branch.

## 8. Floating Pet

The basic floating pet is a good native mapping. macOS uses a non-activating `NSPanel`; Windows uses a transparent topmost WPF `Window` through `FloatingPetController` and `FloatingPokemonWindow`. Windows supports drag, persisted/restored position, multi-monitor clamping, click-to-open, Open/Hide context actions, representative/egg sprites, GIF animation, and sleep-time hide/pause.

Windows now exposes the upstream 48–192 size range, 2.5/5/10fps animation floors, token/limit hover tooltip, and six-second limit-warning bubble. Size changes use the current-monitor clamp and all options persist.

## 9. Tray / Popup

`SystemTrayController`/`NotifyIconTrayIcon` and the WPF popup provide the important Windows-equivalent behavior: startup hidden, left-click toggle, Open/Refresh/Exit menu, cursor-monitor/DPI-aware placement, deactivation-to-hide, and no taskbar entry. Provider selection and manual refresh are reachable.

macOS additionally animates the representative in the `NSStatusItem` and can show configurable token/cost/limit text in the menu bar. Windows now synchronizes the representative as a static notification-area icon and exposes live usage through the localized tray tooltip; animated tray frames remain partial because the shell surface differs.

## 10. Settings

Windows persists and exposes:

- launch at startup (`HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run`),
- floating pet enabled,
- floating position and reset,
- refresh interval (Manual, 1, 2, 5, or 15 minutes),
- seven-language runtime selection, used/remaining limits, notification categories and 80/95 thresholds,
- floating size, animation quality and bubble preference,
- per-provider additive custom scan folders.

`JsonAppSettingsPersistence` stores these settings atomically in LocalAppData, ignores unknown JSON fields, and safely supplies defaults to older files. Phase 6 adds update notification/check controls, version/About information, save transfer, and diagnostics. Remaining settings gaps are configurable tray content and provider auth/status controls.

## 11. Notifications

macOS uses `UNUserNotificationCenter`; the unpackaged Windows build uses the stable `NotifyIcon` balloon equivalent without adding a packaging dependency. Warning/critical alerts use persisted per-window tiers, suppress repeated polling results, re-arm below the warning threshold, limit resume bursts to the highest alert, and remain isolated from refresh/progression failures. Hatch/evolution/graduation/reward events use an independent toggle, and limit alerts can also appear in a six-second floating bubble.

## 12. Localization

Current upstream supports Korean, English, Japanese, Spanish, French, Portuguese, and German. Windows now exposes the same seven-language selector, persists it, switches major popup/settings/tray/floating/notification strings at runtime, and refreshes Pokémon names through the production companion path. A smaller set of economy detail/result copy still falls back to English, so full-UI localization remains partial.

## 13. Lifecycle / Background

Both applications start as background/tray apps, enforce one instance, and respond to sleep/resume. Windows checks `SingleInstanceGuard.TryAcquire` before `AppComposition.Create`, so a second process does not create tray, windows, refresh, or power subscriptions. `UsagePollingController` uses `TimeProvider.CreateTimer`, pauses polling and cancels a pending empty retry on suspend, then restores one schedule after the wake refresh. `WindowsPowerModeEventSource` maps system power events, and shutdown disposal is explicit.

macOS additionally supports launch-agent keep-alive and crash reporting. Windows now has startup/manual/timed/resume refresh, equivalent polling sleep behavior, and sanitized copyable support diagnostics, but no automatic restart or crash-capture pipeline.

## 14. Persistence / Cache

Windows has appropriate native persistence for its implemented scope: LocalAppData JSON for app settings and the complete core companion/economy/collection state, position persistence, sprite caching, base-species caching, Registry auto-start, and versioned export/import with backup and rollback. Claude and Antigravity credentials are discovered read-only from their CLI files; PokeTokenBar neither stores nor exports them. Incremental provider scan caches, legacy app-identity migration, and general provider credential/configuration persistence remain partial or absent.

Neither audit nor any verification modified user `.codex` data.

## 15. Updates / Distribution

macOS `UpdateChecker` checks GitHub releases and the UI presents update availability; Homebrew installs can upgrade/relaunch while other installs open the release page. The repository includes macOS packaging/signing scripts.

Windows uses the same stable GitHub-release meaning: startup/popup checks are debounced to 30 minutes, manual checks bypass the debounce, failures are isolated, and only a validated `https://github.com` release page opens after explicit user action. It never overwrites the running executable or silently launches an installer. `build-release.ps1` produces a versioned self-contained portable directory and verified zip; the per-user Inno source supports Start Menu/optional desktop shortcuts, upgrade, uninstall, and user-data preservation. Installer compilation, real install/uninstall QA, and code signing remain release-environment work.

## 16. Platform-specific Mapping

| macOS mechanism | Windows mapping | Assessment |
|---|---|---|
| `NSStatusItem` tray presence | WinForms `NotifyIcon` hosted by WPF | WINDOWS EQUIVALENT for presence/actions; text-rich menu-bar display is not directly portable |
| `NSPopover` | Borderless WPF tool window, deactivation hides | WINDOWS EQUIVALENT |
| Non-activating `NSPanel` | Transparent topmost WPF `Window` with `ShowActivated=false` | WINDOWS EQUIVALENT |
| Process-based single-instance selection | User-SID named `Mutex` | WINDOWS EQUIVALENT |
| `SMAppService` / launch agent | HKCU Run value | WINDOWS EQUIVALENT for login start; no keep-alive |
| `UserDefaults` | LocalAppData JSON | WINDOWS EQUIVALENT |
| Keychain credential discovery | Read-only Claude and Antigravity CLI credential-file discovery | PARTIAL; file paths are supported without PokeTokenBar writes, but Antigravity Windows OS-store discovery is unverified |
| Workspace/display sleep notifications | `SystemEvents.PowerModeChanged` | WINDOWS EQUIVALENT |
| `UNUserNotificationCenter` | `NotifyIcon` balloon plus floating warning bubble | WINDOWS EQUIVALENT |
| Homebrew/app bundle update | Versioned portable zip plus per-user Inno Setup source and release-page update path | WINDOWS EQUIVALENT; installer compile QA pending |
| SwiftUI localization environment | Runtime observable seven-language catalog | PARTIAL; major surfaces switch live, some detail copy remains English |

## 17. Test Coverage Gaps

Windows has strong tests around all twelve registered providers, including JSON/JSONL and SQLite/protobuf fixtures, roots, malformed records, token/cost semantics, deduplication, multi-session and daily/period aggregation, dashboard auth/pagination/fallback, WAL-safe reads, stale preservation, coalescing, official-limit parsing, multi-bucket quota projection, remaining percentages, and provider visibility. It also tests tray placement/lifecycle seams, power behavior, single instance, persistence, sprite loading, PokeAPI behavior, and the usage-to-companion ledger/hatch/evolution/graduation cycle.

The most important missing behavior tests correspond to missing production features:

1. No Ditto lifecycle tests.
2. Notification opt-in, threshold, deduplication, re-arm, and event paths are covered; native shell balloon appearance still needs manual OS-level QA.
3. Major runtime language-switch paths are covered; untranslated detail copy prevents full localization coverage.
4. Update checking, release selection, version metadata, save transfer, backup, rollback, and release-script contracts are covered; native dialog and trust-prompt appearance still need OS-level QA.
5. No warning/burn forecast/provider-status behavior tests.

The macOS suite contains dedicated coverage for these areas, including `CompanionTests`, `RareCandyTests`, `PremiumEggTests`, `ShopTests`, `DittoTests`, `SaveTransferTests`, provider-specific suites, `CustomRootsTests`, `LocalUsageCacheTests`, and `SingleInstanceTests`. Test-count parity alone would be misleading; Windows should add tests only as each missing production slice is ported.

## 18. Full Feature Matrix

Counts in section 2 are calculated from this table only.

| Category | Feature | macOS behavior | Windows behavior | Status | macOS evidence | Windows evidence | Priority | Size |
|---|---|---|---|---|---|---|---|---|
| Providers | Provider abstraction/selection | Common provider protocol and selected-provider state | Common interface, snapshots, selector | COMPLETE | `UsageProvider.swift`; `UsageStore.swift` | `IUsageProvider.cs`; `UsageViewModel.cs` | P0 | S |
| Providers | Codex local usage | JSONL local usage with enrichment | Detailed JSONL pipeline and enrichment | COMPLETE | `LocalCodexProvider` | `LocalCodexUsageProvider.cs`; `CodexLocalRolloutPipeline.cs` | P0 | — |
| Providers | Claude Code | Local usage provider | Registered local JSONL provider with period enrichment and cost | COMPLETE | `LocalClaudeProvider` | `LocalClaudeUsageProvider.cs`; `AppComposition.CreateUsageViewModel` | P0 | — |
| Providers | Gemini | Local usage provider | Registered JSON/JSONL provider with periods, dedup, token mapping, and cost | COMPLETE | `LocalGeminiProvider` | `LocalGeminiUsageProvider.cs`; `AppComposition.CreateUsageViewModel` | P0 | — |
| Providers | Antigravity | Local usage provider | Registered Windows SQLite/protobuf provider with read-only database copies and periods | COMPLETE | `LocalAntigravityProvider` | `LocalAntigravityUsageProvider.cs`; `AppComposition.CreateUsageViewModel` | P0 | — |
| Providers | OpenCode | Local usage provider | Registered SQLite/legacy JSON provider with periods and cost | COMPLETE | `LocalOpenCodeProvider` | `LocalOpenCodeUsageProvider.cs`; `AppComposition.CreateUsageViewModel` | P1 | — |
| Providers | Hermes Agent | Local usage provider | Registered SQLite provider with periods and cost | COMPLETE | `LocalHermesProvider` | `LocalHermesUsageProvider.cs`; `AppComposition.CreateUsageViewModel` | P1 | — |
| Providers | Cursor | Dashboard API with SQLite fallback | Registered dashboard-primary provider with read-only Windows SQLite fallback | COMPLETE | `LocalCursorProvider` | `LocalCursorUsageProvider.cs`; `AppComposition.CreateUsageViewModel` | P1 | — |
| Providers | Grok | Local usage provider | Registered JSONL provider with replay/subagent filtering, periods, and source cost | COMPLETE | `LocalGrokProvider` | `LocalGrokUsageProvider.cs`; `AppComposition.CreateUsageViewModel` | P1 | — |
| Providers | Copilot | Local usage provider | Registered token-only SQLite provider with periods | COMPLETE | `LocalCopilotProvider` | `LocalCopilotUsageProvider.cs`; `AppComposition.CreateUsageViewModel` | P1 | — |
| Providers | Kiro | Local usage, including 2.20+ JSONL | Registered legacy SQLite, CLI 2.20+, and current JSONL provider | COMPLETE | `LocalKiroProvider` | `LocalKiroUsageProvider.cs`; `AppComposition.CreateUsageViewModel` | P1 | — |
| Providers | Pi | Local usage provider | Registered token-only JSONL provider with periods | COMPLETE | `LocalPiProvider` | `LocalPiUsageProvider.cs`; `AppComposition.CreateUsageViewModel` | P2 | — |
| Providers | omp | Local usage provider | Registered JSONL provider with periods and source-or-model cost | COMPLETE | `LocalOmpProvider` | `LocalOmpUsageProvider.cs`; `AppComposition.CreateUsageViewModel` | P2 | — |
| Usage | Daily aggregation | Common daily snapshot | Twelve registered providers produce daily snapshots | COMPLETE | `UsageStore.refresh` | `UsageStore.RefreshAsync` | P0 | — |
| Usage | Active 5-hour period | Enriched active block | Twelve registered providers produce Recent 5 hours | COMPLETE | `LocalUsageReader` enrichment | local provider implementations | P0 | — |
| Usage | Week aggregation | Calendar week enrichment | Twelve registered providers use calendar-week enrichment | COMPLETE | `LocalUsageReader` | local provider implementations | P0 | — |
| Usage | Month aggregation | Calendar month enrichment | Twelve registered providers support month enrichment and zero-today carriers | COMPLETE | `LocalUsageReader` | `UsageStore.cs`; local provider implementations | P0 | — |
| Usage | Token totals | Input/output/cache/total representation | Twelve registered providers represent their source token buckets | COMPLETE | `Models.swift` | `DailyUsage.cs`; `BlockUsage.cs`; `PeriodUsage.cs`; `UsageViewModel.cs` | P0 | — |
| Usage | Cost display | Displayed for cost-reporting providers | All cost-reporting providers use the shared snapshot/UI path | COMPLETE | `PopoverView.usageSection` | local provider implementations; `UsageViewModel.cs`; `MainWindow.xaml` | P1 | — |
| Usage | Refresh coalescing | Concurrent requests share refresh | Concurrent requests share refresh | COMPLETE | `UsageStore.refresh` | `UsageStore.RefreshAsync` | P0 | — |
| Usage | Stale snapshot preservation | Keeps usable prior data on failure | Keeps daily/period/official prior data | COMPLETE | `UsageStore` refresh phases | `UsageStore.cs` | P0 | — |
| Usage | Recurring polling | Configurable timer refresh | Native timer with Manual/1/2/5/15-minute schedules | COMPLETE | `UsageStore.reschedule` | `UsagePollingController.cs` | P0 | — |
| Usage | Empty-result retry | One 20-second retry after successful empty scan | Same; errors, month-only, and official-only are not empty | COMPLETE | `UsageStore.handleEmptyUsageRetry` | `UsagePollingController.EvaluateEmptyRetry` | P1 | — |
| Usage | Custom scan roots | Per-provider configured roots | Twelve provider-specific additive roots persist and apply on the next refresh | COMPLETE | `CustomRoots`; persisted `UsageStore` settings | `ConfigurableUsageProvider.cs`; `SettingsViewModel.cs`; `MainWindow.xaml` | P1 | — |
| Usage | Burn forecast | Computes burn tier/forecast | No production calculation/UI | MISSING | `UsageStore` burn fields | `UsageViewModel.cs` | P2 | M |
| Usage | Provider status checks | Optional provider health/status | No production status layer | MISSING | `UsageStore` status checks | absent | P2 | M |
| Limits | Codex official fetch | App-server rate limits | App-server rate limits | COMPLETE | `CodexRateLimitsProvider.swift` | `CodexRateLimitsProvider.cs` | P0 | — |
| Limits | Remaining percentages | Used/remaining selectable | Persisted used/remaining mode with clamped labels and progress bars | COMPLETE | `AppSettings.limitDisplayMode` | `UsageViewModel.ApplyOfficialLimits`; `SettingsViewModel.cs`; `MainWindow.xaml` | P0 | — |
| Limits | Reset display | Shows reset timestamps | Shows reset timestamps | COMPLETE | `PopoverView.limitRow` | `MainWindow.xaml` | P0 | — |
| Limits | Multiple Codex buckets | Models/displays bucket set | Parser can see richer data; UI projects primary/secondary only | PARTIAL | `CodexRateLimitsProvider` | `CodexRateLimitsProvider.cs`; `UsageViewModel.ApplyOfficialLimits` | P1 | M |
| Limits | Plan/credits/spend | Displays plan, credits/spend controls where available | Not exposed in UI | MISSING | `CodexRateLimitsProvider`; `PopoverView` | `UsageViewModel.cs` | P1 | M |
| Limits | Claude OAuth limits | OAuth limits/account metadata | Read-only CLI OAuth credential discovery, limits, plan, email, and organization metadata | COMPLETE | `OAuthLimitsProvider.swift` | `ClaudeCredentialProvider.cs`; `ClaudeRateLimitsProvider.cs`; `UsageViewModel.cs` | P0 | — |
| Limits | Antigravity quota | Google quota groups | All groups/buckets displayed from read-only standalone-token auth; Windows OS-store/refresh path unverified | PARTIAL | `AntigravityRateLimitsProvider.swift` | `AntigravityRateLimitsProvider.cs`; `AntigravityCredentialProvider.cs`; `UsageViewModel.cs` | P1 | S |
| Companion | Persisted companion restore | Restores current companion state | Restores current companion state | COMPLETE | `CompanionStore.load` | `CompanionStore.InitializeAsync`; `JsonCompanionPersistence.cs` | P0 | — |
| Companion | Pokémon data lookup | Species/evolution/localized data | GraphQL index plus REST fallback/cache | COMPLETE | Pokémon API services | `PokeApiClient.cs` | P0 | — |
| Companion | Manual representative | Collection representative can be selected | Collection entries expose persisted representative selection | COMPLETE | `CompanionView` representative picker | `CompanionStore.SetRepresentativeAsync`; `EconomyViewModel.cs`; `MainWindow.xaml` | P1 | — |
| Companion | Usage ledger | Per-provider daily ledger consumes deltas | Provider-neutral daily ledger with baseline, rollover, rebase, and restart semantics | COMPLETE | `CompanionStore.update` | `CompanionStore.UpdateUsageAsync` | P0 | — |
| Companion | Egg progression | Usage advances egg | Usage advances egg; 5M hatch threshold and overflow carry match | COMPLETE | `CompanionStore.applyUsage` | `CompanionStore.UpdateUsageAsync` | P0 | — |
| Companion | Evolution | Planned branches and overflow progression | Persisted planned branches and cross-stage overflow progression | COMPLETE | `CompanionStore.applyUsage` | `CompanionStore.ApplyUsageCore` | P0 | — |
| Companion | Graduation/new egg | Graduates final stage and starts cycle | Final stage graduates and starts a zero-progress egg | COMPLETE | `CompanionStore.graduate` | `CompanionStore.GraduateCore` | P0 | — |
| Companion | Dex/catch log mutation | Captures update dex and log | Graduation persists individual dex/catch entries; presentation UI remains separate | COMPLETE | `CompanionStore.graduate`; `CompanionView` | `CompanionStore.GraduateCore`; `JsonCompanionPersistence.cs` | P1 | — |
| Companion | Rarity/nature/shiny roll | Weighted hatch/capture attributes | Production auto-hatch uses weighted species and persists rarity/nature/shiny | COMPLETE | `CompanionStore` sampling | `CompanionStore.HatchRandomAsync` | P1 | — |
| Companion | Ditto disguise/reveal | Full disguise/reveal lifecycle | No production lifecycle | MISSING | `CompanionStore`; `DittoTests` | absent | P2 | M |
| Economy | Currency rewards/ledger | Usage and events grant currency | Usage-backed wallet and official-window candy ledger persist atomically | COMPLETE | `CompanionStore.grantCandies` | `CompanionEconomy.cs`; `CompanionStore.cs`; `UsageCompanionController.cs` | P1 | — |
| Economy | Shop/purchases | Purchases validated and persisted | Prices, balance validation, ownership, and inventory purchases are connected to Shop UI | COMPLETE | `CompanionStore.buy`; `ShopView.swift` | `CompanionStore.PurchaseAsync`; `EconomyViewModel.cs`; `MainWindow.xaml` | P1 | — |
| Economy | Rare Candy | Inventory item advances progress | Adds 100M progress through the production hatch/evolution/graduation path | COMPLETE | `CompanionStore.useRareCandy` | `CompanionStore.UseItemAsync`; `EconomyViewModel.cs` | P1 | — |
| Economy | Mint | Changes nature | Consumable nature reroll is persisted and exposed in Bag | COMPLETE | `CompanionStore.useMint` | `CompanionStore.UseItemAsync`; `EconomyViewModel.cs` | P2 | — |
| Economy | Shiny Charm | Alters shiny odds | Permanent purchase changes future hatch odds from 1/64 to 1/48 | COMPLETE | `CompanionStore`; `ShinyCharmTests` | `CompanionStore.cs`; `CompanionEconomy.cs` | P2 | — |
| Economy | Premium/fresh eggs | Purchasable egg variants affect hatch | Basic/uncommon/rare fresh eggs reset progress and persist hatch guarantees | COMPLETE | `CompanionStore`; `PremiumEggTests` | `CompanionStore.PurchaseAsync`; `CompanionEconomy.cs` | P2 | — |
| Floating | Basic floating window | Non-activating always-on-top panel | Transparent topmost non-activating WPF window | WINDOWS EQUIVALENT | `FloatingPetPanel.swift` | `FloatingPokemonWindow.xaml.cs` | P0 | — |
| Floating | Drag and position persistence | Drag/persist/multi-screen recovery | Drag/persist/multi-monitor clamp | WINDOWS EQUIVALENT | `FloatingPetPanel` | `FloatingPetController.cs`; `FloatingPetPositioner.cs` | P0 | — |
| Floating | Click/context actions | Opens popup; Open/Hide actions | Opens popup; Open/Hide actions | WINDOWS EQUIVALENT | `FloatingPetPanel` | `FloatingPokemonWindow.xaml.cs` | P0 | — |
| Floating | Animated/static fallback | Animated pet/egg with fallback | GIF/static pet/egg with cache/fallback | COMPLETE | `SpriteAnimation.swift`; `SpriteLoader.swift` | `PokemonSpriteLoader.cs`; `AnimatedSpritePresenter.cs`; `WpfPokemonSpriteDecoder.cs` | P0 | — |
| Floating | Configurable size | 48–192 setting | Same persisted range/step with monitor re-clamp | COMPLETE | `AppSettings.floatingPetSize` | `AppSettings.cs`; `FloatingPokemonWindow.xaml.cs` | P2 | — |
| Floating | Animation quality | User-selectable quality | Persisted 2.5/5/10fps frame-duration floors | COMPLETE | `AppSettings.animationQuality` | `AnimatedSpritePresenter.cs`; `SpriteAnimationController.cs` | P2 | — |
| Floating | Hover usage tooltip | Tokens/limit hover summary | Native WPF tooltip with today and headline limit | COMPLETE | `FloatingPetPanel` | `FloatingPetViewModel.cs`; `FloatingPokemonWindow.xaml` | P2 | — |
| Floating | Alert/event bubbles | Limit speech bubbles | Six-second non-activating popup bubble | COMPLETE | `FloatingPetPanel`; app event routing | `NotificationController.cs`; `FloatingPokemonWindow.xaml` | P1 | — |
| Tray | Tray presence/actions | `NSStatusItem` opens app controls | `NotifyIcon` with Open/Refresh/Exit | WINDOWS EQUIVALENT | `PokeTokenBarApp` status item | `SystemTrayController.cs`; `NotifyIconTrayIcon.cs` | P0 | — |
| Tray | Popup behavior | Transient popover closes outside | Tool window closes on deactivation | WINDOWS EQUIVALENT | `PopoverView`; app delegate | `MainWindow.xaml.cs` | P0 | — |
| Tray | Multi-monitor placement | Native popover placement | Cursor-monitor and DPI-aware placement | COMPLETE | AppKit popover | `PopupPositioner.cs` | P0 | — |
| Tray | Animated companion icon | Representative animates in menu bar | Representative static icon synchronizes immediately; tray animation remains absent | PARTIAL | status-item sprite controller | `SystemTrayController.cs`; `NotifyIconTrayIcon.cs` | P1 | S |
| Tray | Menu-bar token/cost/limit text | Configurable status-item text | Exact menu-bar treatment unavailable in notification area | MAC-ONLY / N/A | `UsageStore.menuBarText` | Windows shell constraint | P3 | — |
| Tray | Provider switch/manual refresh | Provider tabs and refresh | Selector and refresh command | COMPLETE | `PopoverView` | `MainWindow.xaml`; `UsageViewModel.cs` | P0 | — |
| Popup | Home usage view | Usage, limits, companion, warnings | Usage, official limits, companion progression, warning/error and update banners | COMPLETE | `PopoverView` | `MainWindow.xaml`; `NotificationController.cs`; `SupportViewModel.cs` | P0 | — |
| Popup | Shop tab | Reachable shop | Reachable catalog with balance, prices, disabled states, and buy actions | COMPLETE | `ShopView.swift`; `PopoverView` | `EconomyViewModel.cs`; `MainWindow.xaml` | P1 | — |
| Popup | Bag tab | Reachable inventory/actions | Reachable inventory with item counts and use actions | COMPLETE | `BagView.swift`; `PopoverView` | `EconomyViewModel.cs`; `MainWindow.xaml` | P1 | — |
| Popup | Collection/dex tab | Collection, dex, catch log, representative | Reachable active/graduated collection and representative selection | COMPLETE | `CompanionView.swift`; `PopoverView` | `EconomyViewModel.cs`; `MainWindow.xaml` | P1 | — |
| Settings | Launch at login | Native login service | HKCU Run entry | WINDOWS EQUIVALENT | settings login service | `WindowsAutoStartService.cs` | P0 | — |
| Settings | Floating enabled/position reset | Toggle/reset | Toggle/reset | COMPLETE | `SettingsView` | `SettingsViewModel.cs`; `MainWindow.xaml` | P0 | — |
| Settings | Refresh interval | Manual/1/2/5/15 minute choices | Same choices, persisted and immediately rescheduled | COMPLETE | `UsageStore.refreshInterval` | `RefreshIntervalMode`; `SettingsViewModel.SelectedRefreshInterval`; `MainWindow.xaml` | P0 | — |
| Settings | Language | Seven-language selector | Same persisted selector with runtime application | COMPLETE | `SettingsView`; `Localization.swift` | `LocalizationService.cs`; `SettingsViewModel.cs`; `MainWindow.xaml` | P1 | — |
| Settings | Limit used/remaining mode | User choice | Persisted choice updates labels and progress values immediately | COMPLETE | `AppSettings.limitDisplayMode` | `UsageViewModel.ApplyOfficialLimits`; `SettingsViewModel.cs` | P2 | — |
| Settings | Floating/animation options | Size, quality, bubble preferences | Same production settings and runtime effects | COMPLETE | `SettingsView` | `SettingsViewModel.cs`; floating/sprite presentation | P2 | — |
| Settings | Notification/threshold options | Category toggles and 80/95 defaults | Independent limit/companion toggles and persisted ordered thresholds | COMPLETE | `AppSettings` | `AppSettings.cs`; `SettingsViewModel.cs`; `MainWindow.xaml` | P1 | — |
| Settings | Provider roots/auth controls | Roots, Keychain opt-out, refresh controls | Twelve additive scan-root editors apply on next refresh; auth controls remain absent | PARTIAL | `SettingsView`; `CustomRoots` | `ConfigurableUsageProvider.cs`; `SettingsViewModel.cs`; `MainWindow.xaml` | P1 | M |
| Localization | Pokémon names/natures | Seven current languages | API names/natures follow the persisted seven-language runtime selection | COMPLETE | `Localization.swift`; model helpers | `AppLanguageRules`; `PokeApiClient.cs`; `CompanionViewModel.cs` | P1 | — |
| Localization | Full application UI | Seven-language UI strings | Major popup/settings/tray/floating/notification strings switch live; some economy detail copy falls back to English | PARTIAL | `Localization.swift` | `LocalizationService.cs`; `MainWindow.xaml`; tray/floating view models | P1 | M |
| Notifications | Limit warning/critical | Configurable, deduped notifications | Persisted edge-triggered `NotifyIcon` balloon and floating bubble equivalent | WINDOWS EQUIVALENT | app notification routing; `UsageStore` | `NotificationController.cs`; `LimitNotificationEvaluator` | P1 | — |
| Notifications | Companion events | Hatch/evolve/graduate/reward notifications | Post-mutation `NotifyIcon` balloon equivalent with isolated failures | WINDOWS EQUIVALENT | `PokeTokenBarApp` | `CompanionStore.GameEventOccurred`; `NotificationController.cs` | P1 | — |
| Lifecycle | Start hidden/background | Accessory tray app | WPF tray app starts hidden | WINDOWS EQUIVALENT | `PokeTokenBarApp` | `App.xaml.cs` | P0 | — |
| Lifecycle | Single instance | Prevents duplicate lifecycle | User-SID named mutex before composition | WINDOWS EQUIVALENT | `SingleInstanceController` | `SingleInstanceGuard.cs`; `App.OnStartup` | P0 | — |
| Lifecycle | Sleep/resume | Pauses polling/work and refreshes after wake | Pauses polling/retry, preserves pet behavior, refreshes, then restores one schedule | COMPLETE | app sleep handlers | `PowerLifecycleController.cs`; `UsagePollingController.cs` | P0 | — |
| Lifecycle | Clean shutdown | Releases app resources | Disposes tray/windows/events/mutex | COMPLETE | app termination | `App.xaml.cs` | P0 | — |
| Lifecycle | Crash diagnostics/keep-alive | Logging/crash reporting and launch-agent behavior | Sanitized copyable support diagnostics exist; crash capture and automatic restart remain absent | PARTIAL | app/logging/launch-agent code | `DiagnosticsReport.cs`; `SupportViewModel.cs` | P2 | M |
| Persistence | App settings | `UserDefaults` | LocalAppData JSON | WINDOWS EQUIVALENT | `UsageStore.init` and persisted properties | `JsonAppSettingsPersistence.cs` | P0 | — |
| Persistence | Companion state | Persisted game state | Ledger, egg, active path, traits, dex, and representative persist/restore | COMPLETE | `CompanionStore` | `CompanionStore.cs`; `JsonCompanionPersistence.cs` | P0 | — |
| Persistence | Sprite/species cache | Cached assets/data | Cached sprites/base index | COMPLETE | sprite/cache services | `PokemonSpriteLoader.cs`; `PokeApiClient.cs` | P1 | — |
| Persistence | Incremental usage cache | Provider scan/cache support | Codex scans and in-memory snapshots; no equivalent persistent usage cache | PARTIAL | `LocalUsageCache` | `CodexLocalRolloutPipeline.cs`; `UsageStore.cs` | P2 | M |
| Persistence | Save export/import | Versioned transfer, validation, pre-import backup and device rebase | Versioned settings+companion envelope, validation, backup, rollback and restart-required UI | COMPLETE | `SaveTransfer.swift`; settings UI | `StateTransferService.cs`; `SupportViewModel.cs`; `MainWindow.xaml` | P2 | — |
| Updates | Update checking/UI | GitHub release check and banner | Stable GitHub release check, 30-minute debounce, manual check and banner | COMPLETE | `UpdateChecker.swift`; `PopoverView` | `GitHubReleaseUpdateChecker.cs`; `SupportViewModel.cs`; `MainWindow.xaml` | P1 | — |
| Distribution | Packaged release | App bundle/Homebrew flows | Reproducible versioned self-contained portable directory/zip and per-user installer source | COMPLETE | packaging scripts | `scripts/build-release.ps1`; `installer/PokeTokenBar.iss`; `WINDOWS_RELEASE.md` | P0 | — |
| Distribution | Upgrade/relaunch | Homebrew upgrade or release-page path | User-confirmed validated GitHub release-page path; no running-EXE overwrite | WINDOWS EQUIVALENT | `UpdateChecker` | `GitHubReleaseUpdateChecker.cs`; `WindowsUserInteraction.cs` | P2 | — |
| Distribution | Signing/installer policy | macOS signing/package workflow | Per-user Inno source and release documentation exist; signing and real installer QA require a release environment | PARTIAL | `scripts/`; package docs | `installer/PokeTokenBar.iss`; `scripts/build-release.ps1`; `WINDOWS_RELEASE.md` | P2 | M |

## 19. Recommended Porting Roadmap

### Phase A — Runtime correctness and provider foundation — complete

Completed with lifecycle-safe polling, interval persistence, empty-result retry, and all twelve upstream providers registered through the existing `IUsageProvider`/`UsageStore`/selector contract.

**Exit criteria:** long-running usage stays current; provider registration is data-driven enough for the implemented set; each provider has local fixtures and period tests; failures preserve stale data.

### Phase B — Connect the companion game loop — complete

Completed with one refresh-to-companion integration seam and focused provider-ledger, egg, evolution, graduation, dex, UI-update, and persistence tests.

**Exit criteria:** real usage deterministically progresses and persists a companion across restart; overflow and date/provider ledger semantics match macOS.

### Phase C — Economy and companion surfaces

Completed in Windows Phase 4: candy rewards, wallet/inventory mutations, Rare Candy, Mint, Shiny Charm, fresh egg variants, purchases, and the Shop/Bag/Collection surfaces are connected to persisted production store actions.

**Exit criteria:** every visible action is persisted, recoverable, and covered by focused state-transition tests.

### Phase D — Alerts, settings, and localization — complete

Completed in Windows Phase 5 with native notification-area balloons, persisted threshold tiers, floating bubbles, refresh/limit/floating/root settings, and a runtime seven-language resource catalog. Provider status and burn displays remain separate product work.

**Exit criteria:** all user-facing strings switch language; warning and companion notifications respect settings and do not spam; floating size/quality/bubbles persist.

### Phase E — Update and distribution closure

Completed in Windows Phase 6 with stable GitHub release checking, user-confirmed release-page handoff, version/About UI, versioned save export/import with backup and rollback, sanitized diagnostics, reproducible portable packaging, and per-user Inno Setup source. Installer compilation/signing and real install/uninstall QA remain explicitly pending because this machine has no Inno compiler or signing identity.

**Exit criteria:** a non-developer can install, update, recover/export state, and provide actionable diagnostics without altering `.codex` data.
