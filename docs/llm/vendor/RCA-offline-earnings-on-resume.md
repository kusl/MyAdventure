# Root Cause Analysis: Silent Earnings Loss on App Resume

**Project:** MyAdventure (github.com/kusl/myadventure)  
**Affected platforms:** Android (primary), Desktop (secondary, rare)  
**Fix approach:** Option A — Apply offline earnings on foreground resume  

---

## 1. The Bug in Plain Terms

When the player puts MyAdventure in the background (switches apps, locks phone, etc.) and comes back, they lose whatever earnings *would have accrued during that suspension*. The game does not crash. It does not show an error. It silently discards up to tens of minutes of income. A player who walks away for 20 minutes and comes back to a running session gets nothing — not even the offline earnings path fires, because the game was never fully closed.

---

## 2. Architecture Context

Understanding the bug requires understanding two separate earnings paths that exist in the codebase:

### 2a. Live Tick Path (foreground)

`MainWindow.axaml.cs` (Desktop) and `MainView.axaml.cs` (Android) each create a `DispatcherTimer` at 16ms intervals (~60fps). On every tick they call `GameViewModel.OnTick()`:

```csharp
// GameViewModel.cs
public void OnTick()
{
    var now = DateTime.UtcNow;
    var delta = (now - _lastTick).TotalSeconds;
    _lastTick = now;

    delta = Math.Min(delta, 1.0);  // ← THE PROBLEM

    _engine.Tick(delta);
    RefreshAll();
    ...
}
```

`GameEngine.Tick(deltaSeconds)` advances each business's `ProgressPercent` proportionally to `deltaSeconds / CycleTimeSeconds`, collects revenue when progress reaches 100%, and applies the angel bonus. This is wall-clock–based and correct in isolation.

### 2b. Offline Earnings Path (on cold load)

`GameEngine.LoadAsync()` reads `LastPlayedAt` from the saved `GameState` and calls `CalculateOfflineEarnings(elapsed)` for businesses that have managers. This correctly pays out the full elapsed time on next app launch.

```csharp
// GameEngine.cs — LoadAsync
var elapsed = _time.GetUtcNow().UtcDateTime - state.LastPlayedAt;
if (elapsed.TotalSeconds > 1)
{
    var offlineEarnings = CalculateOfflineEarnings(elapsed);
    Cash += offlineEarnings;
    LifetimeEarnings += offlineEarnings;
}
```

### 2c. The Gap Between the Two Paths

The offline path only runs inside `LoadAsync`, which is only called from `InitializeAsync`, which is only called from `OnAttachedToVisualTree` (Android) or `OnOpened` (Desktop). These hooks fire **once per cold start**, not on every resume.

There is **no foreground-resume hook** anywhere in the codebase.

---

## 3. The Exact Failure Sequence

1. Player opens the app. `InitializeAsync` → `LoadAsync` runs. Offline earnings are correctly applied for any prior closure period. `_lastTick` is set to `DateTime.UtcNow`.

2. Player uses the app normally. `DispatcherTimer` fires ~60 times/second. Each tick, `delta` is ~0.016s. Everything is correct.

3. Player presses the Home button (Android) or switches away. The OS suspends the process. **The `DispatcherTimer` stops firing.** No ticks occur. `_lastTick` is frozen at the moment of suspension.

4. Player returns to the app. The OS resumes the process. The `DispatcherTimer` fires again.

5. **First tick after resume:** `now - _lastTick` reflects the actual elapsed suspension time — potentially minutes. For example, after 10 minutes away, `delta` would be `600.0` seconds.

6. `delta = Math.Min(delta, 1.0)` clamps it to **1.0 second**. The remaining 599 seconds of elapsed time is silently discarded.

7. `_lastTick` is updated to `now`. The frozen timestamp is gone. There is no record of the suspension period. It cannot be recovered.

8. The game was not closed, so `OnDetachedFromVisualTree` / `OnClosing` did not fire. Therefore `SaveAsync` was not called during the suspension. Therefore `LastPlayedAt` in the database still reflects the last *periodic auto-save* (which runs every ~5 seconds via the `_saveCounter` counter in `OnTick`). On the next cold start, `LoadAsync` *would* cover this gap — but only if the app is fully closed and relaunched, not merely resumed.

**Net result:** Earnings for the entire background suspension period are lost. They are not in the live tick path (capped away). They are not in the offline path (never triggered). They do not exist anywhere.

---

## 4. Why the Cap Exists (and Why It Is Wrong Here)

The `Math.Min(delta, 1.0)` cap was added defensively to prevent a pathological first tick. Without it, the very first `OnTick` call after a cold start or debugger attach could have a huge `delta` (e.g., if `_lastTick` is set in the constructor but `OnTick` doesn't fire until several seconds later). Allowing a huge delta into `Tick()` on cold start would cause all businesses to instantly complete many cycles, which looks wrong in the UI.

The cap is **correct and necessary for cold start**. It is **harmful for background resume** because on resume there is no `LoadAsync` to fall back on — the cap simply throws the time away.

---

## 5. Android-Specific Severity

On Android this is particularly acute:

- Android aggressively suspends background apps, especially on low-RAM devices or devices with aggressive battery management (Xiaomi, Samsung with aggressive doze). Suspension can happen within seconds of backgrounding.
- Android does **not** trigger `OnDetachedFromVisualTree` on background — only on activity destroy. So the auto-save's `LastPlayedAt` timestamp drifts behind real time during a suspension where no ticks fire.
- The `IActivityApplicationLifetime` pattern means Android may recreate the activity (calling `InitializeAsync` again) after a long suspension — but only if the process was killed. For short-to-medium suspensions the process stays alive, the timer resumes, and the gap is silently eaten.

On Desktop (Windows/Linux/macOS), process suspension is rare but can occur on some laptops when the lid is closed or the system hibernates.

---

## 6. Secondary Issue: `LastPlayedAt` Drift During Suspension

The auto-save runs every 300 ticks (~5 seconds of active play). When the timer is suspended, no ticks fire, so no auto-saves fire. `LastPlayedAt` in the database freezes at the last pre-suspension save. If the app is later force-killed without detaching the view, `LoadAsync` on next launch will correctly compute offline earnings from that frozen `LastPlayedAt`. However if the app is resumed (not killed) then later closed normally, `OnDetachedFromVisualTree` fires `SaveAsync`, which writes the *current* time as `LastPlayedAt` — including the suspension period as if the app had been running the whole time. This means the next cold launch will compute a *shorter* offline window than the true gap, slightly under-compensating.

This is a secondary issue, not the primary one. The fix for the primary issue (Option A) also mitigates this by updating `_lastTick` correctly on resume, keeping the `LastPlayedAt` written on close more accurate.

---

## 7. What Option A Must Do

Option A is: **detect foreground resume and apply offline earnings for the suspension gap**, using the same `CalculateOfflineEarnings` logic already in `GameEngine`.

To implement this correctly:

### 7a. Extract `CalculateOfflineEarnings` into a public method on `GameEngine`

It is currently `private`. It needs to be callable from outside `LoadAsync` — specifically from `GameViewModel.OnResumed()`. The method signature and logic do not change:

```csharp
// GameEngine.cs — make this public
public double CalculateOfflineEarnings(TimeSpan elapsed)
{
    double total = 0;
    foreach (var biz in Businesses.Where(b => b.HasManager && b.Owned > 0))
    {
        var cycles = elapsed.TotalSeconds / biz.CycleTimeSeconds;
        total += biz.Revenue * cycles;
    }
    return total * AngelBonus;
}
```

Note: the offline earnings path correctly restricts to businesses **with managers** (`HasManager && Owned > 0`). Businesses without managers require player interaction to run cycles and should not earn offline. This restriction must be preserved.

### 7b. Add `OnResumed(DateTime suspendedAt)` to `GameViewModel`

The ViewModel needs a method the View can call when the app comes back to foreground. It should:

1. Compute `elapsed = DateTime.UtcNow - suspendedAt`.
2. Guard against trivially small gaps (< 1 second) to avoid double-counting normal ticks.
3. Call `_engine.CalculateOfflineEarnings(elapsed)` and apply the result to `Cash` and `LifetimeEarnings` via `GameEngine` (not directly, to keep engine state consistent).
4. Reset `_lastTick = DateTime.UtcNow` so the next regular tick does not also count the gap.
5. Show a toast notification matching the existing "While you were away" UX pattern.
6. Call `RefreshAll()` to push updated cash to the UI immediately.

Because the engine's `Cash` and `LifetimeEarnings` are private setters, the cleanest approach is to add a paired public method on `GameEngine`:

```csharp
// GameEngine.cs
public double ApplyOfflineEarnings(TimeSpan elapsed)
{
    var earned = CalculateOfflineEarnings(elapsed);
    if (earned <= 0) return 0;
    Cash += earned;
    LifetimeEarnings += earned;
    logger.LogInformation("Applied resume earnings: {Earned:F2} for {Seconds:F0}s suspended",
        earned, elapsed.TotalSeconds);
    return earned;
}
```

### 7c. Track suspension time in the View layer

The View (platform-specific) is the correct place to know when the app goes to background and foreground. The ViewModel should not contain platform lifecycle logic.

**Android (`MainView.axaml.cs`):**  
Override `OnDetachedFromVisualTree` to record the timestamp (it already stops the timer and saves — add a suspend timestamp), and override a resume hook to call `vm.OnResumed(suspendedAt)`. In Avalonia 12 on Android, `OnAttachedToVisualTree` fires on activity recreation. For same-process resume (activity not destroyed), Avalonia exposes `Application.Current.ActualThemeVariantChanged` and similar events, but the most reliable approach in Avalonia 12 for Android lifecycle is to override `OnAttachedToVisualTree` and compare whether `InitializeAsync` was already run (using a flag on the ViewModel) versus a pure resume.

A simpler and fully reliable approach: store `_suspendedAt = DateTime.UtcNow` when the timer stops in `OnDetachedFromVisualTree`, and call `vm.OnResumed(_suspendedAt)` when the timer restarts in `OnAttachedToVisualTree` — but only if `InitializeAsync` is **not** being called (cold start), since cold start is handled by `LoadAsync`.

**Desktop (`MainWindow.axaml.cs`):**  
The desktop window does not have a meaningful "background" concept in the Android sense, but the same pattern can be applied defensively: if the window is minimized and restored, or the system hibernates, the timer gap is covered. Hook `Window.Activated` / `Window.Deactivated` or simply use the same `OnResumed` path after checking elapsed > threshold.

### 7d. Guard against double-counting with `LoadAsync`

On Android, when the process is fully killed and relaunched, `InitializeAsync` → `LoadAsync` runs and correctly covers the offline gap. `OnResumed` must **not** also run in this case, or earnings would be double-applied. The guard is: `OnResumed` is only called from `OnAttachedToVisualTree` when `_suspendedAt` has a value from a prior `OnDetachedFromVisualTree` in the **same process lifetime**. Cold start never sets `_suspendedAt`, so it can never trigger `OnResumed`.

---

## 8. Files to Modify

| File | Change |
|---|---|
| `src/MyAdventure.Core/Services/GameEngine.cs` | Change `CalculateOfflineEarnings` from `private` to `public`. Add `public double ApplyOfflineEarnings(TimeSpan elapsed)`. |
| `src/MyAdventure.Shared/ViewModels/GameViewModel.cs` | Add `public void OnResumed(DateTime suspendedAt)`. Reset `_lastTick`. Show toast. Call `RefreshAll()`. |
| `src/MyAdventure.Android/Views/MainView.axaml.cs` | Track `_suspendedAt` on detach. Call `vm.OnResumed(_suspendedAt)` on re-attach (resume, not cold start). |
| `src/MyAdventure.Desktop/Views/MainWindow.axaml.cs` | Optionally hook `Window.Activated`/`Deactivated` with the same pattern for hibernate/sleep coverage. |

Tests to add:
- `GameEngine`: `ApplyOfflineEarnings_ShouldAddToCashAndLifetime`
- `GameEngine`: `ApplyOfflineEarnings_ShouldReturnZeroIfNoManagedBusinesses`
- `GameViewModel`: `OnResumed_ShouldNotApplyEarnings_WhenElapsedLessThanOneSecond`
- `GameViewModel`: `OnResumed_ShouldResetLastTick`

---

## 9. What Must Not Change

- The `Math.Min(delta, 1.0)` cap in `OnTick` should be **kept**. It serves a legitimate purpose for cold-start and debugger scenarios. Only the resume gap needs a separate code path — not a loosened cap.
- The offline earnings restriction to manager-owned businesses must be preserved in `CalculateOfflineEarnings`. Non-manager businesses still require player interaction.
- The angel bonus application inside `CalculateOfflineEarnings` (`return total * AngelBonus`) must not change. The invariant that live ticks and offline earnings apply the same angel multiplier is tested and must remain in sync.
- `LoadAsync` offline earnings logic is correct and untouched. The new `ApplyOfflineEarnings` reuses the same underlying `CalculateOfflineEarnings` formula, ensuring parity.
