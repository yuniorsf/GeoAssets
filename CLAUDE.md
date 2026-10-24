# GeoAssets — Claude Instructions

## Tri-Planar Multi-Agent Architecture & Operational Contract

GeoAssets operates under a **Tri-Planar Dual-Loop Architecture** backed by a **Deterministic Oracle Substrate** and **Git Worktree Isolation**:

- **Outer Loop — Google Antigravity (DAG Orchestrator)**: Decomposes epics/tickets into a dependency-aware DAG, provisions isolated Git Worktrees via `scripts/agent-sandbox.sh`, dispatches parallel work to inner-loop agents, runs the deterministic gatekeeper (`scripts/verify-agent-worktree.sh`), manages the self-healing retry loop (up to 3 cycles), and coordinates rebase/merge preparation.
- **Inner Loop (Deep Reasoning) — Claude Code (Specialist)**: Owns complex domain modeling, cross-cutting architectural refactoring, spatial/topology algorithms (NetTopologySuite / PostGIS), state-machine rules (`GeoAssets.Workflow`), concurrency/observability propagation, and self-healing diagnostic resolution.
- **Inner Loop (Mechanical / QA) — GitHub Copilot (Worker)**: Owns high-velocity mechanical implementation, DTO/mapper scaffolding, xUnit test suites, mock/builder generation, and formatting compliance.

### Mandatory Execution Rules for Claude Code

1. **Strict Git Worktree Isolation**:
   - Execute all file reads, edits, builds, and tests **exclusively inside the assigned Git Worktree** (`../worktrees/<task-id>` provisioned by `scripts/agent-sandbox.sh` or `.claude/worktrees/<id>`).
   - Never modify the root checkout directly during orchestrated runs, and **never** branch from, checkout, merge into, or rebase `main`. Every worktree branch must originate from `origin/develop`.
2. **No Unverified Commits**:
   - Never run `git commit` or report task completion until the deterministic gatekeeper script passes with exit code `0`:
     ```bash
     ./scripts/verify-agent-worktree.sh
     ```
   - In interactive `/implement-ticket` flows, leave changes unstaged and uncommitted after verification so the human reviewer can inspect the verified diff.
3. **Zero Tolerance for Analyzer Suppression**:
   - Never bypass `Directory.Build.props` or `BannedSymbols.txt` by inserting `#pragma warning disable`, `[SuppressMessage]`, or `<NoWarn>`. Fix the underlying architectural or code defect.

### Deterministic Self-Healing Loop (Compiler & Oracle Diagnostics)

When `./scripts/verify-agent-worktree.sh` returns a non-zero exit code, intercept the structured output and execute up to **3 self-healing iterations** before escalating to the orchestrator:

| Exit Code | Gate Stage | Typical Diagnostics | Required Self-Healing Resolution |
|---|---|---|---|
| `10` | `dotnet format --verify-no-changes` | `WHITESPACE`, `IDE0055` | Run `dotnet format GeoAssets.sln` inside the worktree and re-verify that no unintended semantic changes were introduced. |
| `20` | `dotnet build --no-incremental /warnaserror` | `AsyncFixer01`–`04`, `RS0030`, `MA*`, `CA*` | Parse the diagnostic code and resolve without breaking domain invariants (see table below). |
| `30` | `dotnet test --no-build` (NetArchTest / Domain) | Onion layer violation or broken domain invariant | Refactor misplaced infrastructure dependencies back behind `core/` interfaces (`IAssetProvider`, `IServiceOrderRepository`, etc.) and re-run. |

#### Common Analyzer Diagnostic Resolutions (`Exit Code 20`)
- **`AsyncFixer01` (Unnecessary `async`/`await`)**: Remove the `async`/`await` state-machine wrapper and return the underlying `Task` directly, **unless** the `await` is inside a `using` block, `try/catch/finally` block, or active `Activity` span scope (in which case eliding `async` would dispose the resource or end the span before the task completes — restructure the scope instead).
- **`AsyncFixer02` (Long-running or blocking operation inside `async` method)**: Replace synchronous blocking calls (`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`, `Thread.Sleep`, synchronous EF Core/IO methods) with their true asynchronous counterparts (`await ...ConfigureAwait(false)` or `await Task.Delay(..., timeProvider, cancellationToken)`).
- **`AsyncFixer03` (`async void` fire-and-forget)**: Convert `async void` methods to `async Task` (except framework-mandated Blazor/MAUI UI event handlers, which must wrap their body in an explicit `try/catch` error boundary).
- **`RS0030` (Banned Symbol from `BannedSymbols.txt`)**:
  - `DateTime.Now` / `DateTime.UtcNow` / `DateTimeOffset.Now` → Inject `System.TimeProvider` via primary/constructor DI and call `timeProvider.GetUtcNow()`.
  - `Task.Wait` / `Task.Result` / `Thread.Sleep` → Convert to non-blocking `await` with `CancellationToken`.
- **`MA0004` / `MA0040` (Meziantou Concurrency & CancellationToken)**: Forward the caller's `CancellationToken` to all downstream async calls and apply `.ConfigureAwait(false)` in non-UI library layers (`core/`, `providers/`, `workflow/`).

---

## Stack

- .NET 10 / Blazor WebAssembly (Web) + MAUI (mobile/desktop)
- Razor Class Library: `GeoAssets.Shared` (components, CSS, JS)
- Core library: `GeoAssets.Core` (models, services, interfaces)
- Provider: `GeoAssets.Provider.PostgreSQL` (EF Core + Npgsql + PostGIS — server-side only)
- Map: Leaflet.js 1.9.4 + Leaflet-Geoman 2.18.3 (via CDN)
- Spatial: NetTopologySuite 2.5.0 + NTS.IO.GeoJSON4STJ 4.0.0
- Storage: Blazored.LocalStorage (Web), MAUI file APIs (MAUI)

## Folder Structure

- `apps/` — Blazor Web, ASP.NET Core Server, MAUI, Shared RCL, Identity EFCore, Commands Builtin
- `core/` — Core, Commands, Workflow, Identity, IpBilling, Infrastructure.Observability
- `providers/` — PostgreSQL, Rest, Shapefile, WFS, WMS, Xlsx
- `plugins/commands/` — Plugin.Hydrology, Plugin.GeoJsonImport
- `workflow/` — EFCore, Rest, Messaging.Kafka, Messaging.ServiceBus, Agents
- `ipbilling/` — IpBilling.EFCore
- `projects/` — Projects.EFCore, Projects.Rest
- `deploy/` — Docker Compose stacks (`web`, `server`, `otel`)
- `docs/` — Performance baselines and technical reports
- `tools/` — Benchmarks and developer tooling (`GeoAssets.Bench.GetPageAsync`)
- `examples/` — Console examples (Spatial, Topology, Workflow, MultiAgent)

## Key File Paths

| Purpose | Path |
|---|---|
| Main page | `apps/GeoAssets.Shared/Pages/Index.razor` |
| Nav menu | `apps/GeoAssets.Shared/Components/Layout/NavMenu.razor` |
| Top bar | `apps/GeoAssets.Shared/Components/Layout/TopBar.razor` |
| Map component | `apps/GeoAssets.Shared/Components/Map/MapContainer.razor` |
| Context menu | `apps/GeoAssets.Shared/Components/Map/MapContextMenu.razor` |
| Draw toolbar | `apps/GeoAssets.Shared/Components/Map/DrawToolbar.razor` |
| Asset form | `apps/GeoAssets.Shared/Components/Assets/AssetForm.razor` |
| Asset list | `apps/GeoAssets.Shared/Components/Assets/AssetList.razor` |
| JS interop | `apps/GeoAssets.Shared/wwwroot/js/geoassets.js` |
| CSS | `apps/GeoAssets.Shared/wwwroot/css/geoassets.css` |
| Geometry models | `core/GeoAssets.Core/Models/Geometry/` |
| Provider contract | `core/GeoAssets.Core/Interfaces/IAssetProvider.cs` |
| Active provider proxy | `core/GeoAssets.Core/Providers/ActiveAssetProvider.cs` |
| Provider pool | `core/GeoAssets.Core/Services/ProviderPool.cs` |
| Map interop interface | `apps/GeoAssets.Shared/Interfaces/IMapInterop.cs` |
| Map interop impl | `apps/GeoAssets.Shared/Services/MapInteropService.cs` |
| Web app | `apps/GeoAssets.Web/` |
| Server app | `apps/GeoAssets.Server/` |
| MAUI app | `apps/GeoAssets.MAUI/` |

## Architecture

- **State**: `IAssetProvider` (`ActiveAssetProvider` proxying the active entry in `IProviderPool`) as source of truth; C# events (`FeatureAdded/Updated/Deleted/CollectionChanged`) as pub/sub
- **JS↔C# bridge**: `DotNetObjectReference<object>` stored in JS `_maps[divId].dotNetRef`; C# calls JS via `IJSRuntime.InvokeVoidAsync("GeoAssets.*")`
- **JS→C# callbacks** (all `[JSInvokable]` on `MapContainer`): `OnFeatureDrawnFromJs`, `OnFeatureEditedFromJs`, `OnFeatureClickedFromJs`, `OnFeatureContextMenuFromJs`, `OnViewportChangedFromJs`
- **Auto-save**: `AssetService` debounces 500ms on `CollectionChanged` → `IStorageService.SaveAsync`

## Geometry (NTS Integration)

- `GeoGeometry` base: abstract `NtsGeometry` property, `GetBoundingBox()` via NTS envelope, spatial predicates (`Contains`, `Intersects`, `Within`, etc.), measurements (`Area`, `Length`, `Distance`), derived geometries (`Buffer`, `ConvexHull`, `Union`, etc.), `Centroid`, `FromNts()` static factory
- `GeoPoint`, `GeoLineString`, `GeoPolygon`: each builds NTS geometry lazily from coordinate arrays (SRID 4326, X=lon, Y=lat)
- Serialization: coordinate arrays are the JSON source of truth — do not change this
- Spatial queries on `IAssetProvider`: `GetWithinAsync`, `GetIntersectingAsync`, `GetNearbyAsync`, `GetInBoundsAsync`

## Topology (directed graph)

- `TopoEdge` model (`Models/TopoEdge.cs`): `TargetId`, `Kind`, `Weight`, `Metadata`; serialized as `"topology"` array on each `GeoFeature`
- `GeoFeature.Topology`: `List<TopoEdge>` (outgoing edges, persisted in JSON)
- `TopoGraph` static service (`Services/TopoGraph.cs`): `GetNeighbors`, `GetDescendants`, `GetAncestors`, `TopologicalSort` (Kahn's), `FindPath` (BFS), `FindShortestPath` (Dijkstra), `GetConnectedComponents`, `HasCycles`

## PostgreSQL Provider

- Register with `builder.Services.AddGeoAssetsPostgres()` (server-side hosts only — not Blazor WASM)
- `IExternalProviderFactory` (Core) — discoverable; `ProviderPoolPanel` renders entries managed in `IProviderPool`
- NTS bridge: write `feature.Geometry?.NtsGeometry` → `GeoEntityRow.Geom`; read `GeoGeometry.FromNts(row.Geom)` → `GeoFeature.Geometry`
- Key files: `Data/GeoAssetsDbContext.cs`, `Entities/GeoEntityRow.cs`, `Repositories/PostgresAssetProvider.cs`, `PostgresProviderFactory.cs`

## Context Menu

- Right-click on any map feature → `contextmenu` in `geoassets.js` → `OnFeatureContextMenuFromJs(id, clientX, clientY)`
- `MapContextMenu.razor`: `position:fixed` at click coords, z-1500; backdrop div z-1400 closes on outside click
- `<MapContextMenu>` and `<ConfirmDialog>` rendered **outside** `.map-area` in `Index.razor` so `position:fixed` works correctly

## CSS Design System (Catppuccin Mocha / Latte)

```
Dark (Mocha)                            Light (Latte)
--panel-bg: #1e1e2e      --accent: #89b4fa     --panel-bg: #eff1f5      --accent: #1e66f5
--panel-border: #313244  --danger: #f38ba8     --panel-border: #ccd0da  --danger: #d20f39
--text-primary: #cdd6f4  --success: #a6e3a1    --text-primary: #4c4f69  --success: #40a02b
--text-secondary: #6c7086 --warning: #f9e2af    --text-secondary: #6c6f85 --warning: #df8e1d
```

Layout: sidebar (340px fixed) + content-column (flex:1, topbar + map-area). Overlays use `position:absolute` z-1000. Dialogs use `position:fixed` z-2000.

Bootstrap 5.3 (vendored at `apps/GeoAssets.Shared/wwwroot/lib/bootstrap/`, served to both hosts as `_content/GeoAssets.Shared/lib/bootstrap/dist/css/bootstrap.min.css`) is loaded *before* `geoassets.css` in both `index.html` files. `geoassets.css` re-maps Bootstrap's `--bs-*` custom properties under `[data-bs-theme="dark"]`/`[data-bs-theme="light"]` to the tokens above (`--bs-primary` → `--accent`, `--bs-tertiary-bg`/`--bs-secondary-bg` → `--panel-bg`, `--bs-border-color` → `--panel-border`, `--bs-secondary-color` → `--text-secondary`, `--bs-success`/`--bs-danger` → `--success`/`--danger`) so Bootstrap components render in-brand. Note: Bootstrap's own per-component vars (e.g. `--bs-btn-bg`, `--bs-nav-pills-link-active-bg`) are hardcoded in the stock CSS rather than derived from `--bs-primary`, so components that rely on Bootstrap's primary color (buttons, active nav-pills, dropdowns) still need a small scoped override when they're built — the token bridge only covers the base/reboot layer.

Also theme-scoped: `--on-accent` (text/icon color for content on top of `--accent`/`--danger`/`--success` — near-black in Mocha since those are light pastels, near-white in Latte since they're saturated) and `--surface-tint-weak`/`--surface-tint`/`--surface-tint-strong` (hover/subtle-background tints — white-alpha in Mocha, black-alpha in Latte). Use these instead of hardcoding a color that only looks right in one theme.

## Theming (dark / light / system)

- `IThemeService` (`core/GeoAssets.Core/Theming/`) + `BlazorThemeService` (`apps/GeoAssets.Shared/Theming/`) — same interface-in-Core/implementation-in-Shared split as `ICultureService`/`BlazorCultureService`. `Mode` is the user's selection (`ThemeMode.Light`/`Dark`/`System`); `SetModeAsync` persists to `localStorage["geoassets.theme"]` and applies `data-bs-theme` on `<html>` via JS interop.
- **Flash prevention**: a small inline `<script>` in the `<head>` of *both* `index.html` files resolves the same stored-preference-or-`prefers-color-scheme` logic synchronously, before first paint — this is what actually prevents the flash, since Blazor WASM's boot is too slow to rely on `App.razor`'s `OnInitializedAsync`. Keep that script's logic in sync with `BlazorThemeService.ResolveTheme` if either changes.
- `App.razor` calls `ThemeService.InitAsync()` (mirroring `CultureService.InitAsync()`) to sync the C# `Mode`/`ResolvedTheme` state to whatever the inline script already applied — it's a no-op re-application of the same value, not a second source of truth.
- The topbar's theme toggle (`TopBar.razor`) is a 3-button Bootstrap `btn-group` (Light/System/Dark).
- Only wired into `GeoAssets.Web` (`Program.cs` + `App.razor`) — `GeoAssets.MAUI`'s `MauiProgram.cs`/`WebApp.razor` don't register `IThemeService` or `ICultureService` (MAUI's DI for these two remains a pre-existing gap; `ICurrentUserAccessor`/`IAuthNavigationService` were closed for MAUI by XD01-52's MSAL.NET wiring — see `EntraCiamMauiAuthenticationProvider`). The FOUC-prevention inline script is duplicated into MAUI's `index.html` anyway since it's plain JS with no DI dependency.

## Branching & Release Policy

- `develop` is the sole integration branch for all agent work — manual ticket implementation, `/implement-ticket`, `/ship-ticket`, and `/orchestrate-tickets` alike. Every new branch must be created from `origin/develop` explicitly (fetch it, branch from it) — never from whatever branch happens to already be checked out, since this repo's working directory is sometimes shared across concurrent sessions and the current branch is not a reliable signal of intent.
- `main` must never be branched from, merged into, merged from, or rebased by any agent or orchestration workflow, under any circumstance — including when resolving a conflict, "catching main up," or any other seemingly-helpful cleanup. The only way `main` changes is the user explicitly instructing it in an interactive session, by name, in that session. No skill or command in this repo should reference `main` as a base or target branch; if one is found to, that's a bug in the skill, not a precedent to follow.

## Pull Requests & Commits

- When an Agent (Claude Code Action or otherwise) opens a PR implementing a Jira ticket, the PR title must be prefixed with that ticket's key, e.g. `XD01-8: Add REST-backed IServiceOrderRepository client`
- If a PR's changes span multiple tickets, prefix with the primary/parent ticket key
- Commit subjects implementing a ticket end with `(TICKET-KEY)`, e.g. `feat(workflow): round-trip a concurrency version token through IServiceOrderRepository (XD01-26)` — the **GitHub for Atlassian** app (connected to this repo) scans commits/branches/PR titles for this pattern to auto-link them under the ticket's Development panel; it also makes `git log --grep` reliable for human traceability

## Conventions

- All geometry follows RFC 7946 GeoJSON ([longitude, latitude] order)
- Razor components: `EventCallback<T>` up, `[Parameter]` down
- Every Razor component/page ships as a markup-only `.razor` file plus a `.razor.cs` code-behind partial class, regardless of the component's size — see `apps/GeoAssets.MAUI/WebApp.razor`/`WebApp.razor.cs` for the target shape (tracked by XD01-101). **Exception**: `apps/GeoAssets.MAUI/Pages/MauiLogin.razor` stays single-file. `GeoAssets.MAUI`'s Razor toolchain (`Sdk="Microsoft.NET.Sdk"` + `UseMaui=true`, via `Microsoft.AspNetCore.Components.WebView.Maui`) doesn't reliably wire markup that reads a `.razor.cs`-declared field back to that field — confirmed by a field used only in markup (`disabled="@_signingIn"`) triggering a false "unused" warning once moved to code-behind, a warning that does *not* reproduce with the identical pattern in `GeoAssets.Shared` (`Microsoft.NET.Sdk.Razor`) or `GeoAssets.Web` (`Microsoft.NET.Sdk.BlazorWebAssembly`) — evidence of degraded codegen specific to this project, not a Roslyn quirk. A `[Inject]`-in-code-behind workaround does fix the `@inject`-property-visibility half of the problem, but the markup-binding warning persists and couldn't be runtime-verified (no MAUI simulator/device in this environment), so the split was not applied rather than risk a silent regression in an auth-critical flow. See XD01-121 for the investigation.
- Bootstrap is allowed and is the base for component styling in `GeoAssets.Shared` (used by both Web and MAUI); layer bespoke CSS in `geoassets.css` on top for anything Bootstrap doesn't cover — don't hand-roll styles Bootstrap already provides
- The only loaded JS file is `geoassets.js` (IIFE); `mapInterop.js` and `drawInterop.js` are legacy drafts — do not reference them
- Do not add features, refactor, or clean up code beyond what is asked
- Do not add comments or docstrings to code you did not change
