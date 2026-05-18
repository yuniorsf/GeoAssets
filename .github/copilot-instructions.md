# GeoAssets Copilot Instructions

## Build and test

Use .NET 10 (`global.json` pins SDK `10.0.100`; solution and projects target `net10.0`).

```bash
dotnet restore GeoAssets.sln
dotnet build GeoAssets.sln --configuration Release
dotnet test tests/GeoAssets.Core.Tests/GeoAssets.Core.Tests.csproj --configuration Release
```

Run a single xUnit test with a `FullyQualifiedName` filter:

```bash
dotnet test tests/GeoAssets.Core.Tests/GeoAssets.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~GeoAssets.Core.Tests.Services.TopoGraphTests.GetNeighbors_UnknownId_ReturnsEmpty"
```

Useful run commands:

```bash
cd apps/GeoAssets.Web && dotnet run
cd examples/GeoAssets.Examples && dotnet run
```

If you need to build MAUI locally, install the workload first:

```bash
dotnet workload install maui
```

There is no dedicated lint command in the repo today. CI currently validates restore/build/test, and the full solution workflow installs the MAUI Android workload before building.

## High-level architecture

GeoAssets is a multi-target .NET 10 platform built around a shared domain core and a shared Razor Class Library:

- `core/GeoAssets.Core` holds the domain model, geometry abstractions, provider contracts, topology algorithms, and services.
- `apps/GeoAssets.Shared` is the shared UI layer for both Blazor WebAssembly and MAUI: components, CSS, localization, and JavaScript interop live here.
- `apps/GeoAssets.Web` is the WASM host. It wires authentication, localization, storage, provider plugins, observability decorators, and the boot flow.
- `apps/GeoAssets.Server` is the server-side host. It exposes GeoAssets REST endpoints plus WFS/WMS endpoints backed by PostgreSQL/PostGIS.
- `providers/` contains pluggable repository implementations and adapters (InMemory, PostgreSQL, REST, WFS, WMS, Shapefile).
- `workflow/` and `plugins/` extend the platform without pushing those concerns into `GeoAssets.Core`.

The UI entry point is `apps/GeoAssets.Shared/Pages/Index.razor`. It composes the provider pool, layer manager, asset list, asset form, map container, and modal dialogs. Most user-facing behavior is driven from there plus `MapContainer.razor`.

State is centered on `IAssetProvider`, but the active provider is normally accessed through `ActiveAssetProvider`, a proxy over `IProviderPool`. That proxy rewires event subscriptions when the active pool entry changes and re-raises `FeatureAdded`, `FeatureUpdated`, `FeatureDeleted`, and `CollectionChanged` so UI components do not need to know that a provider switch happened.

On WebAssembly, `AssetService` sits between the provider and `IStorageService`: it loads the initial feature collection, merges custom asset types, and auto-saves after mutations using a 500 ms debounce. The boot flow is separate from asset persistence: `BootLoaderService` persists the selected provider plugin and config under `geoassets:boot-config`, recreates the provider on startup, and intentionally strips `*_content` values before saving boot config.

Provider extensibility has two different discovery paths:

- UI-selectable provider plugins implement `IProviderPlugin` and are registered into the `ProviderPluginRegistry`.
- External infrastructure-backed providers register `IExternalProviderFactory`; `ProviderPoolPanel` discovers them from DI so they can be connected from the UI.

Map rendering is a C# to JavaScript boundary, not a pure Razor feature. `MapContainer.razor` owns the `JSInvokable` callbacks (`OnFeatureDrawnFromJs`, `OnFeatureEditedFromJs`, `OnFeatureClickedFromJs`, `OnFeatureContextMenuFromJs`, `OnViewportChangedFromJs`). `MapInteropService` is the only supported bridge from C# into Leaflet and delegates to `window.GeoAssets.*` functions in `apps/GeoAssets.Shared/wwwroot/js/geoassets.js`.

There is an important performance split in the map path: when viewport changes, the map prefers provider methods that return pre-serialized JSON (`GetInBoundsRawJsonAsync` / `GetInBoundsJsonAsync`) so rendering can avoid unnecessary WASM-side parse/serialize work.

The PostgreSQL provider is server-oriented. `AddGeoAssetsPostgres()` registers the factory used to create repository instances, while `AddGeoAssetsDbContextFactory(connectionString)` is a separate registration used by services like the WMS renderer that need short-lived EF Core contexts and should not share the provider cache.

## Key conventions

- GeoJSON and coordinate handling follow RFC 7946 consistently: coordinates are `[longitude, latitude]`, and spatial data uses SRID 4326.
- In the geometry model, coordinate arrays are the serialized source of truth. NetTopologySuite geometries are derived from those arrays; do not flip the source of truth to NTS objects.
- Repository events are part of the app architecture, not incidental implementation details. If you change provider, decorator, or proxy behavior, preserve event propagation semantics.
- The active workspace pattern matters: most UI code should work against the active `IAssetProvider`, not a concrete provider implementation.
- Provider discovery is registration-driven. If a new provider should appear in the connection UI, wire it through the existing plugin/factory patterns instead of hard-coding UI branches.
- Only `geoassets.js` is loaded by the web and MAUI hosts. `mapInterop.js` and `drawInterop.js` exist as legacy drafts and should not be used as the integration point.
- `apps/GeoAssets.Shared` is the shared UI contract for both WebAssembly and MAUI. Prefer changes there over duplicating UI behavior in host projects.
- Razor component communication follows the existing pattern: data flows down via `[Parameter]`, actions flow up via `EventCallback<T>`.
- The app uses bespoke CSS, not Bootstrap. Keep styling aligned with the existing Catppuccin-based design tokens in `apps/GeoAssets.Shared/wwwroot/css/geoassets.css`.
- The context menu and confirm dialog are intentionally rendered outside `.map-area` in `Index.razor` so their `position: fixed` behavior works correctly over the map.
- Current automated tests live in `tests/GeoAssets.Core.Tests` and focus on the core domain plus the in-memory provider. If you add behavior in other layers, follow the same project separation instead of forcing unrelated tests into the core test project.
