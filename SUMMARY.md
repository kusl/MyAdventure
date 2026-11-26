# Change summary: .NET 11 rewrite, audio and haptic feedback

## Platform

- `net11.0` / `net11.0-android`, C# 15, `global.json` → SDK `11.0.100` (`latestFeature`).
- Android: CoreCLR (Mono is gone in .NET 11), min API 24, RIDs `android-arm`, `android-arm64`, `android-x64` (x86 unsupported).
- Microsoft packages 11.0.0. Removed unused: EF Core Design/InMemory, Logging.Console, Localization, Options.ConfigurationExtensions, OpenTelemetry.Api, Instrumentation.Http, Bogus.
- CI reads the SDK from `global.json`; least-privilege permissions; PR runs cancel superseded runs. Dependabot also tracks the SDK.

## Structure

- One shared `App` and one responsive `MainView` replace the duplicated Desktop/Android App and XAML. Heads are a few lines each.
- Core persists through `GameSnapshot`; JSON and text columns are an Infrastructure concern. Save format and table layout are unchanged.
- Save export/import uses `System.Text.Json` source generation; byte-identical to v2 and still reads v1.
- `IDbContextFactory`, lazy one-shot `DatabaseInitializer`, `SchemaUpgrader` adds `PlayerSettings` to existing databases.
- Static `AppRoot`/`AppLifecycleManager` replaced by `LifecycleRouter` and an injected clipboard.
- Tests use fakes and `internal` setters instead of reflection.

## Fixes

- Traces and metrics were never exported: without a generic host nobody resolved `TracerProvider`/`MeterProvider`. `TelemetryLifecycle.Start` now does.
- Business cards were always rendered at 40% opacity (a `long` was bound to a bool converter).
- Desktop saved in `OnClosing` via `async void`, which could lose the last save. Saving and the final telemetry flush now run on `Exit`.
- Unhandled and unobserved exceptions are logged (and reach Sentry Issues).

## New

- Sound, haptics and press animation on every button, with persisted toggles. See README › Feedback.

## Apply

Delete the files listed in the delivery notes, extract the archive over the repo, then:

```bash
dotnet workload install android
dotnet build && dotnet test
```
