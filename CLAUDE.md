# GeoAssets — Claude Instructions

## Stack

- .NET 10 / Blazor WebAssembly (Web) + MAUI (mobile/desktop)
- Razor Class Library: `GeoAssets.Shared` (components, CSS, JS)
- Core library: `GeoAssets.Core` (models, services, interfaces)
- Provider: `GeoAssets.Provider.PostgreSQL` (EF Core + Npgsql + PostGIS — server-side only)
- Map: Leaflet.js 1.9.4 + Leaflet-Geoman 2.18.3 (via CDN)
- Spatial: NetTopologySuite 2.5.0 + NTS.IO.GeoJSON4STJ 4.0.0
- Storage: Blazored.LocalStorage (Web), MAUI file APIs (MAUI)

## Folder Structure

- `apps/` — Blazor Web, MAUI, Shared RCL, Identity EFCore, Commands Builtin
- `core/` — Core, Commands, Workflow, Identity, Observability
- `providers/` — InMemory, PostgreSQL, Active, Rest, Observable, Utils
- `plugins/` — Plugin.Hydrology, Plugin.GeoJsonImport
- `workflow/` — EFCore, Kafka, ServiceBus
- `examples/`

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
| Repository | `core/GeoAssets.Core/Services/InMemoryAssetRepository.cs` |
| Map interop interface | `apps/GeoAssets.Shared/Interfaces/IMapInterop.cs` |
| Map interop impl | `apps/GeoAssets.Shared/Services/MapInteropService.cs` |
| Web app | `apps/GeoAssets.Web/` |
| MAUI app | `apps/GeoAssets.MAUI/` |

## Architecture

- **State**: singleton `IAssetRepository` (InMemoryAssetRepository) as source of truth; C# events (`FeatureAdded/Updated/Deleted/CollectionChanged`) as pub/sub
- **JS↔C# bridge**: `DotNetObjectReference<object>` stored in JS `_maps[divId].dotNetRef`; C# calls JS via `IJSRuntime.InvokeVoidAsync("GeoAssets.*")`
- **JS→C# callbacks** (all `[JSInvokable]` on `MapContainer`): `OnFeatureDrawnFromJs`, `OnFeatureEditedFromJs`, `OnFeatureClickedFromJs`, `OnFeatureContextMenuFromJs`
- **Auto-save**: `AssetService` debounces 500ms on `CollectionChanged` → `IStorageService.SaveAsync`

## Geometry (NTS Integration)

- `GeoGeometry` base: abstract `NtsGeometry` property, `GetBoundingBox()` via NTS envelope, spatial predicates (`Contains`, `Intersects`, `Within`, etc.), measurements (`Area`, `Length`, `Distance`), derived geometries (`Buffer`, `ConvexHull`, `Union`, etc.), `Centroid`, `FromNts()` static factory
- `GeoPoint`, `GeoLineString`, `GeoPolygon`: each builds NTS geometry lazily from coordinate arrays (SRID 4326, X=lon, Y=lat)
- Serialization: coordinate arrays are the JSON source of truth — do not change this
- Spatial queries on `IAssetRepository`: `GetWithin`, `GetIntersecting`, `GetNearby`

## Topology (directed graph)

- `TopoEdge` model (`Models/TopoEdge.cs`): `TargetId`, `Kind`, `Weight`, `Metadata`; serialized as `"topology"` array on each `GeoFeature`
- `GeoFeature.Topology`: `List<TopoEdge>` (outgoing edges, persisted in JSON)
- `TopoGraph` static service (`Services/TopoGraph.cs`): `GetNeighbors`, `GetDescendants`, `GetAncestors`, `TopologicalSort` (Kahn's), `FindPath` (BFS), `FindShortestPath` (Dijkstra), `GetConnectedComponents`, `HasCycles`

## PostgreSQL Provider

- Register with `builder.Services.AddGeoAssetsPostgres()` (server-side hosts only — not Blazor WASM)
- `IExternalRepositoryFactory` (Core) — discoverable; `RepositoryPoolPanel` renders one entry per registered factory
- NTS bridge: write `feature.Geometry?.NtsGeometry` → `GeoEntityRow.Geom`; read `GeoGeometry.FromNts(row.Geom)` → `GeoFeature.Geometry`
- Key files: `Data/GeoAssetsDbContext.cs`, `Entities/GeoEntityRow.cs`, `Repositories/PostgresAssetRepository.cs`, `PostgresRepositoryFactory.cs`

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
