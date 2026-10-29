---
name: solid-code-synthesizer
description: >-
  Scaffold C# 14 / .NET 10 value objects, segregated interfaces, Razor
  code-behind components, NSubstitute mocks, DomainTestDataBuilders, and LSP
  contract / xUnit 100%-branch-coverage test suites with deterministic SOLID
  verification. Use when generating boilerplate, DTOs, adapters, UI bindings,
  or automated unit/contract tests in GeoAssets.
---

# SOLID & Categorical C# 14 Synthesizer (GitHub Copilot Agent Skill)

Guides **GitHub Copilot** (IDE Agent Mode, Copilot CLI, Coding Agent, and Code Review) through GeoAssets' 5-stage SOLID & Categorical synthesis workflow for .NET 10 / C# 14 implementation and exhaustive xUnit QA.

## 1. Stage-by-Stage C# 14 Scaffolding & QA Rules

### Stage 1 — SRP & Value Semantics (`readonly record struct` & `sealed record`)
- Emit `readonly record struct` for small immutable value types, coordinate pairs, bounding boxes, and strongly-typed IDs (zero heap allocation).
- Emit `sealed record` for immutable DTOs, commands, domain events, and REST payloads; prefer source-generated `JsonSerializerContext` (`[JsonSerializable(typeof(...))]`) on high-throughput GeoJSON/REST paths.
- Keep GeoJSON coordinates strictly RFC 7946 `[longitude, latitude]` (SRID 4326) with coordinate arrays as the serialized source of truth.
- Split every Blazor component into markup-only `.razor` + `.razor.cs` partial class (`[Parameter]` down, `EventCallback<T>` up; never add inline `@code {}`, except `apps/GeoAssets.MAUI/Pages/MauiLogin.razor`).

### Stage 2 — ISP & Minimal Mocks (Role-Segregated Contracts)
- Keep new interfaces narrow and role-specific (`<= 5` cohesive methods; separate `I*Reader` from `I*Writer`).
- In xUnit tests, mock only the narrow segregated port via `NSubstitute` (`Substitute.For<IOrderReader>()`) rather than over-mocking large concrete graphs.
- Never emit `throw new NotImplementedException()` stubs in production adapters.

### Stage 3 — DIP & Deterministic Guardrails (`TimeProvider` & `CancellationToken`)
- Validate reference inputs with `ArgumentNullException.ThrowIfNull(arg)` and strings with `ArgumentException.ThrowIfNullOrWhiteSpace(arg)` (`<Nullable>enable</Nullable>` — avoid null-forgiving `!`).
- Never emit `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now`, or `DateTimeOffset.UtcNow` (`RS0030`). Always inject `System.TimeProvider` in production types and `FakeTimeProvider` in xUnit tests.
- Never emit `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`, or `Thread.Sleep(...)` (`AsyncFixer02`). Always `await` asynchronously and accept/forward `CancellationToken`.
- Never emit `Microsoft.EntityFrameworkCore`, `Npgsql`, or `GeoAssets.Provider.*` references inside `core/` projects.

### Stage 4 — OCP & LSP Behavioral Contract Test Suites (100% Branch Coverage)
- **LSP Contract Suites**: When testing an implementation of a multi-backend port (e.g., `IServiceOrderRepository`, `IAssetProvider`), inherit or extend shared abstract contract suites (following `ServiceOrderRepositoryContractTests` in `GeoAssets.Workflow.TestKit`) so EF Core, PostgreSQL, In-Memory, and REST implementations prove identical behavioral and event-propagation semantics.
- **OCP Strategy / Rule Matrices**: Use exhaustive C# 14 `switch` expressions in production and `[Theory]` + `[InlineData]` / `MemberData` matrices in xUnit to verify every rule/plugin permutation without duplicating test bodies.
- **Exhaustive Branch Coverage**: Cover happy paths, null/whitespace guard clauses, boundary geometry/topology conditions, `FakeTimeProvider` clock advances, and cancelled `CancellationToken` paths using shared fluent builders (`DomainTestDataBuilders`).

### Stage 5 — Boundary Error Parity & Deterministic Oracle Verification
- Verify expected "not found" queries return `null` (`Task<T?>`) rather than throwing, while write invariant violations across REST boundaries test **both** server status/payload serialization (`400`/`409`/`404`) and client exception reconstruction (`Rest*Repository`).
- Run both deterministic verifiers before completing any task (never add `#pragma warning disable`, `[SuppressMessage]`, or `<NoWarn>`):

```bash
python3 scripts/verify-solid-contracts.py --target .
./scripts/verify-agent-worktree.sh .
```

If Stage 1 of `verify-agent-worktree.sh` exits `10` (formatting drift), run `dotnet format style GeoAssets.sln` and re-verify until both scripts return `Exit 0`.

