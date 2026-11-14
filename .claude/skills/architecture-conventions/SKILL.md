---
name: architecture-conventions
description: >-
  Language-agnostic software architecture, Clean/Hexagonal/DDD boundaries,
  SOLID and GoF patterns, DI lifetimes, async cancellation and clock
  abstraction, boundary error translation, and contract testing conventions. Use
  when designing, writing, or reviewing code in any programming language.
---

# Architecture & Engineering Conventions (Language-Agnostic)

Distilled, lazy-loaded engineering guidance across any programming stack configured in [`.harness/harness.env`](../../../.harness/harness.env). Read only the reference file relevant to the current task — never load all reference files speculatively.

## References

| File | Topic | Status |
|---|---|---|
| [clean-and-hexagonal-architecture.md](references/clean-and-hexagonal-architecture.md) | Bounded Contexts, Entities vs. Value Objects, Aggregates, Clean/Hexagonal/Onion dependency rules | `current` |
| [solid-and-design-patterns.md](references/solid-and-design-patterns.md) | SOLID guardrails, Strategy/Decorator/Factory/Builder/Command/PubSub, DI lifetimes & captive-dependency prevention | `current` |
| [async-cancellation-and-clock-abstraction.md](references/async-cancellation-and-clock-abstraction.md) | Injected Clock/TimeProvider over raw system wall-clock, end-to-end cancellation propagation, zero sync-over-async | `current` |
| [error-handling-and-boundary-translation.md](references/error-handling-and-boundary-translation.md) | Option/Nullable for absence, Result/Validator for inline branching, bidirectional HTTP/RPC error translation for LSP parity | `current` |
| [testing-strategy-and-contract-suites.md](references/testing-strategy-and-contract-suites.md) | 100% branch coverage, Test Data Builders, shared abstract LSP Contract Test Suites, API seam tests, code-quality metrics | `current` |

## Mandatory Architectural Invariant: Collection & Table Pagination

Every query port, repository method, and API transport endpoint serving tabular data or collection feeds must implement explicit pagination (`PageRequest`/`PagedResult` contracts, limit/offset, or keyset cursor):
1. **Zero Unbounded Queries**: Repository query ports must never expose unbounded collection fetches (`GetAll()`, `SELECT *` without bounds).
2. **Deterministic Ordering**: Paginated queries must define deterministic sort criteria (primary sort + unique identifier tie-breaker) to prevent pagination drift and duplicate records across pages.
3. **Bound Clamping**: Enforce minimum (`page >= 1`, `pageSize >= 1`) and maximum (`pageSize <= MaxPageSize`, e.g. 100) constraints at the API boundary to prevent resource exhaustion.

## Mandatory Architectural Invariant: Table Filtering & Sensitive Field Exclusion

Every query port and client controller serving tabular data must implement structured, composable filtering:
1. **Conjunctive Evaluation (AND Semantics)**: Active filter predicates compose via conjunction (`AND`) — only records satisfying all active criteria simultaneously are returned.
2. **Explicit Column Whitelists**: Only explicitly whitelisted columns may be filtered or searched.
3. **Strict Sensitive Field Ban**: Passwords, credential hashes, security tokens, PINs, or private keys must NEVER be accepted as filter criteria, mapped to query predicates, or searched via reflection.
4. **Pagination Synchronization**: Filtering executes before pagination slicing; filter changes must immediately reset page to 1 and reflect the filtered total count.
