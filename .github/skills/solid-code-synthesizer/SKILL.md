---
name: solid-code-synthesizer
description: >-
  Scaffold immutable value objects, DTOs, segregated interface mocks,
  TestDataBuilders, shared LSP contract suites, and 100%-branch-coverage
  automated unit tests in any programming language with deterministic SOLID
  verification. Use when generating boilerplate, adapters, UI bindings, or
  automated test suites.
---

# SOLID & Categorical Code Synthesizer (GitHub Copilot Agent Skill)

Guides **GitHub Copilot** (IDE Agent Mode, Copilot CLI, Coding Agent, and Code Review) through the 5-stage SOLID & Categorical synthesis workflow for mechanical scaffolding and exhaustive QA across any stack configured in [`.harness/harness.env`](../../../.harness/harness.env).

## Stage-by-Stage Scaffolding & QA Rules

1. **Stage 1 — SRP & Value Semantics**:
   - Emit immutable types for value objects, typed IDs, commands, events, and DTO payloads (`Readonly<T>`, `@dataclass(frozen=True)`, value `struct`, `readonly record struct` / `sealed record`, `data class`).
   - Define immutable pagination and filter models (`PageRequest`, `PagedResult<T>`, strongly typed `FilterCriteria`) for collection/table queries; unbounded collection models and sensitive fields (passwords, tokens) in filter criteria are prohibited.
   - Honor any `pairedFileRules` configured in [`harness.config.json`](../../../harness.config.json).
2. **Stage 2 — ISP & Minimal Mocks**:
   - Keep new interfaces/protocols/traits narrow and consumer-specific (`<= 5` cohesive methods; separate `Reader` from `Writer`).
   - Any query port serving collections for UI tables or lists must require explicit typed filter parameters and pagination contracts (`PageRequest` or cursor) returning paginated envelopes.
   - Mock only narrow segregated ports in unit tests — never emit `NotImplementedError`, `NotImplementedException`, `todo!()`, or `unimplemented!()` stubs in production adapters.
3. **Stage 3 — DIP, Clock & Cancellation Guardrails**:
   - Inject abstract ports and an abstract Clock / `TimeProvider` (using a deterministic fake clock in tests); never read raw system wall-clock directly.
   - Propagate cancellation signals/contexts (`AbortSignal`, `context.Context`, `CancellationToken`) asynchronously without blocking waits or sleeps.
   - Never import ORMs, database drivers, HTTP clients, or UI packages inside `src/core/` or `src/domain/`.
4. **Stage 4 — OCP Matrices & LSP Behavioral Contract Suites (100% Branch Coverage)**:
   - When testing multi-backend ports (SQL, In-Memory, REST/gRPC), build or extend a **Shared Behavioral Contract Test Suite** executed against every concrete adapter to prove Liskov Substitution.
   - Contract test suites for table/collection queries must verify pagination invariants (first/middle/last page slicing, out-of-range empty lists, max page size clamping, and deterministic sorting).
   - Use parameterized/table-driven tests and shared fluent `TestDataBuilders` to cover 100% of branches: happy paths, null/empty guards, boundary values, fake clock transitions, and cancellation aborts.
5. **Stage 5 — Boundary Error Parity & Deterministic Oracle Gate**:
   - Verify expected lookup misses return `null`/`None`/`Option::None`, while write invariant violations across network boundaries test **both** server error serialization and client error reconstruction. Validate pagination bounds (`page >= 1`, `1 <= pageSize <= 100`).
   - Run both deterministic verifiers before finishing (never add `eslint-disable`, `@ts-ignore`, `# noqa`, `#[allow(`, `//nolint`, `#pragma warning disable`, or `<NoWarn>`):

```bash
python3 scripts/verify-solid-contracts.py --target .
./scripts/verify-agent-worktree.sh .
```

If Stage 1 of `verify-agent-worktree.sh` exits `10`, run `FORMAT_FIX_CMD` from `.harness/harness.env` and re-verify until both return `Exit 0`.
