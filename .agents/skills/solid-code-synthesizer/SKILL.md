---
name: solid-code-synthesizer
description: >-
  Synthesize and verify source code in any programming language through a
  5-stage SOLID and Categorical invariant pipeline (SRP pure domain types, ISP
  segregated ports, DIP Clean/Hexagonal injection, OCP/LSP behavioral contract
  suites, and boundary error semantics). Use when generating, refactoring, or
  auditing domain models, services, repositories, or adapters.
---

# SOLID & Categorical Polyglot Code Synthesizer

Enforces **SOLID structural principles**, **Clean / Hexagonal / Onion Architecture**, and **Category Theory target-code invariants** across Python, TypeScript/JavaScript, Go, Rust, C#, Java, and Kotlin codebases.

- Read [`references/solid-categorical-polyglot-matrix.md`](references/solid-categorical-polyglot-matrix.md) for per-language idioms and verification mappings, and [`references/csharp14-solid-categorical-matrix.md`](references/csharp14-solid-categorical-matrix.md) for C# 14 / .NET 10 mappings.
- Read [`harness.config.json`](../../../harness.config.json) and [`.harness/harness.env`](../../../.harness/harness.env) for active project boundaries and commands.

## Deterministic Verification Oracles

1. **Fast Polyglot SOLID & Guardrail Verifier**:
   ```bash
   python3 scripts/verify-solid-contracts.py --target <repo-or-worktree> [--files <paths...>]
   ```
2. **Full 3-Stage Toolchain Gatekeeper**:
   ```bash
   ./scripts/verify-agent-worktree.sh <repo-or-worktree>
   ```

---

## 5-Stage Progressive Code Synthesis Pipeline

### Stage 1 — SRP & Category Objects (Pure Domain Types)
- Model immutable value objects, typed IDs, commands, events, and DTOs using the language's value-type primitives (frozen `@dataclass`, `readonly` TS types, Go value structs, Rust `struct`/`enum`, C# `readonly record struct`/`sealed record`, Java/Kotlin `record`/`data class`).
- Define explicit immutable pagination and filter models (`PageRequest`, `PagedResult<T>`, strongly typed `FilterCriteria`); unbounded collection models and sensitive fields (passwords, tokens) in filter contracts are prohibited.
- Keep **one axis of domain mutation** per class/module (cyclomatic complexity $< 10$).
- **Gate**: Zero ORM, database, HTTP, message-broker, or UI imports inside `domainCoreDirs` (`src/core/`, `src/domain/`).

### Stage 2 — ISP & Categorical Projections (Segregated Role Ports)
- Define narrow, consumer-specific interfaces/protocols/traits (`<= 5` cohesive methods; separate `Reader` queries from `Writer` mutations).
- Query ports serving tabular data or collection feeds must require typed filter parameters and pagination contracts (`PageRequest` / cursor) returning paginated envelopes.
- Project cross-service boundary contracts into minimal schema-validated DTOs (OpenAPI, Protobuf, JSON Schema, Zod, Pydantic).
- **Gate**: Zero bloated "god interfaces" and zero dummy/no-op interface implementations.

### Stage 3 — DIP & Natural Transformations (Inversion, Clock & Cancellation)
- Inject collaborators exclusively via abstract ports + an abstract **Clock / TimeProvider** (never read raw system wall-clock directly inside domain logic).
- Propagate structured cancellation/context (`context.Context`, `AbortSignal`, `CancellationToken`) across all async operations without blocking sync-over-async waits.
- **Gate**: Zero direct instantiation of concrete infrastructure (`HttpClient`, DB pools/contexts, SDK clients) inside domain core modules.

### Stage 4 — OCP & LSP (Pluggable Strategies & Shared Contract Test Suites)
- Extend behavior via open extension points (Strategy, Rule Chain, Decorator, Plugin Registry) rather than proliferating ad-hoc conditionals in core services.
- Guarantee Liskov Substitution: every concrete adapter (SQL, In-Memory, REST/gRPC) must satisfy identical preconditions, postconditions, and event semantics with **zero unimplemented production stubs** (`NotImplementedError`, `NotImplementedException`, `todo!()`, `unimplemented!()`).
- Pair multi-backend ports with a **Shared Behavioral Contract Test Suite** executed against every concrete implementation. Query contract suites must verify pagination invariants (first/middle/last page slicing, out-of-range empty lists, max page size clamping, deterministic sorting) and conjunctive multi-filter execution with sensitive field rejection.

### Stage 5 — Categorical Effect & Boundary Error Semantics
- **Expected Absence ($1 + A$)**: Use compiler-checked Option/Nullable types (`T | None`, `T | undefined | null`, `Option<T>`, `T?`) — never throw exceptions for routine "not found" lookups.
- **Expected Inline Validation / Rule Outcomes**: Use explicit `Result<T, E>` / error-collector structures where callers branch inline on expected validation failures (or natively in Rust/Go). Validate pagination bounds (`page >= 1`, `1 <= pageSize <= 100`).
- **Cross-Boundary Invariant Parity**: Maintain a documented **bidirectional translation** between domain errors and transport status codes/payloads (HTTP `400`/`404`/`409` or gRPC codes) so remote client adapters and local adapters remain Liskov-substitutable.
- **Gate**: Achieve `Exit 0` on `verify-solid-contracts.py` and `verify-agent-worktree.sh` with **zero** linter, typechecker, or compiler suppression directives.
