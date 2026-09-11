# PokeTokenBar Windows 기능 안내

이 문서는 `windows-port` 브랜치의 Windows 2.5.7 구현을 기준으로 합니다. macOS 원본에만 있는 기능을 Windows 기능으로 간주하지 않습니다.

## 한눈에 보기

PokeTokenBar Windows는 알림 영역에 상주하면서 여러 AI 도구의 사용량을 모아 보여 주고, 실제 증가한 token을 Pokémon companion의 성장과 economy에 연결하는 WPF 애플리케이션입니다.

```text
provider별 사용량 읽기
  → Today/5h/Week/Month snapshot과 상태 표시
  → 오늘 사용량 delta만 companion에 적용
  → Egg 부화 → 진화 → 졸업 → Dex 기록
  → 사용 누적 토큰으로 item/Egg 구매
  → tray·floating pet·알림·persistence 갱신
```

## Usage provider

### 현재 등록된 provider

Windows 2.5.7은 다음 12개 provider를 등록합니다.

| Provider | ID | Local usage | 비용 표시 | Official quota |
|---|---|---|---|---|
| Codex | `codex` | 지원 | 지원 | `codex app-server` |
| Claude Code | `claude_code` | 지원 | 지원 | Claude OAuth |
| Gemini | `gemini` | 지원 | 지원 | 없음 |
| Antigravity | `antigravity` | 지원 | 없음 | Antigravity credential/API |
| Cursor | `cursor` | 지원 | 없음 | 없음 |
| OpenCode | `opencode` | 지원 | 지원 | 없음 |
| Hermes Agent | `hermes` | 지원 | 지원 | 없음 |
| Grok | `grok` | 지원 | 지원 | 없음 |
| Copilot | `copilot` | 지원 | 없음 | 없음 |
| Kiro | `kiro` | 지원 | 없음 | 없음 |
| Pi | `pi` | 지원 | 없음 | 없음 |
| omp | `omp` | 지원 | 지원 | 없음 |

각 integration은 provider별 local 로그·JSON·DB 위치를 읽습니다. Settings의 custom root는 기본 root를 대체하지 않고 해당 provider의 탐색 위치에 추가됩니다. provider 선택값은 저장되며 언어를 바꾸거나 refresh해도 첫 항목으로 임의 초기화하지 않습니다.

### Credential access와 official quota 범위

- Codex official limits는 로컬 `codex app-server`를 호출합니다.
- Claude Code official limits는 `~/.claude/.credentials.json`의 OAuth credential을 사용합니다.
- Antigravity official limits는 token 파일 또는 Windows Credential Manager의 credential을 사용합니다.
- credential access를 OFF로 바꾸면 Claude/Antigravity official 조회만 중지됩니다. 이미 읽을 수 있는 local usage는 유지됩니다.
- Gemini, Cursor와 나머지 local provider에 Claude/Antigravity와 같은 official quota가 있다고 표시하지 않습니다.

credential 값, API token, cookie와 Authorization 값은 diagnostics에 기록하지 않습니다.

## 사용량 화면과 refresh

### Local usage

선택한 provider에 대해 다음 값을 표시합니다.

- Today
- Recent 5 hours
- This week
- This month
- 입력·출력·캐시 쓰기·캐시 읽기·전체 token
- 해당 provider가 계산할 수 있는 경우 비용
- local 마지막 갱신 시각

Codex는 `%USERPROFILE%\.codex\sessions`와 존재하는 `archived_sessions`의 JSONL `token_count`를 읽습니다. rollout/fork/canonical 관계, 누적 epoch와 파일 간 중복을 처리한 뒤 local calendar 기준 기간을 계산합니다.

Today가 0이어도 5시간·주간·월간 enrichment 또는 official quota가 있으면 provider를 제거하지 않습니다. 그래서 월간 기록만 남은 경우에도 `No usage data`로 바뀌지 않습니다.

### Official limits와 상태

- Codex: 5-hour session, Weekly, reset 시각, credits
- Claude Code: 5시간·주간 official usage와 조건이 충족될 때 burn rate/한도 소진 예상 시각
- Antigravity: quota group과 bucket

official percentage는 Settings에서 Used 또는 Remaining 의미를 선택할 수 있습니다. 내부 provider 계약은 used percentage를 유지하고 ViewModel이 표시할 때 `remaining = 100 - used`로 바꾸며 결과와 progress bar를 0~100으로 제한합니다.

각 provider는 Ready, Local data only, No sessions, Stale, Error 같은 runtime 상태와 적용 가능한 인증 상태를 가집니다. official만 실패하거나 오래되어도 local usage가 정상이라면 provider 전체를 Failed로 취급하지 않습니다. stale official 값에는 Stale과 마지막 갱신 정보가 붙습니다.

### Refresh와 background 동작

- 팝업과 tray 메뉴에서 수동 Refresh
- Manual, 1분, 2분, 5분, 15분 polling; 기본 2분
- startup refresh와 네트워크 복귀 refresh
- 절전 중 polling/empty retry 중지, 복귀 후 refresh와 기존 간격 복원
- 동시 요청을 `UsageStore`에서 coalescing
- 정상 refresh 결과가 완전히 비어 있을 때만 20초 뒤 한 번 재시도
- refresh 중 기존 snapshot을 유지하고 상태만 Refreshing으로 표시
- provider별 실패와 stale 보존을 분리해 다른 provider 카드 보호
- 마지막 정상 local snapshot을 `usage-cache.json`에 저장하고 재시작 시 복원

## Companion lifecycle

### 사용량 delta 장부

Companion은 표시된 Today 합계를 매 refresh마다 다시 더하지 않습니다.

- 첫 유효 관측은 baseline으로만 저장
- 같은 날짜·provider의 증가분만 새 성장량으로 적용
- 같은 snapshot 재처리 방지
- 감소한 provider만 현재 값으로 rebase
- 날짜가 바뀌면 새 장부 시작
- 새 provider는 먼저 seed한 뒤 다음 증가부터 반영
- provider 누락·stale·재시작 때문에 음수 또는 중복 성장이 생기지 않도록 persistence에 장부 저장

### Egg, 부화와 진화

- 기본 Egg hatch threshold는 5,000,000 tokens입니다.
- hatch overflow는 새 Pokémon의 첫 stage progress로 넘어갑니다.
- PokeAPI의 base species와 evolution tree, capture rate를 사용해 종과 경로를 구성합니다.
- rarity, nature, shiny를 정하고 분기 evolution plan을 저장합니다.
- hatch 네트워크 실패 시 pending species와 Egg progress를 보존하고 다음 refresh에서 재시도합니다.
- 큰 delta는 여러 evolution stage를 연속해서 통과할 수 있습니다.
- 최종 stage를 채우면 graduation하고 Dex에 기록한 뒤 progress 0의 새 Egg를 시작합니다.
- graduation overflow는 새 Egg로 이월하지 않습니다.
- Ditto disguise가 선택될 수 있으며 첫 evolution 시점에 실제 Ditto로 reveal됩니다.

## Economy, Shop, Bag과 Collection

### Currency와 Shop

설치 후 실제로 적용된 사용량은 `UsedSinceInstall`에 누적됩니다. 구매 가능 잔액은 누적 사용량에서 `SpentTokens`를 뺀 값이며 별도 유료 화폐나 결제 시스템이 아닙니다.

Shop에서 다음을 구매할 수 있습니다.

- Mint: active Pokémon의 nature 변경
- Rare Candy: 100,000,000 progression을 적용하며 evolution 또는 graduation 가능
- Shiny Charm: 한 번 보유하면 shiny 확률을 높이는 passive item
- 일반 Egg
- Uncommon 보장 Egg
- Rare 보장 Egg

Rare Candy와 Mint는 Bag에서 사용하고, Shiny Charm은 보유 중 자동 적용됩니다. official limit window의 도달 tier에 따라 주간 candy grant도 관리합니다.

### Egg 구매와 Released 상태

Windows 2.5.7에서는 active Pokémon이 있을 때만 새 Egg를 구매할 수 있습니다. 구매하면 기존 active를 잃지 않습니다.

1. 현재까지 도달한 진화 경로만 `DexEntry.ChainOrder`로 복사합니다.
2. 현재 species ID를 `FinalId`로 기록합니다.
3. rarity, nature, shiny 여부와 조회되어 있던 이름을 보존합니다.
4. `CaughtAt`과 `ReleasedAt`을 구매 시각으로 기록합니다.
5. Dex/Collection에 Released 개체를 추가한 뒤 active를 비우고 progress 0의 새 Egg를 시작합니다.

sprite bitmap은 save에 넣지 않습니다. Collection과 representative/floating 표시가 저장된 species ID와 shiny 상태를 사용해 sprite를 다시 가져옵니다. 아직 도달하지 않은 evolution form은 Released 개체의 소유 경로에 포함하지 않습니다.

### Collection/Dex와 representative

Collection은 active Pokémon과 모든 DexEntry를 보여 줍니다. 항목의 역할 문구는 상태에 따라 다음처럼 구분됩니다.

- `Current`: 현재 active Pokémon
- `Caught`: 정상 graduation으로 획득한 일반 DexEntry
- `Representative`: 보유 species 중 사용자가 대표로 선택한 항목
- `Released`: 새 Egg 구매 과정에서 보존된 이전 active

대표 선택은 active와 별개로 저장됩니다. 선택한 species를 계속 보유하는 한 graduation이나 새 Egg 시작 후에도 유지됩니다.

## Tray와 Floating Pokémon

### Tray popup

- 앱 시작 시 주 창을 띄우지 않고 notification area에 상주
- 왼쪽 클릭으로 popup 열기/닫기
- 우클릭 메뉴의 Open, Refresh, Exit
- provider 상태와 Today 값을 포함한 tooltip
- 현재 companion의 정적/GIF frame을 tray icon에 표시
- monitor work area와 DPI를 고려한 popup 위치
- popup deactivation/close 시 종료하지 않고 숨김

### Floating Pokémon

- 투명하고 항상 위에 있는 별도 WPF 창
- 클릭하면 popup 열기, drag 위치 이동·저장
- 우클릭으로 Token Bar 열기 또는 floating 숨기기
- representative가 있으면 해당 Pokémon, 없으면 current Pokémon 또는 Egg 표시
- GIF animation과 Power saver/Balanced/Smooth 품질
- 크기 48~384 DIP 조절과 위치 초기화
- decode/download 실패 시 정적 이미지 또는 Egg fallback
- limit warning을 floating bubble로 표시 가능

## Localization

Settings에서 다음 7개 언어를 선택할 수 있습니다.

- 한국어
- English
- 日本語
- Español
- Français
- Português
- Deutsch

popup, provider/status 문구, Settings, Shop/Bag/Collection, 알림과 Support 문구가 같은 `LocalizationService`를 사용합니다. Pokémon 이름도 선택 언어의 PokeAPI 이름을 우선 사용합니다.

## Notifications

현재 지원:

- Windows Forms `NotifyIcon` tray balloon
- floating pet의 limit warning bubble
- warning/critical threshold 한도 알림
- hatch, evolution, Ditto reveal, graduation, candy reward 같은 companion event 알림
- 같은 limit window의 같은 tier를 반복 알림하지 않는 저장된 tier 상태

현재 Windows native Toast API/Action Center integration은 사용하지 않습니다. “알림 미지원”이 아니라 tray balloon과 floating bubble 방식으로 지원하는 상태입니다.

## Settings

현재 UI에서 다음을 설정합니다.

- Floating Pokémon 표시, 위치 초기화와 크기
- animation quality와 floating limit bubble
- 로그인 시 자동 시작
- usage refresh 간격
- 언어
- official limit Used/Remaining 표시 모드
- limit/companion/update 알림 on/off
- warning·critical threshold
- provider별 runtime/auth 상태 확인
- provider별 custom root
- Claude/Antigravity official credential access

provider 선택, custom roots, 알림 tier, skipped update와 나머지 설정은 `settings.json`에 저장됩니다. 자동 시작은 사용자 단위 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 값을 사용합니다.

## About, Support, diagnostics와 recovery

- 현재 앱 버전과 update 상태 표시
- GitHub의 안정된 `windows-vX.Y.Z` Release 확인
- 새 버전 release page 열기와 해당 버전 건너뛰기
- startup 시 최소 간격이 적용된 background update check
- save export/import와 import 전 자동 backup·확인·실패 rollback
- Copy Diagnostics

diagnostics report에는 앱/파일/runtime 버전, OS와 architecture, culture/language, update 상태, provider별 canonical runtime/auth/availability, persistence 파일 존재·크기·cache age, 최근 recovery/error 요약이 들어갑니다. token 수치, prompt, API key, Authorization, cookie, credential 값은 넣지 않습니다.

손상되거나 사용할 수 없는 persistence를 무조건 덮어쓰지 않습니다.

- companion: 손상 파일 quarantine 후 정상 `.bak` 복원 시도
- settings: 잘못된 필드는 기본값으로 정규화하고 파싱 불가 파일 격리
- usage cache: 손상/지원하지 않는 format 격리 또는 무시
- PokéAPI base index: 비정상 cache 무시 또는 재생성
- import: 기존 save backup 후 settings+companion 적용, 중간 실패 시 rollback

## 저장 데이터와 외부 통신

기본 data root는 `%LOCALAPPDATA%\PokeTokenBar`입니다.

| 경로 | 용도 |
|---|---|
| `settings.json` | UI/provider/notification/update 설정 |
| `companion-state.json` | progression ledger, active/Egg, economy, inventory, Dex, representative |
| `companion-state.json.bak` | last-known-good companion state |
| `usage-cache.json` | 마지막 정상 local usage snapshot |
| `base-index.json` | PokeAPI base species cache |
| `sprites/` | Pokémon sprite cache |
| `backups/PokeTokenBar-PreImport-*.json` | import 전 save backup |
| `*.corrupt-*` | 격리된 손상 persistence/cache |

외부 또는 별도 process 연결은 기능별로 분리됩니다.

- Codex official limits: 로컬 `codex app-server`
- Claude/Antigravity official limits: 각 provider API와 사용자가 허용한 credential
- Cursor usage integration: 필요한 경우 Cursor dashboard
- Pokémon 종·진화·이름: PokéAPI
- Pokémon sprite: PokeAPI sprite repository
- update check: GitHub Releases API

사용량 JSONL, prompt와 프로젝트 경로를 PokéAPI나 update 요청에 보내지 않습니다.

## Release와 installer

`scripts/build-release.ps1`은 Windows x64 self-contained publish를 staging에서 생성하고 검증한 뒤 결과를 승격합니다.

- `artifacts/publish/win-x64`: 성공한 publish directory를 후속 workflow용으로 유지
- `artifacts/release/PokeTokenBar-<version>-win-x64/`: portable directory
- `artifacts/release/PokeTokenBar-<version>-win-x64.zip`: portable ZIP
- `artifacts/release/PokeTokenBar-Setup-<version>.exe`: `-BuildInstaller` 사용 시 Inno installer
- `artifacts/release/SHA256SUMS.txt`: 최종 ZIP/installer hash

Inno Setup installer는 사용자 단위 `%LOCALAPPDATA%\Programs\PokeTokenBar`에 설치하며 uninstall 시 앱의 auto-start Registry 값을 제거합니다.

unsigned release와 build가 기본 지원되며 인증서가 없어도 동작합니다. `-RequireSigning`은 선택적인 stricter production mode입니다. 이 모드는 다음을 요구합니다.

- `-BuildInstaller`
- Windows Certificate Store의 private key가 있는 Code Signing 인증서와 thumbprint
- timestamp URL
- SignTool과 Inno Setup 6

production signing은 프로젝트가 소유한 EXE/DLL만 서명하고 Microsoft/.NET runtime 파일은 다시 서명하지 않습니다. 서명 검증 뒤 ZIP을 만들고, ZIP을 추출해 원본 hash·signer·timestamp를 다시 검사합니다. Inno uninstaller와 최종 installer도 서명·검증한 뒤 SHA-256 manifest를 생성합니다. 어느 단계든 실패하면 release 성공으로 승격하지 않습니다.

저장소는 `*.pfx`, `*.p12`, `*.pem`, `*.key`를 ignore하고 release payload 검사도 signing material 포함 시 실패합니다. 실제 signed artifact, 신뢰 prompt와 SmartScreen 평가는 production 인증서 환경에서 별도 QA가 필요합니다.

## 현재 제공하지 않는 기능

현재 코드에서 명확히 제공하지 않는 것은 다음입니다.

- 앱이 새 binary를 직접 다운로드하고 실행 파일을 자동 교체하는 full auto-updater: 현재는 update를 확인하고 신뢰 가능한 GitHub Release page를 엽니다.
- Windows native Toast/Action Center integration: 현재 알림은 tray balloon과 floating bubble입니다.
- macOS 전용 `NSStatusItem`/menu-bar UI: Windows에서는 `NotifyIcon` tray와 WPF popup을 사용합니다.

## 실행과 검증

소스 실행과 기본 검증에는 .NET 10 SDK와 Windows가 필요합니다.

```powershell
dotnet run --project src/PokeTokenBar.Windows.App/PokeTokenBar.Windows.App.csproj
dotnet test PokeTokenBar.Windows.sln
dotnet build PokeTokenBar.Windows.sln -c Release
```

현재 문서 작성 시점인 Windows 2.5.7 HEAD의 Windows test case는 1,528개입니다. self-contained publish는 별도 .NET runtime 설치 없이 실행되며 named mutex 때문에 사용자당 하나의 PokeTokenBar process만 유지됩니다.
