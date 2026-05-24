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

Each additional unit you buy costs more (`base cost × multiplier^owned`). Revenue scales linearly with units owned, then gets multiplied by milestone bonuses, speed bonuses, post-milestone scaling (past unit 1000), the cross-business speed bonus, and your angel-investor bonus — all compounding together.

### Core Mechanics

- **Click to Run:** Click the ▶ RUN button on a business to start its production cycle. When the progress bar fills, you collect revenue.
- **Buy Units:** Purchase additional units of any business with the BUY button (one at a time).
- **Buy to Milestone / Buy Max:** Each business card shows a bulk-buy button. While a next revenue milestone exists it reads "BUY N→threshold" and buys exactly the units needed to reach it in one click. Once all milestones are reached at 1000 owned, it switches to "BUY MAX (N)" and buys as many units as you can currently afford. The button is always visible — it never disappears past 1000 owned.
- **Hire Managers:** Each business can have a manager (costs 1000× the business's base cost). Managers automatically restart production cycles so you don't have to click.
- **Offline Earnings:** When you close the game and come back, all businesses with managers earn revenue for the time you were away, boosted by your angel investor bonus and the cross-business speed multiplier.

### Revenue Milestone Multipliers

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

### Speed Milestones

Owning certain quantities of a business also halves its production cycle time. These are separate from revenue milestones and compound multiplicatively with them:

| Units Owned | Effect | Cumulative Speed |
|-------------|--------|-----------------|
| 25 | ×2 Speed | ×2 |
| 50 | ×2 Speed | ×4 |
| 100 | ×2 Speed | ×8 |
| 200 | ×2 Speed | ×16 |
| 300 | ×2 Speed | ×32 |
| 400 | ×2 Speed | ×64 |

At 400 owned the effective cycle time is divided by 64 compared to the base. For lemonade (0.6s base), that's a 9.375ms effective cycle — faster than a single 60Hz frame. The game engine handles this correctly: it counts how many complete cycles accumulated in the current tick and pays all of them out at once, carrying the fractional remainder into the next tick. The `SubFrameCycleTests` suite pins this behavior.

The speed-milestone cap at ×64 is deliberate: cycle time is stored as a `double`, and halving it more than ~1000 times would underflow to zero in IEEE 754, causing a divide-by-zero crash. Instead, the **cross-business speed bonus** (see below) provides unlimited scaling beyond that ceiling as a `BigDouble` revenue multiplier rather than a cycle-time divisor.

### Cross-Business Speed Bonus

This is the **third compounding axis** in the game, on top of revenue milestones and per-business speed milestones. When *every* business simultaneously reaches a shared ownership threshold, every business earns an additional ×2 revenue multiplier. Because it requires the minimum owned count across all six businesses to climb, it rewards balanced play: a player with 1000 lemonade stands and zero shrimp boats gets exactly zero cross-business bonus.

Thresholds: 25, 50, 100, 200, 300, 400, then every +100 forever (500, 600, 700, 800, ...). There is no cap. At minimum-owned = 400, the cross-business multiplier is ×64. At minimum-owned = 1000, it is ×4096 (`2^(6 + (1000-400)/100)` = `2^12`). At 10,000 it is `2^102` ≈ 5×10³⁰. The game never runs out of things to do.

The UI shows your current cross-business multiplier in the header (e.g. "×64 Speed" — the same label AdCap uses) and a "N more of every business → threshold" hint showing which threshold you're approaching.

**Why a revenue multiplier rather than a cycle-time divisor?** Halving cycle time 1000+ times underflows `double` to exactly zero. The cross-business effect is therefore folded into a `BigDouble` revenue multiplier — mathematically identical from the player's perspective (halving cycle time doubles earnings per second) but numerically robust at any scale.

### Post-1000 Revenue Scaling

1000 is the revenue milestone cap. Buying past it used to mean every new unit cost `1.07^N` more than the previous but contributed no more revenue per unit than unit 1000 — eventually each new lemonade stand cost trillions and paid back in centuries. **Post-1000, revenue is now multiplied by `CostMultiplier^((Owned − 1000) / 2)`** — the square root of the cost growth — so unit 1001 is roughly as cost-efficient as unit 1000, and the late game keeps moving instead of stalling. Below 1000 owned this multiplier is exactly 1.0, so all early-game balance is unchanged.

### Prestige System

Prestiging resets all businesses and cash (to **$5**, not $0 — enough to immediately buy the first lemonade stand) in exchange for **Angel Investors**. Each angel provides a permanent **+2% revenue bonus that compounds**, applied to both live cycle earnings and offline earnings.

The "compounds" part is what matters. Under a strictly linear "+2% per angel" rule, 50 angels would be ×2.00 and 700 would be ×15.00 — the curve flattens out and a marginal prestige stops feeling worth it. Under compounding (`1.02 ^ angels`), 50 angels is ×2.69, 200 is ×52.5, and 700 is ×750,000+. Each prestige genuinely makes the next run feel like a different game.

The number of angels your current lifetime earnings are worth is:

```
floor(150 × sqrt(lifetimeEarnings / 1e13))
```

The **PRESTIGE** button unlocks whenever that number is at least one greater than your current angel count — i.e. when prestiging right now would give you at least one new angel. **The UI is the signal:** when the button lights up, you can prestige. The formula is there to explain the shape of the curve, not for you to compute thresholds in your head.

**Lifetime earnings are preserved through prestige** — each subsequent prestige requires more lifetime earnings than the last to net the same number of new angels, but the running total never resets. After prestiging, your cash resets to **$5** so you can immediately get back to clicking. Prestige is optional, but the angel bonus compounds and makes subsequent runs dramatically faster.

**The angel cap is gone.** Angels are now stored as `BigDouble` (see the BigDouble section below). You can accumulate astronomically many angels without hitting any ceiling — the `AngelBonus` computation has a saturation cap at `10^MaxAngelBonusExponent` to prevent downstream overflow, but in practice this is only reachable by hand-editing a save to an absurd value.

### BigDouble: Unbounded Numbers

All monetary values (`Cash`, `LifetimeEarnings`, `AngelInvestors`), and the `NextCost`, `Revenue`, and `RevenuePerSecond` properties of each business are `BigDouble` — a custom floating-point struct with a `double` mantissa and a `long` base-10 exponent. This gives an effective range of roughly `±9.99 × 10^(long.MaxValue)`, which is for practical purposes unlimited.

**Why BigDouble?** An idle game that compounds aggressively will routinely exceed `double.MaxValue` (~1.8 × 10³⁰⁸). Before the BigDouble migration, cash capped silently at `1e200` and stopped growing. BigDouble removes that ceiling. The `Cash_AtFormerCap_ContinuesToGrow` test pins the regression: cash at exactly the old cap must continue increasing, not stall.

**Persistence format:** BigDouble values are stored in SQLite as TEXT strings in canonical form: `"<mantissa>e<exponent>"` (e.g. `"4.25e1"` for 42.5, `"1.5e200"` for 1.5×10²⁰⁰). The conversion is handled by an EF Core `ValueConverter` registered in `AppDbContext.OnModelCreating`. The `GameEngine` works exclusively in `BigDouble` and is unaware of the string form.

**Migration from old saves:** The `DependencyInjection.InitializeDatabaseAsync` method detects old databases that still have `Cash`, `LifetimeEarnings`, and `AngelInvestors` as REAL columns and translates them in-place to the new `CashText`, `LifetimeEarningsText`, `AngelInvestorsText` TEXT columns in a single transaction. Your old save is preserved exactly — the migration is idempotent and a crash mid-migration leaves the old schema intact.

### Save Compatibility

Saves from all earlier versions of MyAdventure remain valid. The v2 export format stores numbers as canonical BigDouble strings (`"4.25e1"`). The import function transparently handles both the legacy v1 format (numbers as native JSON doubles) and the current v2 format, so any save file you have will load.

The two balance changes (compound angel bonus, post-1000 revenue scaling, cross-business speed bonus) are computed on the fly from existing fields, so reopening an old save just shows the new, more rewarding multipliers applied to the angels and units the player already had.

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

All tests (unit, integration, UI/ViewModel) are designed to run fast after every change. No external services, emulators, or running applications are required — the test suite is fully headless.

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

Once a business hits 1000 owned, no further milestone multipliers unlock — the revenue milestone table caps out at the cumulative ×327,680. Without intervention, that creates a wall: every new unit costs `1.07^N` more than the one before, but pays back the same per-unit revenue, so each new lemonade stand becomes exponentially less worthwhile until "the next one" costs trillions and pays back in centuries.

To keep buying past the cap meaningful, **post-1000 revenue is multiplied by `CostMultiplier^((Owned − 1000) / 2)`**. That's the square root of how fast the cost grows, so unit 1001 is roughly as cost-efficient as unit 1000 was, and unit 5000 stays in the same payback ballpark instead of drifting off to infinity. The math is invisible below 1000 — it's exactly 1.0 — so nothing about the early or middle game changes.

The "BUY MAX (N)" button — which shows how many units you can buy right now — uses an O(1) geometric-series closed form, so buying 50,000 lemonade stands at once takes the same time as buying 5.

### The late game: prestige early, prestige often

At some point the **PRESTIGE** button on the top bar lights up. That's the signal: prestiging right now would net you at least one new Angel Investor. Press it. Your cash and businesses reset, your angel count goes up, and **lifetime earnings are not reset** — so the next prestige starts the clock partway through.

The angel-investor formula is `floor(150 × sqrt(lifetime_earnings / 1e13))`, where `lifetime_earnings` is the cumulative all-time total (not per-run). Three practical consequences of the square root:

- **Diminishing returns on waiting.** Doubling lifetime earnings only multiplies your angel count by ~1.41×. Quadrupling it doubles your angels. Waiting "one more order of magnitude" is rarely the right call.
- **Each angel compounds at +2%.** With 50 angels you have a ×2.69 multiplier across every business, every milestone, every cycle (not the ×2.00 you'd get from a flat "+2% × 50"). With 200 angels it's ×52.5. With 700 angels it's around ×750,000. The compounding is what makes the late game feel like real progress instead of asymptotic stagnation.
- **The threshold to unlock the button creeps up each run.** It depends on your current angel count, not on a fixed dollar value — the UI does the math for you and shows the projected new angels next to the button.

Rule of thumb: prestige whenever you'd at least **double your current angel count**, or as soon as the button unlocks if it's your first time. Don't agonize over it. Prestige is a checkpoint, not a sacrifice.

### The very late game: cross-business bonus

Once all businesses have managers and you've done a few prestiges, a third axis of scaling opens up: the **cross-business speed bonus**. Every time the minimum-owned count across all six businesses crosses a shared threshold (25, 50, 100, 200, 300, 400, then every 100 forever), every business gets another ×2 revenue multiplier.

Unlike the per-business milestones, the cross-business bonus is **uncapped** — it climbs forever as long as you keep buying the least-owned business. At 400 of each it's ×64. At 1000 of each it's ×4096. At 10,000 of each it's `2^102` ≈ 5×10³⁰. The game literally never runs out of progression.

The strategic implication is clear: if you have 1000 lemonade stands but only 200 shrimp boats, your lowest business is what gates the cross-business bonus. Balanced ownership pays. The UI tells you exactly which threshold you're approaching and how many of every business you need to get there.

### Offline earnings work — use them

Close the game. Walk away. Come back tomorrow. Every business with a manager will have earned revenue for the entire interval you were gone, boosted by your angel bonus and the cross-business speed multiplier. The math is identical to live play: `cycles × revenue × angel_bonus × cross_business_multiplier`. There is no offline cap and no offline penalty. Sleeping is a viable strategy.

When you reopen the app, a toast notification tells you how much you earned while you were away ("While you were away, you earned $X!"). If the gap was less than one second, it's below the minimum threshold and no payout is shown — this avoids spurious toasts for screen flickers or brief app switches.

The only caveat: offline earnings only count businesses with managers. A business sitting at 200 units with no manager produces nothing while you're away. Buy the manager.

### Modding your save

Export your game, decode the Base64 string (any Base64 decoder works, or use `echo '<string>' | base64 -d` on Linux/macOS), and you'll see JSON like:

```json
{
  "v": 2,
  "cash": "4.25e1",
  "lifetime": "1e3",
  "angels": "0",
  "prestige": 0,
  "businesses": {"lemonade": 3, "newspaper": 0, "carwash": 0, "pizza": 0, "donut": 0, "shrimp": 0},
  "managers": {"lemonade": false, "newspaper": false, "carwash": false, "pizza": false, "donut": false, "shrimp": false},
  "timestamp": "2026-05-23T14:30:00.0000000Z"
}
```

Edit whatever you want, re-encode to Base64 (`echo '<json>' | base64` on Linux/macOS), and import it back. Give yourself cash, 1000 shrimp boats, enable all managers, set angels to 9999 — it's your game.

A few things to know:

- **`v: 2`** is the current save format version. The import function also accepts the legacy `v: 1` format where numbers were plain JSON numbers instead of strings — old saves still load.
- **`cash`, `lifetime`, and `angels`** are BigDouble canonical strings: `"<mantissa>e<exponent>"`. You can also use plain numbers like `42.5` or scientific notation like `1e18` — the importer accepts both. There is no ceiling: `"1e500"` is valid and will work correctly.
- **`angels`** — because the angel bonus is `1.02^angels`, values compound dramatically. `"5e2"` (500 angels) is ×19,956. `"1e4"` (10,000 angels) is astronomical. There is no hard cap in the engine; the `AngelBonus` getter saturates to a very large (but finite) value at extreme angel counts to protect downstream arithmetic.
- **Business and manager keys** must match the IDs exactly: `lemonade`, `newspaper`, `carwash`, `pizza`, `donut`, `shrimp`.
- **The business count value** (e.g. `"lemonade": 3`) is the unit count. Setting it past 1000 doesn't unlock additional revenue milestones — 1000 is the revenue milestone cap — but the cumulative ×327,680 multiplier still applies, and post-1000 revenue gets multiplied by `1.07^((owned-1000)/2)` so each extra unit stays meaningful.
- **The `timestamp` field** is included for debugging only. Two exported saves plus their timestamps can reveal offline-earnings bugs (if five days passed and cash didn't change, something is wrong). The importer ignores it entirely — it is not validated and not enforced.
- The save is **not signed or checksummed** — there's no anti-cheat. We don't think there's anyone to cheat against.

If you import garbage and the game looks strange, reset to a fresh save:

- **Desktop:** delete `{LocalApplicationData}/MyAdventure/myadventure.db`. (On Windows that's `%LOCALAPPDATA%\MyAdventure\`; on Linux, `~/.local/share/MyAdventure/`; on macOS, `~/.local/share/MyAdventure/`.)
- **Android:** clear the app's data via your device's Settings → Apps → MyAdventure → Storage → Clear data. (Reinstalling the APK alone doesn't wipe the save; data lives in the app's private internal storage.)

### Frequently confusing things

- **"My revenue went down after prestige!"** Yes — you reset all businesses to zero owned. The angel bonus compensates over time. Because the bonus compounds, even ~50 angels gives you a ×2.69 multiplier — you'll blow past your previous earnings rate within minutes.
- **"My progress bar isn't moving."** The business is probably not running. Click RUN once to start it; if it has a manager, it should auto-restart on the next cycle.
- **"I have a manager but I'm not earning anything."** You need to own at least one unit *and* the business must be running. Click RUN once to kick it off; the manager handles every cycle after that.
- **"I closed the game for 8 hours and earned barely anything."** Check that your most profitable businesses had managers. Offline earnings ignore unmanaged businesses entirely.
- **"The numbers are getting weird (Qa, Sx, O, N, D)."** Those are abbreviations for quadrillion, sextillion, octillion, nonillion, decillion. The formatter handles up to 10³⁶ with suffixes; past that it switches to scientific notation with Unicode superscript exponents (`7.53 × 10⁴⁰`). You have not broken the game — this is the intended display for very large values.
- **"My cash was stuck at 1e200 in an old version."** That was a real bug — the old `double`-based engine silently capped at 1e200. The fix was the BigDouble migration. If you have an old save from before that migration, simply open it in the new build: the database migration runs automatically and your progress is preserved.

### Honest expectations

This is a small idle game built primarily as a learning vehicle for Avalonia and as a sample for native cross-platform .NET apps. The game itself is **complete but minimal**. There is no event system, no daily reward, no random business generator, no leaderboard, no social features. It's the loop: buy, click, milestone, manager, prestige, repeat.

If that's what you're looking for, welcome. If you wanted Adventure Capitalist with permission, well, this is what an LLM and a determined developer made on a weekend.

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
- **Angel bonus is compounded, not linear.** `AngelBonus = BigDouble(1.02).Pow(angelCount)` — each angel multiplies revenue by 1.02 on top of the previous angel's contribution. The same value is applied identically to live and offline earnings: `GameEngine.Tick()` multiplies per-cycle revenue by `AngelBonus`, and `CalculateOfflineEarnings()` multiplies the offline total by `AngelBonus` once. The invariant test (`OfflineEarnings_ShouldApplyAngelBonusOnce_NotTwice`) guards against either path drifting from the other. The bonus is capped at `10^MaxAngelBonusExponent` to prevent the `long` exponent from overflowing at truly absurd angel counts.
- **Cross-business speed bonus is a `BigDouble` revenue multiplier, not a cycle-time divisor.** Halving cycle time hundreds of times underflows `double` to zero; folding the entire cross-business effect into a `BigDouble` multiplier sidesteps that completely. Revenue can grow without bound because `BigDouble` has no practical ceiling.
- **Post-1000 revenue scaling** lives on `Business.PostMilestoneScaling` and equals exactly `1.0` for `Owned <= 1000` (preserving every pre-cap balance number and test) and `CostMultiplier^((Owned - 1000) / 2.0)` past the cap. The square root of cost growth keeps unit 1001+ purchases roughly as efficient as unit 1000.
- **Toast notifications** use a simple service (`ToastService`) with expiration timestamps, cleaned up on each game tick via `CleanupExpired()`. No platform-specific notification APIs needed. Toast lifetime defaults to 3 seconds; tests can pass `TimeSpan.Zero` to get immediately-expirable toasts.
- **Central package management** uses MSBuild variables (`$(AvaloniaVersion)`, `$(MicrosoftExtensionsVersion)`, `$(EfCoreVersion)`, `$(OpenTelemetryVersion)`, `$(XunitVersion)`) in `Directory.Packages.props` so updating a version is a single-line change.
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
| UI Framework | Avalonia UI 12.0.3 | MIT |
| MVVM | CommunityToolkit.Mvvm 8.4.2 | MIT |
| Database | SQLite via EF Core 10.0.8 | MIT |
| Observability | OpenTelemetry 1.15.3 | Apache-2.0 |
| Optional error tracking | Sentry (via OTLP HTTP — no Sentry SDK) | n/a (free tier) |
| Unit Testing | xUnit 2.9.3 | Apache-2.0 |
| Assertions | Shouldly 4.3.0 | BSD |
| Mocking | NSubstitute 5.3.0 | BSD |
| Test Data | Bogus 35.6.5 | MIT |
| Coverage | Coverlet 10.0.1 | MIT |

### Modern .NET Practices

- **Central package management** via `Directory.Packages.props` — all NuGet versions defined in one place using MSBuild variables for grouped version updates. Updating all Avalonia packages is a one-line change to `$(AvaloniaVersion)`.
- **Shared build configuration** via `Directory.Build.props` — target framework (`net10.0`), `ImplicitUsings`, `Nullable enable`, `LangVersion latest`, versioning (driven by `$(BuildNumber)` from CI), deterministic builds, `TreatWarningsAsErrors` in Release.
- **Solution file** uses the new `.slnx` XML format (one solution for the whole repo, no per-platform `.sln`s).
- **C# 14 features** including primary constructors, records, collection expressions, `required` properties, and global usings.
- **Compiled bindings** enabled by default in Avalonia (`AvaloniaUseCompiledBindingsByDefault`).
- **`InternalsVisibleTo`** used in `MyAdventure.Shared.csproj` to give `MyAdventure.UI.Tests` access to `AppLifecycleManager.ResetForTesting()` without over-exposing internals to production code.
- **Global `using Xunit;`** injected by `tests/Directory.Build.props` so test files don't need per-file using directives for xUnit attributes.
- **`global.json`** pins the SDK to `10.0.100` with `rollForward: latestMinor` so local builds and CI always use a compatible SDK version; `allowPrerelease: false` keeps things stable.

---

## Tests

The test suite runs fully headless — no emulators, no running applications, no external services. `dotnet test` from the repo root runs everything.

### Test Projects

**`MyAdventure.Core.Tests`** — unit tests for the domain layer.

- `BigDoubleTests` — construction/normalization, arithmetic (add/subtract/multiply/divide), exponentiation (including past `double` overflow range), sqrt, log10, comparison, string round-tripping, implicit conversions, Floor, and edge cases (NaN, Infinity, zero, negative).
- `BusinessAffordableTests` — `AffordableCount` closed-form calculation including huge-cash edge cases.
- `BusinessTests` — `NextCost`, `Revenue`, `PostMilestoneScaling`, `MilestoneMultiplier`, `SpeedMultiplier`, `CycleTimeSeconds` at various owned counts including extreme values past 1000.
- `CrossBusinessSpeedBonusTests` — `BonusCount`, `CalculateSpeedMultiplier`, `NextThreshold`, `UnitsToNext` at all threshold boundaries and into the open-ended ladder past 400.
- `GameEngineTests` — the most comprehensive suite: `LoadAsync`, `Tick`, `StartBusiness`, `BuyBusiness`, `BuyManager`, `BuyMultiple`, `BuyMax`, `Prestige`, `CalculateAngels`, `AngelBonus`, `ApplyOfflineEarnings`, `ExportToString`/`ImportFromString` (including the legacy v1 format), and invariant tests for offline-earnings equivalence with live ticks.
- `MilestoneTests` — `CalculateMultiplier`, `NextMilestone`, `UnitsToNext`.
- `NumberFormatterTests` — suffix formatting (K, M, B, T, Qa, Qi, Sx, Sp, O, N, D), scientific notation past 10³⁶, `BigDouble` overload including values at 10⁵⁰⁰ and 10⁵⁰⁰⁰, infinity/NaN display.
- `SpeedMilestoneTests` — `CalculateCycleTimeMultiplier`, `CalculateSpeedMultiplier`, `NextSpeedMilestone` at all threshold boundaries.
- `SubFrameCycleTests` — invariants that guarantee sub-frame cycles (effective cycle time < 16ms) pay out correctly without being lost or double-counted.

**`MyAdventure.Integration.Tests`** — tests that hit the real EF Core + SQLite stack.

- `GameStateRepositoryTests` — round-trip save/load for all fields including BigDouble string columns; extreme-magnitude BigDouble values; upsert behaviour (single row kept, `CreatedAt` never changes across saves).
- `SchemaMigrationTests` — verifies that the legacy REAL-column schema (pre-BigDouble migration) is correctly translated to the new TEXT-column schema in place, without data loss, in a single transaction.

**`MyAdventure.UI.Tests`** — ViewModel and service tests. No Avalonia UI host needed; all tests run in a standard xUnit process.

- `AppLifecycleManagerTests` — `Attach` with and without an active Avalonia `Application`, replaceable target on repeated `Attach` calls, `ResetForTesting` isolation.
- `BusinessViewModelSpeedTests` — `SpeedMultiplierText`, `HasSpeedBonus`, `HasNextSpeedMilestone`, `NextSpeedMilestoneText` at every speed threshold; cross-business multiplier applied correctly to the displayed revenue.
- `BusinessViewModelTests` — `Refresh` with various cash and angel bonus combinations; affordability flags; bulk-buy button visibility and label transitions ("BUY N→threshold" vs. "BUY MAX (N)" past 1000 owned); `CanBulkBuy` dims on zero affordable rather than vanishing.
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




