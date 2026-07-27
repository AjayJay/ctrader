# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Type

A cTrader Automate (cAlgo) robot ("cBot") — runs inside the cTrader desktop platform, not standalone. Currently a fresh project scaffold: `Scalper/Scalper.cs` contains only the default template (`OnStart`/`OnTick`/`OnStop` stubs printing "Hello world!").

## Build & Run

- No CLI build/test/lint commands apply — cBots are compiled and run by the cTrader platform itself.
- Open `Scalper.sln` in Visual Studio, Rider, or the cTrader IDE and run/debug the robot from within cTrader.
- Verifying behavior requires the cTrader client (backtesting or a demo account) — there is no way to execute or test this code outside cTrader.

## Framework

- Target: .NET 6.0 (`Scalper/Scalper.csproj`)
- Dependency: `cTrader.Automate` NuGet package (latest version), which provides the `cAlgo.API` namespaces.

## Structure

- `Scalper/Scalper.cs` — single robot class `Scalper : Robot`, decorated with `[Robot(AccessRights = AccessRights.None, AddIndicators = true)]`.
- Lifecycle entry points: `OnStart()` (setup), `OnTick()` (per-price-update logic), `OnStop()` (cleanup).
- `[Parameter]`-decorated properties surface as configurable inputs in the cTrader UI.
- No tests, no linting, no build scripts.
