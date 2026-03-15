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
- **Main window**: `MainWindow.axaml` / `MainWindow.axaml.cs`
- **Output type**: `WinExe`
- **Bindings**: `AvaloniaUseCompiledBindingsByDefault=true` — use compiled bindings (`x:DataType`) not reflection-based ones
- **Nullable**: enabled — all code must be null-safe
- **InvariantGlobalization**: true — no locale-sensitive formatting

## Project Domain

This app is about **logistic strategy** — managing and planning container-based freight/shipping logistics.

## Task Management

1. **Plan First**: Write plan to `tasks/todo.md` with checkable items
2. **Verify Plan**: Check in before starting implementation
3. **Track Progress**: Mark items complete as you go
4. **Explain Changes**: High-level summary at each step
5. **Document Results**: Add review section to `tasks/todo.md`
6. **Capture Lessons**: Update `tasks/lessons.md` after corrections
