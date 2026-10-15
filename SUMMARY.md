# Change summary: balance redesign, xunit.v3, dependency refresh

## Balance

- Angel bonus is linear: `1 + 0.02 × angels` (was `1.02^angels`, which caused runaway prestige loops).
- `Business.Efficiency` replaces `PostMilestoneScaling`, so revenue tracks `cost^0.49` across the whole range. This removes the hard wall at ~10⁹/s.
- Cross-business bonus is bounded: ×1.5 per threshold at 25/50/100/200/300/400 (max ×11.39). `NextThreshold` returns `null` once maxed.
- Angel divisor raised from 10¹³ to 10¹⁴.
- All tuning constants live in `GameBalance`.
- New engine members: `IncomePerSecond`, `PotentialAngels`, `PrestigeIncomeMultiplier`, `Business.CostOf(n)`.
- `Tick` counts cycles as a floored `double` instead of an `int` cast.
- Prestige panel text shows the income multiplier a prestige would grant.
- New `Balance/` test suite: a deterministic bot plays the real engine and pins pacing bounds. See README "Balance design".

## Tooling

- xunit 2.9.3 → xunit.v3 4.0.2 on Microsoft Testing Platform v2 (`global.json` `test.runner`).
- Removed `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` and the unused `Avalonia.Headless*` entries.
- `coverlet.collector` → `coverlet.MTP` 10.1.0.
- OpenTelemetry 1.19.1 → 1.20.0.
- Test methods pass `TestContext.Current.CancellationToken` (xUnit1051).
- Actions: checkout v7, setup-dotnet v6, setup-java v6, upload-artifact v7, download-artifact v8, action-gh-release v3.

## Apply

```bash
tar xzf myadventure.tar.gz
dotnet clean && dotnet restore && dotnet build && dotnet test
```
