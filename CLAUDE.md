# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run
dotnet run --project logistic/logistic.csproj

# Publish (AOT)
dotnet publish -c Release
```

## Architecture

Single .NET 10 console application (`logistic/`) with AOT compilation enabled.

- **Entry point**: `logistic/Program.cs`
- **Target**: `net10.0`, `OutputType=Exe`
- **AOT**: `PublishAot=true` with `Microsoft.DotNet.ILCompiler` — avoid reflection, dynamic types, and runtime code generation as they break AOT compatibility
- **Nullable**: enabled — all code must be null-safe
- **InvariantGlobalization**: true — no locale-sensitive formatting
