# PokeTokenBar Windows Port Parity Audit

## 1. Audit Baseline

This is a code-based parity audit, not an implementation plan disguised as completed work. README claims were used only for orientation; every finding below is grounded in source or tests.

| Item | Value |
|---|---|
| Audit date | 2026-08-30 |
| Windows branch | `windows-port` |
| Windows commit | `62204bcc29aecacecec7faf8579baa3c232bb8c8` |
| macOS baseline | `upstream/main` at `a69444c852559639dcde600fd71a41665f549e91` |
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

The Windows port is a credible Codex- and Claude-focused tray application, but it is not yet a full port of the current macOS product. Its strongest areas are Codex/Claude local parsing and period aggregation, their official limits, configurable background polling, the core usage-driven companion loop, single-instance startup, tray/popup mechanics, floating-window mechanics, persistence, and sleep/resume handling. The largest parity boundary is now provider and product breadth: macOS registers twelve local providers and exposes a complete companion/economy surface, while Windows registers Codex and Claude Code and does not expose the economy or collection surfaces.

The full matrix in section 18 contains **93 atomic feature rows**:

| Status | Count | Share |
|---|---:|---:|
| COMPLETE | 33 | 35.5% |
| PARTIAL | 10 | 10.8% |
| MISSING | 40 | 43.0% |
| WINDOWS EQUIVALENT | 9 | 9.7% |
| MAC-ONLY / N/A | 1 | 1.1% |

These counts deliberately do not award parity merely because a model type or dormant method exists. A feature must be reachable from the production composition and UI.

### P0 findings

1. **Ten production usage providers are missing.** Windows registers Codex and Claude Code; Gemini, Antigravity, OpenCode, Hermes, Cursor, Grok, Copilot, Kiro, Pi, and omp are absent.

## 3. Critical Missing Features

| Priority | Gap | macOS behavior | Windows state | Size | Dependencies | Principal files |
|---|---|---|---|---|---|---|
| P0 | Multi-provider coverage | `UsageStore.init` registers 12 providers with common period enrichment. | `AppComposition.CreateUsageViewModel` registers Codex and Claude Code. | XL | Remaining provider parsers, roots/auth, fixtures, selector integration | macOS `Core/UsageStore.swift`, `Core/LocalUsageProvider.swift`, `Core/LocalAdditionalUsageProvider.swift`; Windows `App/AppComposition.cs` |
| P1 | Companion product UI | Home shows progression and celebrations; Shop, Bag, Collection, catch log, dex details, and representative selection are reachable. | Popup shows a read-only companion header; no Shop/Bag/Collection navigation. | L | Companion loop and economy actions | macOS `CompanionView.swift`, `ShopView.swift`, `BagView.swift`, `PopoverView.swift`; Windows `MainWindow.xaml` |
| P1 | Economy and items | Usage awards candy; Rare Candy, Mint, Shiny Charm, premium/fresh eggs, purchases, and inventory mutations are functional. | Data shapes exist, but no production actions or UI implement the economy. | L | Companion loop, atomic persistence, UI | macOS `Core/CompanionStore.swift`; Windows `Core/CompanionModels.cs`, `Core/CompanionStore.cs` |
| P1 | Notifications and warnings | Configurable warning/critical usage notifications, companion event notifications, and floating bubbles are deduplicated and re-armed. | No Windows notification or warning service exists. | L | Polling, thresholds, companion events | macOS `PokeTokenBarApp.swift`, `Core/UsageStore.swift`; Windows: absent |
| P1 | Full settings surface | Language, refresh, animation quality, limit mode, menu content, floating size/bubbles, notifications, thresholds, provider roots/auth, updates, and save transfer are configurable. | Only launch-at-startup, floating enabled, and reset floating position are exposed. | L | Features being configured | macOS `SettingsView.swift`, `Core/UsageStore.swift`; Windows `MainWindow.xaml`, `SettingsViewModel.cs`, `Core/AppSettings.cs` |
| P2 | Updates and Windows release UX | In-app release checks, update banner, Homebrew upgrade/relaunch or release-page fallback. | Self-contained publish exists; no update checker, installer flow, signing policy, or update UI. | L | Windows distribution choice and signing identity | macOS `Core/UpdateChecker.swift`, `PopoverView.swift`; Windows `.csproj`, `README.md` |

## 4. Usage Providers

macOS provider registration is explicit in `UsageStore.init` and provider implementations are in `LocalUsageProvider.swift` and `LocalAdditionalUsageProvider.swift`. Windows production registration is explicit in `AppComposition.CreateUsageViewModel`.

| Provider | macOS | Windows | Status | Evidence |
|---|---|---|---|---|
| Codex | Local session parsing, period enrichment, cost/token aggregates, official app-server limits. | Detailed JSONL rollout/fork/canonical-session pipeline, daily/5h/week/month enrichment, official limits. | COMPLETE | macOS `LocalCodexProvider` in `LocalUsageProvider.swift`; Windows `LocalCodexUsageProvider.cs`, `CodexLocalRolloutPipeline.cs`, `CodexRateLimitsProvider.cs` |
| Claude Code | Local JSONL usage plus OAuth limits/account metadata. | Recursive local JSONL usage/cost parsing plus read-only CLI OAuth limits/account metadata. | COMPLETE | macOS `LocalClaudeProvider`, `OAuthLimitsProvider.swift`; Windows `LocalClaudeUsageProvider.cs`, `ClaudeRateLimitsProvider.cs`, `ClaudeCredentialProvider.cs` |
| Gemini | Local usage provider. | No production provider. | MISSING | macOS `LocalGeminiProvider`; Windows composition |
| Antigravity | Local usage plus Google quota limits. | No production provider. | MISSING | macOS `LocalAntigravityProvider`, `AntigravityRateLimitsProvider.swift` |
| OpenCode | Local usage provider. | No production provider. | MISSING | macOS `LocalOpenCodeProvider` |
| Hermes Agent | Local usage provider. | No production provider. | MISSING | macOS `LocalHermesProvider` |
| Cursor | Dashboard API primary path with SQLite fallback, including the zero-local-token fix. | No production provider. | MISSING | macOS `LocalCursorProvider` in `LocalAdditionalUsageProvider.swift` |
| Grok | Local usage provider. | No production provider. | MISSING | macOS `LocalGrokProvider` |
| GitHub Copilot | Local usage provider. | No production provider. | MISSING | macOS `LocalCopilotProvider` |
| Kiro | Local usage including Kiro CLI 2.20+ JSONL sessions. | No production provider. | MISSING | macOS `LocalKiroProvider` |
| Pi | Local usage provider. | No production provider. | MISSING | macOS `LocalPiProvider` |
| omp | Provider added after the Windows branch point. | No production provider. | MISSING | macOS `LocalOmpProvider`; upstream commit `b833f12` |

Windows `IUsageProvider`, `UsageSnapshot`, and provider-selector plumbing are reusable foundations, but they are not provider parity by themselves.

## 5. Usage / Rate Limits

The Windows Codex data path is one of the most complete parts of the port. `UsageStore.RefreshAsync` coalesces refreshes, performs daily and enrichment work, preserves stale snapshots on failure, and retains a provider when daily usage is empty but another period or official limit exists. `UsageViewModel.ApplyOfficialLimits` converts provider-level used percentages into clamped remaining values for the UI. `MainWindow.xaml` displays Today, Recent 5 hours, This week, This month, and official 5-hour/weekly reset information.

Remaining gaps:

- Windows `UsagePollingController` matches the macOS Manual/1/2/5/15-minute schedule, defaults to two minutes, retries a truly empty successful refresh once after 20 seconds, and preserves `UsageStore` refresh coalescing.
- macOS exposes used/remaining display preference, warning thresholds, burn-rate forecast, provider status checks, and configurable menu-bar summaries; Windows does not.
- macOS Codex UI can represent multiple buckets, plan metadata, credits/spend controls, and warnings. Windows parses richer app-server data, but `UsageViewModel.ApplyOfficialLimits` selects only primary/secondary rows.
- Antigravity official quota integration is absent on Windows. Claude OAuth limits use the Claude CLI credential file read-only and preserve prior limits on transient or authorization failures.
- Claude Code is a production cost-reporting provider; calculated local cost flows through the existing snapshot and UI path.

## 6. Companion / Pokemon

Windows now connects every successful usage refresh to one provider-neutral companion seam. `CompanionStore` seeds the first valid daily observation, consumes independent provider deltas, handles date rollover/regression/disappearance, carries egg and evolution overflow, persists planned branches, graduates into the dex/catch history, and starts the next egg. Automatic hatch reuses the existing weighted PokeAPI selection and rarity/nature/shiny rules; the view model updates immediately and state survives restart.

The remaining companion gaps are product/economy features intentionally outside this phase: rewards/items, Ditto disguise/reveal, celebrations/notifications, and the Shop, Bag, Collection, and catch-log UI.

## 7. Economy / Shop / Items

macOS `CompanionStore` owns a persisted currency ledger and inventory mutations. `ShopView` and `BagView` expose purchases and item use. Rare Candy advances progression, Mint changes nature, Shiny Charm affects shiny odds, and premium/fresh eggs alter hatch behavior.

Windows model types retain inventory/economy-shaped fields, but there is no connected reward calculation, purchase API, item-use API, or Shop/Bag UI. This category is functionally missing, not merely hidden.

## 8. Floating Pet

The basic floating pet is a good native mapping. macOS uses a non-activating `NSPanel`; Windows uses a transparent topmost WPF `Window` through `FloatingPetController` and `FloatingPokemonWindow`. Windows supports drag, persisted/restored position, multi-monitor clamping, click-to-open, Open/Hide context actions, representative/egg sprites, GIF animation, and sleep-time hide/pause.

Windows lacks the user-selectable 48–192 size, animation-quality selection, token/limit hover tooltip, limit-warning speech bubbles, and companion-event bubbles. The current size is fixed. Upstream also contains recent fixes for animation quality and floating tooltip visibility that have no Windows setting/UI counterpart.

## 9. Tray / Popup

`SystemTrayController`/`NotifyIconTrayIcon` and the WPF popup provide the important Windows-equivalent behavior: startup hidden, left-click toggle, Open/Refresh/Exit menu, cursor-monitor/DPI-aware placement, deactivation-to-hide, and no taskbar entry. Provider selection and manual refresh are reachable.

macOS additionally animates the representative in the `NSStatusItem` and can show configurable token/cost/limit text in the menu bar. A Windows notification-area icon cannot reproduce multiline menu-bar text literally; this is marked macOS-only for the exact shell treatment, while the absence of any equivalent live tray tooltip/badge remains a partial product gap. The Windows icon is currently generic rather than the companion sprite.

## 10. Settings

Windows persists and exposes:

- launch at startup (`HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run`),
- floating pet enabled,
- floating position and reset,
- refresh interval (Manual, 1, 2, 5, or 15 minutes).

`JsonAppSettingsPersistence` stores the app settings in LocalAppData and safely defaults older files to the macOS two-minute interval. The macOS settings surface additionally covers language, representative, animation quality, used/remaining mode, menu content, floating size/bubbles, notification categories, warning thresholds, status checks, update behavior, save transfer, Keychain access, token refresh, and custom scan roots. Those omissions should be implemented only alongside the behavior they configure.

## 11. Notifications

macOS uses `UNUserNotificationCenter` for limit warning/critical alerts and companion events, with opt-in controls and deduplication/re-arm behavior. It also provides floating bubbles. Windows has neither a toast/notification service nor the related settings/event integration. This is a full functional gap.

## 12. Localization

macOS localizes the application and Pokémon data for Korean, English, Japanese, Spanish, French, and Portuguese through `Localization.swift` and `AppLanguage`. Windows preserves the language enum, localized Pokémon names from PokeAPI, and some companion/nature display strings, but `MainWindow.xaml`, tray commands, usage labels, and settings are hard-coded English and there is no language selector or runtime language application. Overall status is partial.

## 13. Lifecycle / Background

Both applications start as background/tray apps, enforce one instance, and respond to sleep/resume. Windows checks `SingleInstanceGuard.TryAcquire` before `AppComposition.Create`, so a second process does not create tray, windows, refresh, or power subscriptions. `UsagePollingController` uses `TimeProvider.CreateTimer`, pauses polling and cancels a pending empty retry on suspend, then restores one schedule after the wake refresh. `WindowsPowerModeEventSource` maps system power events, and shutdown disposal is explicit.

macOS additionally supports launch-agent keep-alive and includes logging/crash reporting. Windows now has startup/manual/timed/resume refresh and equivalent polling sleep behavior, but no automatic restart or equivalent diagnostics pipeline.

## 14. Persistence / Cache

Windows has appropriate native persistence for its implemented scope: LocalAppData JSON for app settings and the complete core companion progression state, position persistence, sprite caching, base-species caching, and Registry auto-start. Claude credentials are discovered read-only from the Claude CLI file; PokeTokenBar neither stores nor refreshes them. macOS also has incremental usage caches, economy state mutation, save export/import, migration from earlier app identity, custom provider roots, and Keychain credential discovery. Windows lacks save transfer and general provider credential/configuration persistence.

Neither audit nor any verification modified user `.codex` data.

## 15. Updates / Distribution

macOS `UpdateChecker` checks GitHub releases and the UI presents update availability; Homebrew installs can upgrade/relaunch while other installs open the release page. The repository includes macOS packaging/signing scripts.

Windows supports a self-contained Release publish and has release packaging metadata, but no in-app update check, installer/uninstaller UX, Windows signing policy, release-channel UI, or automated update path. A direct `.exe` publish is a valid initial distribution mechanism, but only partial parity with the supported macOS lifecycle.

## 16. Platform-specific Mapping

| macOS mechanism | Windows mapping | Assessment |
|---|---|---|
| `NSStatusItem` tray presence | WinForms `NotifyIcon` hosted by WPF | WINDOWS EQUIVALENT for presence/actions; text-rich menu-bar display is not directly portable |
| `NSPopover` | Borderless WPF tool window, deactivation hides | WINDOWS EQUIVALENT |
| Non-activating `NSPanel` | Transparent topmost WPF `Window` with `ShowActivated=false` | WINDOWS EQUIVALENT |
| Process-based single-instance selection | User-SID named `Mutex` | WINDOWS EQUIVALENT |
| `SMAppService` / launch agent | HKCU Run value | WINDOWS EQUIVALENT for login start; no keep-alive |
| `UserDefaults` | LocalAppData JSON | WINDOWS EQUIVALENT |
| Keychain credential discovery | Read-only Claude CLI credential-file discovery | WINDOWS EQUIVALENT for Claude CLI OAuth; no PokeTokenBar credential writes |
| Workspace/display sleep notifications | `SystemEvents.PowerModeChanged` | WINDOWS EQUIVALENT |
| `UNUserNotificationCenter` | No Windows notification implementation | MISSING |
| Homebrew/app bundle update | Self-contained publish | PARTIAL; distribution exists, update lifecycle does not |
| SwiftUI localization environment | No WPF resource/runtime language layer | PARTIAL |

## 17. Test Coverage Gaps

Windows has strong tests around Codex parsing and aggregation, including rollout/fork/canonical handling, daily/period behavior, stale preservation, coalescing, official-limit parsing, remaining-percentage projection, and provider visibility. It also tests tray placement/lifecycle seams, power behavior, single instance, persistence, sprite loading, PokeAPI behavior, and the usage-to-companion ledger/hatch/evolution/graduation cycle.

The most important missing behavior tests correspond to missing production features:

1. No production tests for the ten absent providers or their custom-root/auth variants.
2. No reward, purchase, item-use, premium egg, Shiny Charm, Mint, Rare Candy, or Ditto lifecycle tests.
3. No Shop/Bag/Collection navigation and mutation tests.
4. No notification opt-in, threshold, deduplication, re-arm, or event tests.
5. No full UI localization/language-switch tests.
6. No update checking, release selection, update UX, or save export/import tests.
7. No warning/burn forecast/provider-status behavior tests.

The macOS suite contains dedicated coverage for these areas, including `CompanionTests`, `RareCandyTests`, `PremiumEggTests`, `ShopTests`, `DittoTests`, `SaveTransferTests`, provider-specific suites, `CustomRootsTests`, `LocalUsageCacheTests`, and `SingleInstanceTests`. Test-count parity alone would be misleading; Windows should add tests only as each missing production slice is ported.

## 18. Full Feature Matrix

Counts in section 2 are calculated from this table only.

| Category | Feature | macOS behavior | Windows behavior | Status | macOS evidence | Windows evidence | Priority | Size |
|---|---|---|---|---|---|---|---|---|
| Providers | Provider abstraction/selection | Common provider protocol and selected-provider state | Common interface, snapshots, selector | COMPLETE | `UsageProvider.swift`; `UsageStore.swift` | `IUsageProvider.cs`; `UsageViewModel.cs` | P0 | S |
| Providers | Codex local usage | JSONL local usage with enrichment | Detailed JSONL pipeline and enrichment | COMPLETE | `LocalCodexProvider` | `LocalCodexUsageProvider.cs`; `CodexLocalRolloutPipeline.cs` | P0 | — |
| Providers | Claude Code | Local usage provider | Registered local JSONL provider with period enrichment and cost | COMPLETE | `LocalClaudeProvider` | `LocalClaudeUsageProvider.cs`; `AppComposition.CreateUsageViewModel` | P0 | — |
| Providers | Gemini | Local usage provider | Not registered/implemented | MISSING | `LocalGeminiProvider` | `AppComposition.CreateUsageViewModel` | P0 | M |
| Providers | Antigravity | Local usage provider | Not registered/implemented | MISSING | `LocalAntigravityProvider` | `AppComposition.CreateUsageViewModel` | P0 | L |
| Providers | OpenCode | Local usage provider | Not registered/implemented | MISSING | `LocalOpenCodeProvider` | `AppComposition.CreateUsageViewModel` | P1 | M |
| Providers | Hermes Agent | Local usage provider | Not registered/implemented | MISSING | `LocalHermesProvider` | `AppComposition.CreateUsageViewModel` | P1 | M |
| Providers | Cursor | Dashboard API with SQLite fallback | Not registered/implemented | MISSING | `LocalCursorProvider` | `AppComposition.CreateUsageViewModel` | P1 | L |
| Providers | Grok | Local usage provider | Not registered/implemented | MISSING | `LocalGrokProvider` | `AppComposition.CreateUsageViewModel` | P1 | M |
| Providers | Copilot | Local usage provider | Not registered/implemented | MISSING | `LocalCopilotProvider` | `AppComposition.CreateUsageViewModel` | P1 | M |
| Providers | Kiro | Local usage, including 2.20+ JSONL | Not registered/implemented | MISSING | `LocalKiroProvider` | `AppComposition.CreateUsageViewModel` | P1 | M |
| Providers | Pi | Local usage provider | Not registered/implemented | MISSING | `LocalPiProvider` | `AppComposition.CreateUsageViewModel` | P2 | M |
| Providers | omp | Local usage provider | Not registered/implemented | MISSING | `LocalOmpProvider` | `AppComposition.CreateUsageViewModel` | P2 | M |
| Usage | Daily aggregation | Common daily snapshot | Codex and Claude daily snapshots | COMPLETE | `UsageStore.refresh` | `UsageStore.RefreshAsync` | P0 | — |
| Usage | Active 5-hour period | Enriched active block | Codex and Claude Recent 5 hours | COMPLETE | `LocalUsageReader` enrichment | `CodexUsagePeriodAggregator.cs`; `LocalClaudeUsageProvider.cs` | P0 | — |
| Usage | Week aggregation | Calendar week enrichment | Codex and Claude calendar-week enrichment | COMPLETE | `LocalUsageReader` | `CodexUsagePeriodAggregator.cs`; `LocalClaudeUsageProvider.cs` | P0 | — |
| Usage | Month aggregation | Calendar month enrichment | Codex and Claude month enrichment, including zero-today carriers | COMPLETE | `LocalUsageReader` | `UsageStore.cs`; `CodexUsagePeriodAggregator.cs`; `LocalClaudeUsageProvider.cs` | P0 | — |
| Usage | Token totals | Input/output/cache/total representation | Codex and Claude token totals represented | COMPLETE | `Models.swift` | `DailyUsage.cs`; `BlockUsage.cs`; `PeriodUsage.cs`; `UsageViewModel.cs` | P0 | — |
| Usage | Cost display | Displayed for cost-reporting providers | Claude local cost uses the shared snapshot/UI path | COMPLETE | `PopoverView.usageSection` | `LocalClaudeUsageProvider.cs`; `UsageViewModel.cs`; `MainWindow.xaml` | P1 | — |
| Usage | Refresh coalescing | Concurrent requests share refresh | Concurrent requests share refresh | COMPLETE | `UsageStore.refresh` | `UsageStore.RefreshAsync` | P0 | — |
| Usage | Stale snapshot preservation | Keeps usable prior data on failure | Keeps daily/period/official prior data | COMPLETE | `UsageStore` refresh phases | `UsageStore.cs` | P0 | — |
| Usage | Recurring polling | Configurable timer refresh | Native timer with Manual/1/2/5/15-minute schedules | COMPLETE | `UsageStore.reschedule` | `UsagePollingController.cs` | P0 | — |
| Usage | Empty-result retry | One 20-second retry after successful empty scan | Same; errors, month-only, and official-only are not empty | COMPLETE | `UsageStore.handleEmptyUsageRetry` | `UsagePollingController.EvaluateEmptyRetry` | P1 | — |
| Usage | Custom scan roots | Per-provider configured roots | Conventional roots plus Claude's `CLAUDE_CONFIG_DIR`; no settings UI/persistence | MISSING | `CustomRoots`; persisted `UsageStore` settings | `CodexSessionLocator.cs`; `LocalClaudeUsageProvider.cs` | P1 | M |
| Usage | Burn forecast | Computes burn tier/forecast | No production calculation/UI | MISSING | `UsageStore` burn fields | `UsageViewModel.cs` | P2 | M |
| Usage | Provider status checks | Optional provider health/status | No production status layer | MISSING | `UsageStore` status checks | absent | P2 | M |
| Limits | Codex official fetch | App-server rate limits | App-server rate limits | COMPLETE | `CodexRateLimitsProvider.swift` | `CodexRateLimitsProvider.cs` | P0 | — |
| Limits | Remaining percentages | Used/remaining selectable | Remaining values, clamped 0–100 | PARTIAL | `AppSettings.limitDisplayMode` | `UsageViewModel.ApplyOfficialLimits` | P0 | S |
| Limits | Reset display | Shows reset timestamps | Shows reset timestamps | COMPLETE | `PopoverView.limitRow` | `MainWindow.xaml` | P0 | — |
| Limits | Multiple Codex buckets | Models/displays bucket set | Parser can see richer data; UI projects primary/secondary only | PARTIAL | `CodexRateLimitsProvider` | `CodexRateLimitsProvider.cs`; `UsageViewModel.ApplyOfficialLimits` | P1 | M |
| Limits | Plan/credits/spend | Displays plan, credits/spend controls where available | Not exposed in UI | MISSING | `CodexRateLimitsProvider`; `PopoverView` | `UsageViewModel.cs` | P1 | M |
| Limits | Claude OAuth limits | OAuth limits/account metadata | Read-only CLI OAuth credential discovery, limits, plan, email, and organization metadata | COMPLETE | `OAuthLimitsProvider.swift` | `ClaudeCredentialProvider.cs`; `ClaudeRateLimitsProvider.cs`; `UsageViewModel.cs` | P0 | — |
| Limits | Antigravity quota | Google quota groups | No Antigravity integration | MISSING | `AntigravityRateLimitsProvider.swift` | absent | P1 | L |
| Companion | Persisted companion restore | Restores current companion state | Restores current companion state | COMPLETE | `CompanionStore.load` | `CompanionStore.InitializeAsync`; `JsonCompanionPersistence.cs` | P0 | — |
| Companion | Pokémon data lookup | Species/evolution/localized data | GraphQL index plus REST fallback/cache | COMPLETE | Pokémon API services | `PokeApiClient.cs` | P0 | — |
| Companion | Manual representative | Collection representative can be selected | Stored representative can be selected through VM seam, no full collection UI | PARTIAL | `CompanionView` representative picker | `CompanionStore.SetRepresentativeAsync`; `CompanionViewModel.cs` | P1 | M |
| Companion | Usage ledger | Per-provider daily ledger consumes deltas | Provider-neutral daily ledger with baseline, rollover, rebase, and restart semantics | COMPLETE | `CompanionStore.update` | `CompanionStore.UpdateUsageAsync` | P0 | — |
| Companion | Egg progression | Usage advances egg | Usage advances egg; 5M hatch threshold and overflow carry match | COMPLETE | `CompanionStore.applyUsage` | `CompanionStore.UpdateUsageAsync` | P0 | — |
| Companion | Evolution | Planned branches and overflow progression | Persisted planned branches and cross-stage overflow progression | COMPLETE | `CompanionStore.applyUsage` | `CompanionStore.ApplyUsageCore` | P0 | — |
| Companion | Graduation/new egg | Graduates final stage and starts cycle | Final stage graduates and starts a zero-progress egg | COMPLETE | `CompanionStore.graduate` | `CompanionStore.GraduateCore` | P0 | — |
| Companion | Dex/catch log mutation | Captures update dex and log | Graduation persists individual dex/catch entries; presentation UI remains separate | COMPLETE | `CompanionStore.graduate`; `CompanionView` | `CompanionStore.GraduateCore`; `JsonCompanionPersistence.cs` | P1 | — |
| Companion | Rarity/nature/shiny roll | Weighted hatch/capture attributes | Production auto-hatch uses weighted species and persists rarity/nature/shiny | COMPLETE | `CompanionStore` sampling | `CompanionStore.HatchRandomAsync` | P1 | — |
| Companion | Ditto disguise/reveal | Full disguise/reveal lifecycle | No production lifecycle | MISSING | `CompanionStore`; `DittoTests` | absent | P2 | M |
| Economy | Currency rewards/ledger | Usage and events grant currency | Ledger-shaped model only | MISSING | `CompanionStore.grantCandies` | `CompanionModels.cs` | P1 | L |
| Economy | Shop/purchases | Purchases validated and persisted | No actions/UI | MISSING | `CompanionStore.buy`; `ShopView.swift` | absent | P1 | L |
| Economy | Rare Candy | Inventory item advances progress | Model shape only | MISSING | `CompanionStore.useRareCandy` | `CompanionModels.cs` | P1 | M |
| Economy | Mint | Changes nature | Model shape only | MISSING | `CompanionStore.useMint` | `CompanionModels.cs` | P2 | M |
| Economy | Shiny Charm | Alters shiny odds | Model shape only | MISSING | `CompanionStore`; `ShinyCharmTests` | `CompanionModels.cs` | P2 | M |
| Economy | Premium/fresh eggs | Purchasable egg variants affect hatch | No production behavior | MISSING | `CompanionStore`; `PremiumEggTests` | absent | P2 | M |
| Floating | Basic floating window | Non-activating always-on-top panel | Transparent topmost non-activating WPF window | WINDOWS EQUIVALENT | `FloatingPetPanel.swift` | `FloatingPokemonWindow.xaml.cs` | P0 | — |
| Floating | Drag and position persistence | Drag/persist/multi-screen recovery | Drag/persist/multi-monitor clamp | WINDOWS EQUIVALENT | `FloatingPetPanel` | `FloatingPetController.cs`; `FloatingPetPositioner.cs` | P0 | — |
| Floating | Click/context actions | Opens popup; Open/Hide actions | Opens popup; Open/Hide actions | WINDOWS EQUIVALENT | `FloatingPetPanel` | `FloatingPokemonWindow.xaml.cs` | P0 | — |
| Floating | Animated/static fallback | Animated pet/egg with fallback | GIF/static pet/egg with cache/fallback | COMPLETE | `SpriteAnimation.swift`; `SpriteLoader.swift` | `PokemonSpriteLoader.cs`; `AnimatedSpritePresenter.cs`; `WpfPokemonSpriteDecoder.cs` | P0 | — |
| Floating | Configurable size | 48–192 setting | Fixed size | MISSING | `AppSettings.floatingPetSize` | `FloatingPokemonWindow.xaml` | P2 | S |
| Floating | Animation quality | User-selectable quality | No setting | MISSING | `AppSettings.animationQuality` | absent | P2 | M |
| Floating | Hover usage tooltip | Tokens/limit hover summary | No tooltip | MISSING | `FloatingPetPanel` | `FloatingPokemonWindow.xaml` | P2 | M |
| Floating | Alert/event bubbles | Limit and companion speech bubbles | No bubbles | MISSING | `FloatingPetPanel`; app event routing | absent | P1 | M |
| Tray | Tray presence/actions | `NSStatusItem` opens app controls | `NotifyIcon` with Open/Refresh/Exit | WINDOWS EQUIVALENT | `PokeTokenBarApp` status item | `SystemTrayController.cs`; `NotifyIconTrayIcon.cs` | P0 | — |
| Tray | Popup behavior | Transient popover closes outside | Tool window closes on deactivation | WINDOWS EQUIVALENT | `PopoverView`; app delegate | `MainWindow.xaml.cs` | P0 | — |
| Tray | Multi-monitor placement | Native popover placement | Cursor-monitor and DPI-aware placement | COMPLETE | AppKit popover | `PopupPositioner.cs` | P0 | — |
| Tray | Animated companion icon | Representative animates in menu bar | Generic application icon | PARTIAL | status-item sprite controller | `NotifyIconTrayIcon.cs` | P1 | M |
| Tray | Menu-bar token/cost/limit text | Configurable status-item text | Exact menu-bar treatment unavailable in notification area | MAC-ONLY / N/A | `UsageStore.menuBarText` | Windows shell constraint | P3 | — |
| Tray | Provider switch/manual refresh | Provider tabs and refresh | Selector and refresh command | COMPLETE | `PopoverView` | `MainWindow.xaml`; `UsageViewModel.cs` | P0 | — |
| Popup | Home usage view | Usage, limits, companion, warnings | Usage, limits, companion header | PARTIAL | `PopoverView` | `MainWindow.xaml` | P0 | M |
| Popup | Shop tab | Reachable shop | Absent | MISSING | `ShopView.swift`; `PopoverView` | absent | P1 | M |
| Popup | Bag tab | Reachable inventory/actions | Absent | MISSING | `BagView.swift`; `PopoverView` | absent | P1 | M |
| Popup | Collection/dex tab | Collection, dex, catch log, representative | Absent | MISSING | `CompanionView.swift`; `PopoverView` | absent | P1 | L |
| Settings | Launch at login | Native login service | HKCU Run entry | WINDOWS EQUIVALENT | settings login service | `WindowsAutoStartService.cs` | P0 | — |
| Settings | Floating enabled/position reset | Toggle/reset | Toggle/reset | COMPLETE | `SettingsView` | `SettingsViewModel.cs`; `MainWindow.xaml` | P0 | — |
| Settings | Refresh interval | Manual/1/2/5/15 minute choices | Same choices, persisted and immediately rescheduled | COMPLETE | `UsageStore.refreshInterval` | `RefreshIntervalMode`; `SettingsViewModel.SelectedRefreshInterval`; `MainWindow.xaml` | P0 | — |
| Settings | Language | Six-language selector | No app-language selector | MISSING | `SettingsView`; `Localization.swift` | `MainWindow.xaml` | P1 | M |
| Settings | Limit used/remaining mode | User choice | Fixed remaining display | PARTIAL | `AppSettings.limitDisplayMode` | `UsageViewModel.ApplyOfficialLimits` | P2 | S |
| Settings | Floating/animation options | Size, quality, bubble preferences | Enabled only | PARTIAL | `SettingsView` | `SettingsViewModel.cs` | P2 | M |
| Settings | Notification/threshold options | Category toggles and 80/95 defaults | Absent | MISSING | `AppSettings` | absent | P1 | M |
| Settings | Provider roots/auth controls | Roots, Keychain opt-out, refresh controls | Absent | MISSING | `SettingsView`; `CustomRoots` | absent | P1 | L |
| Localization | Pokémon names/natures | Six languages | API names and some companion strings support six languages | PARTIAL | `Localization.swift`; model helpers | `PokeApiClient.cs`; `CompanionViewModel.cs` | P1 | M |
| Localization | Full application UI | Six-language UI strings | Usage/tray/settings hard-coded English | MISSING | `Localization.swift` | `MainWindow.xaml`; `NotifyIconTrayIcon.cs` | P1 | L |
| Notifications | Limit warning/critical | Configurable, deduped notifications | Absent | MISSING | app notification routing; `UsageStore` | absent | P1 | L |
| Notifications | Companion events | Hatch/evolve/graduate notifications | Absent | MISSING | `PokeTokenBarApp` | absent | P1 | M |
| Lifecycle | Start hidden/background | Accessory tray app | WPF tray app starts hidden | WINDOWS EQUIVALENT | `PokeTokenBarApp` | `App.xaml.cs` | P0 | — |
| Lifecycle | Single instance | Prevents duplicate lifecycle | User-SID named mutex before composition | WINDOWS EQUIVALENT | `SingleInstanceController` | `SingleInstanceGuard.cs`; `App.OnStartup` | P0 | — |
| Lifecycle | Sleep/resume | Pauses polling/work and refreshes after wake | Pauses polling/retry, preserves pet behavior, refreshes, then restores one schedule | COMPLETE | app sleep handlers | `PowerLifecycleController.cs`; `UsagePollingController.cs` | P0 | — |
| Lifecycle | Clean shutdown | Releases app resources | Disposes tray/windows/events/mutex | COMPLETE | app termination | `App.xaml.cs` | P0 | — |
| Lifecycle | Crash diagnostics/keep-alive | Logging/crash reporting and launch-agent behavior | No equivalent diagnostics or restart | MISSING | app/logging/launch-agent code | absent | P2 | L |
| Persistence | App settings | `UserDefaults` | LocalAppData JSON | WINDOWS EQUIVALENT | `UsageStore.init` and persisted properties | `JsonAppSettingsPersistence.cs` | P0 | — |
| Persistence | Companion state | Persisted game state | Ledger, egg, active path, traits, dex, and representative persist/restore | COMPLETE | `CompanionStore` | `CompanionStore.cs`; `JsonCompanionPersistence.cs` | P0 | — |
| Persistence | Sprite/species cache | Cached assets/data | Cached sprites/base index | COMPLETE | sprite/cache services | `PokemonSpriteLoader.cs`; `PokeApiClient.cs` | P1 | — |
| Persistence | Incremental usage cache | Provider scan/cache support | Codex scans and in-memory snapshots; no equivalent persistent usage cache | PARTIAL | `LocalUsageCache` | `CodexLocalRolloutPipeline.cs`; `UsageStore.cs` | P2 | M |
| Persistence | Save export/import | Transfer companion save | Absent | MISSING | save transfer service/UI | absent | P2 | M |
| Updates | Update checking/UI | GitHub release check and banner | Absent | MISSING | `UpdateChecker.swift`; `PopoverView` | absent | P1 | M |
| Distribution | Packaged release | App bundle/Homebrew flows | Self-contained Windows publish | PARTIAL | packaging scripts | Windows `.csproj`; `README.md` | P0 | M |
| Distribution | Upgrade/relaunch | Homebrew upgrade or release-page path | No updater/relaunch path | MISSING | `UpdateChecker` | absent | P2 | L |
| Distribution | Signing/installer policy | macOS signing/package workflow | No equivalent documented automated Windows installer/signing path | MISSING | `scripts/`; package docs | Windows project/release docs | P2 | L |

## 19. Recommended Porting Roadmap

### Phase A — Runtime correctness and provider foundation

With lifecycle-safe polling, interval persistence, empty-result retry, and Claude Code complete, port the remaining providers in dependency/value order: Gemini, Antigravity, Cursor, then the remaining local-only providers. Reuse the existing `IUsageProvider`/`UsageStore`/selector contract and add no speculative provider framework.

**Exit criteria:** long-running usage stays current; provider registration is data-driven enough for the implemented set; each provider has local fixtures and period tests; failures preserve stale data.

### Phase B — Connect the companion game loop — complete

Completed with one refresh-to-companion integration seam and focused provider-ledger, egg, evolution, graduation, dex, UI-update, and persistence tests.

**Exit criteria:** real usage deterministically progresses and persists a companion across restart; overflow and date/provider ledger semantics match macOS.

### Phase C — Economy and companion surfaces

Port candy rewards, inventory mutations, Rare Candy, Mint, Shiny Charm, egg variants, and purchases; then expose Shop, Bag, Collection/dex, catch log, and representative selection. Do not build screens before their store actions are functional and tested.

**Exit criteria:** every visible action is persisted, recoverable, and covered by focused state-transition tests.

### Phase D — Alerts, settings, and localization

Add Windows notifications with threshold/deduplication semantics, floating bubbles, full refresh/limit/floating settings, and a WPF resource-based six-language UI. Add burn/status displays only after polling and notifications supply a reason for them.

**Exit criteria:** all user-facing strings switch language; warning and companion notifications respect settings and do not spam; floating size/quality/bubbles persist.

### Phase E — Update and distribution closure

Choose and document a Windows installer/signing/update strategy, add release checking and safe handoff to the chosen updater or release page, then add save export/import and support diagnostics.

**Exit criteria:** a non-developer can install, update, recover/export state, and provide actionable diagnostics without altering `.codex` data.
