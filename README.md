# Logistic Planner

A desktop application for planning and visualising 3D freight packing layouts inside shipping containers.

## Overview

Select a container type, choose products with quantities, and the app calculates an optimised packing arrangement displayed as an interactive isometric 3D view.

**Supported containers**

| Name | Size |
|------|------|
| ตู้สั้น | 20 ft |
| ตู้ยาว | 40 ft |
| ตู้ไฮคิวบ์ | 40 ft HC |

## Tech Stack

- **.NET 10** / **Avalonia 11** desktop (cross-platform)
- Custom isometric canvas rendered via Avalonia `Control`
- Pure-logic packing engine with no UI dependency
- xUnit test suite

## Getting Started

**Prerequisites**: .NET 10 SDK

```bash
# Clone and build
git clone <repo-url>
cd logistic-project
dotnet build

# Run
dotnet run --project logistic/logistic.csproj

# Run tests
dotnet test logistic.Tests/logistic.Tests.csproj --logger "console;verbosity=detailed"
```

Data files (`containers.json`, `products.json`) live at the repo root and are edited at runtime through the Settings window.

## Features

- **Packing engine** — 6-phase algorithm: Primary → Balancing → LayerBalancing → PartialRemoval → Condo → ScatteredTopPlacement
- **Isometric canvas** — interactive camera (azimuth, elevation, zoom, drag), layer-cut slider, wireframe / colour-by-layer / dimension toggles
- **Per-product statistics** — packed vs requested, full stacks, mixed stacks, condo boxes, scatter boxes, CBM utilisation
- **Product patterns** — define A/B layer patterns per product with rotation support
- **Settings** — CRUD for containers and products with CSV/JSON import-export

## Project Structure

```
logistic/           — Avalonia app
  PackingEngine.cs  — core packing logic
  IsometricCanvas.cs
  PlanningView.cs
  SettingsWindow.axaml
logistic.Tests/     — xUnit tests
containers.json     — container specs
products.json       — product specs
```

## UI Language

All UI text is in Thai (ภาษาไทย).
