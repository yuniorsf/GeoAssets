# GeoAssets Copilot Instructions

## Tri-Planar Agent Role & .NET 10 Clean Code Contract

Within GeoAssets' **Tri-Planar Dual-Loop Architecture**, **GitHub Copilot** operates as the **High-Velocity Mechanical & QA Worker** (complementing Google Antigravity as Outer-Loop DAG Orchestrator and Claude Code as Deep Reasoning / Refactoring Specialist).

### 1. Copilot Responsibilities (Inner-Loop Mechanical & QA)
- **Mechanical Implementation & Boilerplate**: Rapidly scaffold DTOs, EF Core entity configurations, REST client request/response contracts, DI registration extensions, and Razor code-behind (`.razor.cs`) bindings following established interfaces.
- **Automated Test & Mock Scaffolding**: Generate exhaustive xUnit `[Fact]` and `[Theory]` unit tests, `NSubstitute`/`Moq` test doubles, and shared fluent builders (`DomainTestDataBuilders`) targeting 100% branch coverage across happy paths, guard clauses, boundary conditions, and cancellation paths.
- **Formatting & Gate Compliance**: Ensure all generated code passes `./scripts/verify-agent-worktree.sh` (`dotnet format --verify-no-changes`, `dotnet build --no-incremental /warnaserror`, and architecture tests).

### 2. Strictly Banned Patterns (Enforced by `BannedSymbols.txt` & Analyzers)
- **No Sync-Over-Async or Thread Blocking**: Never emit `.Result`, `.Wait()`, `.WaitAll()`, `.WaitAny()`, `.GetAwaiter().GetResult()`, or `Thread.Sleep(...)`. Always `await` tasks asynchronously and accept/forward a `CancellationToken`.
- **No Direct System Clock Calls**: Never use `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now`, or `DateTimeOffset.UtcNow` (`RS0030`). Always inject `System.TimeProvider` (or `FakeTimeProvider` in tests) and call `timeProvider.GetUtcNow()`.
- **No Infrastructure or PostGIS Leakage into Domain Layers**: Projects under `core/` (`GeoAssets.Core`, `GeoAssets.Workflow`, `GeoAssets.Identity`, `GeoAssets.IpBilling`, `GeoAssets.Commands`) must **never** reference `Npgsql`, `Microsoft.EntityFrameworkCore`, `GeoAssets.Provider.PostgreSQL`, or raw SQL/PostGIS functions. Keep all PostGIS and EF Core logic strictly inside `providers/`, `ipbilling/`, `projects/`, `workflow/GeoAssets.Workflow.EFCore`, and `apps/GeoAssets.Server`.
- **No Unchecked Fire-and-Forget Threading**: Never use `ThreadPool.QueueUserWorkItem`, `UnsafeQueueUserWorkItem`, or `Task.Run` without cancellation and error handling (`AsyncFixer03`).
- **No Analyzer Suppressions**: Never add `#pragma warning disable`, `[SuppressMessage]`, or `<NoWarn>` to silence `AsyncFixer`, `Meziantou.Analyzer`, `RS0030`, or Roslyn warnings.

### 3. Clean Code Conventions for .NET 10 / C# 14
- **Value Semantics (`readonly record struct` & `record`)**:
  - Use `readonly record struct` for small, immutable value types, coordinate pairs, bounding boxes, and strongly-typed IDs to eliminate heap allocations.
  - Use `sealed record` for immutable DTOs, commands, domain events, and API payloads; use `class` for stateful services and entities with identity/lifecycle.
- **Modern Pattern Matching**:
  - Prefer exhaustive `switch` expressions, property patterns (`{ Status: ServiceOrderStatus.Dispatched, AssigneeId: not null }`), and list patterns over nested `if`/`else` chains or manual type casts.
- **Source-Generated JSON (`System.Text.Json`)**:
  - Prefer source-generated `JsonSerializerContext` (`[JsonSerializable(typeof(...))]`) for high-throughput serialization paths (e.g., GeoJSON viewport payloads and REST DTOs) instead of reflection-based serialization.
- **Nullable Reference Types & Guard Clauses**:
  - `<Nullable>enable</Nullable>` is enforced solution-wide. Use `ArgumentNullException.ThrowIfNull(arg)` and `ArgumentException.ThrowIfNullOrWhiteSpace(arg)` for parameter validation instead of null-forgiving `!` operators.

---

## Build, test, and run

Use .NET 10. `global.json` pins SDK `10.0.100`, and the test projects target `net10.0`.

```bash
dotnet restore GeoAssets.sln
dotnet build GeoAssets.sln --configuration Release
dotnet test GeoAssets.sln --configuration Release
```

Run the test projects directly when you only need one layer:

```bash
dotnet test tests/GeoAssets.Core.Tests/GeoAssets.Core.Tests.csproj --configuration Release
dotnet test tests/GeoAssets.Commands.Tests/GeoAssets.Commands.Tests.csproj --configuration Release
```

Run a single xUnit test with a `FullyQualifiedName` filter:

```bash
dotnet test tests/GeoAssets.Core.Tests/GeoAssets.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~GeoAssets.Core.Tests.Services.TopoGraphTests.GetNeighbors_UnknownId_ReturnsEmpty"
dotnet test tests/GeoAssets.Commands.Tests/GeoAssets.Commands.Tests.csproj --configuration Release --filter "FullyQualifiedName~GeoAssets.Commands.Tests.Generation.GeoCommandPluginScaffolderTests.Generate_CreatesExpectedAssemblyAndFiles"
```

Useful run commands:

```bash
cd apps/GeoAssets.Web && dotnet run
cd examples/GeoAssets.Examples && dotnet run
```

If you need to build MAUI or mirror the full solution CI locally, install the MAUI workload first:

```bash
dotnet workload install maui
dotnet workload install maui-android
```

There is no dedicated lint command in the repo today. CI validates restore/build/test, and `.github/workflows/dotnet.yml` installs the MAUI Android workload before building the full solution.

## High-level architecture

GeoAssets is a multi-target .NET 10 platform. The main layers are:

- `core/GeoAssets.Core`: domain model, geometry abstractions, provider contracts, topology services, and multi-agent orchestration abstractions.
- `core/GeoAssets.Commands`: MEF-based GIS command contracts, built-in command discovery, and the deterministic command-plugin scaffolder.
- `core/GeoAssets.Workflow`, `core/GeoAssets.Identity`, `core/GeoAssets.IpBilling`, and `core/GeoAssets.Infrastructure.Observability`: domain contracts and rules for service-order workflows, authentication/authorization, IP billing, and OpenTelemetry instrumentation.
- `apps/GeoAssets.Shared`: shared Razor Class Library for both WebAssembly and MAUI; components, localization, CSS, and JavaScript interop live here.
- `apps/GeoAssets.Web`: the Blazor WebAssembly host; it wires authentication, storage, provider plugins, observability decorators, and boot-time provider selection.
- `apps/GeoAssets.Server`: the server host for GeoAssets REST endpoints, identity/project/service-order APIs, and WFS/WMS endpoints backed by PostgreSQL/PostGIS.
- `providers/`, `plugins/commands/`, `workflow/`, `ipbilling/`, and `projects/`: extension layers that add provider implementations, MEF command plugins, workflow persistence/messaging/agents, IP-billing persistence, and project persistence/REST clients without moving those concerns into the core domain.

The main UI entry point is `apps/GeoAssets.Shared/Pages/Index.razor`. It composes the provider pool, layer manager, asset list, asset form, draw toolbar, and `MapContainer`. `MapContextMenu` and `ConfirmDialog` are intentionally rendered outside `.map-area` so their `position: fixed` overlays behave correctly.

Runtime state centers on `IAssetProvider`, but most UI code should use the active workspace exposed through `ActiveAssetProvider`, a proxy over `IProviderPool`. The pool can hold multiple named providers at once: one active entry receives edits, while other open entries can remain visible as overlays. `ActiveAssetProvider` rewires subscriptions when the active entry changes and re-raises `FeatureAdded`, `FeatureUpdated`, `FeatureDeleted`, and `CollectionChanged` so components do not need to know a provider switch happened.

On the client side, boot and persistence are separate concerns. `BootLoaderService` recreates the selected provider plugin from persisted config under `geoassets:boot-config`, makes it the active pool entry, and strips `*_content` keys before saving boot config. `AssetService` handles collection persistence: it loads the saved feature collection on startup, merges custom asset types, and auto-saves after `CollectionChanged` using a 500 ms debounce.

Map rendering crosses a C#↔JavaScript boundary. `MapContainer.razor` owns the `[JSInvokable]` callbacks (`OnFeatureDrawnFromJs`, `OnFeatureEditedFromJs`, `OnFeatureClickedFromJs`, `OnFeatureContextMenuFromJs`, `OnViewportChangedFromJs`). `MapInteropService` is the supported C# bridge into Leaflet and delegates to `window.GeoAssets.*` functions in `apps/GeoAssets.Shared/wwwroot/js/geoassets.js`. For viewport refreshes, the map prefers provider methods that return pre-serialized JSON (`GetInBoundsRawJsonAsync` / `GetInBoundsJsonAsync`) to avoid unnecessary WASM-side parse/serialize work.

Provider extensibility has two paths. UI-configurable providers implement `IProviderPlugin` and are collected by `ProviderPluginRegistry` for the boot dialog and connect flow. Infrastructure-backed providers can also expose `IExternalProviderFactory` registrations that produce `IAssetProvider` instances from connection details.

The command/plugin subsystem is separate from the map/provider flow. `GeoPluginContainer` composes built-in command handlers plus external assemblies matching `GeoAssets.Plugin.*.dll`. `GeoCommandPluginScaffolder` takes a `GeoCommandPluginSpec` and deterministically generates a compilable plugin project. The agent orchestration abstractions that can produce those specs live in `core/GeoAssets.Core/Agents`, while Anthropic-backed examples stay under `examples/MultiAgent/`.

The PostgreSQL provider is server-oriented. `AddGeoAssetsPostgres()` registers the provider factory used to create repository instances, while `AddGeoAssetsDbContextFactory(connectionString)` is a separate registration for services such as the WMS renderer that need short-lived EF Core contexts and should not share the provider cache.

## Key conventions

- GeoJSON and coordinate handling follow RFC 7946 consistently: coordinates are `[longitude, latitude]`, and spatial data uses SRID 4326.
- In the geometry model, coordinate arrays are the serialized source of truth. NetTopologySuite geometries are derived from those arrays; do not flip the source of truth to NTS objects.
- Repository/provider events are architectural, not incidental. If you change providers, decorators, or the active-provider proxy, preserve event propagation semantics.
- Most UI behavior should go through the active `IAssetProvider`, not a concrete provider implementation.
- Provider discovery is registration-driven. Wire new providers through `IProviderPlugin`, `IExternalProviderFactory`, and the existing registry/factory flow instead of hard-coding UI branches.
- Only `apps/GeoAssets.Shared/wwwroot/js/geoassets.js` is loaded by the web and MAUI hosts. `mapInterop.js` and `drawInterop.js` are legacy drafts and should not be used as integration points.
- `apps/GeoAssets.Shared` is the shared UI contract for WebAssembly and MAUI. Prefer changes there over duplicating behavior in host projects.
- Razor component communication follows the existing pattern: data flows down via `[Parameter]`, actions flow up via `EventCallback<T>`. Every Razor component/page in `GeoAssets.Shared`, `GeoAssets.Web`, and `GeoAssets.MAUI` ships as a markup-only `.razor` file plus a `.razor.cs` code-behind partial class (except `apps/GeoAssets.MAUI/Pages/MauiLogin.razor`; see `CLAUDE.md` § Conventions).
- Bootstrap 5.3 (vendored under `apps/GeoAssets.Shared/wwwroot/lib/bootstrap/`) is the base for component styling in `GeoAssets.Shared`, with `--bs-*` custom properties mapped to the Catppuccin Mocha/Latte design tokens in `apps/GeoAssets.Shared/wwwroot/css/geoassets.css`. Layer bespoke CSS in `geoassets.css` on top for anything Bootstrap does not cover.
- MEF command plugins follow the `GeoAssets.Plugin.*` assembly naming pattern and expose handlers with `[ExportGeoCommand]`; built-in and generated commands both rely on that discovery convention.
- Automated tests are split by layer: `tests/GeoAssets.Core.Tests` covers the core domain, topology, agents, and the in-memory/provider logic, while `tests/GeoAssets.Commands.Tests` covers command discovery and command-plugin scaffolding.
