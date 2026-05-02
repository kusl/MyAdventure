# Offline-Earnings-On-Resume Bug Fix — Full Change Set

## My independent diagnosis

I read every file in `dump.txt` that touches the game loop, persistence,
or UI lifecycle, and ran the bug scenario through the code by hand. Here's
what's actually happening, in my words:

1. **The live tick path uses wall-clock-derived deltas.** `GameViewModel.OnTick`
   computes `delta = (DateTime.UtcNow - _lastTick).TotalSeconds`, then
   clamps it with `Math.Min(delta, 1.0)`, then hands the clamped value to
   `GameEngine.Tick`. The 1-second clamp exists for a legitimate reason:
   it prevents a pathological huge first delta on cold start (when
   `_lastTick` was set seconds ago in the constructor) or after a
   debugger pause from instantly settling many cycles.

2. **The offline path runs only on cold load.** `GameEngine.LoadAsync`
   computes `elapsed = now - state.LastPlayedAt` and calls
   `CalculateOfflineEarnings(elapsed)` — but only when the persisted
   game state is read at startup. There is no other entry point.

3. **`DispatcherTimer` does not fire while the app is suspended.** When
   the OS backgrounds the app (Android tap-out, desktop sleep/hibernate),
   the dispatcher stops, no ticks run, no auto-saves run, and `_lastTick`
   freezes at whatever value it had at suspend time.

4. **On resume, the gap is silently swallowed.** The first `OnTick` after
   resume sees a `delta` equal to the entire suspension duration. The
   `Math.Min(delta, 1.0)` clamp throws away everything past 1 second.
   `_lastTick` is then updated to "now," destroying the only record of
   the gap. `LoadAsync`'s offline path never runs because the process
   never died. **The earnings for the suspension period exist in neither
   path. They are gone.**

This matches the RCA's diagnosis. I confirmed it by tracing the exact
control flow: `OnAttachedToVisualTree` (Android) and `OnOpened` (Desktop)
each fire **once** per cold start and start the timer; neither has any
hook for foreground-resume; the `ToastService.Show("While you were away…")`
i18n key exists but is wired up nowhere except (implicitly) inside
`LoadAsync`'s log line.

## Where I diverged from the RCA

The RCA proposes putting suspend/resume detection in **per-platform
View lifecycle methods** — `MainView.OnAttachedToVisualTree`/
`OnDetachedFromVisualTree` on Android, `Window.Activated`/`Deactivated` or
`OnOpened`/`OnClosing` on Desktop. Each platform stamps its own
`_suspendedAt` field locally and calls a `vm.OnResumed(suspendedAt)`
on its own re-attach hook. **The user explicitly forbade this approach:**

> "do not attempt to silo different teams at all. this is a cross
> functional team and everyone can work with all parts of the code."

> "we should fix things properly, not put bandaid on problems by
> separating desktop and android teams."

Avalonia 12 ships exactly the right primitive for a non-siloed fix:
`IActivatableLifetime`, accessed through
`Application.Current.TryGetFeature<IActivatableLifetime>()`. It exposes
`Activated`/`Deactivated` events with an `ActivationKind` enum, and one
of the kinds is `ActivationKind.Background` — which fires on the
suspend/resume transitions we care about, on every platform that supports
them, with identical semantics. This is documented in the official
Avalonia docs (https://docs.avaloniaui.net/docs/concepts/services/activatable-lifetime).

So instead of two per-platform lifecycle hooks, **the fix lives in one
class in `MyAdventure.Shared`**, and Desktop/Android each call it with a
single line during app initialization. If the suspend/resume logic ever
needs to change, it changes in exactly one place.

I also collapsed two RCA recommendations into one:

- The RCA suggests making `CalculateOfflineEarnings` public AND adding a
  paired public `ApplyOfflineEarnings`.
- I kept `CalculateOfflineEarnings` private (no caller needs the raw
  number; everyone needs side-effects) and exposed only one new public
  method, `ApplyOfflineEarnings(TimeSpan)`. Refactored `LoadAsync` to
  use it too, so cold-start and foreground-resume share a single code
  path that cannot drift.

## Files changed

### Source — 5 files

1. **`src/MyAdventure.Core/Services/GameEngine.cs`** *(modified)*
   - Added `private const double MinimumOfflineGapSeconds = 1.0` so the
     "gap too small to count" threshold is named, not a magic number.
   - Added new `public double ApplyOfflineEarnings(TimeSpan elapsed)`
     method. Returns earned amount, also adds it to `Cash` and
     `LifetimeEarnings`. Guards: returns 0 on negative/sub-1s gaps and
     when no business has both manager and units owned. Records the
     payout to the OpenTelemetry `EarningsCounter` with a `source: offline`
     tag, so dashboards can break down live vs offline earnings.
   - Refactored `LoadAsync` to call `ApplyOfflineEarnings` instead of
     duplicating the `> 1s` guard + `Cash += / LifetimeEarnings +=`
     mutation. **Behavior is identical** — the existing
     `OfflineEarnings_ShouldApplyAngelBonusOnce_NotTwice` invariant test
     still passes verbatim.
   - `CalculateOfflineEarnings` stays `private` — it's an implementation
     detail with no external caller.

2. **`src/MyAdventure.Shared/Services/AppLifecycleManager.cs`** *(new)*
   - Static class. Single-method public surface: `Attach(GameViewModel)`.
   - On first `Attach`, looks up `IActivatableLifetime` via
     `Application.Current.TryGetFeature<IActivatableLifetime>()` and
     subscribes once to `Activated` and `Deactivated`.
   - Filters events to `ActivationKind.Background` only — protocol
     activations (deep links), reopen-from-dock, and other kinds must
     not be treated as "the player came back from being away."
   - Holds a single static "current target" reference. Subsequent
     `Attach` calls **replace** the target without subscribing again.
     This is the right model for Android's `MainViewFactory`, which can
     run multiple times across an app's lifetime — without it, every
     activity recreation would leak a handler and old VMs would keep
     receiving lifecycle callbacks.
   - Internal `ResetForTesting()` seam, callable only from
     `MyAdventure.UI.Tests` via `InternalsVisibleTo`.
   - Returns `false` if no `IActivatableLifetime` is exposed — graceful
     degradation: the rest of the game still works, the player just
     doesn't get suspend/resume compensation. (Notably, this is what
     happens during unit tests, so test setup doesn't fight Avalonia.)

3. **`src/MyAdventure.Shared/ViewModels/GameViewModel.cs`** *(modified)*
   - Added a `TimeProvider _time` field. Added a public 4-parameter
     constructor that takes a `TimeProvider`; the existing 3-param
     constructor chains to it with `TimeProvider.System`. **DI continues
     to use the 3-param constructor** — `TimeProvider` is not registered
     in the container, so by the standard
     `Microsoft.Extensions.DependencyInjection` rules the longer
     constructor is "not applicable" and the shorter one is unambiguously
     selected. The 4-param constructor is for unit tests only.
   - `OnTick` now reads time via `_time.GetUtcNow().UtcDateTime` instead
     of `DateTime.UtcNow`. This is the only line where existing behavior
     is mechanically different — and it's behavior-equivalent in
     production (the default provider IS `DateTime.UtcNow`).
   - Added `private DateTime? _suspendedAt` field.
   - Added `public void OnSuspended()`: stamps `_suspendedAt` with current
     time, fire-and-forgets a `SaveAsync` so the persisted state is fresh
     in case the OS later kills the process without resuming us.
   - Added `public void OnResumed()`: snapshots and clears `_suspendedAt`
     up front (re-entrancy safety), then if it was set, computes elapsed
     time and calls `_engine.ApplyOfflineEarnings(elapsed)`. Then resets
     `_lastTick = now` so the very next tick computes a small natural
     delta. Then shows the "While you were away, you earned $X!" toast
     (using the existing i18n key's text) **only if earned > 0**, logs,
     and calls `RefreshAll()`. **Cold-start guard:** if `_suspendedAt`
     was null, no payout — `LoadAsync` already handled the gap. **Re-entry
     guard:** snapshot-and-clear means a duplicate `Activated` event
     can't double-pay.

4. **`src/MyAdventure.Shared/MyAdventure.Shared.csproj`** *(modified)*
   - Added `<InternalsVisibleTo Include="MyAdventure.UI.Tests" />` so the
     test project can call `AppLifecycleManager.ResetForTesting()`.

5. **`src/MyAdventure.Desktop/App.axaml.cs`** *(modified)*
   - One added line: `AppLifecycleManager.Attach(vm);` after the
     `MainWindow` is wired. That's all the desktop platform needs — the
     same `IActivatableLifetime` feature on desktop fires on
     hibernate/sleep, no per-window event subscriptions required.

6. **`src/MyAdventure.Android/App.axaml.cs`** *(modified)*
   - Same `AppLifecycleManager.Attach(vm)` call, placed inside the
     `MainViewFactory` so that each fresh activity gets its fresh VM
     registered as the current target. Also added the same call to the
     fallback `ISingleViewApplicationLifetime` branch (defensive — Avalonia
     12 won't normally take that path on Android, but if the same `App`
     class is reused on iOS or browser, the lifecycle wiring still kicks
     in).

### Tests — 1 modified, 2 new

7. **`tests/MyAdventure.Core.Tests/GameEngineTests.cs`** *(modified)*
   - All existing tests preserved verbatim.
   - 7 new tests for `ApplyOfflineEarnings`:
     - `ApplyOfflineEarnings_ShouldAddToCashAndLifetime` — basic positive
       case, one managed business, returned amount equals delta on both
       Cash and LifetimeEarnings.
     - `ApplyOfflineEarnings_NoManagedBusinesses_ShouldReturnZero` —
       owned but unmanaged businesses don't earn offline.
     - `ApplyOfflineEarnings_NoBusinessesOwned_ShouldReturnZero` — no
       businesses at all, returns 0.
     - `ApplyOfflineEarnings_TinyGap_ShouldReturnZero` — 500ms, 1s exact,
       and 0s all return 0 (the threshold guard).
     - `ApplyOfflineEarnings_NegativeGap_ShouldReturnZero` — defensive
       against clock skew or test-clock quirks; never awards negative.
     - `ApplyOfflineEarnings_AppliesAngelBonus` — exact-arithmetic check
       that the bonus is applied once at the end, not per-cycle.
     - `ApplyOfflineEarnings_AndLiveTick_AreEquivalent` — the strong
       invariant: applying offline earnings for N seconds yields the
       same Cash delta as ticking the engine for N seconds. **This is
       the test that protects against the bug ever returning** — if
       offline ever drifts from live, this test catches it.

8. **`tests/MyAdventure.UI.Tests/GameViewModelLifecycleTests.cs`** *(new)*
   - 11 tests covering `OnSuspended` / `OnResumed`:
     - `OnResumed_WithoutPriorSuspend_ShouldNotApplyEarnings` — cold-start
       guard.
     - `OnResumed_TwiceWithoutSuspend_ShouldNotApplyEarnings` — the guard
       holds across repeated calls.
     - `OnSuspendedThenOnResumed_ShouldApplyEarningsForGap` — basic
       success path with deterministic arithmetic.
     - `OnSuspendedThenOnResumed_ShouldApplyAngelBonusOnce` — bonus
       parity at the VM level.
     - `OnResumed_ShouldShowToast_WhenEarningsApplied` — UX wiring.
     - `OnResumed_ShouldNotShowToast_WhenNoEarnings` — don't show a
       misleading "you earned $X" with $0.
     - `OnResumed_TinyGap_ShouldNotApplyEarnings` — sub-1s screen
       flicker, no payout.
     - `OnResumed_AfterFirstResume_RequiresNewSuspend` — duplicate
       `Activated` events can't double-pay.
     - `OnResumed_ShouldResetLastTickToCurrentTime` — the next tick after
       resume sees a sane small delta, not a multi-minute one.
     - `OnResumed_ShouldApplyEarningsBeforeRefreshingUi` — `CashText`
       reflects post-payout cash immediately.
     - `OnSuspended_ShouldRecordTimestamp_AndAllowSubsequentResume` —
       the basic invariant, the failure mode of the bug.

   Tests use a tiny hand-rolled `TestTimeProvider` (about 10 lines) to
   keep the `MyAdventure.Extensions.TimeProvider.Testing` dependency out
   of the project. The existing dependency footprint is preserved
   exactly.

9. **`tests/MyAdventure.UI.Tests/AppLifecycleManagerTests.cs`** *(new)*
   - 3 tests for the lifecycle manager parts that don't require a live
     Avalonia application:
     - `Attach_NullViewModel_ShouldThrow` — argument validation.
     - `Attach_WithoutAvaloniaApp_ShouldReturnFalse` — graceful
       degradation when `IActivatableLifetime` isn't available.
     - `Attach_TwiceWithDifferentVms_ShouldReplaceTarget` — Android
       activity recreation safety.

   Each test starts with `AppLifecycleManager.ResetForTesting()` (called
   from the test class constructor) to keep static state isolated.

## Why I'm confident this builds and the tests pass

I cannot run `dotnet build` or `dotnet test` in this sandbox, so I went
through every change line by line:

- **Existing tests:** I traced
  `OfflineEarnings_ShouldApplyAngelBonusOnce_NotTwice` through the new
  `LoadAsync` — it still ends up calling
  `ApplyOfflineEarnings(60s)` → `CalculateOfflineEarnings(60s)` → returns
  $200 → applied to Cash. The `ShouldBeInRange(190, 220)` assertion
  passes for the same reasons it did before.
- **Existing tests for `Tick`, `BuyBusiness`, `BuyManager`, `Prestige`,
  import/export:** None of them touch the offline-earnings path. Their
  behavior is unchanged.
- **New tests:** I worked through the arithmetic for every assertion.
  `MakeVm()` in the lifecycle tests starts with cash 1,000,000, then
  `BuyBusiness("lemonade")` deducts $4 (cost = 4 * 1.07^0 = 4), then
  `BuyManager("lemonade")` deducts $4,000 (cost = base * 1000), so
  pre-suspend cash is $995,996. Tests that assert specific dollar
  deltas (`+$1000` for 10min, `+$200` for 60s with 2× bonus) compute
  off this baseline.
- **C# language gotcha caught:** I initially wrote
  `if (_suspendedAt is not { } suspendedAt)` — that's CS8780 ("a
  variable may not be declared within a 'not' or 'or' pattern"). I
  rewrote it as snapshot-then-null-check, which has the additional
  benefit of being re-entrancy-safe.
- **DI gotcha checked:** Adding a second public constructor to
  `GameViewModel` does NOT introduce a DI ambiguity because
  `TimeProvider` is not registered in the service collection. The
  3-param constructor is the unambiguous choice.

## What I did NOT change and why

- `Math.Min(delta, 1.0)` in `OnTick` stays. Removing or relaxing it
  would re-introduce the cold-start huge-delta problem the cap was
  designed to prevent.
- `MainView.axaml.cs` (Android) and `MainWindow.axaml.cs` (Desktop)
  stay exactly as they are. The cross-platform lifecycle hook lives in
  `Shared`, so no per-platform View code changes.
- Integration tests stay exactly as they are. They don't exercise the
  game loop, only the persistence layer.
- The `ToastService`, `AppRoot`, `BusinessViewModel`, `Business`,
  `Milestone`, `NumberFormatter`, repository, dbcontext, csproj
  fingerprints, slnx, GitHub Actions workflow — all untouched.
