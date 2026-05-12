# AGENTS.md

## Project Type
cAlgo/cTrader trading robot - runs inside cTrader desktop platform, not standalone.

## Build & Run
- No standard build commands (dotnet build, etc.) - must be loaded via cTrader IDE
- Open `.sln` in Visual Studio or Rider, then run inside cTrader
- Requires cTrader client installed to test/verify

## Framework
- Target: .NET 6.0
- Dependency: `cTrader.Automate` (latest version via NuGet)

## Structure
- Single robot class `LadderOrderBotUSD` in `MultiSafeLimitOrderPlacer.cs`
- Entry point: `OnStart()` method - initializes UI and event handlers
- No tests, no linting, no codegen