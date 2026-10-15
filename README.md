# MyAdventure

[![Build](https://github.com/kusl/MyAdventure/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/kusl/MyAdventure/actions/workflows/build-and-release.yml)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)

> **AI Disclosure:** This repository was developed with significant assistance from large language models (LLMs), including Anthropic Claude (Opus and Sonnet models) and Google Gemini. Substantial portions of the code, documentation, architecture decisions, and test suites were generated, reviewed, and iterated on with LLM help. **AI was a tool, not an author** — the human developer directed all decisions, verified all output, and takes responsibility for the result. If you operate a web scraper, crawler, or AI training pipeline and wish to exclude LLM-assisted content, this notice is for you.

An idle/clicker game **inspired by Adventure Capitalist**, built with **Avalonia UI 12** and **.NET 10** (C# 14). Cross-platform — desktop (Windows, Linux, macOS) and Android — from a single codebase and a single solution file. No ads, no payments, no tracking, no strings attached. Free to play, free to modify, free forever.

---

## Downloads

Every push to `main` automatically builds and releases for all platforms via GitHub Actions.

| Platform | Architecture | Download |
|----------|-------------|----------|
| Windows | x64 | [Download](https://github.com/kusl/MyAdventure/releases/latest) |
| Windows | ARM64 | [Download](https://github.com/kusl/MyAdventure/releases/latest) |
| Linux | x64 | [Download](https://github.com/kusl/MyAdventure/releases/latest) |
| Linux | ARM64 | [Download](https://github.com/kusl/MyAdventure/releases/latest) |
| macOS | x64 (Intel) | [Download](https://github.com/kusl/MyAdventure/releases/latest) |
| macOS | ARM64 (Apple Silicon) | [Download](https://github.com/kusl/MyAdventure/releases/latest) |
| Android | APK | [Download](https://github.com/kusl/MyAdventure/releases/latest) |

**Android users (Obtainium):** Point [Obtainium](https://github.com/ImranR98/Obtainium) at this repository's releases for automatic updates. The APK version code (set by `ApplicationVersion`) increments with every CI run, so Obtainium can track updates automatically.

---

## The Game

Idle/clicker inspired by Adventure Capitalist. Start with $5, buy businesses, hire managers, prestige for angels.

### Businesses

| Business | Base Cost | Base Revenue | Cycle | Cost Multiplier |
|----------|-----------|--------------|-------|-----------------|
| 🍋 Lemonade Stand | $4 | $1 | 0.6s | 1.07 |
| 📰 Newspaper Route | $60 | $60 | 3s | 1.15 |
| 🚗 Car Wash | $720 | $540 | 6s | 1.14 |
| 🍕 Pizza Delivery | $8,640 | $4,320 | 12s | 1.13 |
| 🍩 Donut Shop | $103,680 | $51,840 | 24s | 1.12 |
| 🦐 Shrimp Boat | $1,244,160 | $622,080 | 96s | 1.11 |

Next unit costs `baseCost × r^owned`. Managers cost 1000× base cost and run the business automatically, including offline.

### Formulas

All tuning constants live in `src/MyAdventure.Core/Entities/GameBalance.cs`.

| Quantity | Formula |
|----------|---------|
| Revenue per cycle | `baseRevenue × owned × milestones(owned) × efficiency(owned)` |
| Cycle time | `baseTime / speedMilestones(owned)` |
| Efficiency | `max(1, r^(0.49·owned) / Q^(min(owned,1000)/1000))`, `Q` = milestone × speed product at 1000 (≈2.1×10⁷) |
| Cross-business bonus | ×1.5 each time *every* business reaches 25, 50, 100, 200, 300, 400 (max ×11.39) |
| Angels | `floor(150 × √(lifetime / 10¹⁴))`, lifetime ≥ 10¹² |
| Angel bonus | `1 + 0.02 × angels` (linear) |
| Earnings | `revenue × angelBonus × crossBonus`, identical live and offline |

Revenue milestones: ×2 at 25/50/100/200/300/400, ×4 at 500–900, ×5 at 1000. Speed milestones: ×2 at 25/50/100/200/300/400.

### Balance design

The old economy was bistable. Before prestige, revenue hit a hard wall at about 10⁹/s because milestones could not keep up with exponential costs. After prestige, compounding angels (`1.02^angels`) created a positive feedback loop: one prestige enabled hundreds more within minutes.

Real idle games avoid both failure modes the same way:

- Costs grow exponentially and revenue grows more slowly, so every run hits a *soft* wall.
- Prestige currency is sublinear in earnings (√ or ∛), and its bonus is *linear*, never compounding.
- Only one feedback loop is unbounded. Everything else (milestones, global bonuses) is bounded content.
- Pacing is tuned by simulation against explicit targets and pinned by tests.

This game makes revenue track `cost^0.49`: efficiency fills the gap between the milestone ladder and that curve. If in-run income grows like `spent^α`, a run reaches `spent ∝ (multiplier × time)^(1/(1−α))`. Angels scale with `√lifetime`, so the prestige loop gain is `1/(2(1−α))`. With α = 0.49 the loop gain is 0.98: just under 1. Progress is steady and very slowly decelerating. It never runs away (gain > 1) and never stalls (gain ≪ 1).

`tests/MyAdventure.Core.Tests/Balance` holds a deterministic bot that plays the real `GameEngine`. It prestiges whenever doing so would double income. It pins:

| Scenario | Bound | Measured |
|----------|-------|----------|
| First manager | < 2 min | 37 s |
| First prestige | 8–60 min | 19 min |
| First 10 prestige runs | 5–90 min each | 13–31 min |
| Seeded at lifetime 10⁴⁰ / 10⁸⁰ | 3 min – 4 h | 12–14 min / 18–20 min |
| Never prestige | income still rising after a week | 10¹³·¹/s at 1 day → 10¹³·⁷/s at 7 days |

`EfficiencyExponent` must stay inside (0.40, 0.50); a test enforces it.

### Saves

The save format is unchanged: SQLite TEXT columns hold canonical BigDouble strings, and v1/v2 Base64 export/import both work. Old saves load as-is with the new formulas applied. Angels earned under the old 10¹³ divisor are kept. New angels resume once lifetime earnings catch up with the new curve.

### Import and Export

📤 Export produces Base64 JSON (`v`, `cash`, `lifetime`, `angels`, `prestige`, `businesses`, `managers`, `timestamp`). 📥 Import accepts it back. Editing is encouraged. Business keys: `lemonade`, `newspaper`, `carwash`, `pizza`, `donut`, `shrimp`. `timestamp` is informational and ignored on import.

---

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (the exact version is pinned in `global.json` — if you see a version mismatch error on first build, install the SDK version listed there)
- For Android builds: Java 21 (Temurin) and the Android workload (`dotnet workload install android`)

### Build and Run (Desktop)

```bash
dotnet restore
dotnet build
dotnet run --project src/MyAdventure.Desktop
```

### Build Android APK

```bash
dotnet workload install android
dotnet publish src/MyAdventure.Android/MyAdventure.Android.csproj --configuration Release --output ./publish/android
```

See [docs/KEYSTORE.md](docs/KEYSTORE.md) for APK signing instructions.

### Run All Tests

```bash
dotnet test
```

All tests (unit, integration, UI/ViewModel) are designed to run fast after every change. No external services, emulators, or running applications are required — the test suite is fully headless.

---

## Player Guide

- **First minutes:** buy lemonade stands and click ▶ RUN. Hire the lemonade manager ($4,000) as soon as you can.
- **First half hour:** add businesses and managers; use the bulk-buy button (`BUY N→threshold`, then `BUY MAX (N)` past 1000) to hit milestones.
- **Prestige:** the button lights up when you can gain at least one angel. The panel shows the income multiplier you would get. Prestiging at ×2 or better keeps runs around 15–30 minutes.
- **Balance ownership:** the cross-business bonus needs *every* business at the threshold.
- **Offline:** managed businesses earn while the app is closed, with no cap.
- **Reset a save:** delete `{LocalApplicationData}/MyAdventure/myadventure.db` (desktop) or clear app data (Android).

---

## Architecture

```
MyAdventure.slnx
├── src/
│   ├── MyAdventure.Core           — Domain entities, game engine, BigDouble, number formatting
│   ├── MyAdventure.Infrastructure — EF Core SQLite persistence, DI, OpenTelemetry, BigDouble schema migration
│   ├── MyAdventure.Shared         — ViewModels, converters, toast service, AppRoot, AppLifecycleManager, i18n
│   ├── MyAdventure.Desktop        — Avalonia desktop app (Windows/Linux/macOS)
│   └── MyAdventure.Android        — Avalonia Android app
└── tests/
    ├── MyAdventure.Core.Tests         — Unit tests for entities, engine, BigDouble, milestones, number formatting
    ├── MyAdventure.Integration.Tests  — EF Core repository round-trip tests, schema migration tests
    └── MyAdventure.UI.Tests           — ViewModel, lifecycle, and service tests
```

### Design Principles

**One solution, one team.** There is one `.slnx` file, one CI pipeline, and one build command. Desktop and Android are not siloed into separate solutions or build scripts. Everyone works with all parts of the code. If the build is slow, everyone feels it, so it gets fixed quickly. There is no `build-desktop.sh`, no `build-android.sh`, no per-platform solution file. Single-team culture is enforced by the project structure itself.

**Clean architecture with pragmatism.** Core has zero UI dependencies. Infrastructure handles persistence and telemetry. Shared contains ViewModels used by both Desktop and Android. Platform projects are thin shells: they wire up DI, set up the timer, and host the view.

**Testable from the ground up.** The `GameEngine` accepts injected dependencies (`IGameStateRepository`, `ILogger`, `TimeProvider`) and is fully testable without any UI framework. ViewModels are tested against real engine instances with mocked repositories. Integration tests use EF Core's SQLite in a temp file, not an in-memory fake, so schema migrations run against the real driver. The `TimeProvider` abstraction (via `TestTimeProvider`) lets tests advance the clock deterministically without sleeping.

**No scrollbars — designed for at-a-glance play.** The UI fits on screen without scrolling so the entire game state is visible at once on any device. Desktop uses a 3×2 grid for businesses; Android uses a 2×3 grid. The import/export transfer panel overlays the business grid rather than adding height. This is a deliberate design stance, not a missing feature.

**Cash display auto-scales.** Both the Desktop and Android cash text are wrapped in a `Viewbox` with `StretchDirection="DownOnly"`, so when a value like `7.53 × 10⁴⁰` would otherwise overflow the header, it auto-shrinks to fit the available width while retaining its natural size when there's room.

### Key Technical Decisions

- **SQLite for persistence** via EF Core. Uses `DateTime` (UTC) instead of `DateTimeOffset` because SQLite's EF Core provider doesn't support `DateTimeOffset` in `ORDER BY` clauses; dates are stored and loaded as `DateTime.UtcNow` / `.UtcDateTime`.
- **BigDouble for all monetary values.** `Cash`, `LifetimeEarnings`, `AngelInvestors`, and the game balance values that scale with ownership are all `BigDouble`. The columns are stored as TEXT in SQLite (canonical form `"<mantissa>e<exponent>"`). Legacy databases with REAL columns are migrated in-place by `DependencyInjection.InitializeDatabaseAsync` in a single transaction.
- **Geometric-series bulk buy (O(1)).** `GameEngine.BuyMultiple` uses the closed-form `c₀ × (rⁿ − 1) / (r − 1)` to calculate the total cost of N units in constant time, regardless of how many units that is. The prior implementation looped unit-by-unit up to a 10,000-unit safety cap; the new one handles BuyMax of 50,000 in the same time as buying 5. A defensive fallback loop catches any floating-point noise that would cause the geometric series to produce a total just over the cash balance and backs off by one unit.
- **Progress bars use percentage-based rendering** (`ScaleTransform` with a `PercentToFractionConverter`) instead of pixel widths, which ensures correct display on both desktop and Android without any platform-specific layout code.
- **Android logging** goes through `Android.Util.Log` rather than console-based providers, since console output is not visible on Android. OpenTelemetry's console exporter is also disabled on Android (`AddInfrastructure` takes an optional `androidLogging` flag to suppress it).
- **AOT compilation is disabled** for Android (`RunAOTCompilation=false`, `PublishTrimmed=false`) because EF Core's reflection-heavy patterns and OpenTelemetry cause silent trimming crashes. Re-enable once trimmer roots are properly configured.
- **Angel bonus is linear** (`1 + 0.02 × angels`), applied once per payout in both `Tick` and offline earnings; `OfflineEarnings_ShouldApplyAngelBonusOnce_NotTwice` pins the equivalence.
- **Cross-business bonus is a bounded revenue multiplier** (max ×11.39), not a cycle-time divisor, so cycle times never underflow.
- **Efficiency** (`GameBalance.Efficiency`) keeps total per-business multipliers on the `cost^0.49` curve; from 1000 owned onward the product of milestones, speed and efficiency is exactly `r^(0.49·owned)`.
- **Toast notifications** use a simple service (`ToastService`) with expiration timestamps, cleaned up on each game tick via `CleanupExpired()`. No platform-specific notification APIs needed. Toast lifetime defaults to 3 seconds; tests can pass `TimeSpan.Zero` to get immediately-expirable toasts.
- **Central package management** uses MSBuild variables (`$(AvaloniaVersion)`, `$(MicrosoftExtensionsVersion)`, `$(EfCoreVersion)`, `$(OpenTelemetryVersion)`) in `Directory.Packages.props` so updating a version is a single-line change.
- **Clipboard access via a static `AppRoot.CurrentVisual` registered by the active view**, not via per-platform branching on `IApplicationLifetime`. This is necessary in Avalonia 12 because Android's `IActivityApplicationLifetime` exposes only a `MainViewFactory` (a `Func<Control>`) and not a live view reference. Views register `AppRoot.CurrentVisual = this` in `OnOpened` / `OnAttachedToVisualTree` and clear it on detach.
- **Android safe-area is handled explicitly by `MainView`**, not by the framework's auto-padding. The Android `UserControl` sets `TopLevel.AutoSafeAreaPadding="False"`, captures `InsetsManager.SafeAreaPadding` on attach, and subscribes to `SafeAreaChanged` to keep its `Padding` in sync with the OS-reported insets. This is needed because Android 15+ enforces edge-to-edge rendering — without explicit handling, the top bar (PRESTIGE / cash) gets drawn under the status bar and front-camera cutout on devices like the Moto G Stylus 2025, and the first row of business cards visually rides on top of the prestige bar. Owning the padding deterministically on `MainView` rather than relying on auto-injection at the TopLevel root is also more robust across activity recreation, which is aggressive on Android.
- **Offline earnings on app resume are handled by `AppLifecycleManager`**, a static service that wires into Avalonia's `IActivatableLifetime` (available via `Application.Current.TryGetFeature<IActivatableLifetime>()`), which works uniformly on all platforms. When the app goes to background (`ActivationKind.Background` Deactivated event), `AppLifecycleManager` calls `GameViewModel.OnSuspended()` to record the timestamp. When it returns to foreground (`ActivationKind.Background` Activated event), it calls `GameViewModel.OnResumed()`, which computes offline earnings for the gap and applies them immediately before refreshing the UI — so the cash display is correct on the very first frame after returning to the app. Sub-second gaps (e.g. screen flickers) are below the minimum threshold and produce no payout. The `AppLifecycleManager` holds a replaceable static "current target" so Android activity recreation (which constructs a fresh ViewModel each time) doesn't leak handlers on the old VM.
- **Cold-start vs. foreground-resume are distinct paths.** `GameEngine.LoadAsync` handles the cold-start path: it reads `LastPlayedAt` from the saved state and calls `ApplyOfflineEarnings(now - lastPlayedAt)` if the gap exceeds the minimum threshold. The foreground-resume path is handled by `GameViewModel.OnSuspended()` / `OnResumed()`, which uses `AppLifecycleManager`. Both paths call the same `GameEngine.ApplyOfflineEarnings(TimeSpan)` method — the single public entry point for offline earnings — so the logic is never duplicated and both paths are pinned by the same invariant tests.
- **Localization** is wired via `Microsoft.Extensions.Localization` with JSON resource files (`src/MyAdventure.Shared/Resources/i18n/`). English (`en.json`) and Spanish (`es.json`) are included. The infrastructure is in place to add more locales by adding a new JSON file and updating the supported-cultures list in `DependencyInjection.cs`.
- **Optional Sentry error tracking via OTLP — no Sentry SDK.** Sentry's hosted ingestion accepts standard OpenTelemetry traces and logs over OTLP/HTTP (`Settings → Client Keys (DSN) → OpenTelemetry`). The Infrastructure project's existing OpenTelemetry stack adds an OTLP exporter for logs and traces when a DSN is configured; metrics stay console-only because Sentry's OTLP ingestion does not accept metrics. No `Sentry.*` NuGet package is used — swapping to any other OTLP backend (Grafana Cloud, Honeycomb, Tempo, Loki, an OpenTelemetry Collector) is a one-line config change. See the [Observability](#observability-and-error-tracking) section for the configuration surface.
- **No `Avalonia.Diagnostics` package.** Removed in Avalonia 12; the official replacement (`AvaloniaUI.DiagnosticsSupport`) gates the actual Dev Tools UI behind a paid Avalonia Plus / Pro subscription. The Community tier is free for non-commercial use only — and this project's policy is to avoid any package whose use is conditional on payment of any kind. Use the FOSS Avalonia VS Code or Rider extensions for design-time previewing.

### Avalonia 12 migration notes

This project tracks the latest Avalonia stable release. These notes capture the gotchas that cost real time during the v11 → v12 migration; documenting them here in case they save someone else hours.

- **Android `MainActivity` was split.** In v11 it was `AvaloniaMainActivity<App>` and `WithInterFont()` lived on its `CustomizeAppBuilder` override. In v12 those virtual hooks are no longer called by the framework. The activity is now an empty `AvaloniaMainActivity` (non-generic) declaring only its `[Activity]` metadata, and a new `[Application] AndroidApp : AvaloniaAndroidApplication<App>` class hosts the AppBuilder customization (`WithInterFont()`). The `[Application]` attribute must **not** set `Name` equal to the package name (e.g. `"com.myadventure.app"`) — that would cause a javac collision between the generated Application class and R.java.
- **`package` attribute on `<manifest>` removed.** In modern .NET for Android the package identity is set by `<ApplicationId>` in the `.csproj`, which is the single canonical source. Setting it in both places causes javac collisions.
- **Android lifetime is `IActivityApplicationLifetime`** (not `ISingleViewApplicationLifetime`). Set `MainViewFactory = () => new MainView { DataContext = vm }` rather than `MainView = ...`. The factory runs each time Android creates a fresh activity, producing a fresh view + fresh ViewModel that re-loads state from the database.
- **Plugins are no longer configurable** and the data-annotations plugin is **off by default**. This removed the long-standing nuisance where `CommunityToolkit.Mvvm` validation conflicted with Avalonia's, so no extra config is needed.
- **`DispatcherTimer` binds to the dispatcher of the constructing thread** rather than the UI thread implicitly. Our timers are constructed in `OnOpened` / `OnAttachedToVisualTree`, both of which run on the UI thread, so behavior is unchanged.
- **Compiled bindings remain enabled by default** via `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>` in the platform csprojs.
- **Edge-to-edge is enforced on Android 15+.** `InsetsManager.DisplayEdgeToEdge` is now obsolete (replaced by `DisplayEdgeToEdgePreference`), and the OS no longer respects requests to draw inside the system-bar area. Apps must handle `SafeAreaPadding` explicitly. We do this on the Android `MainView` rather than relying on Avalonia's auto-padding (which depends on the `TopLevel.AutoSafeAreaPadding` attached property and has historical regressions around activity recreation and orientation changes — see Avalonia issue #20448 for one example).
- **`Application.GetTopLevel()` is not a valid Avalonia 12 API.** Use the `AppRoot.CurrentVisual` static pattern instead: views register themselves on attach and the VM reads `TopLevel.GetTopLevel(AppRoot.CurrentVisual)?.Clipboard` to get the clipboard.
- **`IActivatableLifetime` replaces `Window.Activated`/`MainView.OnAttachedToVisualTree` for lifecycle events.** Obtained via `Application.Current.TryGetFeature<IActivatableLifetime>()`. Listen for `ActivationKind.Background` events only — `ActivationKind.Application` fires for things like dialog focus and would cause spurious offline-earnings payouts.

---

## Technology Stack

All dependencies are free and use permissive open-source licenses (MIT, Apache-2.0, BSD). **No NuGet package in this project requires payment for any use, commercial or otherwise.** "Free for non-commercial" is explicitly not enough — we avoid those too.

| Category | Technology | License |
|----------|-----------|---------|
| Runtime | .NET 10 / C# 14 | MIT |
| UI Framework | Avalonia UI 12.1.4 | MIT |
| MVVM | CommunityToolkit.Mvvm 8.4.2 | MIT |
| Database | SQLite via EF Core 10.0.13 | MIT |
| Observability | OpenTelemetry 1.20.0 | Apache-2.0 |
| Optional error tracking | Sentry (via OTLP HTTP — no Sentry SDK) | n/a (free tier) |
| Testing | xUnit.net v3 4.0.2 on Microsoft Testing Platform | Apache-2.0 |
| Assertions | Shouldly 4.3.0 | BSD |
| Mocking | NSubstitute 6.2.0 | BSD |
| Test Data | Bogus 35.6.5 | MIT |
| Coverage | coverlet.MTP 10.1.0 | MIT |

### Modern .NET Practices

- **Central package management** via `Directory.Packages.props` — all NuGet versions defined in one place using MSBuild variables for grouped version updates. Updating all Avalonia packages is a one-line change to `$(AvaloniaVersion)`.
- **Shared build configuration** via `Directory.Build.props` — target framework (`net10.0`), `ImplicitUsings`, `Nullable enable`, `LangVersion latest`, versioning (driven by `$(BuildNumber)` from CI), deterministic builds, `TreatWarningsAsErrors` in Release.
- **Solution file** uses the new `.slnx` XML format (one solution for the whole repo, no per-platform `.sln`s).
- **C# 14 features** including primary constructors, records, collection expressions, `required` properties, and global usings.
- **Compiled bindings** enabled by default in Avalonia (`AvaloniaUseCompiledBindingsByDefault`).
- **`InternalsVisibleTo`** used in `MyAdventure.Shared.csproj` to give `MyAdventure.UI.Tests` access to `AppLifecycleManager.ResetForTesting()` without over-exposing internals to production code.
- **Global `using Xunit;`** injected by `tests/Directory.Build.props` so test files don't need per-file using directives for xUnit attributes.
- **`global.json`** pins the SDK to `10.0.100` (`rollForward: latestMinor`, no prereleases) and opts `dotnet test` into Microsoft Testing Platform (`"test": { "runner": "Microsoft.Testing.Platform" }`).
- **Test projects are MTP executables** (xunit.v3); shared test settings and packages live in `tests/Directory.Build.props`. Coverage: `dotnet test --coverlet`. Test methods pass `TestContext.Current.CancellationToken` (analyzer xUnit1051).

---

## Tests

The test suite runs fully headless — no emulators, no running applications, no external services. `dotnet test` from the repo root runs everything.

### Test Projects

**`MyAdventure.Core.Tests`** — unit tests for the domain layer.

- `BigDoubleTests` — construction/normalization, arithmetic (add/subtract/multiply/divide), exponentiation (including past `double` overflow range), sqrt, log10, comparison, string round-tripping, implicit conversions, Floor, and edge cases (NaN, Infinity, zero, negative).
- `BusinessAffordableTests` — `AffordableCount` closed-form calculation including huge-cash edge cases.
- `Balance/*` — deterministic bot playing the real engine; pins pacing (first manager, first prestige, run-length band, late-game seeds, never-prestige soft wall).
- `BusinessTests` — `NextCost`, `Revenue` (including efficiency) and extreme ownership.
- `CrossBusinessSpeedBonusTests` — ladder, bounded maximum, `NextThreshold` returning `null` once maxed.
- `GameEngineTests` — efficiency invariants (never below 1, monotonic, exact `r^(0.49·n)` past 1000, bounded wiggle below), linear angel bonus, `LoadAsync`, `Tick`, `StartBusiness`, `BuyBusiness`, `BuyManager`, `BuyMultiple`, `BuyMax`, `Prestige`, `CalculateAngels`, `AngelBonus`, `ApplyOfflineEarnings`, `ExportToString`/`ImportFromString` (including the legacy v1 format), and invariant tests for offline-earnings equivalence with live ticks.
- `MilestoneTests` — `CalculateMultiplier`, `NextMilestone`, `UnitsToNext`.
- `NumberFormatterTests` — suffix formatting (K, M, B, T, Qa, Qi, Sx, Sp, O, N, D), scientific notation past 10³⁶, `BigDouble` overload including values at 10⁵⁰⁰ and 10⁵⁰⁰⁰, infinity/NaN display.
- `SpeedMilestoneTests` — `CalculateCycleTimeMultiplier`, `CalculateSpeedMultiplier`, `NextSpeedMilestone` at all threshold boundaries.
- `SubFrameCycleTests` — invariants that guarantee sub-frame cycles (effective cycle time < 16ms) pay out correctly without being lost or double-counted.

**`MyAdventure.Integration.Tests`** — tests that hit the real EF Core + SQLite stack.

- `GameStateRepositoryTests` — round-trip save/load for all fields including BigDouble string columns; extreme-magnitude BigDouble values; upsert behaviour (single row kept, `CreatedAt` never changes across saves).
- `SchemaMigrationTests` — verifies that the legacy REAL-column schema (pre-BigDouble migration) is correctly translated to the new TEXT-column schema in place, without data loss, in a single transaction.

**`MyAdventure.UI.Tests`** — ViewModel and service tests. No Avalonia UI host needed.

- `AppLifecycleManagerTests` — `Attach` with and without an active Avalonia `Application`, replaceable target on repeated `Attach` calls, `ResetForTesting` isolation.
- `BusinessViewModelSpeedTests` — `SpeedMultiplierText`, `HasSpeedBonus`, `HasNextSpeedMilestone`, `NextSpeedMilestoneText` at every speed threshold; cross-business multiplier applied correctly to the displayed revenue.
- `BusinessViewModelTests` — `Refresh` with various cash and angel bonus combinations; affordability flags; bulk-buy button visibility and label transitions ("BUY N→threshold" vs. "BUY MAX (N)" past 1000 owned); `CanBulkBuy` dims on zero affordable rather than vanishing.
- `GameViewModelPrestigeTests` — prestige panel shows the income multiplier; cross-bonus texts before the first and after the last threshold.
- `GameViewModelLifecycleTests` — `OnSuspended`/`OnResumed` round-trip with a controllable `TestTimeProvider`: payout for a 10-minute gap, angel bonus applied once, toast notification on payout, no toast when no managed businesses, tiny gaps below threshold produce no payout, second `OnResumed` without intervening `OnSuspended` produces no payout, `LastTick` reset on resume prevents the next timer tick from replaying the gap.
- `ToastServiceTests` — `Show`, `CleanupExpired`, mixed expired/active toasts.

### Why the tests are designed this way

**No mocked time — injectable `TimeProvider`.** `GameEngine` and `GameViewModel` accept a `TimeProvider` (the .NET 8+ standard abstraction). Tests pass a `TestTimeProvider` that starts at a fixed UTC instant and can be advanced with `Advance(TimeSpan)`. This makes every time-dependent test deterministic and instant — no `Thread.Sleep`, no flakiness.

**Offline earnings equivalence invariant.** The most important invariant in the test suite: `OnSuspendedThenOnResumed` results must match what live ticks would have produced for the same duration. This is not just a sanity check — it's the guard against the class of bug where the live-tick path and the offline-earnings path use different multipliers (e.g. angel bonus applied twice, or cross-business multiplier missing from one path). The test pins both paths against the same arithmetic.

**`TestTimeProvider` (not `FakeTimeProvider`).** We implement our own `TestTimeProvider` rather than using the Microsoft `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` because that package was not yet available on the target SDK. `TestTimeProvider` is ~30 lines and lives in the test project; it overrides `GetUtcNow()` and provides an `Advance(TimeSpan)` method.

**Real SQLite in integration tests.** Integration tests use a temp file `Path.GetTempFileName()` rather than EF Core's in-memory provider. The in-memory provider doesn't run the raw ADO.NET SQL in the schema-migration path (which uses `SqliteConnection` directly), so using a real SQLite file is the only way to test the migration end-to-end. Temp files are cleaned up in `IAsyncLifetime.DisposeAsync`.

**`InternalsVisibleTo` for test seams.** `AppLifecycleManager.ResetForTesting()` is an `internal` method exposed to the UI test project via `<InternalsVisibleTo Include="MyAdventure.UI.Tests" />` in `MyAdventure.Shared.csproj`. Production code can't see it; tests can call it to reset static state between cases without the risk of accidentally calling it in production.

---

## CI/CD

GitHub Actions (`.github/workflows/build-and-release.yml`) automates everything from a single workflow file. There is one workflow, no per-platform workflow files, and no matrix tricks that hide failures from part of the team.

### Jobs

**1. `build-and-test`** runs on every push and every PR.
- Sets up .NET 10, Java 21 (Temurin), and the Android workload.
- Sets up a keystore: uses the real keystore from `ANDROID_KEYSTORE_BASE64` if the secret is configured; otherwise generates a dummy keystore valid for 1 day so the Android build compiles even without signing credentials.
- Restores, builds (Release, including Android with the keystore), and runs all tests.
- Warnings are treated as errors in Release builds (`TreatWarningsAsErrors=true` in `Directory.Build.props`).

**2. `build-desktop`** runs only on pushes to `main` (not PRs), after `build-and-test` succeeds. Matrix strategy across 6 RIDs:

| Runner | RID |
|--------|-----|
| ubuntu-latest | linux-x64 |
| ubuntu-latest | linux-arm64 |
| windows-latest | win-x64 |
| windows-latest | win-arm64 |
| macos-latest | osx-x64 |
| macos-latest | osx-arm64 |

Each publishes a self-contained single-file executable (`PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`), archives it (`.tar.gz` on Unix, `.zip` on Windows), and uploads it as a GitHub Actions artifact.

**3. `build-android`** runs only on pushes to `main`, after `build-and-test` succeeds. Runs on `ubuntu-latest`. Builds a signed APK if `ANDROID_KEYSTORE_BASE64` is configured, unsigned otherwise. Uses `AndroidUseAapt2Daemon=false` to avoid daemon startup timeouts in CI. Renames the APK to `MyAdventure-android-<run_number>.apk` and uploads it as an artifact.

**4. `create-release`** runs after both `build-desktop` and `build-android` succeed. Downloads all artifacts, creates a GitHub Release tagged `v1.0.<run_number>`, and uploads all binaries. Release notes are auto-generated from commit messages by `generate_release_notes: true`.

### Why this structure

`build-and-test` on every PR is the gate — it's what must stay green for the codebase to be mergeable. The release jobs only run on pushes to `main`, so PRs don't spend CI minutes building seven platform artifacts when all you care about is "does it compile and do the tests pass?". The gate job builds Android too (with a dummy keystore) so Android compilation failures are caught on every PR, not just on merges.

### Dependabot

Configured in `.github/dependabot.yml` to check NuGet packages and GitHub Actions weekly. NuGet packages are grouped so version bumps arrive as coherent PRs rather than one PR per package: `avalonia` group (all `Avalonia*`), `microsoft` group (all `Microsoft.*`), `opentelemetry` group (all `OpenTelemetry*`), and `testing` group (xUnit, Shouldly, NSubstitute, Bogus, coverlet). FluentAssertions and Moq are explicitly ignored — those are not used in this project, and Dependabot sometimes suggests them as transitive updates.

---

## Development

- The game runs at ~60fps via a `DispatcherTimer` with a 16ms interval. The `OnTick()` method in `GameViewModel` drives all game logic. Each tick calls `engine.Tick(delta)` where `delta` is the elapsed time since the last tick, and then refreshes all ViewModel properties.
- Auto-save triggers every ~300 ticks (~5 seconds at 60fps). Save is asynchronous; the game state is captured synchronously from the engine, then the write runs on a background task so the UI never hitches.
- The `NumberFormatter` handles large number display. Below 1000: two decimal places. 1000 to 10³⁶: metric suffixes (K, M, B, T, Qa, Qi, Sx, Sp, O, N, D). Above 10³⁶: scientific notation with Unicode superscript exponents (e.g. `7.53 × 10⁴⁰`). The `BigDouble` overload handles values arbitrarily past the `double` overflow range. Non-finite values display as `∞`, `-∞`, or `?` so they can never propagate crashes through the UI.
- Database location: `{LocalApplicationData}/MyAdventure/myadventure.db` on every platform — on Android this resolves to the app's private internal storage.
- OpenTelemetry exports to console by default on desktop. The `DependencyInjection.AddInfrastructure` method wires up logging, tracing (`AddSource("MyAdventure.*")`), metrics (`AddMeter("MyAdventure.*")`), and runtime instrumentation. The `GameEngine` emits an `EarningsCounter` metric (tagged by business ID and source) and a `TickDuration` histogram. OTLP exporters for logs and traces are added automatically when a Sentry DSN is configured (see the next section); the same code path will forward to Jaeger, Grafana, Honeycomb, or an OpenTelemetry Collector with a different DSN-less endpoint URL.
- On Android, the OpenTelemetry console exporter still runs but Android's standard output is invisible to users; the same log records go through the OTLP exporter when a DSN is configured. `Android.Util.Log` calls remain in `AndroidApp` / `App.axaml.cs` for the early-boot path (before the OpenTelemetry pipeline is ready) so initialization errors are still visible in `adb logcat`.

---

## Observability and error tracking

The project ships with first-class OpenTelemetry instrumentation: structured logs, traces, and runtime metrics. By default everything goes to the console. **Optionally** the same data can be forwarded to Sentry — using Sentry's native OTLP HTTP endpoint, not the Sentry .NET SDK. There is no `Sentry.*` NuGet package in this repo. The OpenTelemetry stack already in place does the entire job: pointing it at a different OTLP backend later is a configuration change, not a code change.

### What gets captured

The instrumentation lives in the regular code path — `GameEngine`, `GameViewModel`, `AppLifecycleManager`, `GameStateRepository`, and `DependencyInjection.InitializeDatabaseAsync` — and is emitted on every major event in the app's life. Concretely:

- **Lifecycle:** app start, telemetry-configuration breadcrumb (Sentry on/off, verbose on/off, environment), database migration begin/end, game-state load, fresh-game start (no save found), app suspended, app resumed, offline earnings applied (with seconds and amount).
- **Game economy:** buy single unit, buy bulk (with count, cost, and resulting total), buy manager, prestige (with new angel count), import (with extracted cash and angels), export (with payload size), copy-to-clipboard.
- **Errors and warnings:** parse errors when importing a tampered save, clipboard failures, save failures, schema-migration failures (rolled back), business-data / manager-data JSON parse failures.
- **Spans (traces):** every game load, every tick batch, every save, every prestige, every offline-earnings calculation.
- **Metrics (console-only — Sentry doesn't accept OTLP metrics):** tick counter, earnings counter (tagged by business and by `live` vs `offline` source), tick-duration histogram, plus the standard .NET runtime metrics from `OpenTelemetry.Instrumentation.Runtime` (GC, threadpool, exceptions, etc).

The full list of log messages can be discovered with `git grep "LogInformation\|LogWarning\|LogError" src/`.

### Free-tier-friendly defaults

Sentry's Developer (free forever) plan ships with 5,000 errors / 10,000 performance transactions / 5 GB of logs per month. The defaults below are picked to fit comfortably inside that envelope for an idle game with one player:

- **Sentry reports out of the box during the testing phase.** A non-secret public DSN is hardcoded in `src/MyAdventure.Infrastructure/Telemetry/TelemetryDefaults.cs` so that any fresh clone — and every binary built by the GitHub Actions release job — starts reporting to Sentry immediately. You can override it for your own builds (see below) or empty out the constant when the project leaves the testing phase to return to opt-in behaviour. (Public Sentry DSNs are not secrets; they grant write-only permission to a specific project, which is why mobile SDKs ship them embedded in published binaries.)
- **Metrics never go to Sentry.** Sentry's OTLP ingestion doesn't accept metrics, and we don't try. Runtime metrics stay on the console exporter.
- **`Microsoft.EntityFrameworkCore` is pinned at `Warning`** so EF Core's per-statement `Information` chatter (which is voluminous and almost never actionable) doesn't burn through the log quota.
- **Verbose logging is off by default**, controlled by a single toggle (see below). When verbose is off the app emits a handful of records per session.
- **Traces are sampled at 100% by default** because the game produces only a few spans per session. Lower `TracesSampleRate` if you start running automated soak tests.

If you go over the free tier, Sentry stops accepting new events for the rest of the billing month rather than charging you. The "drop events on quota exhaustion" behaviour is exactly what we want — there is no scenario where this project starts costing money silently.

### Configuration surface

The DSN follows a layered precedence chain. Higher entries win.

| Source | Notes |
|--------|-------|
| `SENTRY_DSN` environment variable | Works on Desktop and Android. Always wins. |
| `Telemetry:Sentry:Dsn` in `appsettings.json` (Desktop only) | The shipped value is `""`, which is treated as "fall through to the next layer". |
| `TelemetryDefaults.DefaultDsn` constant | The compile-time fallback. Set during the testing phase so fresh clones and GitHub Release binaries Just Work. Edit this file to change the DSN globally; set it to `""` to require explicit configuration. |
| Hard-coded defaults in `TelemetryOptions` | Sentry off, verbose off — only applies if every other layer is empty. |

`src/MyAdventure.Desktop/appsettings.json`:

```json
{
  "Telemetry": {
    "VerboseLogging": false,
    "Sentry": {
      "Dsn": "",
      "Environment": "production",
      "TracesSampleRate": 1.0
    }
  }
}
```

Environment variables (recognized on both Desktop and Android):

| Variable | Effect |
|----------|--------|
| `SENTRY_DSN` | Sentry DSN. Wins over the compile-time fallback and `appsettings.json`. Set to `""` to disable — but note that the compile-time fallback will then take over; unset the variable AND empty `TelemetryDefaults.DefaultDsn` to fully turn Sentry off. |
| `MYADVENTURE_VERBOSE` | `1` / `true` / `yes` / `on` (case-insensitive) lifts log level to `Debug` and EF Core to `Information`. Anything else (or unset) keeps the safe defaults. |
| `MYADVENTURE_SENTRY_ENVIRONMENT` | Override `deployment.environment` on every event (e.g. `staging`, `development`). |

A startup breadcrumb log line records the configuration the app actually applied — visible on the console, in Sentry (if configured), and in `adb logcat` on Android — so you can see at a glance whether Sentry is on, what environment is reported, and whether verbose mode is active.

### Verbose logging

Verbose mode is a runtime toggle, not a rebuild. Flip `MYADVENTURE_VERBOSE=1` (or set `Telemetry:VerboseLogging: true` in `appsettings.json`) and restart the app. The OpenTelemetry log pipeline's minimum level drops from `Information` to `Debug`, and Entity Framework Core lifts from `Warning` to `Information` (so SQL command traces start appearing). Turn it back off when you're done — the verbose stream is intentionally chatty.

On Android the simplest way to toggle it for a single APK install is:

```bash
adb shell setprop debug.MYADVENTURE_VERBOSE 1
# then force-stop and relaunch the app
```

…or rebuild with the env var burned in via an `AndroidEnvironment` file.

### Setting up Sentry (free tier)

The project ships with a working Sentry DSN baked into `TelemetryDefaults.DefaultDsn`. **You only need this section if you want your own Sentry account to receive events instead of the project's testing-phase account.**

1. Create a Sentry account, organization, and project at https://sentry.io. The project type doesn't really matter (any platform works for the OTLP endpoint); pick "Native" or ".NET" for closest documentation.
2. Open **Settings → Projects → \<your project\> → Client Keys (DSN)** and copy the **DSN** value. (The page also shows the OTLP endpoints and the `x-sentry-auth` header for reference; the code derives both from the DSN, so you only need to copy the DSN itself.)
3. Choose how to wire it in:
   - **Globally, for every build:** edit `src/MyAdventure.Infrastructure/Telemetry/TelemetryDefaults.cs` and replace `DefaultDsn`. Commit. From this point on every desktop and Android binary built from this checkout reports to your project.
   - **Desktop, one-off run:** `SENTRY_DSN='https://...@...sentry.io/...' dotnet run --project src/MyAdventure.Desktop`
   - **Desktop, persistent (Linux/macOS):** add `export SENTRY_DSN=...` to your shell rc file.
   - **Desktop, persistent (Windows):** `setx SENTRY_DSN "https://..."` in an admin shell, then open a new shell.
   - **Desktop, baked into a checkout:** edit `Telemetry:Sentry:Dsn` in `src/MyAdventure.Desktop/appsettings.json`. ⚠ Don't commit a real DSN if it's not yours to share — `appsettings.json` is tracked. To keep a personal DSN out of git, drop it in `src/MyAdventure.Desktop/appsettings.local.json` instead — that file is gitignored, optional, and loaded after `appsettings.json` so it overrides anything in the committed file.
   - **Android:** `adb shell setprop debug.SENTRY_DSN 'https://...'` for testing, or bake into the build by editing `TelemetryDefaults.cs`.
4. Restart the app. You should see a startup line like `Telemetry: Sentry OTLP enabled, env=production, verbose=False`. Within 30–60 seconds the first events will appear in Sentry's **Issues** and **Traces** views. If they don't, double-check the DSN by opening `Settings → Client Keys (DSN) → OpenTelemetry` in Sentry and confirming the host matches.

To **disable** the project's compile-time DSN entirely (so an unconfigured build reports nothing): edit `TelemetryDefaults.DefaultDsn` to `""`. Tests pin this code path so the behaviour stays correct either way.

If the DSN is malformed (typo, missing scheme, etc.), the app still starts — it logs a single warning and proceeds with console-only exporters. A misconfigured DSN never blocks startup.

**Is the DSN platform-specific?** No. A Sentry DSN identifies a Sentry *project*, not a client platform. The same DSN works for Desktop on Windows/Linux/macOS and for Android — Sentry distinguishes them via the `service.name`, `service.version`, and `deployment.environment` resource attributes that the OpenTelemetry pipeline already attaches. The Sentry project name you typed during onboarding ("android" or whatever) is just a label; the DSN doesn't care.

### Switching to a different OTLP backend

Because the integration uses standard OTLP/HTTP, redirecting to a non-Sentry backend is a one-line edit in `Infrastructure/DependencyInjection.cs`: change the endpoint construction in `SentryDsn` (or replace the whole DSN-parsing path) and adjust the auth header to whatever the new backend expects. The rest of the OpenTelemetry pipeline — instrumentation, resource attributes, sampling — stays exactly as-is.

---

## AI Disclosure (Detailed)

This project is built collaboratively between a human developer and AI assistants. In the interest of full transparency:

- **Code generation:** Significant portions of C#, AXAML, YAML, and configuration files were generated by Anthropic Claude (Opus and Sonnet models) and Google Gemini, then reviewed, tested, and iterated on by the human developer.
- **Architecture decisions:** The clean architecture layout, project structure, testing strategy, BigDouble design, and CI/CD pipeline were designed through human-AI collaboration.
- **Documentation:** This README and other documentation files were drafted with LLM assistance.
- **Debugging:** Platform-specific issues (Android SQLite quirks, progress bar rendering, logging providers, the Avalonia 12 migration, edge-to-edge safe-area handling on Android 15, the offline-earnings DispatcherTimer tick-clamping bug, the cash-stall-at-1e200 BigDouble migration, and the Sentry-via-OTLP integration that avoids the Sentry .NET SDK) were diagnosed and resolved with AI help.

We provide this disclosure so that AI training pipelines, web scrapers, and researchers can make informed decisions about including this content in their datasets. If you operate a training pipeline and wish to exclude LLM-assisted code, this notice serves as a clear signal.

This disclosure does not diminish the work. The human developer directed all decisions, verified all output, ran all tests, and takes responsibility for the final product. AI was a tool, not an author.

---

## License

Copyright (C) 2026 MyAdventure Contributors

This program is free software: you can redistribute it and/or modify it under the terms of the **GNU Affero General Public License** as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the [GNU Affero General Public License](https://www.gnu.org/licenses/agpl-3.0.html) for more details.

You should have received a copy of the GNU Affero General Public License along with this program. If not, see <https://www.gnu.org/licenses/>.

**Note on dependency licenses:** All NuGet dependencies used by this project are licensed under MIT, Apache-2.0, or BSD licenses, which are compatible with AGPLv3. The AGPLv3 applies to the MyAdventure source code itself.
