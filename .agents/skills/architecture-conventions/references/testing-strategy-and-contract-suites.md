# Testing Strategy, Shared Contract Suites & Code Quality Metrics

**Status: `current`** — language-agnostic QA and maintainability discipline.

## 1. 100% Branch Coverage on Touched Logic

Target **100% branch coverage** across modified domain and adapter modules:
- Cover every `if`/`else`, `switch`/`match` arm, null/optional coalescing branch, guard clause, boundary value (`0`, `1`, `N`), fake-clock transition, and cancellation path.
- Include at least one test proven to fail without the production change (non-vacuous test proof) and explicit negative-scope tests for authorization/permission rules.

## 2. Shared Behavioral Contract Suites (Proving LSP)

Whenever an interface/port has multiple implementations (e.g., PostgreSQL/SQLite, In-Memory fake, REST/gRPC client, caching/validating decorator):
- Author **one shared abstract/parameterized Contract Test Suite** asserting every behavioral, concurrency, error, and event invariant of the port.
- Execute that exact suite against **every** concrete implementation (unwrapped and decorated) so Liskov Substitution is continuously verified in CI.

## 3. Closing the Server $\leftrightarrow$ Client Transport Seam

Mocking HTTP responses in client tests *and* testing server handlers separately can leave a drift gap if server error envelopes evolve. Pair REST/RPC client repositories with in-memory transport seam tests routing the real client directly through the real server router/handler pipeline.

## 4. Shared `TestDataBuilders` & Code Metrics

- Promote repetitive multi-field domain test setup into fluent `TestDataBuilders` with sensible defaults so tests specify only the attributes relevant to their scenario.
- Use code metrics as concrete refactoring triggers:
  - **Cyclomatic complexity $< 10$** per function/method (extract polymorphic strategies or lookup tables when exceeded).
  - **Interface size $\le 5$ methods** for role ports (`Reader` vs. `Writer`).
  - **Zero empty `catch`/`except` blocks** and deterministic resource disposal (`using`, `defer`, `with`, `Drop`).

## 5. Pagination Contract Testing

Shared contract test suites and repository tests must explicitly verify pagination invariants:
- **Boundary Slicing**: Verify page 1, middle page, and last page slice counts and items match expected sub-ranges.
- **Out-of-Range Requests**: Verify requesting a page index beyond total items returns an empty items collection with accurate total count, never throwing unhandled exceptions.
- **Page Size Clamping**: Verify passing page size `0` or negative defaults safely, and exceeding `MaxPageSize` is strictly clamped.
- **Deterministic Sort Invariant**: Verify that iterating across all sequential pages yields the exact universe of items with zero duplicates and zero omissions.

## 6. Table Filtering Contract Testing

Contract suites and unit tests must verify:
- **Conjunctive Filtering (`AND`)**: Verify that combining multiple active filters returns only records satisfying ALL criteria simultaneously.
- **Empty Filtered Set**: Verify that filters matching zero records return an empty collection with `totalCount = 0` (never null or error).
- **Sensitive Field Isolation**: Verify that sensitive fields (e.g. `password`, `token`, `secret`) are completely absent from filterable column lists, are rejected if passed as query keys, and never appear in generated SQL predicates or telemetry.
- **Filter-Pagination Coordination**: Verify that modifying filters recalculates total matching items and resets the page index to 1.
