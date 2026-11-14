# SOLID, Design Patterns & Dependency Injection Lifetimes

**Status: `current`** — language-agnostic structural design guidance.

## 1. SOLID as Concrete Structural Guardrails

- **Single Responsibility (SRP)**: One axis of change per type/module. Separate input parsing, domain invariant evaluation, persistence, and outbound notifications into distinct collaborators.
- **Open / Closed (OCP)**: Add new behaviors by registering a new Strategy, Rule, Decorator, or Plugin implementing an existing port rather than adding branches to a central conditional.
- **Liskov Substitution (LSP)**: Every concrete implementation of a port must honor the port's full behavioral contract. Silent no-ops or throwing `NotImplemented` / `NotSupported` where behavior was promised breaks substitutability.
- **Interface Segregation (ISP)**: Split fat interfaces by actual consumer need (`Reader` vs. `Writer`, `<= 5` cohesive methods) so read-only consumers and unit-test mocks never carry unused members.
- **Dependency Inversion (DIP)**: Inject abstract ports via constructors/parameters; never instantiate concrete infrastructure adapters inside domain services.

## 2. High-Leverage Patterns (Use Only Where Earned)

- **Strategy / Rule Chain**: Composable, independently testable policy units.
- **Decorator / Proxy**: Layer observability spans, caching, validation, or active-provider routing transparently around a port while preserving event and error semantics.
- **Factory**: Centralize runtime adapter selection (config, tenant, environment) behind one creation boundary.
- **Builder**: Reserve for complex, variably configured objects and shared `TestDataBuilders`; skip for simple types where constructors/literals are clear.

## 3. Dependency Injection Lifetimes & Captive Dependencies

Whether using an explicit DI container or manual composition roots, treat lifetime selection as a **correctness** decision:

- **Transient / Per-Call**: Safe default for lightweight stateless collaborators.
- **Scoped / Per-Request**: Required for unit-of-work state, DB sessions/transactions, and per-tenant request context.
- **Singleton / Process-Wide**: Safe **only** for genuinely stateless or concurrency-safe components. Never allow a `Singleton` to capture a `Scoped` dependency ("captive dependency" leak across requests).
