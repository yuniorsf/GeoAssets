---
name: solid-code-synthesizer
description: >-
  Synthesize and refactor pure domain models, Clean/Hexagonal ports and
  adapters, business rule chains, and cross-boundary services in any language
  using a 5-stage SOLID and Categorical invariant pipeline. Use when designing,
  implementing, or reviewing production code (Branch 2A or /implement-ticket).
---

# SOLID & Categorical Code Synthesizer (Claude Code Specialist)

Complements [`architecture-conventions`](../architecture-conventions/SKILL.md) with an executable 5-stage synthesis and static-verification workflow tailored for Claude Code's **Deep Reasoning & Domain Architecture** role across any stack configured in `.harness/harness.env`.

## 5-Stage Synthesis Workflow

1. **Stage 1 — SRP & Category Objects (Pure Domain Core)**:
   - Model immutable value objects, typed IDs, commands, events, and DTOs with immutable value primitives; keep stateful entities/services focused on a **single axis of mutation** (cyclomatic complexity $< 10$).
   - Model explicit pagination contracts (`PageRequest`, `PagedResult<T>`) and strongly typed `FilterCriteria` for collection/table queries; unbounded collection models and sensitive fields (passwords, tokens) in filter criteria are prohibited.
   - **Domain Purity**: Files under `domainCoreDirs` (`src/core/`, `src/domain/`) must have **zero** imports of ORMs, database drivers, HTTP clients, message brokers, or UI frameworks.
2. **Stage 2 — ISP & Limit Projections ($\pi_k$) (Segregated Role Ports)**:
   - Design narrow, consumer-specific interfaces/protocols/traits (`<= 5` cohesive methods; split `Reader` queries from `Writer` mutations).
   - Any query port serving collections for UI tables or lists must require explicit typed filter parameters and pagination contracts (`PageRequest` or cursor) returning paginated envelopes.
   - Project network/messaging boundaries into minimal schema-validated DTOs.
3. **Stage 3 — DIP & Natural Transformations (Port Injection, Clock & Cancellation)**:
   - Inject collaborators exclusively via abstract ports + an abstract **Clock / TimeProvider** (never call raw system wall-clock directly in domain logic).
   - Propagate cancellation signals/contexts (`context.Context`, `AbortSignal`, `CancellationToken`) end-to-end without sync-over-async thread blocking; avoid captive DI lifetime bugs.
4. **Stage 4 — OCP & LSP (Pluggable Extension & Behavioral Substitutability)**:
   - Extend behavior via open Strategy, Rule Chain, Decorator, or Plugin ports rather than editing stable core orchestrators.
   - Guarantee Liskov Substitution across all adapters (SQL, In-Memory, REST/gRPC) with **zero unimplemented production stubs** (`NotImplementedError`, `NotImplementedException`, `todo!()`, `unimplemented!()`), backed by shared abstract Contract Test Suites verifying boundary pagination (first/middle/last page slicing, out-of-range empty lists, max page size clamping, and deterministic sorting) and conjunctive multi-filter execution with sensitive field rejection.
5. **Stage 5 — Categorical Effect & Boundary Error Semantics**:
   - Use compiler-checked Option/Nullable types for expected lookup absence (`null` / `None` / `Option::None` $\to$ HTTP `404`), explicit validation/result structures for inline multi-error branching, validate pagination boundaries (`page >= 1`, `1 <= pageSize <= 100`), and documented **bidirectional transport error translation** (`400`/`404`/`409`) so remote and in-process adapters behave identically.

## Mandatory Self-Verification Gate

Run both deterministic verifiers before reporting completion (zero linter, typechecker, or compiler suppressions permitted):

```bash
python3 scripts/verify-solid-contracts.py --target .
./scripts/verify-agent-worktree.sh .
```
