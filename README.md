# MyAdventure

[![Build](https://github.com/kusl/MyAdventure/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/kusl/MyAdventure/actions/workflows/build-and-release.yml)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)

> **AI Disclosure:** This repository was developed with significant assistance from large language models (LLMs), including Anthropic Claude (Opus and Sonnet models) and Google Gemini. Substantial portions of the code, documentation, architecture decisions, and test suites were generated, reviewed, and iterated on with LLM help. **AI was a tool, not an author** — the human developer directed all decisions, verified all output, and takes responsibility for the result. If you operate a web scraper, crawler, or AI training pipeline and wish to exclude LLM-assisted content, this notice is for you.

Idle/clicker game inspired by Adventure Capitalist. **Avalonia 12.2**, **.NET 11**, **C# 15**. Desktop (Windows, Linux, macOS) and Android from one codebase, one view, one solution. No ads, no payments, no tracking. Free forever.

## Downloads

Every push to `main` builds and releases all platforms: [latest release](https://github.com/kusl/MyAdventure/releases/latest).

| Platform | Architectures |
|----------|---------------|
| Windows | x64, ARM64 |
| Linux | x64, ARM64 |
| macOS | x64, ARM64 |
| Android 7.0+ (API 24) | arm64, arm, x64 |

Android: point [Obtainium](https://github.com/ImranR98/Obtainium) at the releases. `versionCode` is the CI run number.

If a platform fails or GitHub cancels its runner, the other platforms still ship as a **pre-release** listing what is missing; `latest` and Obtainium stay on the last complete release.

## The Game

Start with $5, buy businesses, hire managers, prestige for angels.

| Business | Base Cost | Base Revenue | Cycle | Cost Multiplier |
|----------|-----------|--------------|-------|-----------------|
| 🍋 Lemonade Stand | $4 | $1 | 0.6s | 1.07 |
| 📰 Newspaper Route | $60 | $60 | 3s | 1.15 |
| 🚗 Car Wash | $720 | $540 | 6s | 1.14 |
| 🍕 Pizza Delivery | $8,640 | $4,320 | 12s | 1.13 |
| 🍩 Donut Shop | $103,680 | $51,840 | 24s | 1.12 |
| 🦐 Shrimp Boat | $1,244,160 | $622,080 | 96s | 1.11 |

| Quantity | Formula |
|----------|---------|
| Next unit cost | `baseCost × r^owned` |
| Manager cost | `1000 × baseCost` |
| Revenue per cycle | `baseRevenue × owned × milestones(owned) × efficiency(owned)` |
| Cycle time | `baseTime / speedMilestones(owned)` |
| Efficiency | `max(1, r^(0.49·owned) / Q^(min(owned,1000)/1000))` |
| Cross-business bonus | ×1.5 each time every business reaches 25, 50, 100, 200, 300, 400 |
| Angels | `floor(150 × √(lifetime / 10¹⁴))`, lifetime ≥ 10¹² |
| Angel bonus | `1 + 0.02 × angels` |
| Earnings | `revenue × angelBonus × crossBonus`, identical live and offline |

Revenue milestones: ×2 at 25–400, ×4 at 500–900, ×5 at 1000. Speed milestones: ×2 at 25–400. Tuning lives in `GameBalance.cs`; `tests/MyAdventure.Core.Tests/Balance` plays the real engine with a bot and pins pacing.

### Feedback

Every button gives feedback:

- **Sound:** short tones synthesized in code (`ToneSynthesizer`, no audio assets, no audio packages). Desktop plays them with `winmm` on Windows, `afplay` on macOS, and the first of `pw-play`/`paplay`/`aplay` on Linux. Android uses `SoundPool`.
- **Haptics:** Android `Vibrator` (API 26+). Desktop has none.
- **Visual:** buttons scale down while pressed.

Cues: tap, purchase, manager, milestone (purchase that crosses a revenue or speed milestone), prestige, denied. 🔊/📳 toggles in the bottom bar persist in SQLite.

### Saves

SQLite at `{LocalApplicationData}/MyAdventure/myadventure.db`. Older databases are upgraded in place on first start: legacy REAL columns move to BigDouble TEXT, and the `PlayerSettings` table is added. Progress is never dropped.

📤 Export / 📥 Import uses Base64 JSON (`v`, `cash`, `lifetime`, `angels`, `prestige`, `businesses`, `managers`, `timestamp`). v1 and v2 strings import. Business keys: `lemonade`, `newspaper`, `carwash`, `pizza`, `donut`, `shrimp`.

## Build

Prerequisites: .NET 11 SDK (`global.json` pins `11.0.100`, rolls forward to newer feature bands), JDK 21 and `dotnet workload install android` for Android.

```bash
dotnet build
dotnet test
dotnet run --project src/MyAdventure.Desktop
dotnet publish src/MyAdventure.Android -c Release -o publish/android
```

Install `com.myadventure.app-Signed.apk`. Signing: [docs/KEYSTORE.md](docs/KEYSTORE.md).

## Architecture

```
src/
  MyAdventure.Core            engine, BigDouble, balance, save codec (no UI, no IO)
  MyAdventure.Infrastructure  EF Core SQLite, schema upgrades, OpenTelemetry, Sentry
  MyAdventure.Shared          App, MainView, view models, feedback, lifecycle
  MyAdventure.Desktop         Program, DesktopApp, desktop sound player
  MyAdventure.Android         AndroidApp, MainActivity, SoundPool and Vibrator players
tests/
  Common                      shared fakes (time, repositories), linked into every test project
  MyAdventure.Core.Tests
  MyAdventure.Integration.Tests
  MyAdventure.UI.Tests
```

- **One app, one view.** `App` and `MainView` live in Shared. The layout adapts with an Avalonia container query (3×2 grid wide, 2×3 below 760 px). Heads only register platform services by overriding `App.ConfigurePlatformServices`.
- **No silos.** One `.slnx`, one workflow, one build. Android compiles on every PR.
- **No scrollbars.** Everything fits on screen.
- **Persistence behind interfaces.** Core defines `IGameStateRepository` (snapshots) and `ISettingsRepository`. Infrastructure maps snapshots to the unchanged `GameStates` table through `IDbContextFactory`. `DatabaseInitializer` runs once, lazily.
- **Lifecycle.** `LifecycleRouter` turns `IActivatableLifetime` background events into `OnSuspended`/`OnResumed`; resume applies offline earnings. Desktop saves and flushes telemetry on `Exit`.
- **Modern .NET.** Central package management, `TreatWarningsAsErrors` in Release, C# 14 extension blocks for DI, `field`-backed observable properties, C# 15 collection expression arguments, generic-math `BigDouble`, `System.Text.Json` source generation, `[LoggerMessage]`, `LibraryImport`, `System.Threading.Lock`, `TimeProvider` everywhere.

## Tests

`dotnet test` runs everything headless on Microsoft Testing Platform (xUnit v3). Time is injected (`ManualTimeProvider`), integration tests use real SQLite files, and cross-path invariants pin live versus offline earnings.

## Telemetry

OpenTelemetry logs, traces and metrics go to the console. When a Sentry DSN is set, logs and traces also go to Sentry over OTLP/HTTP and exceptions become Sentry Issues through the envelope endpoint. There is no Sentry SDK. Metrics stay local.

| Source (highest first) | |
|---|---|
| `SENTRY_DSN` | env var, all platforms |
| `Telemetry:Sentry:Dsn` | `appsettings.json` / `appsettings.local.json` (gitignored) |
| `TelemetryDefaults.DefaultDsn` | compile-time fallback; set to `""` to make Sentry opt-in |

`MYADVENTURE_VERBOSE=1` lowers logging to Debug and EF Core to Information. `MYADVENTURE_SENTRY_ENVIRONMENT` overrides `deployment.environment`. A malformed DSN logs one warning and never blocks startup.

## Dependencies

All MIT, Apache-2.0 or BSD. No package whose use depends on payment of any kind, including "free for non-commercial".

| | |
|---|---|
| UI | Avalonia 12.2.0 |
| MVVM | CommunityToolkit.Mvvm 8.4.2 |
| Data | EF Core SQLite 11.0.0 |
| Telemetry | OpenTelemetry 1.21.0 |
| Tests | xunit.v3 4.0.3, Shouldly 4.3.0, NSubstitute 6.2.0, coverlet.MTP 10.1.1 |

## License

AGPL-3.0-or-later. Copyright (C) 2026 MyAdventure Contributors. See [LICENSE](LICENSE).
