# Design patterns, SOLID, and dependency injection in .NET

**Status: `current`** — GoF patterns and SOLID are timeless; the DI/.NET Generic
Host and cloud-pattern material is current .NET 10 guidance.

**Source**: Gabriel Baptista & Francesco Abbruzzese, *Software Architecture with
C# 14 and .NET 10*, 5th Edition (Packt Publishing, 2026), Chapter 6 "Design
Patterns and .NET Implementation." Distilled and paraphrased.

## SOLID as the guardrails, patterns as the applied solutions

SOLID doesn't give you code recipes — it defines the *qualities* good
structure should have; design patterns are proven ways of *achieving* those
qualities in practice. Worth checking, when studying any pattern, which SOLID
principle it's actually reinforcing:

- **Single responsibility** — one reason to change per module. A service that
  validates input, applies business rules, persists, *and* sends
  notifications has (at least) four reasons to change; split it.
- **Open-closed** — extend without modifying stable code. In .NET this is the
  strategy pattern, middleware pipelines, and DI: new behavior arrives as a
  new implementation of an existing abstraction, not a new branch in an
  existing conditional.
- **Liskov substitution** — a subtype must be swappable for its base type
  without surprising callers. A base abstraction where one implementation
  silently no-ops or throws `NotSupportedException` where the contract
  promised behavior breaks this, and makes the whole hierarchy fragile to
  depend on.
- **Interface segregation** — a fat interface forces every implementer to
  depend on members it doesn't need, which increases coupling and mocking
  cost in tests. Split by actual consumer need (e.g. a read-only consumer
  shouldn't have to implement write members) rather than by "everything this
  concept could ever need."
- **Dependency inversion** — depend on abstractions, not concrete
  collaborators (e.g. an interface for outbound email, not a concrete SMTP
  client), so the concrete choice can change or be swapped for a test double
  without touching the dependent code.

## The patterns worth having ready

- **Builder** — decouples constructing a complex, variably-configured object
  from *using* it, typically via a fluent chain of `With...()` calls returning
  `this`. Skip it when a plain constructor or object initializer is already
  clear — introducing a builder for a simple object is pure overhead.
- **Factory** — centralizes "which concrete implementation of this
  abstraction do I need right now" behind one creation point, keyed off
  runtime configuration (region, environment, feature flag). Keeps consumers
  coupled to the abstraction, never to the concrete types.
- **Singleton** — one instance for the process lifetime. Two live concerns:
  thread-safety of any mutable state it holds, and — in modern .NET — that a
  DI container registering a service with **Singleton lifetime** covers this
  intent for you; reach for a hand-written classic Singleton only outside a
  DI-managed context.
- **Proxy** — an object that controls access to another, most commonly to
  defer expensive creation/loading until actually needed (lazy loading). EF
  Core's own lazy-loading proxies for navigation properties are a built-in
  example of this exact pattern already in the framework — worth knowing
  before reaching for a hand-rolled proxy where EF Core already offers one.
- **Command** — encapsulates a request as an object, decoupling the invoker
  from the action and enabling independent evolution, queuing, logging,
  retry, undo/redo per command type. Distinct from **Memento** (which
  captures/restores internal *state*, not a request). ASP.NET Core MVC's
  `IActionResult` hierarchy is a real, already-in-the-framework use of this
  pattern. Skip it for a handful of simple, unlikely-to-grow direct method
  calls — the indirection isn't free.
- **Publisher/Subscriber** — conceptually related to **Observer**, but scoped
  differently: Observer is typically in-process, direct object relationships;
  Pub/Sub decouples publisher from subscribers through a broker, and is the
  right shape once you have an indefinite/large number of independent
  consumers, especially across process/service boundaries. Given the
  operational complexity of building a broker yourself, prefer an existing
  one (Azure Service Bus, RabbitMQ) over a hand-rolled implementation. Skip
  it for simple, synchronous, tightly-related-component interactions.

## Dependency injection — three injection styles, three lifetimes

**Injection style** (how a dependency reaches a class):
- **Constructor injection** — the default choice. Makes required dependencies
  explicit and guarantees the object is never in a half-valid state.
- **Property injection** — reserve for genuinely optional or
  framework-managed dependencies; overusing it makes an object's valid
  configuration state unpredictable from the outside.
- **Method injection** — a dependency needed only for one specific call,
  avoiding storing it in the object's permanent state for something it only
  needs occasionally.

**Container lifetime** (how long a resolved instance lives) — this is a real
correctness decision, not just a performance knob:
- **Transient** — a new instance every time it's requested. Default-safe
  choice for lightweight, stateless services.
- **Scoped** — one instance per scope (a web request, by default; a custom
  scope for something like per-tenant isolation in a multi-tenant app).
  Usually right for anything that needs to share context across one
  logical operation/unit of work — e.g. a `DbContext`.
- **Singleton** — one instance for the whole app lifetime, shared across all
  requests. Only safe for stateless services, or state that's genuinely
  thread-safe for concurrent use — the same caution as the classic Singleton
  pattern, now expressed as a registration choice instead of hand-written
  code.

Picking the wrong lifetime is a concrete bug source, not just a style
preference: a Scoped/Transient service accidentally captured by a Singleton
("captive dependency") holds onto state across requests it was never designed
to share, and a stateful service registered Singleton without thread-safety
becomes a concurrency bug under load.

## .NET's own pattern usage

**.NET Generic Host** (`Host.CreateApplicationBuilder`) isn't itself a single
pattern — it combines Builder (fluent configuration of services/logging before
`Build()`) with the DI container and a composite-style host for multi-service
apps (background/hosted services registered alongside each other). It's the
common foundation across console apps, web apps, and Blazor hosts alike — not
tied to one application style.

## Cloud and AI-agent patterns (pointer-level)

Cloud-native patterns worth knowing exist even where GeoAssets doesn't
currently need them: **Bulkhead Isolation** (fault containment between
components), **Cache-Aside**, **Circuit-breaker**, **CQRS**, **Retry** (Polly
is the standard .NET library for this), plus Pub/Sub already covered above.

**AI-agent orchestration patterns** are an emerging category for breaking a
complex problem into specialized units of work run sequentially, concurrently,
or grouped — e.g. sequential orchestration is structurally similar to the
classic **Pipes and Filters** pattern, but with non-deterministic stages
instead of fixed procedural ones, which makes validation, context propagation,
latency, and failure handling first-class design concerns in a way a
traditional pipeline doesn't need to worry about. The stated guidance: start
with the simplest workable option (a direct model call, or a single agent with
tools) and only adopt multi-agent orchestration once the problem genuinely
needs specialized collaboration or parallelism — the coordination/cost
overhead isn't free.

## Where this would apply in GeoAssets

- `WorkflowPrincipalFactory` (`apps/GeoAssets.Shared/Services`) is a small,
  clean example of the DI/dependency-inversion shape described above: it
  depends on `IGeoAuthorizationService` (an abstraction), not a concrete
  identity implementation — exactly the "depend on `IEmailSender`, not a
  concrete SMTP client" shape from the Dependency Inversion section.
- `ServiceOrderRules`' deny-overrides rule chain (`core/GeoAssets.Workflow/Rules`)
  is architecturally close to the Command pattern's motivation — each
  `IServiceOrderRule` is an independently evolvable, pluggable unit rather
  than a branch in one large conditional — without literally being Command
  (no queuing/undo need here, so that's the right amount of pattern, not more).
  The **XD01-4** implementation (dispatch-recipient AND-composition, see
  `Authorization.md` §6) is a concrete example of open-closed in action: it
  extended the rule chain's capability by adding a configurable grants map to
  an existing rule rather than modifying the chain's evaluation logic itself.
- The AI-agent orchestration guidance here maps directly onto
  `workflow/GeoAssets.Workflow.Agents` (`ServiceOrder.md` §9) — its
  `CreateServiceOrderExecutor`/`DispatchServiceOrderExecutor` pair, wired into
  a Microsoft Agent Framework graph, is exactly the "simplest workable
  option" scope this chapter recommends starting from (two narrow executors
  driving the same domain calls a human uses) rather than a larger
  multi-agent orchestration — worth keeping in mind as a deliberate scope
  boundary if that module grows.
