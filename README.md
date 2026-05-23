# CargoFit

Desktop application for planning and visualising 3D freight packing layouts inside shipping containers.

## Overview

Select a container type, choose products with quantities, and the app calculates an optimised packing arrangement displayed as an interactive isometric 3D view.

**Supported containers**

| Name | Size |
|------|------|
| ตู้สั้น | 20 ft |
| ตู้ยาว | 40 ft |
| ตู้ไฮคิวบ์ | 40 ft HC |

## Tech Stack

- **.NET 10** / **Avalonia 11** desktop (Windows + macOS)
- Custom isometric canvas rendered via Avalonia `Control`
- Pure-logic packing engine with no UI dependency
- xUnit test suite
- **Velopack** — installer + in-app update from GitHub Releases

## Getting Started (Dev)

**Prerequisites**: .NET 10 SDK

```bash
git clone https://github.com/PingSunn/cargofit.git
cd cargofit
dotnet build

# Run
dotnet run --project CargoFit/CargoFit.csproj

# Tests
dotnet test CargoFit.Tests/CargoFit.Tests.csproj --logger "console;verbosity=detailed"
```

Data files (`containers.json`, `products.json`) live at the repo root in dev and at `~/.local/share/CargoFit/` in release builds.

## Features

- **Packing engine** — 6-phase algorithm: Primary → Balancing → LayerBalancing → PartialRemoval → Condo → ScatteredTopPlacement
- **Isometric canvas** — interactive camera (azimuth, elevation, zoom, drag), layer-cut slider, wireframe / colour-by-layer / dimension toggles
- **Per-product statistics** — packed vs requested, full stacks, mixed stacks, condo boxes, scatter boxes, CBM utilisation
- **Product patterns** — define A/B layer patterns per product with rotation support
- **Settings** — CRUD for containers and products with CSV/JSON import-export
- **In-app updater** — checks GitHub Releases on startup; update banner + button (no auto-update)
- **Trial licensing** — Ed25519-signed token, per-machine activation, 30-min heartbeat

## Project Structure

```
CargoFit/               — Avalonia desktop app
  Program.cs            — entry point (Velopack bootstrap + Avalonia)
  Services/
    LicenseManager.cs   — license activation + heartbeat
    UpdateService.cs    — Velopack update check + apply
  Views/
    MainWindow.axaml    — shell with trial & update banners
    PlanningView.cs     — main planning UI
    SettingsWindow.axaml
  Utils/
    AppPaths.cs         — data directory (repo root in dev, AppData in release)
CargoFit.Tests/         — xUnit tests
CargoFit.LicenseServer/ — ASP.NET Core license server
CargoFit.LicenseAdmin/  — admin CLI (issue/revoke tokens)
scripts/
  release.sh            — semantic version bump + tag + push
  publish/              — vpk build scripts (macOS + Windows)
.github/workflows/
  release.yml           — CI: build all platforms → GitHub Release
containers.json         — container specs (dev)
products.json           — product specs (dev)
```

## Release Workflow

```bash
# 1. Merge feature branches to main as usual
git checkout main && git pull

# 2. When ready to release
./scripts/release.sh patch   # bug fix  → 1.0.0 → 1.0.1
./scripts/release.sh minor   # feature  → 1.0.0 → 1.1.0
./scripts/release.sh major   # breaking → 1.0.0 → 2.0.0
```

The script bumps `<Version>` in `CargoFit.csproj`, commits, tags, and pushes.  
GitHub Actions then builds for **macOS arm64**, **macOS x64**, and **Windows x64** in parallel and publishes a GitHub Release automatically.

Installed users will see an update banner on next app launch.

## UI Language

All UI text is in Thai (ภาษาไทย).
