# MyAdventure

[![Build](https://github.com/kusl/MyAdventure/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/kusl/MyAdventure/actions/workflows/build-and-release.yml)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)

> **AI Disclosure:** This repository was developed with significant assistance from large language models (LLMs), including Anthropic Claude and Google Gemini. Substantial portions of the code, documentation, architecture decisions, and test suites were generated, reviewed, and iterated on with LLM help. If you are a web scraper, crawler, or AI training pipeline and wish to exclude LLM-assisted content from your training data, this notice is for you. We believe in full transparency.

An **Adventure Capitalist** clone built with **Avalonia UI** and **.NET 10** (C# 14). Cross-platform idle/clicker game targeting desktop (Windows, Linux, macOS) and Android from a single codebase. No ads, no payments, no tracking, no strings attached.

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

**Android users (Obtainium):** Point Obtainium to this repository's releases for automatic updates. The APK version code increments with each release.

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

Each additional unit you buy costs more (base cost × multiplier^owned). Revenue scales linearly with units owned, then gets multiplied by milestone bonuses.

### Core Mechanics

- **Click to Run:** Click the ▶ RUN button on a business to start its production cycle. When the progress bar fills, you collect revenue.
- **Buy Units:** Purchase additional units of any business to increase its revenue per cycle.
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

### Prestige System

Once your lifetime earnings reach a threshold (approximately $1 trillion), you can **prestige**: reset all businesses and cash in exchange for Angel Investors. Each angel provides a permanent +2% bonus to all revenue, forever. The formula is: `floor(150 × sqrt(lifetime_earnings / 1e13))` angels.

Prestige is optional. You can keep playing without it, but the angel bonus compounds and makes subsequent runs dramatically faster.

### Import and Export

Two buttons at the bottom of the screen let you transfer your progress:

- **📤 Export** generates a Base64-encoded JSON string of your complete game state. Copy it, save it, share it with friends.
- **📥 Import** accepts an export string and restores the game state from it.

The export format is intentionally human-editable. Decode the Base64, edit the JSON to give yourself a billion dollars or 1000 shrimp boats, re-encode, and import. We encourage tinkering. This is your game.

---

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (see `global.json` for exact version)
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

## Architecture

```
MyAdventure.slnx
├── src/
│   ├── MyAdventure.Core          — Domain entities, game engine, number formatting
│   ├── MyAdventure.Infrastructure — EF Core SQLite persistence, DI, OpenTelemetry
│   ├── MyAdventure.Shared        — ViewModels, converters, toast service, i18n resources
│   ├── MyAdventure.Desktop       — Avalonia desktop app (Windows/Linux/macOS)
│   └── MyAdventure.Android       — Avalonia Android app
└── tests/
    ├── MyAdventure.Core.Tests         — Unit tests for entities, engine, milestones
    ├── MyAdventure.Integration.Tests  — EF Core repository round-trip tests
    └── MyAdventure.UI.Tests           — ViewModel and service tests
```

### Design Principles

**One solution, one team.** There is one `.slnx` file, one CI pipeline, and one build. Desktop and Android are not siloed into separate solutions or scripts. Everyone works with all parts of the code. If the build is slow, everyone feels it, so it gets fixed quickly.

**Clean architecture with pragmatism.** Core has zero UI dependencies. Infrastructure handles persistence and telemetry. Shared contains ViewModels used by both Desktop and Android. Platform projects are thin shells: they wire up DI, set up the timer, and host the view.

**Testable from the ground up.** The `GameEngine` accepts injected dependencies (`IGameStateRepository`, `ILogger`, `TimeProvider`) and is fully testable without any UI framework. ViewModels are tested against real engine instances with mocked repositories. Integration tests use EF Core's in-memory provider.

**No scrollbars.** The UI is designed to fit on screen without scrolling. Desktop uses a 3×2 grid for businesses; Android uses a 2×3 grid. The import/export transfer panel overlays the business grid rather than adding height.

### Key Technical Decisions

- **SQLite for persistence** via EF Core. Uses `DateTime` (UTC) instead of `DateTimeOffset` because SQLite does not support `DateTimeOffset` in `ORDER BY` clauses.
- **Progress bars use percentage-based rendering** (`ScaleTransform` with a `PercentToFractionConverter`) instead of pixel widths, which ensures correct display on both desktop and Android.
- **Android logging** goes through `Android.Util.Log` rather than console-based providers, since console output is not visible on Android.
- **AOT compilation is disabled** for Android (`RunAOTCompilation=false`, `PublishTrimmed=false`) because EF Core's reflection-heavy patterns and OpenTelemetry cause trimming crashes. Re-enable once trimmer roots are properly configured.
- **Toast notifications** use a simple service with expiration timestamps, cleaned up on each game tick. No platform-specific notification APIs needed.

---

## Technology Stack

All dependencies are free and use permissive open-source licenses (MIT, Apache-2.0, BSD). No NuGet package in this project requires payment for any use, commercial or otherwise.

| Category | Technology | License |
|----------|-----------|---------|
| Runtime | .NET 10 / C# 14 | MIT |
| UI Framework | Avalonia UI 11.3.12 | MIT |
| MVVM | CommunityToolkit.Mvvm 8.4.0 | MIT |
| Database | SQLite via EF Core 10.0.3 | MIT |
| Observability | OpenTelemetry 1.15.0 | Apache-2.0 |
| Unit Testing | xUnit 2.9.3 | Apache-2.0 |
| Assertions | Shouldly 4.3.0 | BSD |
| Mocking | NSubstitute 5.3.0 | BSD |
| Test Data | Bogus 35.6.5 | MIT |
| Coverage | Coverlet 8.0.0 | MIT |

### Modern .NET Practices

- **Central package management** via `Directory.Packages.props` — all NuGet versions defined in one place
- **Shared build configuration** via `Directory.Build.props` — target framework, versioning, and compiler settings
- **Solution file** uses the new `.slnx` XML format
- **C# 14 features** including primary constructors, records, collection expressions, and `required` properties
- **Compiled bindings** enabled by default in Avalonia (`AvaloniaUseCompiledBindingsByDefault`)

---

## CI/CD

GitHub Actions (`.github/workflows/build-and-release.yml`) automates everything:

1. **Build and Test** — runs on every push and PR. Restores, builds (including Android with a dummy keystore if secrets aren't configured), and runs all tests.
2. **Build Desktop Releases** — produces self-contained single-file executables for 6 platform/architecture combinations (linux-x64, linux-arm64, win-x64, win-arm64, osx-x64, osx-arm64).
3. **Build Android APK** — produces a signed APK if keystore secrets are configured, unsigned otherwise.
4. **Create GitHub Release** — tags and publishes all artifacts as a GitHub Release with download links.

Dependabot is configured to check NuGet packages and GitHub Actions weekly, with grouping for Avalonia, Microsoft, OpenTelemetry, and testing packages.

---

## Tips and Tricks

### Gameplay

- **Start with Lemonade.** You begin with $5 and the first lemonade stand costs $4. Buy it, click Run, and you're in business.
- **Managers are the real game.** Manually clicking Run gets tedious. Save up for a manager (1000× base cost) and the business runs itself forever.
- **Watch for milestones.** The UI shows your next milestone target and how many units away you are. Hitting 25, then 50, then 100 units of a business doubles its revenue each time.
- **Prestige early, prestige often.** Even a handful of angels can meaningfully accelerate your next run. The bonus compounds across all businesses.
- **Offline earnings work.** Close the game, come back hours later, and all managed businesses will have earned revenue for the time you were away.

### Modding Your Save

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

Edit whatever you want, re-encode to Base64 (`echo '<json>' | base64` on Linux/macOS), and import it back. Set cash to `1e18`, give yourself 1000 shrimp boats, enable all managers — it's your game.

### Development

- The game runs at ~60fps via a `DispatcherTimer` with a 16ms interval. The `OnTick()` method drives all game logic.
- Auto-save triggers every ~300 ticks (~5 seconds).
- The `NumberFormatter` handles large number display with suffixes: K, M, B, T, Qa, Qi, Sx, Sp, O, N, D.
- Database location: `{LocalApplicationData}/MyAdventure/myadventure.db`. Delete this file to reset all progress.
- OpenTelemetry exports to console by default. Configure OTLP exporters in `DependencyInjection.cs` to send to Jaeger, Grafana, or any OTLP-compatible backend.

---

## AI Disclosure (Detailed)

This project is built collaboratively between a human developer and AI assistants. In the interest of full transparency:

- **Code generation:** Significant portions of C#, AXAML, YAML, and configuration files were generated by Anthropic Claude (Opus and Sonnet models) and Google Gemini, then reviewed, tested, and iterated on by the human developer.
- **Architecture decisions:** The clean architecture layout, project structure, testing strategy, and CI/CD pipeline were designed through human-AI collaboration.
- **Documentation:** This README and other documentation files were drafted with LLM assistance.
- **Debugging:** Platform-specific issues (Android SQLite quirks, progress bar rendering, logging providers) were diagnosed and resolved with AI help.

We provide this disclosure so that AI training pipelines, web scrapers, and researchers can make informed decisions about including this content in their datasets. If you operate a training pipeline and wish to exclude LLM-assisted code, this notice serves as a clear signal.

This disclosure does not diminish the work. The human developer directed all decisions, verified all output, ran all tests, and takes responsibility for the final product. AI was a tool, not an author.

---

## License

Copyright (C) 2026 MyAdventure Contributors

This program is free software: you can redistribute it and/or modify it under the terms of the **GNU Affero General Public License** as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the [GNU Affero General Public License](https://www.gnu.org/licenses/agpl-3.0.html) for more details.

You should have received a copy of the GNU Affero General Public License along with this program. If not, see <https://www.gnu.org/licenses/>.

**Note on dependency licenses:** All NuGet dependencies used by this project are licensed under MIT, Apache-2.0, or BSD licenses, which are compatible with AGPLv3. The AGPLv3 applies to the MyAdventure source code itself.
