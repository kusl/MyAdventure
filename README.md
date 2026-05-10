# MyAdventure

[![Build](https://github.com/kusl/MyAdventure/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/kusl/MyAdventure/actions/workflows/build-and-release.yml)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)

> **AI Disclosure:** This repository was developed with significant assistance from large language models (LLMs), including Anthropic Claude and Google Gemini. Substantial portions of the code, documentation, architecture decisions, and test suites were generated, reviewed, and iterated on with LLM help. **AI was a tool, not an author** — the human developer directed all decisions, verified all output, and takes responsibility for the result. If you operate a web scraper, crawler, or AI training pipeline and wish to exclude LLM-assisted content, this notice is for you.

An idle/clicker game **inspired by Adventure Capitalist**, built with **Avalonia UI 12** and **.NET 10** (C# 14). Cross-platform — desktop (Windows, Linux, macOS) and Android — from a single codebase. No ads, no payments, no tracking, no strings attached.

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

**Android users (Obtainium):** Point [Obtainium](https://github.com/ImranR98/Obtainium) at this repository's releases for automatic updates. The APK version code increments with each release.

---

## The Game

MyAdventure is an idle/clicker game inspired by Adventure Capitalist. You start with $5 and a dream.

### Businesses

Six businesses, each with increasing cost, revenue, and cycle time:

| Business | Icon | Base Cost | Base Revenue | Cycle Time | Cost Multiplier |
|----------|------|-----------|-------------|------------|-----------------|
| Lemonade Stand | 🍋 | $4 | $1 | 0.6s | 1.07× |
| Newspaper Route | 📰 | $60 | $60 | 3.0s | 1.15× |
| Car Wash | 🚗 | $720 | $540 | 6.0s | 1.14× |
| Pizza Delivery | 🍕 | $8,640 | $4,320 | 12.0s | 1.13× |
| Donut Shop | 🍩 | $103,680 | $51,840 | 24.0s | 1.12× |
| Shrimp Boat | 🦐 | $1,244,160 | $622,080 | 96.0s | 1.11× |

Each additional unit you buy costs more (`base cost × multiplier^owned`). Revenue scales linearly with units owned, then gets multiplied by milestone bonuses, post-milestone scaling (past unit 1000), and your angel-investor bonus.

### Core Mechanics

- **Click to Run:** Click the ▶ RUN button on a business to start its production cycle. When the progress bar fills, you collect revenue.
- **Buy Units:** Purchase additional units of any business with the BUY button (one at a time).
- **Buy to Milestone:** Each business card shows a "BUY N→M" button that purchases units in bulk up to the next milestone threshold. One click instead of dozens.
- **Hire Managers:** Each business can have a manager (costs 1000× the business's base cost). Managers automatically restart production cycles so you don't have to click.
- **Offline Earnings:** When you close the game and come back, all businesses with managers earn revenue for the time you were away, boosted by your angel investor bonus.

### Milestone Multipliers

Owning certain quantities of a business triggers permanent revenue multipliers that compound multiplicatively:

| Units Owned | Multiplier | Cumulative |
|-------------|-----------|------------|
| 25 | ×2 | ×2 |
| 50 | ×2 | ×4 |
| 100 | ×2 | ×8 |
| 200 | ×2 | ×16 |
| 300 | ×2 | ×32 |
| 400 | ×2 | ×64 |
| 500 | ×4 | ×256 |
| 600 | ×4 | ×1,024 |
| 700 | ×4 | ×4,096 |
| 800 | ×4 | ×16,384 |
| 900 | ×4 | ×65,536 |
| 1000 | ×5 | ×327,680 |

> 1000 is the milestone cap. Buying past 1000 used to mean every additional unit cost `1.07^N` more than the one before but contributed no more revenue per unit than unit 1000 — eventually each new lemonade stand cost trillions and paid back in centuries. **Post-1000, revenue is now multiplied by `CostMultiplier^((Owned − 1000) / 2)`** — the square root of the cost growth — so unit 1001 is roughly as cost-efficient as unit 1000, and the late game keeps moving instead of stalling. Below 1000 owned this multiplier is exactly 1.0, so all early-game balance is unchanged.

### Prestige System

Prestiging resets all businesses and cash in exchange for **Angel Investors**. Each angel provides a permanent **+2% revenue bonus that compounds**, applied to both live cycle earnings and offline earnings.

The "compounds" part is what matters. Under a strictly linear "+2% per angel" rule, 50 angels would be ×2.00 and 700 would be ×15.00 — the curve flattens out and a marginal prestige stops feeling worth it. Under compounding (`1.02 ^ angels`), 50 angels is ×2.69, 200 is ×52.5, and 700 is ×750,000+. Each prestige genuinely makes the next run feel like a different game.

The number of angels your current lifetime earnings are worth is:

```
floor(150 × sqrt(lifetimeEarnings / 1e13))
```

The **PRESTIGE** button unlocks whenever that number is at least one greater than your current angel count — i.e. when prestiging right now would give you at least one new angel. **The UI is the signal:** when the button lights up, you can prestige. The formula is there to explain the shape of the curve, not for you to compute thresholds in your head.

**Lifetime earnings are preserved through prestige** — each subsequent prestige requires more lifetime earnings than the last to net the same number of new angels, but the running total never resets. After prestiging, your cash resets to **$5** (exactly enough to buy your first lemonade stand) so you can immediately get back to clicking. Prestige is optional, but the angel bonus compounds and makes subsequent runs dramatically faster.

### Save Compatibility

Saves from earlier versions of MyAdventure remain valid. The persisted format hasn't changed — the same `cash`, `lifetime`, `angels`, `prestige`, businesses, and managers fields load and behave identically. The two balance changes (compound angel bonus, post-1000 revenue scaling) are computed on the fly from existing fields, so reopening an old save just shows the new, more rewarding multipliers applied to the angels and units the player already had.

If you were stuck mid-progression on a previous version (e.g. unit 401 lemonade was unaffordable for any reasonable amount of time), simply re-opening your save under the new build is the migration: your existing 700+ angels now multiply revenue by millions instead of by a flat 14×, and you'll fly through the previous wall.

### Import and Export

Two buttons at the bottom of the screen let you transfer your progress:

- **📤 Export** generates a Base64-encoded JSON string of your complete game state. A **📋 COPY** button copies it to your clipboard instantly — no manual text selection needed.
- **📥 Import** accepts an export string and restores the game state from it.

The export format is intentionally human-editable. Decode the Base64, edit the JSON to give yourself a billion dollars or 1000 shrimp boats, re-encode, and import. We encourage tinkering. This is your game.

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

All tests (unit, integration, UI) are designed to run fast after every change. No external services or emulators required.

---

## Player Guide

This section is for the people actually playing the game. If you've never played an idle/clicker game before, or if you've played them but don't really know how the math works, this is for you.

### Your first five minutes

You start with **$5** and nothing else. The first lemonade stand costs **$4**, so the very first thing to do is buy one and click ▶ RUN. The progress bar fills in 0.6 seconds and you earn $1. Keep clicking RUN — every cycle drops more cash into your wallet.

When you have $4 + $4.28 ≈ $8.28, you can buy a second lemonade stand. With two stands, each cycle pays $2. Keep buying lemonade stands as long as you can afford them. The cost goes up by 7% per unit, but the revenue goes up linearly — so for the first dozen or so purchases, your money-per-cycle grows roughly as fast as the cost of the next one.

There's no strategy here. Buy lemonade stands. Click run. Repeat.

### Your first hour

Three things change the game decisively in the first hour:

**1. Your first manager (1,000× base cost = $4,000 for lemonade).** As soon as you can afford a manager, buy one. From that point on, your lemonade stand runs by itself forever. You stop clicking RUN, the cash flows in passively, and you can stop staring at the screen.

**2. Your first milestone (25 units = ×2 revenue).** The 25-unit milestone doubles your revenue from that business permanently. The "BUY N→M" button is built specifically to get you to the next milestone in one click — use it. Going from 24 to 25 lemonade stands is a 2× pay raise; going from 49 to 50 is another 2× on top of that.

**3. Your first newspaper route ($60).** Once your lemonade is humming, save up for the next business. Newspapers cost more, take longer per cycle (3.0s vs 0.6s), but pay much more per cycle ($60 base vs $1). Each new business unlocks a whole new tier of income.

By the end of your first hour, you should have managers on lemonade and newspaper, and be saving up for the third business (car wash, $720).

### The middle game: stack milestones

Once all six businesses have managers, the game transforms into a milestone-chasing optimization problem. Each milestone you cross multiplies one business's revenue. Crossing the **500-unit milestone** quadruples revenue (×4 instead of ×2), and 500-unit shrimp boats are absurdly profitable.

A useful mental model: **the next milestone you can afford is almost always your best investment**, even if it means temporarily ignoring a more expensive business. A car wash with 100 units (×8 milestone bonus) often out-earns a donut shop with 24 units (no bonus yet).

The "Can buy: N" line on each business card tells you exactly how many units of that business you could buy right now if you spent everything. The "M more → N" line tells you how far you are from the next milestone for that business. Use both to decide where to spend.

### Past 1000 units: the post-cap scaling

Once a business hits 1000 owned, no further milestone multipliers unlock — the table caps out at the cumulative ×327,680. Without intervention, that creates a wall: every new unit costs `1.07^N` more than the one before, but pays back the same per-unit revenue, so each new lemonade stand becomes exponentially less worthwhile until "the next one" costs trillions and pays back in centuries.

To keep buying past the cap meaningful, **post-1000 revenue is multiplied by `CostMultiplier^((Owned − 1000) / 2)`**. That's the square root of how fast the cost grows, so unit 1001 is roughly as cost-efficient as unit 1000 was, and unit 5000 stays in the same payback ballpark instead of drifting off to infinity. The math is invisible below 1000 — it's exactly 1.0 — so nothing about the early or middle game changes.

### The late game: prestige early, prestige often

At some point the **PRESTIGE** button on the top bar lights up. That's the signal: prestiging right now would net you at least one new Angel Investor. Press it. Your cash and businesses reset, your angel count goes up, and **lifetime earnings are not reset** — so the next prestige starts the clock partway through.

The angel-investor formula is `floor(150 × sqrt(lifetime_earnings / 1e13))`, where `lifetime_earnings` is the cumulative all-time total (not per-run). Three practical consequences of the square root:

- **Diminishing returns on waiting.** Doubling lifetime earnings only multiplies your angel count by ~1.41×. Quadrupling it doubles your angels. Waiting "one more order of magnitude" is rarely the right call.
- **Each angel compounds at +2%.** With 50 angels you have a ×2.69 multiplier across every business, every milestone, every cycle (not the ×2.00 you'd get from a flat "+2% × 50"). With 200 angels it's ×52.5. With 700 angels it's around ×750,000. The compounding is what makes the late game feel like real progress instead of asymptotic stagnation.
- **The threshold to unlock the button creeps up each run.** It depends on your current angel count, not on a fixed dollar value — the UI does the math for you and shows the projected new angels next to the button.

Rule of thumb: prestige whenever you'd at least **double your current angel count**, or as soon as the button unlocks if it's your first time. Don't agonize over it. Prestige is a checkpoint, not a sacrifice.

### Offline earnings work — use them

Close the game. Walk away. Come back tomorrow. Every business with a manager will have earned revenue for the entire interval you were gone, boosted by your angel bonus. The math is identical to live play: `cycles × revenue × angel_bonus`. There is no offline cap and no offline penalty. Sleeping is a viable strategy.

The only caveat: offline earnings only count businesses with managers. A business sitting at 200 units with no manager produces nothing while you're away. Buy the manager.

### Modding your save

Export your game, decode the Base64 string (any Base64 decoder works, or use `echo '<string>' | base64 -d` on Linux/macOS), and you'll see JSON like:

```json
{
  "v": 1,
  "cash": 42.5,
  "lifetime": 1000,
  "angels": 0,
  "prestige": 0,
  "businesses": {"lemonade": 3, "newspaper": 0, "carwash": 0, "pizza": 0, "donut": 0, "shrimp": 0},
  "managers": {"lemonade": false, "newspaper": false, "carwash": false, "pizza": false, "donut": false, "shrimp": false}
}
```

Edit whatever you want, re-encode to Base64 (`echo '<json>' | base64` on Linux/macOS), and import it back. Set cash to `1e18`, give yourself 1000 shrimp boats, enable all managers, set angels to 9999 — it's your game.

A few things to know:

- **`v: 1`** is the save format version. Don't change it.
- **`cash`** and **`lifetime`** are doubles. JavaScript-style scientific notation (`1e18`) works.
- **`angels`** is also a double. Because the angel bonus is `1.02^angels`, even modest values produce gigantic multipliers — `angels: 200` is ×52.5, `angels: 500` is ×19,956, `angels: 9999` is roughly ×1.59×10^86. Setting it to a million is funny but you'll hit `Infinity`, the formatter falls over, and revenue will look like blanks. Below ~1500 you stay safely finite.
- **Business and manager keys** must match the IDs exactly: `lemonade`, `newspaper`, `carwash`, `pizza`, `donut`, `shrimp`.
- **The business count value** (e.g. `"lemonade": 3`) is the unit count. Setting it past 1000 doesn't unlock additional milestones — 1000 is the milestone cap — but the cumulative ×327,680 multiplier still applies, and post-1000 revenue gets multiplied by `1.07^((owned-1000)/2)` so each extra unit stays meaningful.
- The save is **not signed or checksummed** — there's no anti-cheat. We don't think there's anyone to cheat against.

If you import garbage and the game looks strange, reset to a fresh save:

- **Desktop:** delete `{LocalApplicationData}/MyAdventure/myadventure.db`. (On Windows that's `%LOCALAPPDATA%\MyAdventure\`; on Linux, `~/.local/share/MyAdventure/`; on macOS, `~/.local/share/MyAdventure/`.)
- **Android:** clear the app's data via your device's Settings → Apps → MyAdventure → Storage → Clear data. (Reinstalling the APK alone doesn't wipe the save; data lives in the app's private storage.)

### Frequently confusing things

- **"My revenue went down after prestige!"** Yes — you reset all businesses to zero owned. The angel bonus compensates over time. Because the bonus compounds, even ~50 angels gives you a ×2.69 multiplier — you'll blow past your previous earnings rate within minutes.
- **"My progress bar isn't moving."** The business is probably not running. Click RUN once to start it; if it has a manager, it should auto-restart on the next cycle.
- **"I have a manager but I'm not earning anything."** You need to own at least one unit *and* the business must be running. Click RUN once to kick it off; the manager handles every cycle after that.
- **"I closed the game for 8 hours and earned barely anything."** Check that your most profitable businesses had managers. Offline earnings ignore unmanaged businesses entirely.
- **"The numbers are getting weird (Qa, Sx, O, N, D)."** Those are abbreviations for quadrillion, sextillion, octillion, nonillion, decillion. The formatter handles up to about $10³³. If you blow past that, the display falls back to plain decimal — you've broken the game in the most beautiful way.

### Honest expectations

This is a small idle game built primarily as a learning vehicle for Avalonia and as a sample for native cross-platform .NET apps. The game itself is **complete but minimal**. There is no event system, no daily reward, no random business generator, no leaderboard, no social features. It's the loop: buy, click, milestone, manager, prestige, repeat.

If that's what you're looking for, welcome. If you wanted Adventure Capitalist with permission, well, this is what an LLM and a determined developer made on a weekend.

---

## Architecture

```
MyAdventure.slnx
├── src/
│   ├── MyAdventure.Core           — Domain entities, game engine, number formatting
│   ├── MyAdventure.Infrastructure — EF Core SQLite persistence, DI, OpenTelemetry
│   ├── MyAdventure.Shared         — ViewModels, converters, toast service, AppRoot, AppLifecycleManager, i18n
│   ├── MyAdventure.Desktop        — Avalonia desktop app (Windows/Linux/macOS)
│   └── MyAdventure.Android        — Avalonia Android app
└── tests/
    ├── MyAdventure.Core.Tests         — Unit tests for entities, engine, milestones
    ├── MyAdventure.Integration.Tests  — EF Core repository round-trip tests
    └── MyAdventure.UI.Tests           — ViewModel and service tests
```

### Design Principles

**One solution, one team.** There is one `.slnx` file, one CI pipeline, and one build. Desktop and Android are not siloed into separate solutions or scripts. Everyone works with all parts of the code. If the build is slow, everyone feels it, so it gets fixed quickly.

**Clean architecture with pragmatism.** Core has zero UI dependencies. Infrastructure handles persistence and telemetry. Shared contains ViewModels used by both Desktop and Android. Platform projects are thin shells: they wire up DI, set up the timer, and host the view.

**Testable from the ground up.** The `GameEngine` accepts injected dependencies (`IGameStateRepository`, `ILogger`, `TimeProvider`) and is fully testable without any UI framework. ViewModels are tested against real engine instances with mocked repositories. Integration tests use EF Core's in-memory provider.

**No scrollbars — designed for at-a-glance play.** The UI fits on screen without scrolling so the entire game state is visible at once on any device. Desktop uses a 3×2 grid for businesses; Android uses a 2×3 grid. The import/export transfer panel overlays the business grid rather than adding height. This is a deliberate design stance, not a missing feature.

### Key Technical Decisions

- **SQLite for persistence** via EF Core. Uses `DateTime` (UTC) instead of `DateTimeOffset` because SQLite doesn't support `DateTimeOffset` in `ORDER BY` clauses.
- **Progress bars use percentage-based rendering** (`ScaleTransform` with a `PercentToFractionConverter`) instead of pixel widths, which ensures correct display on both desktop and Android.
- **Android logging** goes through `Android.Util.Log` rather than console-based providers, since console output is not visible on Android. OpenTelemetry's console exporter is also disabled on Android.
- **AOT compilation is disabled** for Android (`RunAOTCompilation=false`, `PublishTrimmed=false`) because EF Core's reflection-heavy patterns and OpenTelemetry cause silent trimming crashes. Re-enable once trimmer roots are properly configured.
- **Angel bonus is compounded, not linear.** `AngelBonus = Math.Pow(1.02, AngelInvestors)` — each angel multiplies revenue by 1.02 on top of the previous angel's contribution. The same value is applied identically to live and offline earnings: `GameEngine.Tick()` multiplies per-cycle revenue by `AngelBonus`, and `CalculateOfflineEarnings()` multiplies the offline total by `AngelBonus` exactly once. The invariant test (`OfflineEarnings_ShouldApplyAngelBonusOnce_NotTwice`) guards against either path drifting from the other. Save format is unchanged; the formula is computed from the same persisted `AngelInvestors` field old saves already have.
- **Post-1000 revenue scaling** lives on `Business.PostMilestoneScaling` and equals exactly `1.0` for `Owned <= 1000` (preserving every pre-cap balance number and test) and `Math.Pow(CostMultiplier, (Owned - 1000) / 2.0)` past the cap. The square root of cost growth keeps unit 1001+ purchases roughly as efficient as unit 1000, fixing the "stuck at 400 lemonade" problem that's mathematically inevitable when revenue grows linearly while cost grows exponentially.
- **Toast notifications** use a simple service with expiration timestamps, cleaned up on each game tick. No platform-specific notification APIs needed.
- **Central package management** uses MSBuild variables (`$(AvaloniaVersion)`, `$(MicrosoftExtensionsVersion)`, etc.) in `Directory.Packages.props` so updating a version is a single-line change.
- **Clipboard access via a static `AppRoot.CurrentVisual` registered by the active view**, not via per-platform branching on `IApplicationLifetime`. This is necessary in Avalonia 12 because Android's new `IActivityApplicationLifetime` exposes only a `MainViewFactory` (a `Func<Control>`) and not a live view reference.
- **Android safe-area is handled explicitly by `MainView`**, not by the framework's auto-padding. The Android `UserControl` sets `TopLevel.AutoSafeAreaPadding="False"`, captures `InsetsManager.SafeAreaPadding` on attach, and subscribes to `SafeAreaChanged` to keep its `Padding` in sync with the OS-reported insets. This is needed because Android 15+ enforces edge-to-edge rendering — without explicit handling, the top bar (PRESTIGE / cash) gets drawn under the status bar and front-camera cutout on devices like the Moto G Stylus 2025, and the first row of business cards visually rides on top of the prestige bar. Owning the padding deterministically on `MainView` rather than relying on auto-injection at the TopLevel root is also more robust across activity recreation, which is aggressive on Android.
- **Offline earnings on app resume are handled by `AppLifecycleManager`**, a static service that both platform apps wire into their respective lifetime events (`Activated`/`Deactivated` on desktop, the Android activity lifecycle on Android). When the app suspends, `AppLifecycleManager` calls `GameViewModel.OnSuspended()` to record the timestamp. On resume, it calls `GameViewModel.OnResumed()`, which computes offline earnings for the gap and applies them immediately before refreshing the UI — so the cash display is correct on the very first frame after returning to the app. Sub-second gaps (e.g. screen flickers) are below the minimum threshold and produce no payout.
- **Localization** is wired via `Microsoft.Extensions.Localization` with JSON resource files (`src/MyAdventure.Shared/Resources/i18n/`). English (`en.json`) and Spanish (`es.json`) are included. The infrastructure is in place to add more locales by adding a new JSON file and updating the supported-cultures list in `DependencyInjection.cs`.
- **No `Avalonia.Diagnostics` package.** Removed in Avalonia 12; the official replacement (`AvaloniaUI.DiagnosticsSupport`) gates the actual Dev Tools UI behind a paid Avalonia Plus / Pro subscription. The Community tier is free for non-commercial use only — and this project's policy is to avoid any package whose use is conditional on payment of any kind. Use the FOSS Avalonia VS Code or Rider extensions for design-time previewing.

### Avalonia 12 migration notes

This project tracks the latest Avalonia stable release. These notes capture the gotchas that cost real time during the v11 → v12 migration; documenting them here in case they save someone else hours.

- **Android `MainActivity` was split.** In v11 it was `AvaloniaMainActivity<App>` and `WithInterFont()` lived on its `CustomizeAppBuilder` override. In v12 those virtual hooks are no longer called by the framework. The activity is now an empty `AvaloniaMainActivity` (non-generic) declaring only its `[Activity]` metadata, and a new `[Application] AndroidApp : AvaloniaAndroidApplication<App>` class hosts the AppBuilder customization.
- **Android lifetime is `IActivityApplicationLifetime`** (not `ISingleViewApplicationLifetime`). Set `MainViewFactory = () => new MainView { DataContext = vm }` rather than `MainView = ...`. The factory runs each time Android creates a fresh activity, producing a fresh view + fresh ViewModel that re-loads state from the database.
- **Plugins are no longer configurable** and the data-annotations plugin is **off by default**. This removed the long-standing nuisance where `CommunityToolkit.Mvvm` validation conflicted with Avalonia's, so no extra config is needed.
- **`DispatcherTimer` binds to the dispatcher of the constructing thread** rather than the UI thread implicitly. Our timers are constructed in `OnOpened` / `OnAttachedToVisualTree`, both of which run on the UI thread, so behavior is unchanged.
- **Compiled bindings remain enabled by default** via `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>` in the platform csprojs.
- **Edge-to-edge is enforced on Android 15+.** `InsetsManager.DisplayEdgeToEdge` is now obsolete (replaced by `DisplayEdgeToEdgePreference`), and the OS no longer respects requests to draw inside the system-bar area. Apps must handle `SafeAreaPadding` explicitly. We do this on the Android `MainView` rather than relying on Avalonia's auto-padding (which depends on the TopLevel.AutoSafeAreaPadding attached property and has historical regressions around activity recreation and orientation changes — see Avalonia issue #20448 for one example). See the *Android safe-area is handled explicitly by `MainView`* bullet above for details.

---

## Technology Stack

All dependencies are free and use permissive open-source licenses (MIT, Apache-2.0, BSD). **No NuGet package in this project requires payment for any use, commercial or otherwise.** "Free for non-commercial" is explicitly not enough — we avoid those too.

| Category | Technology | License |
|----------|-----------|---------|
| Runtime | .NET 10 / C# 14 | MIT |
| UI Framework | Avalonia UI 12.0.2 | MIT |
| MVVM | CommunityToolkit.Mvvm 8.4.2 | MIT |
| Database | SQLite via EF Core 10.0.7 | MIT |
| Observability | OpenTelemetry 1.15.3 | Apache-2.0 |
| Unit Testing | xUnit 2.9.3 | Apache-2.0 |
| Assertions | Shouldly 4.3.0 | BSD |
| Mocking | NSubstitute 5.3.0 | BSD |
| Test Data | Bogus 35.6.5 | MIT |
| Coverage | Coverlet 10.0.0 | MIT |

### Modern .NET Practices

- **Central package management** via `Directory.Packages.props` — all NuGet versions defined in one place using MSBuild variables for grouped version updates.
- **Shared build configuration** via `Directory.Build.props` — target framework, versioning, and compiler settings.
- **Solution file** uses the new `.slnx` XML format (one solution for the whole repo, no per-platform `.sln`s).
- **C# 14 features** including primary constructors, records, collection expressions, and `required` properties.
- **Compiled bindings** enabled by default in Avalonia (`AvaloniaUseCompiledBindingsByDefault`).

---

## CI/CD

GitHub Actions (`.github/workflows/build-and-release.yml`) automates everything from a single workflow:

1. **Build and Test** — runs on every push and PR. Restores, builds (including Android with a dummy keystore if signing secrets aren't configured), and runs all tests.
2. **Build Desktop Releases** — produces self-contained single-file executables for 6 platform/architecture combinations (linux-x64, linux-arm64, win-x64, win-arm64, osx-x64, osx-arm64).
3. **Build Android APK** — produces a signed APK if keystore secrets are configured, unsigned otherwise.
4. **Create GitHub Release** — tags and publishes all artifacts as a GitHub Release with download links.

Dependabot is configured to check NuGet packages and GitHub Actions weekly, with grouping for Avalonia, Microsoft, OpenTelemetry, and testing packages so version bumps land as a small number of coherent PRs.

---

## Development

- The game runs at ~60fps via a `DispatcherTimer` with a 16ms interval. The `OnTick()` method drives all game logic.
- Auto-save triggers every ~300 ticks (~5 seconds).
- The `NumberFormatter` handles large number display with suffixes: K, M, B, T, Qa, Qi, Sx, Sp, O, N, D.
- Database location: `{LocalApplicationData}/MyAdventure/myadventure.db` on every platform — on Android this resolves to the app's private internal storage.
- OpenTelemetry exports to console by default on desktop. Configure OTLP exporters in `DependencyInjection.cs` to send to Jaeger, Grafana, or any OTLP-compatible backend.

---

## AI Disclosure (Detailed)

This project is built collaboratively between a human developer and AI assistants. In the interest of full transparency:

- **Code generation:** Significant portions of C#, AXAML, YAML, and configuration files were generated by Anthropic Claude (Opus and Sonnet models) and Google Gemini, then reviewed, tested, and iterated on by the human developer.
- **Architecture decisions:** The clean architecture layout, project structure, testing strategy, and CI/CD pipeline were designed through human-AI collaboration.
- **Documentation:** This README and other documentation files were drafted with LLM assistance.
- **Debugging:** Platform-specific issues (Android SQLite quirks, progress bar rendering, logging providers, the Avalonia 12 migration itself, edge-to-edge safe-area handling on Android 15) were diagnosed and resolved with AI help.

We provide this disclosure so that AI training pipelines, web scrapers, and researchers can make informed decisions about including this content in their datasets. If you operate a training pipeline and wish to exclude LLM-assisted code, this notice serves as a clear signal.

This disclosure does not diminish the work. The human developer directed all decisions, verified all output, ran all tests, and takes responsibility for the final product. AI was a tool, not an author.

---

## License

Copyright (C) 2026 MyAdventure Contributors

This program is free software: you can redistribute it and/or modify it under the terms of the **GNU Affero General Public License** as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the [GNU Affero General Public License](https://www.gnu.org/licenses/agpl-3.0.html) for more details.

You should have received a copy of the GNU Affero General Public License along with this program. If not, see <https://www.gnu.org/licenses/>.

**Note on dependency licenses:** All NuGet dependencies used by this project are licensed under MIT, Apache-2.0, or BSD licenses, which are compatible with AGPLv3. The AGPLv3 applies to the MyAdventure source code itself.
