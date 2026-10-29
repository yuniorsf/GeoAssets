---
name: solid-code-synthesizer
description: >-
  Synthesize and refactor C# 14 / .NET 10 domain models, Onion ports/adapters,
  workflow rules, and spatial/topology services using a 5-stage SOLID and
  Categorical invariant pipeline. Use when designing, implementing, or reviewing
  production C# code (Branch 2A or /implement-ticket) in GeoAssets.
---

# SOLID & Categorical C# 14 Code Synthesizer (Claude Code Specialist)

Complements [`csharp-conventions`](../csharp-conventions/SKILL.md) with an executable 5-stage synthesis and static-verification workflow tailored for Claude Code's **Deep Reasoning & Domain Architecture** role in GeoAssets.

## 5-Stage Synthesis Workflow

Execute domain synthesis in five progressive stages where each stage's output satisfies the next stage's preconditions ($g \circ f$):

### 1. SRP & Category Objects — Pure Domain Modeling
- Model immutable values, coordinates, bounding boxes, and typed IDs as `readonly record struct`; model commands, events, and DTO payloads as `sealed record`; reserve `class` for stateful services and entities with identity/lifecycle.
- Keep **one axis of domain mutation** per class (cyclomatic complexity $< 10$; cohesive methods).
- **Spatial Invariant**: RFC 7946 `[longitude, latitude]` coordinate arrays (SRID 4326) remain the serialized JSON source of truth on `GeoPoint`/`GeoLineString`/`GeoPolygon`; `NtsGeometry` is lazily derived.
- **Onion Purity**: `core/*` assemblies must have **zero** references to `Microsoft.EntityFrameworkCore`, `Npgsql`, `GeoAssets.Provider.*`, `HttpClient`, or `apps/*` (`ArchitectureTests.cs`).

### 2. ISP & Limit Projections ($\pi_k$) — Segregated Role Interfaces
- Design narrow, consumer-specific interfaces (`<= 5` cohesive members per new port). Split query and mutation responsibilities (`I*Reader` vs. `I*Writer`) so read-only consumers never depend on write operations or bloated test mocks.
- Project cross-service payloads (`apps/GeoAssets.Server` $\leftrightarrow$ `*.Rest`) into minimal DTO records (`JsonSerializerContext` for high-throughput GeoJSON/REST paths).
- Enforce Razor separation: every component/page ships as markup-only `.razor` + `.razor.cs` partial class (sole exception: `apps/GeoAssets.MAUI/Pages/MauiLogin.razor`).

### 3. DIP & Natural Transformations — Abstractions & DI Lifetimes
- Inject all collaborators via primary/constructor DI over `core/` interfaces (`IAssetProvider`, `IGeoAuthorizationService`, `IServiceOrderRepository`) and `System.TimeProvider` (never `DateTime.Now`/`UtcNow` or `DateTimeOffset.Now`/`UtcNow` — `RS0030`).
- Never instantiate concrete infrastructure (`new *DbContext(...)`, `new HttpClient(...)`, `new Postgres*(...)`) inside domain or application services.
- Choose explicit DI lifetimes (`Transient` stateless, `Scoped` per-request/`DbContext`, `Singleton` thread-safe only) and prevent captive dependencies (`Singleton` capturing `Scoped`).
- Forward `CancellationToken` to every async call and apply `.ConfigureAwait(false)` across non-UI library layers (`core/`, `providers/`, `workflow/`).

### 4. OCP & LSP — Pluggable Extension & Behavioral Substitutability
- **OCP**: Extend capabilities via pluggable contracts (`IServiceOrderRule` chains, `IProviderPlugin`, `IExternalProviderFactory`, `[ExportGeoCommand]`, and event-forwarding decorators like `ActiveAssetProvider` / `ObservableServiceOrderRepository`) instead of adding ad-hoc conditionals to core orchestrators.
- **LSP**: Every concrete backend (`EF*`, `Postgres*`, `Rest*`, `Shapefile*`, `InMemory*`) must honor identical behavioral postconditions and event semantics (`FeatureAdded`/`Updated`/`Deleted`/`CollectionChanged`) with **zero `NotImplementedException` stubs**.
- Pair multi-backend repository/provider contracts with shared abstract contract test suites (modeled on `ServiceOrderRepositoryContractTests` in `GeoAssets.Workflow.TestKit`).

### 5. Categorical Effect & Boundary Error Semantics
Follow [`csharp-conventions/references/error-handling-and-result-pattern.md`](../csharp-conventions/references/error-handling-and-result-pattern.md):
- **Expected Absence**: Return `Task<T?>` (`null` / HTTP `404`) or `Try*` shapes for routine lookup misses — never throw exceptions for control flow.
- **Inline Validation / Rule Outcomes**: Use explicit result/error-collector records where callers branch inline on expected validation outcomes.
- **Cross-Boundary Write Invariants (LSP Bijection)**: Throw specific domain exceptions on write/state-machine violations (`InvalidServiceOrderTransitionException`, `ServiceOrderConcurrencyException`, `ServiceOrderAttributeValidationException`) and maintain **bidirectional HTTP status + payload translation** between `apps/GeoAssets.Server` minimal APIs and `*.Rest` client repositories so remote and in-process implementations behave identically.

---

## Mandatory Self-Verification Gate

Before reporting completion (or moving a Jira ticket to `In Review`), run both deterministic verifiers inside the active worktree:

```bash
python3 scripts/verify-solid-contracts.py --target .
./scripts/verify-agent-worktree.sh .
```

Resolve all reported `Exit 10`, `20`, and `30` diagnostics in source — **never** add `#pragma warning disable`, `[SuppressMessage]`, or `<NoWarn>`.

