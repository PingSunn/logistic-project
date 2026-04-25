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

- **Entry point**: `logistic/Program.cs` — bootstraps `App` via `AppBuilder`
- **App**: `App.axaml` / `App.axaml.cs` — applies `FluentTheme`, creates `MainWindow`
- **Main window**: `MainWindow.axaml` / `MainWindow.axaml.cs` — single-page app; loads `PlanningView` directly on startup. Settings opens `SettingsWindow` (UserControl) in a dialog `Window`.
- **Planning view**: `PlanningView.cs` — code-behind UserControl (no AXAML); two-column layout: left = isometric canvas + stats (layer-cut slider, wireframe/color/dimension toggles, reset view), right = container selector, product search/checklist, quantity inputs, calculate button.
- **Isometric canvas**: `IsometricCanvas.cs` — custom `Control` that renders `BoxPlacement` records in 3D isometric projection. Supports mouse-drag rotation (azimuth + elevation), layer-cut ratio, wireframe mode, color-by-layer mode, dimension labels. `BoxPlacement` carries position, size, product index, rotation flag, and stack index.
- **Layer pattern editor**: `LayerPatternEditor.cs` — UserControl for editing a `LayerSection[]` pattern. Each `LayerSection` holds one or more `SectionSubRow` records (rows × cols × rotated). Sections are placed left-to-right within a layer. Fires `PatternChanged` event.
- **Settings window**: `SettingsWindow.axaml` / `SettingsWindow.axaml.cs` — sidebar-nav UserControl with two pages: Container (edit `ContainerSpec` list, save to `containers.json`) and Product (edit `ProductSpec` list with `LayerPatternEditor`, save to `products.json`).
- **Models**:
  - `ContainerSpec.cs` — record with nominal + interior dimensions; loads/saves `containers.json`; Thai container names (ตู้สั้น, ตู้ยาว, ตู้ไฮคิวบ์)
  - `ProductSpec.cs` — record with box dimensions, weight, pack size, `PatternA`/`PatternB` layer patterns, `MaxLayers`; loads/saves `products.json`
  - `LayerSection` / `SectionSubRow` — records for packing pattern; `SubRows` overrides legacy `Rows`/`Cols`/`Rotated` fields
- **Utilities**: `AppPaths.cs` — resolves data directory (repo root in dev, exe dir in release)
- **Data files**: `containers.json`, `products.json` at repo root (runtime-editable)
- **Output type**: `WinExe`
- **Bindings**: `AvaloniaUseCompiledBindingsByDefault=true` — use compiled bindings (`x:DataType`) not reflection-based ones
- **Nullable**: enabled — all code must be null-safe
- **InvariantGlobalization**: true — no locale-sensitive formatting

## UI Language

All UI text is in **Thai**. Labels, buttons, headings, and status messages must be Thai strings.

## Project Domain

This app plans **container-based freight/shipping logistics** — user selects a container type, picks products with quantities, and the app calculates and visualises a 3D isometric packing arrangement.

## Task Management

1. **Plan First**: Write plan to `tasks/todo.md` with checkable items
2. **Verify Plan**: Check in before starting implementation
3. **Track Progress**: Mark items complete as you go
4. **Explain Changes**: High-level summary at each step
5. **Document Results**: Add review section to `tasks/todo.md`
6. **Capture Lessons**: Update `tasks/lessons.md` after corrections
