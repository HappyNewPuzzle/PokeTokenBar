# PokeTokenBar Windows 코드 구조

이 문서는 `windows-port` 브랜치의 Windows 2.5.7 코드를 학습하기 위한 안내서입니다. macOS Swift 원본은 비교 자료이고, 아래 설명은 현재 Windows C# 구현을 기준으로 합니다.

## 전체 구조

```text
src/
├─ PokeTokenBar.Windows.App/             WPF 화면, tray, 앱 실행과 lifecycle
├─ PokeTokenBar.Windows.Core/            플랫폼 독립적인 모델과 업무 규칙
└─ PokeTokenBar.Windows.Infrastructure/  파일·프로세스·네트워크·Windows 연결
WindowsTests/PokeTokenBar.Windows.Tests/ Windows xUnit 테스트
installer/                               Inno Setup installer 정의
scripts/                                 Windows/macOS 빌드·테스트·릴리스 자동화
assets/                                  아이콘·스크린샷·GIF
Sources/PokeTokenBar/                    macOS Swift 원본
Tests/PokeTokenBarTests/                 macOS Swift 테스트
docs/                                    사용자·개발자 문서
```

`bin/`, `obj/`, `artifacts/`는 자동 생성되는 빌드 산출물입니다. 현재 Windows 앱 버전은 App 프로젝트의 `Version`, `AssemblyVersion`, `FileVersion`에 정의된 2.5.7입니다.

## 프로젝트 의존성

```text
App ─────────────→ Core
 │
 └─→ Infrastructure ─→ Core
```

- `App`은 화면에서 Core 모델을 사용하고 실제 서비스 구성을 위해 Infrastructure도 직접 참조합니다.
- `Infrastructure`는 Core가 정의한 인터페이스를 구현합니다.
- `Core`는 App과 Infrastructure를 참조하지 않는 `net10.0` 프로젝트입니다.
- `App`은 WPF와 Windows Forms tray를 사용하는 `net10.0-windows` 실행 프로젝트입니다.

이 방향 덕분에 사용량 집계, provider 상태, companion lifecycle과 economy 규칙을 WPF 없이 테스트할 수 있습니다.

## `src/PokeTokenBar.Windows.Core/`

Core는 “무엇을 계산하고 어떤 상태를 보존할 것인가”를 담당합니다. 파일·Registry·HTTP·WPF 구현은 두지 않습니다.

### 사용량과 provider 상태

- `DailyUsage.cs`: 날짜별 입력·출력·캐시 쓰기·캐시 읽기·전체 토큰과 비용
- `PeriodUsage.cs`: 주간·월간 합계
- `BlockUsage.cs`: 최근 5시간 block, 속도와 비용
- `ProviderSnapshot.cs`: provider 하나의 Today/5h/Week/Month 화면 상태
- `ProviderEnrichment.cs`: daily와 독립적으로 기간 데이터를 보충하는 결과
- `IUsageProvider.cs`: 모든 usage provider가 구현하는 공통 계약
- `UsageStore.cs`: 병렬 provider refresh, 단계별 snapshot 확정, stale 보존, refresh coalescing, official quota 결합
- `ProviderStatusModels.cs`: Ready, LocalDataOnly, NoSessions, Stale, Error와 인증 상태
- `UsageSnapshotCache.cs`: 마지막 정상 local snapshot을 재시작 후 복원하기 위한 모델과 persistence 계약
- `CodexRateLimitModels.cs`, `ClaudeRateLimitModels.cs`, `AntigravityRateLimitModels.cs`: provider별 official quota 모델

`UsageStore`는 daily가 없더라도 5시간·주간·월간 값 또는 official quota가 있으면 provider를 유지합니다. 한 provider의 실패는 다른 provider snapshot을 실패 상태로 바꾸지 않으며, official 조회 실패도 정상 local usage를 제거하지 않습니다.

### Companion lifecycle

- `CompanionModels.cs`: Egg, active Pokémon, 진화 경로, `DexEntry`, representative, rarity, nature, shiny, Ditto 상태
- `PokemonBalance`: Egg 부화 기준 5,000,000 tokens와 rarity·진화 단계별 threshold
- `CompanionStore.cs`: usage baseline과 provider별 delta, Egg 성장, hatch, evolution, graduation, representative와 Dex 상태
- `ICompanionPersistence.cs`: companion 저장·로드 계약
- `IPokeApiClient.cs`: Pokémon 종, 진화 계보, 다국어 이름 조회 계약

성장량의 진입점은 `CompanionStore.UpdateUsageAsync()`입니다. 현재 Today 합계를 매번 더하지 않고 provider별로 마지막 적용값을 기억해 증가분만 반영합니다. 부화 threshold를 넘긴 값은 active Pokémon으로, 진화 threshold를 넘긴 값은 다음 stage로 이월하지만 graduation 뒤 남은 값은 새 Egg에 이월하지 않습니다.

### Companion economy와 Released 상태

- `CompanionEconomy.cs`: Mint, Rare Candy, Shiny Charm, 일반·Uncommon·Rare Egg, 가격, 구매·사용 결과와 candy grant 모델
- `CompanionStore.PurchaseAsync()`: 사용 가능 토큰 차감, item 적재 또는 새 Egg 구매
- `CompanionState.Inventory`, `UsedSinceInstall`, `SpentTokens`: 가방과 구매 가능 잔액의 근거
- `DexEntry.ReleasedAt`, `DexEntry.IsReleased`: 졸업 개체와 중도 Released 개체의 구분

Windows 2.5.7에서 Egg는 active Pokémon이 있을 때만 구매할 수 있습니다. 구매하면 기존 active를 삭제하지 않고, 지금까지 도달한 `PathIds` 부분을 `ChainOrder`로 복사하고 현재 species를 `FinalId`로 기록한 `DexEntry`를 추가합니다. rarity, nature, shiny 여부, 조회된 이름, `CaughtAt`, `ReleasedAt`도 보존한 뒤 progress 0의 새 Egg를 시작합니다. bitmap 자체는 저장하지 않으며 Collection/Floating UI가 species ID로 sprite를 다시 로드합니다.

### 설정, 알림, update와 state transfer 모델

- `AppSettings.cs`: 언어, provider 선택, custom roots, refresh 간격, used/remaining 표시, 알림과 임계값, credential access, update 알림, floating 크기·위치·animation quality
- `Notifications.cs`: 한도 경고, companion event, 알림 tier 판정 모델
- `Updates.cs`: update 상태, 결과, semantic version 및 신뢰 가능한 Windows release URL 검증
- `StateTransferModels.cs`: save import/export preview, summary와 오류 모델
- `ReliabilityEventLog.cs`: recovery와 최근 오류를 민감정보 없이 diagnostics에 전달하는 메모리 로그

## `src/PokeTokenBar.Windows.Infrastructure/`

Infrastructure는 Core 계약을 실제 사용자 환경에 연결합니다.

### 등록된 usage provider

`AppComposition`은 다음 12개 provider를 등록합니다.

| ID | 표시 이름 | 구현 | 비용 집계 |
|---|---|---|---|
| `codex` | Codex | `LocalCodexUsageProvider` | 지원 |
| `claude_code` | Claude Code | `LocalClaudeUsageProvider` | 지원 |
| `gemini` | Gemini | `LocalGeminiUsageProvider` | 지원 |
| `antigravity` | Antigravity | `LocalAntigravityUsageProvider` | 미지원 |
| `cursor` | Cursor | `LocalCursorUsageProvider` | 미지원 |
| `opencode` | OpenCode | `LocalOpenCodeUsageProvider` | 지원 |
| `hermes` | Hermes Agent | `LocalHermesUsageProvider` | 지원 |
| `grok` | Grok | `LocalGrokUsageProvider` | 지원 |
| `copilot` | Copilot | `LocalCopilotUsageProvider` | 미지원 |
| `kiro` | Kiro | `LocalKiroUsageProvider` | 미지원 |
| `pi` | Pi | `LocalPiUsageProvider` | 미지원 |
| `omp` | omp | `LocalOmpUsageProvider` | 지원 |

각 provider는 고유한 local 로그·JSON·DB 위치를 읽어 `IUsageProvider` 결과로 변환합니다. `ConfigurableUsageProvider`는 기본 root에 Settings의 provider별 custom root를 추가합니다.

### Codex local pipeline

```text
CodexSessionLocator
  → CodexJsonlReader / CodexRolloutReader
  → CodexTokenCountParser
  → fork dependency 복원
  → canonical·consecutive·cross-file 중복 제거
  → cumulative epoch 확정
  → CodexUsageAggregator / CodexUsagePeriodAggregator
  → LocalCodexUsageProvider
```

`%USERPROFILE%\.codex\sessions`와 존재하는 `archived_sessions`를 읽고, local calendar 기준 Today/5h/Week/Month를 계산합니다.

### Official quota와 credential access

- `CodexRateLimitsProvider`: 로컬 `codex app-server`에서 Codex 5시간·주간 한도와 credits를 조회
- `ClaudeRateLimitsProvider`: Claude OAuth credential을 사용해 official 5시간·주간 한도를 조회
- `AntigravityRateLimitsProvider`: Antigravity credential을 사용해 official quota group을 조회
- `ClaudeCredentialProvider`: `~/.claude/.credentials.json`을 읽음
- `AntigravityCredentialProvider`: Antigravity token 파일과 Windows Credential Manager를 읽음
- `WindowsCredentialStore`: Win32 Credential Manager read adapter

Settings의 credential access toggle은 Claude와 Antigravity official 조회에만 적용됩니다. OFF여도 local usage provider는 계속 동작하며 official 값만 비워집니다. Codex official 조회는 로컬 app-server 경로입니다. 나머지 provider에는 동일한 official quota 계약을 가장하지 않습니다.

### Pokémon, update와 persistence

- `PokeApiClient`: base species·evolution line·다국어 이름 조회와 `base-index.json` cache
- `PokemonSpriteLoader`: sprite 다운로드, 검증과 `sprites/` cache
- `GitHubReleaseUpdateChecker`: 안정된 `windows-vX.Y.Z` GitHub Release 확인
- `JsonAppSettingsPersistence`: `settings.json`
- `JsonCompanionPersistence`: `companion-state.json`, last-known-good `.bak`, 손상 파일 격리
- `JsonUsageSnapshotPersistence`: `usage-cache.json`
- `AtomicFile`: 임시 파일 작성, disk flush, 원자적 overwrite와 잔여 temp 정리
- `StateTransferService`: settings+companion save export/import, import 전 backup, 실패 시 rollback
- `WindowsAutoStartService`: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

## `src/PokeTokenBar.Windows.App/`

App는 WPF 표현과 Windows lifecycle을 조립합니다.

### Composition과 주요 ViewModel

- `App.xaml.cs`: single-instance 검사 후 앱을 조립하고 종료 시 resource를 역순 정리
- `AppComposition.cs`: provider, quota, persistence, companion, update와 ViewModel을 연결하는 composition root
- `ApplicationComposition.cs`: `HttpClient`, polling, usage-companion bridge와 ViewModel의 lifetime 소유
- `MainViewModel.cs`: `Usage`, `Companion`, `Economy`, `Settings`, `Support`를 단일 DataContext로 제공
- `UsageViewModel.cs`: provider 선택, local 기간 사용량, official quota, stale/status, credits, burn-rate forecast, refresh
- `CompanionViewModel.cs`: Egg/Pokémon 이름, sprite, stage와 progress
- `EconomyViewModel.cs`: Shop, Bag, Collection/Dex, Current/Caught/Representative/Released 표시와 명령
- `SettingsViewModel.cs`: 설정 persistence, provider 선택 유지, custom roots와 언어 전환
- `SupportViewModel.cs`: update 확인·건너뛰기, GitHub release 열기, save import/export, Copy Diagnostics
- `LocalizationService.cs`: 한국어·영어·일본어·스페인어·프랑스어·포르투갈어·독일어 UI 문자열
- `DiagnosticsReport.cs`: 버전·환경·provider canonical 상태·persistence 상태·recovery/error 요약 생성

### UI와 Windows adapter

- `MainWindow.xaml`: Home, Shop, Bag, Collection 탭과 Settings/About·Support 영역
- `Tray/`: companion icon animation, localized Open/Refresh/Exit, popup 표시와 DPI-aware 위치
- `FloatingPet/`: 투명 always-on-top companion 창, drag 위치 저장, 클릭/우클릭, 알림 bubble
- `Sprites/`: 정적/GIF decode, frame timing과 animation quality 적용
- `Commands/AsyncCommand.cs`: 중복 실행을 막는 비동기 `ICommand`

### Lifecycle

- `InitialRefreshController`: startup usage refresh
- `InitialCompanionController`: 저장된 companion과 sprite 준비
- `UsagePollingController`: Manual/1/2/5/15분 polling과 빈 결과 20초 재시도
- `UsageCompanionController`: 성공한 usage snapshot을 companion delta 경로로 한 번만 전달
- `PowerLifecycleController`: 절전 중 polling 중지, 복귀 refresh와 interval 복원
- `NetworkReconnectController`: 네트워크 복귀 시 refresh 요청
- `NotificationController`: official limit tier, companion hatch/evolution/graduation event를 tray balloon·floating bubble로 전달
- `SingleInstanceGuard`: 사용자 단위 named mutex로 두 번째 프로세스의 composition 이전 종료

startup 순서는 대략 다음과 같습니다.

```text
SingleInstanceGuard
  → AppComposition
  → MainWindow / tray / notifications / floating pet
  → power·network lifecycle 연결
  → initial usage·companion refresh
  → polling 시작
  → background update check
```

## 저장 파일과 복구

기본 root는 `%LOCALAPPDATA%\PokeTokenBar`이며 테스트·격리 환경에서는 `POKETOKENBAR_DATA_ROOT`로 바꿀 수 있습니다.

| 경로 | 책임 |
|---|---|
| `settings.json` | UI, provider, notification, update, credential access 설정 |
| `companion-state.json` | ledger, Egg/active, evolution, economy, inventory, Dex, representative |
| `companion-state.json.bak` | 마지막 정상 companion backup |
| `usage-cache.json` | 마지막 정상 provider local snapshot |
| `base-index.json` | PokéAPI base-species index cache |
| `sprites/` | Pokémon sprite cache |
| `backups/PokeTokenBar-PreImport-*.json` | import 전 자동 save backup, 최근 5개 유지 |
| `*.corrupt-*` | 파싱 불가능한 persistence/cache의 quarantine 산출물 |

설정·companion·usage cache는 `AtomicFile`을 사용합니다. companion은 정상 `.bak` 복원을 시도하고, settings/usage/base index는 손상된 입력을 격리하거나 무시·재생성합니다. import는 현재 save를 먼저 backup하고 두 파일 적용 중 실패하면 rollback합니다.

## Release, installer와 production signing

- `installer/PokeTokenBar.iss`: 사용자 단위 Inno Setup 설치·제거, optional signed uninstaller 정의
- `scripts/build-release.ps1`: restore/test/self-contained publish, portable ZIP, optional installer, manifest와 staging promotion
- `scripts/ReleaseSigning.psm1`: signing 설정·인증서·서명·ZIP·hash 검증 helper
- `artifacts/publish/win-x64`: 성공한 self-contained publish를 후속 workflow용으로 유지
- `artifacts/release`: portable directory/ZIP, optional installer, `SHA256SUMS.txt`

unsigned release/build가 기본이며 인증서가 없어도 지원됩니다. `-RequireSigning`은 명시적으로 선택하는 stricter production mode로, `-BuildInstaller`, Windows Certificate Store의 private-key Code Signing 인증서, thumbprint, timestamp URL, SignTool과 Inno Setup을 요구합니다.

production mode는 프로젝트 소유 PE(`PokeTokenBar.exe`, 앱/Core/Infrastructure DLL)를 서명·검증한 뒤 ZIP을 만들고, ZIP을 다시 풀어 hash·signer·timestamp를 확인합니다. Inno가 uninstaller와 installer를 서명하며 installer 검증 뒤 최종 bytes를 기준으로 `SHA256SUMS.txt`를 만듭니다. 실패한 staging 결과는 release로 승격하지 않습니다.

`.gitignore`는 `*.pfx`, `*.p12`, `*.pem`, `*.key`를 차단하고, release pipeline도 이 형식이 publish/portable/ZIP에 들어오면 실패합니다. 실제 signed 성공, 신뢰 prompt와 SmartScreen 평가는 production 인증서가 설치된 release 환경에서 별도 QA가 필요합니다.

## `WindowsTests/PokeTokenBar.Windows.Tests/`

현재 문서 작성 시점인 Windows 2.5.7 HEAD에는 1,528개 test case가 있습니다. 주요 영역은 다음과 같습니다.

- Codex JSONL/fork/canonical/epoch/기간 집계
- Claude, Gemini, Antigravity, Cursor와 추가 local provider
- UsageStore stale/coalescing/cache와 UsageViewModel/status/official quota
- Companion baseline, hatch, evolution, graduation, Ditto, representative
- economy 구매·item·candy·Shop/Bag/Collection·Released persistence
- tray/floating sprite·position·animation과 single instance
- polling, sleep/wake, network reconnect와 notifications
- localization, update UX, diagnostics, recovery와 save transfer
- release packaging, installer discovery와 Phase 11A production signing contract

테스트는 실제 사용자 `.codex`나 `%LOCALAPPDATA%\PokeTokenBar` 대신 fixture, fake service와 임시 디렉터리를 사용합니다.

## 공부 시작점

- local usage: `MainWindow.xaml` → `UsageViewModel` → `UsageStore` → provider → parser
- official quota: `UsageViewModel` → `UsageStore` → Codex/Claude/Antigravity rate-limit provider
- companion: `UsageCompanionController` → `CompanionStore.UpdateUsageAsync` → progression
- economy/Released: `EconomyViewModel` → `CompanionStore.PurchaseAsync` → `DexEntry`
- persistence/recovery: JSON persistence → `AtomicFile` → backup/quarantine → diagnostics
- update/support: `SupportViewModel` → `GitHubReleaseUpdateChecker` / `StateTransferService`
- release/signing: `build-release.ps1` → `ReleaseSigning.psm1` → Inno Setup
