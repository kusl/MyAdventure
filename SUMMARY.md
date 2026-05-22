# MyAdventure — BigDouble migration

## What this delivers

Switches the game's money/angel system from `double` (capped at 1e200 in
the engine) to a `BigDouble` struct (no practical ceiling), plus three
UI fixes you asked for.

## Files included (64 total)

### New files
- `src/MyAdventure.Core/Numerics/BigDouble.cs` — mantissa-double + long-exponent struct, ~520 lines
- `tests/MyAdventure.Core.Tests/BigDoubleTests.cs` — ~430 lines of unit tests
- `tests/MyAdventure.Integration.Tests/SchemaMigrationTests.cs` — verifies the v1→v2 SQLite migration

### Significantly changed
- `src/MyAdventure.Core/Entities/Business.cs` — `NextCost`, `Revenue`, `PostMilestoneScaling`, `RevenuePerSecond` are now `BigDouble`; `AffordableCount` uses the closed-form geometric series (O(1)) instead of the prior 10,000-step loop
- `src/MyAdventure.Core/Entities/GameState.cs` — `Cash`/`LifetimeEarnings`/`AngelInvestors` are now `*Text` string columns
- `src/MyAdventure.Core/Services/GameEngine.cs` — `Cash`, `LifetimeEarnings`, `AngelInvestors`, `AngelBonus` are `BigDouble`; all 1e200/1e90/1e9 clamps removed; new `BuyMax(businessId)` method; export uses v2 (string numbers), import accepts both v1 and v2
- `src/MyAdventure.Core/Services/NumberFormatter.cs` — new `Format(BigDouble)` overload, suffix table indexed by exponent, scientific notation extended to `long` exponents
- `src/MyAdventure.Infrastructure/Data/AppDbContext.cs` — schema updated for the renamed columns
- `src/MyAdventure.Infrastructure/DependencyInjection.cs` — in-place SQLite ALTER TABLE migration: detects the legacy REAL columns and lifts them into the new TEXT columns; idempotent
- `src/MyAdventure.Infrastructure/Repositories/GameStateRepository.cs` — copies the new *Text fields
- `src/MyAdventure.Shared/ViewModels/BusinessViewModel.cs` — `Refresh` takes `BigDouble` for cash and angel bonus; new `BulkBuyCommand`/`BulkBuyText`/`CanBulkBuy` properties that switch between "BUY N→milestone" and "BUY MAX (N)" automatically
- `src/MyAdventure.Shared/ViewModels/GameViewModel.cs` — lifted to `BigDouble` throughout; snapshots cash + angel bonus once per refresh so all businesses see the same values
- `src/MyAdventure.Desktop/Views/MainWindow.axaml` — cash text wrapped in `Viewbox` (auto-shrinks to fit); prestige column right-aligned; bulk-buy button bound to `BulkBuyText`/`CanBulkBuy` (always visible)
- `src/MyAdventure.Android/Views/MainView.axaml` — same three UI fixes for the phone layout

### Test files (all updated)
- `BusinessAffordableTests.cs` — uses `BigDouble`, adds a "huge cash" test
- `BusinessTests.cs` — uses `BigDouble`, adds an "extreme owned" test
- `GameEngineTests.cs` — ~600 lines, full coverage lifted to `BigDouble`; new tests for the user's exact symptom (cash at 1e200 continues to grow), BuyMax, v1-format import, schema-migrated saves
- `NumberFormatterTests.cs` — adds `BigDouble` overload tests including values at 10^500 and 10^5000
- `MilestoneTests.cs` — unchanged
- `BusinessViewModelTests.cs` — adds tests proving the bulk-buy button stays visible after milestone 1000
- `GameViewModelLifecycleTests.cs` — lifted to `BigDouble`
- `ToastServiceTests.cs`, `AppLifecycleManagerTests.cs` — unchanged
- `GameStateRepositoryTests.cs` — uses BigDouble strings, adds extreme-magnitude round-trip test
- `SchemaMigrationTests.cs` — new: verifies the legacy-REAL → new-TEXT migration runs in-place

### Project-level (unchanged or minimally changed)
- `MyAdventure.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `global.json` — same as before
- `MyAdventure.Core.csproj`, `MyAdventure.Infrastructure.csproj`, `MyAdventure.Shared.csproj`, `MyAdventure.Desktop.csproj`, `MyAdventure.Android.csproj` — unchanged
- All Shared services and converters (`AppRoot.cs`, `AppLifecycleManager.cs`, `ToastService.cs`, `GameConverters.cs`, `ViewModelBase.cs`) — verbatim copies for completeness
- All Android/Desktop startup files (`App.axaml.cs`, `Program.cs`, etc.) — verbatim copies for completeness

### Files NOT included (intentionally — they don't need to change)
- `.gitattributes`, `.gitignore`, `.github/dependabot.yml`, `.github/workflows/build-and-release.yml`
- `LICENSE`, `README.md`, `docs/KEYSTORE.md`

## How to apply

The tarball mirrors the existing project layout. Copy its contents over your
working tree:

```bash
cd ~/src/dotnet/MyAdventure
tar xzf myadventure-bigdouble-migration.tar.gz --strip-components=1
time dotnet clean
time dotnet restore
time dotnet build
time dotnet test
```

The first run will perform a one-time SQLite schema migration on your
existing save (the v1 REAL columns get translated to the new TEXT
columns in place — your cash, lifetime, angels, businesses, and managers
are all preserved). After that the game can grow past 10^200 freely.

## What this fixes (your three asks)

1. **"Stuck at 1e200"** — gone. Cash, lifetime, angels, NextCost, Revenue
   are all `BigDouble` with effectively unbounded range. The
   `Cash_AtFormerCap_ContinuesToGrow` test pins this regression.

2. **"Buy max button shouldn't disappear at 1000 owned"** — fixed. The
   bulk-buy button now stays visible at every ownership level. Below 1000
   it says "BUY N→threshold"; at/past 1000 it says "BUY MAX (N)" where N
   is the affordable count. The `Refresh_AllMilestonesReached_BulkBuyButtonStaysVisibleAsBuyMax`
   test pins this.

3. **"1.00 × 10^200 is too big"** — fixed. Cash text on both Desktop and
   Android is wrapped in a `Viewbox` with `StretchDirection="DownOnly"`,
   so the text auto-shrinks to fit available width on any display while
   keeping its natural size when there's room.

4. **"Prestige button should be right aligned"** — fixed. Both views now
   place the Prestige button in a right-aligned column with
   `HorizontalAlignment="Right"` on both the button and its explanation text.

## Sanity-check checklist

- All FOSS NuGet packages (MIT/Apache/BSD); `Avalonia.Diagnostics` and `AvaloniaUI.DiagnosticsSupport` are not referenced anywhere
- Single `MyAdventure.slnx`; no per-platform solution files
- No `build-desktop.sh` / `build-android.sh`; the existing `commands.txt` recipe (`dotnet build`, `dotnet test`) still works
- Tests cover the new BigDouble behavior, the buy-max button, the schema migration, and the user's exact "stuck at 1e200" symptom
- OpenTelemetry telemetry preserved; the only change is that `EarningsCounter.Add` now takes `earned.ToDouble()` (saturating at `double.MaxValue` is fine for graphs)
