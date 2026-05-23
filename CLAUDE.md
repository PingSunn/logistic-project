# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run
dotnet run --project CargoFit/CargoFit.csproj

# Run all tests (with readable per-product dump output)
dotnet test CargoFit.Tests/CargoFit.Tests.csproj --logger "console;verbosity=detailed"

# Run a single test by name fragment
dotnet test CargoFit.Tests/CargoFit.Tests.csproj --filter "FullyQualifiedName~<TestName>"

# Release (bump version + tag + push → triggers GitHub Actions)
./scripts/release.sh patch   # 1.0.0 → 1.0.1
./scripts/release.sh minor   # 1.0.0 → 1.1.0
./scripts/release.sh major   # 1.0.0 → 2.0.0
```

## Architecture

Avalonia 11 desktop application (`CargoFit/`) targeting `net10.0`.

### Entry / Shell
- **Entry point**: `CargoFit/Program.cs` — calls `VelopackApp.Build().Run()` first (must be first line), then bootstraps `App` via `AppBuilder`
- **App**: `App.axaml` / `App.axaml.cs` — applies `FluentTheme`, runs license check, loads data, creates `MainWindow`
- **Main window**: `MainWindow.axaml` / `MainWindow.axaml.cs` — single-page app; loads `PlanningView` on startup. Has two status banners (trial expiry + update available). Settings opens `SettingsWindow` in a dialog `Window`.

### Planning
- **`PlanningView.cs`** — code-behind UserControl (no AXAML); two-column layout: left = isometric canvas + stats (layer-cut slider, wireframe/color/dimension toggles, reset view), right = container selector, product search/checklist, quantity inputs, calculate button. Calls `PackingEngine.Calculate`, then `StatsCalculator.ComputeRows` to populate the stats panel.
- **`PackingEngine.cs`** — pure static class; implements the 6-phase packing algorithm (Primary → Balancing → LayerBalancing → PartialRemoval → Condo → ScatteredTopPlacement). No Avalonia/UI dependency. Entry point: `PackingEngine.Calculate(container, requests)` → `PackingOutput`. The final Scatter phase places residual leftovers (boxes that fit in neither primary nor condo) on top of the **same product's** primary stacks, shortest-first, continuing the A/B flip sequence; never tops one product's stack with another's.
- **`StatsCalculator.cs`** — pure static class; computes per-product stats (packed, requested, full stacks, mixed, condo, scatter) and CBM utilisation from a `PackingOutput`.

### Isometric Canvas
- **`IsometricCanvas.cs`** — custom `Control`; renders `BoxPlacement` records using `IsometricProjection`. Owns camera state (`CameraState` inner struct: azimuth, elevation, zoom, drag). Supports layer-cut ratio, wireframe mode, color-by-layer, color-by-stack-layer, hidden products.
- **`IsometricProjection.cs`** — `readonly struct`; encapsulates azimuth/elevation/zoom → screen projection math. `Project(x,y,z)` maps world coords to `Point`. `GetCorners` returns all 8 box corners.
- **`CanvasLabelRenderer.cs`** — static class; all annotation drawing: layer labels, stack-width brackets, direction badges (ประตู/ในสุด), edge dimension labels, info card, hint overlays. Takes `IsometricProjection` for all projections.

### Settings
- **`SettingsWindow.axaml` / `SettingsWindow.axaml.cs`** — sidebar-nav shell (~25 lines); routes "Container" / "Product" tabs to the two panels below.
- **`ContainerSettingsPanel.cs`** — code-behind-only `UserControl`; CRUD for `ContainerSpec` list with import/export JSON.
- **`ProductSettingsPanel.cs`** — code-behind-only `UserControl`; CRUD for `ProductSpec` list with `LayerPatternEditor`, CBM display, import/export CSV/JSON.
- **`LayerPatternEditor.cs`** — `UserControl` for editing a `LayerSection[]` pattern. Each `LayerSection` holds one or more `SectionSubRow` records (rows × cols × rotated). Sections are placed left-to-right within a layer. Fires `PatternChanged` event.

### License System
- **`LicenseManager.cs`** — stateful singleton; Ed25519-signed token validation, per-machine binding, 30-min background heartbeat. Cache stored at `AppPaths.DataDir/license-cache.json`.
- **`LicenseConfig.cs`** — `ServerUrl` + `ServerPublicKeyBase64` (edit before each client build).
- **`LicenseWindow.axaml`** — token entry dialog shown on first run or license loss.

### Update System (Velopack)
- **`UpdateService.cs`** — `CheckAsync()` checks GitHub Releases (`PingSunn/cargofit`) for newer version; `ApplyUpdateAsync()` downloads + restarts. Both are safe no-ops in dev mode.
- **`MainWindow.axaml.cs`** — calls `UpdateService.CheckAsync()` on startup; shows `UpdateBanner` (blue) if update found; `UpdateButton_Click` calls `ApplyUpdateAsync()`.

### Models
- **`PackingModels.cs`** — `BoxPlacement` record (position, size, product index, rotation flag, stack index, layer index).
- **`ContainerSpec.cs`** — record with nominal + interior dimensions; loads/saves `containers.json`; Thai container names (ตู้สั้น, ตู้ยาว, ตู้ไฮคิวบ์).
- **`ProductSpec.cs`** — record with box dimensions, weight, pack size, `PatternA`/`PatternB` layer patterns, `MaxLayers`; loads/saves `products.json`.
- **`LayerSection` / `SectionSubRow`** — records for packing pattern; `SubRows` overrides legacy `Rows`/`Cols`/`Rotated` fields.

### Shared Utilities
- **`AppPaths.cs`** — resolves data directory: repo root in dev (finds `.git`), `%LocalAppData%/CargoFit/` in release (survives Velopack updates). Exposes `ContainersFile`, `ProductsFile`.
- **`JsonOptions.cs`** — shared `JsonSerializerOptions.WriteIndented` instance.
- **`ThemeColors.cs`** — shared `SolidColorBrush` constants (Surface, BorderLight, Ink, InkMuted, InkFaint, AccentBg/Border/Text, Success, Danger, BoxNormal, BoxRotated).

### Project Settings
- **Output type**: `WinExe`
- **Version**: defined in `<Version>` in `CargoFit.csproj` — single source of truth; bumped by `scripts/release.sh`
- **Bindings**: `AvaloniaUseCompiledBindingsByDefault=true` — use compiled bindings (`x:DataType`) not reflection-based ones
- **Nullable**: enabled — all code must be null-safe
- **InvariantGlobalization**: true — no locale-sensitive formatting
- **Data files**: `containers.json`, `products.json` — at repo root in dev; at `%LocalAppData%/CargoFit/` in release

## Release Workflow

```
feat/xxx  →  PR  →  merge to main  →  (สะสมหลาย feature)  →  ./scripts/release.sh
```

Always tag from `main`. Never tag from a feature branch.

```bash
git checkout main && git pull
./scripts/release.sh minor   # bumps version, commits, tags, pushes
# GitHub Actions builds osx-arm64 + osx-x64 + win-x64 → GitHub Release
```

Installed users see update banner on next app launch → click to update.

## UI Language

All UI text is in **Thai**. Labels, buttons, headings, and status messages must be Thai strings.

## Project Domain

This app plans **container-based freight/shipping logistics** — user selects a container type, picks products with quantities, and the app calculates and visualises a 3D isometric packing arrangement.

### Test Harness
- **`CargoFit.Tests/`** — xUnit project; references main project via `InternalsVisibleTo`.
- **`PackingEngineTests.cs`** — 13 hermetic scenarios (inline `ContainerSpec`/`ProductSpec`, no file I/O). Covers single product, no-pattern product, multi-product balancing, large qty, tall boxes in HC, small qty, realistic 3-product load, multi-product 20ft variants, the DevPreset (Mogu 1000ML + Mogu 320ML 20ft), and two scatter-phase scenarios (cross-product invariant + minimal single-product placement).
- **`TestHelpers.cs`** — `DumpOutput()` prints container interior, CBM utilisation, and per-product table (requested / primary / condo / scatter / has-pattern). Container factories use `Gap=10` matching `containers.json`.
- All test data is inline — no dependency on `containers.json` / `products.json`.
- **Workflow**: run tests before and after any `PackingEngine.cs` change; diff the dump to verify behaviour changed as intended.

## Task Management

1. **Verify Plan**: Check in before starting implementation
2. **Track Progress**: Mark items complete as you go
3. **Explain Changes**: High-level summary at each step

If you wondering something, feel free to ask me for clearly

## Model Assignment Rules

- Architecture decisions and reviews: Use Opus
- Implementation tasks (new features, refactors): Use Sonnet
- Simple edits, formatting, renaming: Use Haiku
- Security-sensitive changes: Always escalate to Opus for review
