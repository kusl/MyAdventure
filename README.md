# MyAdventure

[![Build](https://github.com/kusl/MyAdventure/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/kusl/MyAdventure/actions/workflows/build-and-release.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

An **Adventure Capitalist** clone built with **Avalonia UI** and **.NET 10** (C# 14).
Cross-platform idle/clicker game with polished UI, big bold buttons, and rich progression.

## Downloads

| Platform | Architecture | Download |
|----------|--------------|----------|
| Windows | x64 | [Download](https://github.com/kusl/MyAdventure/releases/latest) |
| Windows | ARM64 | [Download](https://github.com/kusl/MyAdventure/releases/latest) |
| Linux | x64 | [Download](https://github.com/kusl/MyAdventure/releases/latest) |
| Linux | ARM64 | [Download](https://github.com/kusl/MyAdventure/releases/latest) |
| macOS | x64 (Intel) | [Download](https://github.com/kusl/MyAdventure/releases/latest) |
| macOS | ARM64 (Apple Silicon) | [Download](https://github.com/kusl/MyAdventure/releases/latest) |
| Android | APK | [Download](https://github.com/kusl/MyAdventure/releases/latest) |

## Quick Start

```bash
dotnet restore
dotnet build
dotnet run --project src/MyAdventure.Desktop
```

## Run Tests

```bash
dotnet test
```

## Architecture

- **MyAdventure.Core** — Domain entities, interfaces, game engine logic
- **MyAdventure.Infrastructure** — EF Core SQLite, repositories, telemetry
- **MyAdventure.Shared** — ViewModels, converters, localization resources
- **MyAdventure.Desktop** — Avalonia desktop app (Windows/Linux/macOS)
- **MyAdventure.Android** — Avalonia Android app

## Technology

- .NET 10 / C# 14 with central package management
- Avalonia UI 11.3.12
- SQLite via EF Core
- OpenTelemetry for logging and metrics
- xUnit + Shouldly + NSubstitute for testing
- All dependencies MIT/Apache-2.0/BSD licensed

## License

MIT License — Free for any use, forever.
