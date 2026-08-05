# Domain-Driven Design: bounded contexts, aggregates, CQRS

**Status: `current`** — DDD (Eric Evans) is a two-decade-old methodology;
nothing here is version-gated. The Onion-architecture and CQRS material is
standard current .NET architectural practice.

**Source**: Gabriel Baptista & Francesco Abbruzzese, *Software Architecture with
C# 14 and .NET 10*, 5th Edition (Packt Publishing, 2026), Chapter 7
"Understanding the Different Domains in Software Solutions." Distilled and
paraphrased.

## Why DDD exists: two specific problems

1. **No single expert knows the whole domain** of a large enterprise system —
   knowledge is inherently split across people.
2. **Each expert speaks a different vocabulary**, and the same word can mean
   genuinely different things in different parts of an org (a "customer" to
   billing is not the same shape of thing as a "customer" to fulfillment).
   For code to be validated by the actual domain expert (not just by another
   developer), it has to speak *their* language, not a generic one.

DDD's answer to both is to stop building one unified data model with
projections for each subsystem (which requires getting the *entire*
organization to agree on every attribute/relationship — feasible at 20 people,
organizationally impossible at 2,000) and instead split the system into
**Bounded Contexts**: separate models, each with its own **Ubiquitous
Language** — the shared vocabulary between the domain experts and the dev team
working on *that* context, and nothing else.

A practical side-effect worth knowing: a monolithic shared data model also
becomes a technical bottleneck as a system grows — write parallelism needs
sharding, and it's difficult to shard a single, tightly interconnected model
cleanly. Splitting by bounded context (and giving each its own storage) sidesteps
this, not just the organizational problem.

## Bounded Contexts

- **Add a new boundary whenever the meaning of a term changes** as you cross
  from one part of the org to another — that's the signal a distinct domain of
  expertise exists.
- **Context Map**: the document recording every Bounded Context and its
  relationships to the others. It lets teams work independently on their own
  context while still having a clear, explicit picture of how it connects to
  everything else — without needing continuous cross-team coordination.
- **Continuous Integration** across contexts (meetings + simplified system
  prototypes) is still necessary to verify the independently-evolving pieces
  actually integrate into the behavior the whole app needs.
- **Team collaboration patterns** for how two teams owning related contexts
  work together (these describe team relationships, not just system
  relationships):
  - **Partner** — the default. Both teams have a mutual dependency and
    jointly decide the interface between their contexts.
  - **Customer/Supplier** — the customer-side team defines the interface (and
    acceptance tests) the supplier must satisfy; once agreed, the supplier can
    work independently against that contract.
  - **Conformist** — the customer side just accepts whatever interface the
    supplier already exposes, no negotiation. Typically forced by a legacy or
    otherwise-unchangeable supplier system, not chosen for its own merits.

## Crossing a boundary: domain events and translation

Bounded contexts communicate through **domain events**, commonly via
Publish/Subscribe so a new context can subscribe without any existing
publisher needing to change. The critical rule: **the moment an event or
operation crosses a context boundary, it must be translated into the
receiving context's own Ubiquitous Language before it touches any domain
logic there** — otherwise the receiving context's vocabulary gets
contaminated with foreign terms (a telltale symptom: the domain expert on the
receiving side starts calling some term in the code "strange"). When the
mismatch between sender and receiver vocabulary is large, that translation
step is formalized as an explicit **anti-corruption layer**.

## Entities, value objects, aggregates

- **Entities** have identity and encapsulate the operations defined on them.
  The key DDD distinction from a plain data-holder approach: state changes go
  through entity *methods* that enforce business rules, not through freely
  settable properties — this is what "DDD enforces SOLID on entities" means
  in practice. Business rules that are intrinsic to one entity belong inside
  that entity; rules that span multiple entities belong in the layer that
  coordinates them, not forced into one entity that doesn't fully own the
  rule.
- **Value objects** have no identity — they're compared by their property
  values, not by reference, and are immutable once created (any "change"
  produces a new instance). C# `record` types are a natural fit: structural
  equality comes for free, and `with`-expressions give you the
  create-a-modified-copy semantics value objects need without hand-rolling
  `Equals`/`GetHashCode`.
- **Aggregates** are the actual unit of consistency — an object tree (root +
  its subparts) that must always be loaded, modified, and saved as one whole,
  because operating on a subpart independently of its parent can silently
  produce an incoherent result (the canonical example: two people
  concurrently incrementing different line-item quantities on the same order
  without the whole order being locked together can double-count the total).
  Only the **aggregate root** is referenced/operated on from outside the
  aggregate — external code never reaches into a subpart directly.

## Validating entity state: exceptions vs. a scoped error collector vs. result objects

Three real options, each with a real cost, not a clear universal winner:
- **Throw on validation failure** — simplest; aborts execution and reports
  the error in one motion. Cost: exceptions are computationally expensive, so
  reserve this for genuinely exceptional conditions, not routine input
  validation on a hot path.
- **A scoped "current error" collector service** (DI-scoped per request) —
  avoids exception cost, and the call stack can inspect accumulated errors
  when control naturally returns. Cost: it can't automatically abort
  mid-operation the way an exception does — only an exception can unwind you
  immediately to a pre-set handler.
- **Result objects** returned from every method in the call chain — explicit,
  cheap, no special control-flow. Cost: couples every method signature in the
  chain to the result shape, and any signature change ripples through
  maintenance.

## Onion architecture (and why it fits DDD better than classic layers)

Classic 3-layer architecture (Presentation → Business → Data, each layer only
talking to its immediate neighbor) assumes **record-like objects with no
behavior** flowing between layers — which is a poor match for DDD's
behavior-bearing entities.

**Onion architecture** inverts the dependency direction instead of stacking
layers linearly: concentric rings, with the **domain layer** (entities, value
objects) at the center, **application services** (the business-operation API,
independent of any specific UI/host) around that, and the **outermost layer**
(UI, tests, infrastructure — DB drivers, cloud services, file access) on the
outside. The one hard rule: **a layer may depend only on layers inward of
it, never outward** — infrastructure depends on the domain, never the reverse,
which is what lets the whole infrastructure (including the database engine)
be swapped without touching domain logic. Related architectures (Clean
Architecture, Ports & Adapters) share this same "isolate business logic from
environment specifics" shape under different names (Clean Architecture calls
the domain layer "Entities" and application services "Use Cases," scoped to
a whole use case rather than one atomic operation).

## Repository and Unit of Work

- **Repository pattern**: one repository per **aggregate**, not per entity —
  the aggregate is the smallest granularity that makes sense for a data
  operation. **Classical repositories** expose full CRUD because they're built
  around record-like objects with no self-mutating behavior. **DDD-adapted
  repositories** typically only need Create and Delete, because Update
  happens by calling business methods on the aggregate itself — the
  repository's job is just to persist whatever state those methods produced.
  Pick classical for simple, low-business-rule domains; DDD-adapted once the
  business rules get genuinely rich.
- **Unit of Work**: needed the moment a transaction spans more than one
  aggregate (e.g. booking travel touches both available-inventory and a
  customer's basket in one atomic operation). Each repository holds a
  reference to a shared Unit-of-Work instance representing "this
  transaction" — repositories sharing that reference belong to the same
  transaction, and a single save commits everything pending across all of
  them together.

## CQRS — and why reads and writes want different shapes

The core insight: write operations need rich, rule-enforcing aggregates;
*read* results are just data to display, never mutated further by business
logic — so forcing both through the same object shape is unnecessary
overhead. **Command Query Responsibility Segregation** formalizes this: writes
go through aggregates/repositories as usual, queries bypass aggregate
construction entirely and project straight from storage into flat DTOs.

There's a **light form** (queries just skip aggregate hydration, same
database) and a **strong/distributed form** (separate, denormalized,
precomputed read stores — sometimes their own microservices — updated
asynchronously from change events). The strong form trades atomic consistency
for eventual consistency: updates arrive asynchronously and out of order, so
records need version numbers to apply them correctly, and out-of-order
arrivals get cached until their prerequisite version lands. It's worth the
complexity specifically when a query needs to combine data that would
otherwise require expensive live fan-out across multiple bounded
contexts/microservices at request time. Even with a precomputed read side,
the original write-side data must be kept — it's the recovery source of
truth, and updates still need to re-read/validate full entity state (e.g.
uniqueness constraints) before being applied.

**Event sourcing** is the most extreme form of this: instead of storing
current state, store the append-only sequence of events that produced it;
current state becomes a derived, replayable projection (snapshotted
periodically so a full history replay isn't needed every time). This only
works safely if events are **idempotent** — replaying (or double-delivering)
the same event must have the same effect as processing it once, since
distributed, at-least-once delivery makes duplicates a normal case to design
for, not a rare edge case.

## Command handlers and cross-aggregate events

A command handler executes one whole domain operation as a single
all-or-nothing transaction via aggregate + repository calls. But a side
effect triggered by a state change *inside* an aggregate (affecting another
aggregate, or another bounded context) can't be issued directly by that
top-level handler — it goes through the domain-event/Pub-Sub mechanism
instead, which is what keeps aggregates decoupled from each other. In
practice, events raised while an aggregate is being processed are typically
queued on the entity rather than fired immediately, and drained right before
the surrounding transaction commits — so an event handler's side effect never
interrupts or re-enters the aggregate's own in-flight processing. Rather than
hand-wiring this plumbing, **MediatR** is the well-known off-the-shelf NuGet
package implementing exactly this command/event-handler dispatch shape — worth
reaching for before reinventing it.

## Where this would apply in GeoAssets

- The repo already has real Bounded-Context-shaped separation:
  `core/GeoAssets.Core` (geometry/assets), `core/GeoAssets.Identity`
  (auth/RBAC), and `core/GeoAssets.Workflow` (service orders) are independent
  modules with their own vocabularies. `WorkflowPrincipal` vs. `AppUser` is a
  textbook instance of the "same concept, different bounded-context
  vocabulary" problem this chapter describes — and `ServiceOrder.md`'s own
  design note that `WorkflowPrincipal` is "deliberately decoupled from
  `GeoAssets.Identity`, so the workflow core has no dependency on any
  specific identity system" is exactly a bounded-context boundary being
  drawn on purpose. `WorkflowPrincipalFactory` (which builds a
  `WorkflowPrincipal` from `IGeoAuthorizationService`) is functionally the
  translation step this chapter says must happen the moment something
  crosses a context boundary.
- `ServiceOrder` (with its `Dispatches`/`ActionLog` collections, mutated only
  through methods like `DispatchTo`/`RecordAction`, never by reaching into a
  collection directly) is already a correct aggregate-root shape — external
  code never manipulates a single dispatch or action-log entry independent of
  the order it belongs to.
- The `GeoGeometry`/`GeoPoint`/`GeoLineString`/`GeoPolygon` hierarchy
  (`core/GeoAssets.Core/Models/Geometry/`) is behavior-bearing (spatial
  predicates, measurements, derived geometries as methods) rather than a
  record-like data bag — already the DDD-entity shape this chapter argues
  for, not the anemic-model shape it warns against.
- None of the CQRS/event-sourcing material is relevant at GeoAssets' current
  scale — worth knowing as a reference for if/when a read-heavy reporting
  need or a multi-microservice split (see `Authorization.md`'s multi-tenancy
  discussion) makes live-query fan-out genuinely too slow, not before.
