---
name: solid-code-synthesizer
description: >-
  Synthesize and verify C# 14 / .NET 10 code through a 5-stage SOLID and
  Categorical invariant pipeline (SRP pure domain types, ISP segregated ports,
  DIP Onion injection, OCP/LSP behavioral contract tests, and boundary error
  semantics). Use when generating, refactoring, or auditing domain models,
  services, repositories, or providers in GeoAssets.
---

# SOLID & Categorical C# 14 Code Synthesizer

Enforces **SOLID structural principles**, **Onion Architecture**, and **Category Theory target-code invariants** across synthesized C# 14 / .NET 10 artifacts using a deterministic 5-stage synthesis and verification pipeline.

- Read [`references/csharp14-solid-categorical-matrix.md`](references/csharp14-solid-categorical-matrix.md) for the complete SOLID $\leftrightarrow$ Category Theory $\leftrightarrow$ Roslyn/xUnit verification mapping.
- Respect [`.claude/skills/csharp-conventions/SKILL.md`](../../../.claude/skills/csharp-conventions/SKILL.md) as the authoritative source for GeoAssets C# idioms.

## Verification Oracles

1. **Fast SOLID & Guardrail Verifier**:
   ```bash
   python3 scripts/verify-solid-contracts.py --target <repo-or-worktree> [--files <paths...>]
   ```
2. **Full Solution Gatekeeper**:
   ```bash
   ./scripts/verify-agent-worktree.sh <repo-or-worktree>
   ```

---

## 5-Stage Progressive Code Synthesis Pipeline

Execute or audit code synthesis across these five ordered stages so each stage's postconditions satisfy the next stage's preconditions ($g \circ f$):

### Stage 1 — SRP & Category Objects (Pure Domain Types)
- **Synthesize**:
  - `readonly record struct` for small immutable value objects, strongly-typed IDs, coordinates, and bounding boxes.
  - `sealed record` for immutable commands, domain events, and REST DTOs.
  - Focused `class` entities/services with a **single axis of mutation** (keep methods cohesive, cyclomatic complexity $< 10$).
  - Preserve GeoAssets' geometry invariant: RFC 7946 `[longitude, latitude]` coordinate arrays (SRID 4326) are the serialized source of truth; `NtsGeometry` is lazily derived.
- **Gate**: Zero `Microsoft.EntityFrameworkCore`, `Npgsql`, `HttpClient`, or `GeoAssets.Provider.*` references inside `core/`.

### Stage 2 — ISP & Categorical Projections (Segregated Role Ports)
- **Synthesize**:
  - Fine-grained, consumer-specific interfaces (`<= 5` cohesive methods per new role port; split read queries from mutations, e.g., `IServiceOrderReader` vs. `IServiceOrderWriter`).
  - Minimal DTO projections (`JsonSerializerContext` source generation where high-throughput) for REST/messaging boundaries.
  - UI components split strictly into markup-only `.razor` + `.razor.cs` code-behind partial class (except `MauiLogin.razor`).
- **Gate**: Zero fat "god interfaces" and zero dummy/no-op interface implementations.

### Stage 3 — DIP & Natural Transformations (Inversion & DI Lifetimes)
- **Synthesize**:
  - Constructor injection exclusively over domain abstractions (`IAssetProvider`, `IGeoAuthorizationService`, `System.TimeProvider`).
  - Forward `CancellationToken` across all async morphisms (`ConfigureAwait(false)` in non-UI libraries: `core/`, `providers/`, `workflow/`).
  - Match DI lifetimes carefully (`Transient` stateless, `Scoped` per-request/`DbContext`, `Singleton` thread-safe only — never capture a `Scoped` service inside a `Singleton`).
- **Gate**: Zero `new *DbContext(...)`, `new HttpClient(...)`, or `DateTime.Now`/`UtcNow` (`RS0030`) inside domain services.

### Stage 4 — OCP & LSP (Extensible Strategies & Behavioral Contract Suites)
- **Synthesize**:
  - Extend behavior via open extension points (`IServiceOrderRule` chains, `IProviderPlugin`, `IExternalProviderFactory`, `[ExportGeoCommand]`, event-preserving decorators like `ActiveAssetProvider`) instead of modifying stable core dispatchers.
  - Guarantee Liskov Substitution: every concrete adapter (`EF*`, `Postgres*`, `Rest*`, `Shapefile*`, `InMemory*`) must honor base preconditions/postconditions and event propagation (`FeatureAdded`/`Updated`/`Deleted`/`CollectionChanged`) with **zero `NotImplementedException` stubs**.
  - Pair multi-backend interfaces with shared abstract contract test suites (following `ServiceOrderRepositoryContractTests` in `GeoAssets.Workflow.TestKit`).
- **Gate**: Shared contract tests + 100% branch coverage xUnit `[Fact]`/`[Theory]` suites pass.

### Stage 5 — Categorical Effect & Boundary Error Semantics
- **Follow GeoAssets' Boundary Rule** ([`error-handling-and-result-pattern.md`](../../../.claude/skills/csharp-conventions/references/error-handling-and-result-pattern.md)):
  - **Expected Absence / Inline Branching**: Return explicit nullable types (`T?` with `<Nullable>enable</Nullable>`), `Try*` methods, or narrow validation/rule `Result` objects — never throw exceptions for routine "not found" reads (`GetByIdAsync` $\to$ `null` / HTTP `404`).
  - **Invariant Violations Across Process Boundaries**: Throw specific typed domain exceptions on invalid writes/transitions (`InvalidServiceOrderTransitionException`, `ServiceOrderConcurrencyException`, etc.) and implement **bidirectional HTTP translation** (`Map*Api` structured `400`/`409`/`404` $\leftrightarrow$ `Rest*Repository` exception reconstruction) so remote REST adapters remain Liskov-substitutable for in-process EF Core adapters.
- **Gate**: Run `python3 scripts/verify-solid-contracts.py` (`Exit 0`) followed by `./scripts/verify-agent-worktree.sh` (`Exit 0`) with zero `#pragma warning disable`, `[SuppressMessage]`, or `<NoWarn>`.

