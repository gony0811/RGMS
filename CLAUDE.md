# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**RGMS** — Respiratory-Gated Radiation Therapy Measurement System. ASP.NET Core **Blazor Web App** (server-interactive) targeting **.NET 8**.

At present the codebase is the default `dotnet new blazor` scaffold (Counter / Weather / Home pages). No domain logic for respiratory gating or measurement has been implemented yet — treat new feature work as greenfield within the Blazor Web App template.

## Repository layout

The git root (`C:\GitHub\RGMS\`) is the **solution root**: `RGMS.sln` and `global.json` live here. Each project sits in its own subfolder:

- `RGMS/` — ASP.NET Core Blazor web project (`RGMS.csproj`, `Program.cs`, `Components/`, `wwwroot/`, `appsettings*.json`).
- `RGMS.Lib/` — class library (`RGMS.Lib.csproj`) with the DAQ service, EF Core `RgmsDbContext`, entities, and migrations under `RGMS.Lib/Data/Migrations/`.

Run all `dotnet` commands from the solution root (git root), not from inside a project folder.

## Commands

All commands run from the solution root (`C:\GitHub\RGMS\`):

```powershell
dotnet restore                              # restore packages
dotnet build                                # build entire solution
dotnet run --project RGMS                   # run web app (http profile → http://localhost:5077)
dotnet run --project RGMS --launch-profile https   # HTTPS → https://localhost:7071
dotnet watch --project RGMS                 # hot-reload dev loop
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

## UI 설계
# 설정 페이지
- DAQ 보드 channel, sampling rate, Start/Stop, DAQ 보드 연결 상태, DAQ 보드 연결 설정
- 설정 phase : gate on phase (-45도) / off phase (예 +45도)
# 측정 결과 페이지
    . 측정 결과 그래프
    . 측정 결과 테이블
    . 측정 결과 저장
    . 포토디텍터로 측정한 beam on/off trigger와 레이저 거리 측정기로 측정한 호흡파형 (SINE/COSINE WAVE) PHASE가 설정한 PHASE에서 얼마나 차이가 있는지 비교