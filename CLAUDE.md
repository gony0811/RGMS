# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**RGMS** — Respiratory-Gated Radiation Therapy Measurement System. ASP.NET Core **Blazor Web App** (server-interactive) targeting **.NET 8**.

At present the codebase is the default `dotnet new blazor` scaffold (Counter / Weather / Home pages). No domain logic for respiratory gating or measurement has been implemented yet — treat new feature work as greenfield within the Blazor Web App template.

## Repository layout

The git root is `/RGMS/`; the actual project lives one level deeper in `/RGMS/RGMS/`. Run all `dotnet` commands from `RGMS/RGMS/` (where `RGMS.csproj` and `RGMS.sln` live), not from the git root.

## Commands

All commands run from `RGMS/RGMS/`:

```bash
dotnet restore                  # restore packages
dotnet build                    # build
dotnet run                      # run (uses "http" profile by default → http://localhost:5077)
dotnet run --launch-profile https   # run with HTTPS → https://localhost:7071
dotnet watch                    # hot-reload dev loop
```

No test project exists yet; `dotnet test` will be a no-op until one is added.

The SDK is pinned by `global.json` to **8.0.0** with `rollForward: latestMinor`. Installing a newer major (.NET 9+) will not satisfy this constraint.

## Architecture notes

- **Render mode**: `Program.cs` registers `AddInteractiveServerComponents()` and `AddInteractiveServerRenderMode()`. Components that need interactivity must opt in with `@rendermode InteractiveServer` (see `Components/Pages/Counter.razor`) — without it they render statically on the server with no event handlers wired up.
- **Routing entry point**: `Components/App.razor` is the root HTML document; `Components/Routes.razor` hosts the `<Router>` and defaults to `Layout/MainLayout`. New pages go under `Components/Pages/` with `@page "/route"`.
- **Global usings**: `Components/_Imports.razor` is the single place to add `@using` directives shared by every Razor component in the tree.
- **Static assets**: `wwwroot/` is served by `UseStaticFiles()`. Bootstrap is bundled locally under `wwwroot/bootstrap/` (not via CDN).

## 프로그램 목적

- 방사선 치료 장치의 호흡동조 측정과 방사선 빔의 동조 정도를 측정하고자함.

# 측정 보드 (NI USB-6001)
- AI0에 포토디텍터의 시그널이 입력됨
    . HAMAMATSU사 Si Photodiode S8559 
    . OPAx145 High-Precision, Low-Noise, Rail-to-Rail Output, 5.5-MHz JFET Operational Amplifiers
- AI1에 비접촉 레이저 거리 측정기의 시그널이 입력됨
    . KEYENCE사 IL-1000, IL-S100

