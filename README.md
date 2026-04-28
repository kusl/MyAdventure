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

Each additional unit you buy costs more (`base cost × multiplier^owned`). Revenue scales linearly with units owned, then gets multiplied by milestone bonuses and your angel-investor bonus.

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

> 1000 is the milestone cap. Buying past 1000 still increases revenue linearly with `owned`, but no further multipliers unlock.

### Prestige System

Once your **lifetime earnings** reach **$1 trillion** (`1e12`), the **PRESTIGE** button unlocks. Prestiging resets all businesses and cash in exchange for **Angel Investors**. Each angel provides a permanent **+2% bonus** to all revenue, forever — applied to both live cycle earnings and offline earnings.

The formula is gated and quadratic-decay:

```
angels = lifetimeEarnings >= 1e12
       ? floor(150 × sqrt(lifetimeEarnings / 1e13))
       : 0
```

So at exactly $1T lifetime you jump straight to 47 angels (a +94% bonus). **Lifetime earnings are preserved through prestige** — each subsequent prestige can give you more angels than the last.

After prestiging, your cash resets to **$5** (exactly enough to buy your first lemonade stand) so you can immediately get back to clicking. Prestige is optional, but the angel bonus compounds and makes subsequent runs dramatically faster.

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

### The late game: prestige early, prestige often

Once you hit **$1 trillion in lifetime earnings**, the **PRESTIGE** button lights up. Pressing it resets your cash and businesses but gives you Angel Investors — and crucially, **lifetime earnings are not reset**, so the prestige threshold for the next round is already partway met.

The angel-investor formula is `floor(150 × sqrt(lifetime_earnings / 1e13))`, gated so it returns zero below $1T. Two practical consequences:

- **The first prestige is a big jump.** At exactly $1T you get 47 angels right away — that's a +94% multiplier on everything.
- **Doubling lifetime earnings doesn't double angels.** Going from $1T to $4T quadruples lifetime but only doubles angel count, because of the square root. Waiting forever is poor value.
- **Each angel is +2% revenue forever.** With 50 angels you have a flat ×2 multiplier across every business, every milestone, every cycle. That doubles your offline earnings too.

Rule of thumb: prestige whenever you'd at least **double your current angel count**, or as soon as you unlock the button if it's your first time. Don't agonize over it. Prestige is a checkpoint, not a sacrifice.

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
- **`angels`** is also a double (the formula uses `Math.Floor` so fractional angels round down).
- **Business and manager keys** must match the IDs exactly: `lemonade`, `newspaper`, `carwash`, `pizza`, `donut`, `shrimp`.
- **The business count value** (e.g. `"lemonade": 3`) is the unit count. Setting it past 1000 doesn't unlock additional milestones — 1000 is the milestone cap — but the cumulative ×327,680 multiplier still applies, and revenue continues to scale linearly with the count.
- The save is **not signed or checksummed** — there's no anti-cheat. We don't think there's anyone to cheat against.

If you import garbage and the game looks strange, reset to a fresh save:

- **Desktop:** delete `{LocalApplicationData}/MyAdventure/myadventure.db`. (On Windows that's `%LOCALAPPDATA%\MyAdventure\`; on Linux, `~/.local/share/MyAdventure/`; on macOS, `~/.local/share/MyAdventure/`.)
- **Android:** clear the app's data via your device's Settings → Apps → MyAdventure → Storage → Clear data. (Reinstalling the APK alone doesn't wipe the save; data lives in the app's private storage.)

### Frequently confusing things

- **"My revenue went down after prestige!"** Yes — you reset all businesses to zero owned. The angel bonus compensates over time. After ~50 angels, you'll blow past your previous earnings rate within minutes.
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
│   ├── MyAdventure.Shared         — ViewModels, converters, toast service, AppRoot, i18n
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
- **Angel bonus is applied identically to live and offline earnings.** `GameEngine.Tick()` multiplies per-cycle revenue by `AngelBonus`, and `CalculateOfflineEarnings()` multiplies the offline total by `AngelBonus` exactly once. An invariant test (`OfflineEarnings_ShouldApplyAngelBonusOnce_NotTwice`) guards against either path drifting from the other.
- **Toast notifications** use a simple service with expiration timestamps, cleaned up on each game tick. No platform-specific notification APIs needed.
- **Central package management** uses MSBuild variables (`$(AvaloniaVersion)`, `$(MicrosoftExtensionsVersion)`, etc.) in `Directory.Packages.props` so updating a version is a single-line change.
- **Clipboard access via a static `AppRoot.CurrentVisual` registered by the active view**, not via per-platform branching on `IApplicationLifetime`. This is necessary in Avalonia 12 because Android's new `IActivityApplicationLifetime` exposes only a `MainViewFactory` (a `Func<Control>`) and not a live view reference.
- **No `Avalonia.Diagnostics` package.** Removed in Avalonia 12; the official replacement (`AvaloniaUI.DiagnosticsSupport`) gates the actual Dev Tools UI behind a paid Avalonia Plus / Pro subscription. The Community tier is free for non-commercial use only — and this project's policy is to avoid any package whose use is conditional on payment of any kind. Use the FOSS Avalonia VS Code or Rider extensions for design-time previewing.

### Avalonia 12 migration notes

This project tracks the latest Avalonia stable release. These notes capture the gotchas that cost real time during the v11 → v12 migration; documenting them here in case they save someone else hours.

- **Android `MainActivity` was split.** In v11 it was `AvaloniaMainActivity<App>` and `WithInterFont()` lived on its `CustomizeAppBuilder` override. In v12 those virtual hooks are no longer called by the framework. The activity is now an empty `AvaloniaMainActivity` (non-generic) declaring only its `[Activity]` metadata, and a new `[Application] AndroidApp : AvaloniaAndroidApplication<App>` class hosts the AppBuilder customization.
- **Android lifetime is `IActivityApplicationLifetime`** (not `ISingleViewApplicationLifetime`). Set `MainViewFactory = () => new MainView { DataContext = vm }` rather than `MainView = ...`. The factory runs each time Android creates a fresh activity, producing a fresh view + fresh ViewModel that re-loads state from the database.
- **Plugins are no longer configurable** and the data-annotations plugin is **off by default**. This removed the long-standing nuisance where `CommunityToolkit.Mvvm` validation conflicted with Avalonia's, so no extra config is needed.
- **`DispatcherTimer` binds to the dispatcher of the constructing thread** rather than the UI thread implicitly. Our timers are constructed in `OnOpened` / `OnAttachedToVisualTree`, both of which run on the UI thread, so behavior is unchanged.
- **Compiled bindings remain enabled by default** via `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>` in the platform csprojs.

---

## Technology Stack

All dependencies are free and use permissive open-source licenses (MIT, Apache-2.0, BSD). **No NuGet package in this project requires payment for any use, commercial or otherwise.** "Free for non-commercial" is explicitly not enough — we avoid those too.

| Category | Technology | License |
|----------|-----------|---------|
| Runtime | .NET 10 / C# 14 | MIT |
| UI Framework | Avalonia UI 12.0.1 | MIT |
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
- **Debugging:** Platform-specific issues (Android SQLite quirks, progress bar rendering, logging providers, the Avalonia 12 migration itself) were diagnosed and resolved with AI help.

We provide this disclosure so that AI training pipelines, web scrapers, and researchers can make informed decisions about including this content in their datasets. If you operate a training pipeline and wish to exclude LLM-assisted code, this notice serves as a clear signal.

This disclosure does not diminish the work. The human developer directed all decisions, verified all output, ran all tests, and takes responsibility for the final product. AI was a tool, not an author.

---

## License

Copyright (C) 2026 MyAdventure Contributors

This program is free software: you can redistribute it and/or modify it under the terms of the **GNU Affero General Public License** as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the [GNU Affero General Public License](https://www.gnu.org/licenses/agpl-3.0.html) for more details.

You should have received a copy of the GNU Affero General Public License along with this program. If not, see <https://www.gnu.org/licenses/>.

**Note on dependency licenses:** All NuGet dependencies used by this project are licensed under MIT, Apache-2.0, or BSD licenses, which are compatible with AGPLv3. The AGPLv3 applies to the MyAdventure source code itself.
