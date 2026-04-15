# GeoAssets — Claude Instructions

## Stack

- .NET 9 / Blazor WebAssembly (Web) + MAUI (mobile/desktop)
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

## CSS Design System (Catppuccin Mocha)

```
--panel-bg: #1e1e2e      --accent: #89b4fa     --danger: #f38ba8
--panel-border: #313244  --success: #a6e3a1    --text-primary: #cdd6f4
```

Layout: sidebar (340px fixed) + map-area (flex:1). Overlays use `position:absolute` z-1000. Dialogs use `position:fixed` z-2000.

## Conventions

- All geometry follows RFC 7946 GeoJSON ([longitude, latitude] order)
- Razor components: `EventCallback<T>` up, `[Parameter]` down
- No Bootstrap — bespoke CSS only
- The only loaded JS file is `geoassets.js` (IIFE); `mapInterop.js` and `drawInterop.js` are legacy drafts — do not reference them
- Do not add features, refactor, or clean up code beyond what is asked
- Do not add comments or docstrings to code you did not change
