# Error Handling & Cross-Boundary Translation (LSP Parity)

**Status: `current`** — language-agnostic boundary error semantics.

## 1. Distinguish Expected Absence, Inline Validation, and Invariant Faults

1. **Expected Absence ($1 + A$)**:
   - Lookup misses (`findById` / `getParent`) are normal outcomes, not exceptional faults. Return compiler-checked Option/Nullable types (`T | null`, `T | None`, `Option<T>`, `T?`) and map them cleanly to `404 Not Found` at transport edges.
2. **Expected Inline Branching (`Result<T, E>` / Error Collectors)**:
   - Use explicit `Result` types or structured validation-error collectors wherever callers routinely branch inline on expected validation or policy outcomes (and natively across Rust/Go).
3. **Domain Write / Transition Invariant Violations**:
   - Represent state-machine violations, optimistic-concurrency conflicts, and schema invariant breaks with distinct, strongly typed domain errors/exceptions carrying structured fields (not opaque free-text strings).

## 2. Bidirectional Transport Translation Preserves Liskov Substitution

Domain errors do not survive raw across HTTP/gRPC/messaging wires unless translated deliberately on **both** ends:

- **Server / Producer Boundary**: Map the closed set of domain errors deterministically onto status codes + minimal structured payloads (e.g., not-found $\to$ `404`, optimistic-concurrency conflict $\to$ `409`, validation/transition failure $\to$ `400` with structured fields).
- **Client / Consumer Adapter**: Parse the status code + structured fields back into the **exact same domain error type** that an in-process SQL/In-Memory adapter produces.
- Cross-reference both halves in docstrings and verify the seam with automated round-trip contract tests so callers remain 100% agnostic to whether a port is backed by local persistence or a remote API.
