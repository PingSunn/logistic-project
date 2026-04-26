# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run
dotnet run --project logistic/logistic.csproj

# Publish
dotnet publish -c Release
```

## Architecture

Avalonia 11 desktop application (`logistic/`) targeting `net10.0`.

### Entry / Shell
- **Entry point**: `logistic/Program.cs` — bootstraps `App` via `AppBuilder`
- **App**: `App.axaml` / `App.axaml.cs` — applies `FluentTheme`, creates `MainWindow`
- **Main window**: `MainWindow.axaml` / `MainWindow.axaml.cs` — single-page app; loads `PlanningView` directly on startup. Settings opens `SettingsWindow` (UserControl) in a dialog `Window`.

### Planning
- **`PlanningView.cs`** — code-behind UserControl (no AXAML); two-column layout: left = isometric canvas + stats (layer-cut slider, wireframe/color/dimension toggles, reset view), right = container selector, product search/checklist, quantity inputs, calculate button. Calls `PackingEngine.Calculate`, then `StatsCalculator.ComputeRows` to populate the stats panel.
- **`PackingEngine.cs`** — pure static class; implements the 5-phase packing algorithm (Primary → Balancing → PartialRemoval → Mixed → Condo). No Avalonia/UI dependency. Entry point: `PackingEngine.Calculate(container, requests)` → `PackingOutput`.
- **`StatsCalculator.cs`** — pure static class; computes per-product stats (packed, requested, full stacks, mixed, condo) and CBM utilisation from a `PackingOutput`.

### Isometric Canvas
- **`IsometricCanvas.cs`** — custom `Control`; renders `BoxPlacement` records using `IsometricProjection`. Owns camera state (`CameraState` inner struct: azimuth, elevation, zoom, drag). Supports layer-cut ratio, wireframe mode, color-by-layer, color-by-stack-layer, hidden products.
- **`IsometricProjection.cs`** — `readonly struct`; encapsulates azimuth/elevation/zoom → screen projection math. `Project(x,y,z)` maps world coords to `Point`. `GetCorners` returns all 8 box corners.
- **`CanvasLabelRenderer.cs`** — static class; all annotation drawing: layer labels, stack-width brackets, direction badges (ประตู/ในสุด), edge dimension labels, info card, hint overlays. Takes `IsometricProjection` for all projections.

### Settings
- **`SettingsWindow.axaml` / `SettingsWindow.axaml.cs`** — sidebar-nav shell (~25 lines); routes "Container" / "Product" tabs to the two panels below.
- **`ContainerSettingsPanel.cs`** — code-behind-only `UserControl`; CRUD for `ContainerSpec` list with import/export JSON.
- **`ProductSettingsPanel.cs`** — code-behind-only `UserControl`; CRUD for `ProductSpec` list with `LayerPatternEditor`, CBM display, import/export CSV/JSON.
- **`LayerPatternEditor.cs`** — `UserControl` for editing a `LayerSection[]` pattern. Each `LayerSection` holds one or more `SectionSubRow` records (rows × cols × rotated). Sections are placed left-to-right within a layer. Fires `PatternChanged` event.

### Models
- **`PackingModels.cs`** — `BoxPlacement` record (position, size, product index, rotation flag, stack index, layer index).
- **`ContainerSpec.cs`** — record with nominal + interior dimensions; loads/saves `containers.json`; Thai container names (ตู้สั้น, ตู้ยาว, ตู้ไฮคิวบ์).
- **`ProductSpec.cs`** — record with box dimensions, weight, pack size, `PatternA`/`PatternB` layer patterns, `MaxLayers`; loads/saves `products.json`.
- **`LayerSection` / `SectionSubRow`** — records for packing pattern; `SubRows` overrides legacy `Rows`/`Cols`/`Rotated` fields.

### Shared Utilities
- **`AppPaths.cs`** — resolves data directory (repo root in dev, exe dir in release); exposes `ContainersFile` and `ProductsFile` paths.
- **`JsonOptions.cs`** — shared `JsonSerializerOptions.WriteIndented` instance.
- **`ThemeColors.cs`** — shared `SolidColorBrush` constants (Surface, BorderLight, Ink, InkMuted, InkFaint, AccentBg/Border/Text, Success, Danger, BoxNormal, BoxRotated).

### Project Settings
- **Output type**: `WinExe`
- **Bindings**: `AvaloniaUseCompiledBindingsByDefault=true` — use compiled bindings (`x:DataType`) not reflection-based ones
- **Nullable**: enabled — all code must be null-safe
- **InvariantGlobalization**: true — no locale-sensitive formatting
- **Data files**: `containers.json`, `products.json` at repo root (runtime-editable)

## UI Language

All UI text is in **Thai**. Labels, buttons, headings, and status messages must be Thai strings.

## Project Domain

This app plans **container-based freight/shipping logistics** — user selects a container type, picks products with quantities, and the app calculates and visualises a 3D isometric packing arrangement.

## Task Management

1. **Verify Plan**: Check in before starting implementation
2. **Track Progress**: Mark items complete as you go
3. **Explain Changes**: High-level summary at each step
