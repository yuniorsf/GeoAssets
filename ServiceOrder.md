# Service Order — Design Reference

This document describes the design of the **Service Order** module — the workflow
orchestration layer for field/analytical work over georeferenced assets. It covers
the domain model, the authorization engine, the feature-selection subsystem, the
persistence layer, and the end-to-end flow, with diagrams for the status lifecycle,
the actors/use-cases, and a representative operational sequence.

The module lives in `core/GeoAssets.Workflow` (domain, rules, selection — no
infrastructure dependencies) and `workflow/GeoAssets.Workflow.EFCore` /
`workflow/GeoAssets.Workflow.Messaging.*` (persistence and messaging
infrastructure, referencing the core module).

---

## 1. What a Service Order is

A **Service Order** represents a unit of field or analytical work over a set of
`GeoFeature`s — e.g. "inspect these three transformers," "trace the impact of a
valve failure downstream," "repair this hydrant." An order:

- owns a collection of `GeoFeature`s (the assets involved),
- carries standard workflow metadata (status, priority, timestamps, assignee),
- can participate in a parent/child hierarchy (a roll-up order with per-network-segment
  child orders, for example),
- records **how** its feature set was populated (which selection strategy, with which
  parameters) for audit and reproducibility,
- accumulates a dispatch history and an append-only action log.

---

## 2. Architecture at a glance

```mermaid
flowchart TB
    subgraph Host["Host application"]
        UI["Blazor / MAUI UI<br/>(not yet wired — see §9 Known Limitations)"]
    end

    subgraph Core["core/GeoAssets.Workflow  (no infrastructure dependencies)"]
        direction TB
        Orders["<b>Orders</b><br/>ServiceOrder · IServiceOrder · OrderType<br/>ServiceOrderTransitions · repositories"]
        Rules["<b>Rules</b><br/>ServiceOrderRules — deny-overrides<br/>authorization engine"]
        Selection["<b>Selection</b><br/>FeatureSelectionRegistry (MEF)<br/>+ built-in IFeatureSelectionStrategy set"]
        Notifications["<b>Notifications</b><br/>IOrderEventPublisher abstraction"]
    end

    subgraph Infra["Infrastructure (references Core)"]
        direction TB
        EFCore["GeoAssets.Workflow.EFCore<br/>EFServiceOrderRepository · ServiceOrderDbContext"]
        Kafka["GeoAssets.Workflow.Messaging.Kafka"]
        SvcBus["GeoAssets.Workflow.Messaging.ServiceBus"]
    end

    UI -.->|"AddWorkflowInMemory() /<br/>AddWorkflowPersistence()"| Orders
    Orders --> Rules
    Orders --> Selection
    Orders --> Notifications
    EFCore -.implements.-> Orders
    Notifications --> Kafka
    Notifications --> SvcBus
```

| Layer | Responsibility | Key types |
|---|---|---|
| **Orders** | Domain model, status legality, persistence contracts | `ServiceOrder`, `IServiceOrder`, `OrderType`, `ServiceOrderTransitions`, `IServiceOrderRepository` |
| **Rules** | *Who* may perform an action, per order and per order type | `ServiceOrderRules`, `IServiceOrderRule`, `IOrderCreationRule` |
| **Selection** | Populating an order's feature set, pluggably | `FeatureSelectionRegistry`, `IFeatureSelectionStrategy` |
| **Notifications** | Publishing state-change events to a transport | `IOrderEventPublisher`, `OrderNotificationService` |
| **EFCore** | Relational persistence | `EFServiceOrderRepository`, `ServiceOrderDbContext` |
| **Messaging.\*** | Kafka / Azure Service Bus transports | `KafkaOrderEventPublisher`, `ServiceBusOrderEventPublisher` |

---

## 3. Domain model

```mermaid
classDiagram
    class IServiceOrder {
        <<interface>>
        +string Id
        +string Title
        +string OrderTypeId
        +ServiceOrderStatus Status
        +ServiceOrderPriority Priority
        +string CreatedBy
        +string AssignedTo
        +string ParentOrderId
        +IReadOnlyList~string~ ChildOrderIds
        +IReadOnlyList~GeoFeature~ Features
        +FeatureSelectionSpec SelectionSpec
        +IReadOnlyList~OrderDispatch~ Dispatches
        +IReadOnlyList~OrderActionLog~ ActionLog
        +bool IsRoot
        +bool IsLeaf
    }
    class ServiceOrder {
        +Transition(newStatus)
        +DispatchTo(targetId, targetType, by, note)
        +RecordAction(action, by, comment, resultingStatus)
        +WithFeatures(features, spec)
    }
    class OrderType {
        +string Id
        +string DisplayName
        +List~OrderCreationPolicy~ CreationPolicies
        +List~OrderActionPermission~ ActionPermissions
    }
    class ServiceOrderStatus {
        <<enumeration>>
        Draft
        Pending
        InProgress
        OnHold
        Completed
        Cancelled
    }
    class OrderActionType {
        <<enumeration>>
        View
        Approve
        Reject
        Assign
        Dispatch
        Execute
        Complete
        Cancel
        Annotate
    }
    class OrderDispatch {
        +string TargetId
        +DispatchTargetType TargetType
        +string DispatchedBy
        +DateTime DispatchedAt
        +string Note
    }
    class OrderActionLog {
        +OrderActionType Action
        +string PerformedBy
        +DateTime PerformedAt
        +ServiceOrderStatus ResultingStatus
    }

    IServiceOrder <|.. ServiceOrder
    ServiceOrder --> ServiceOrderStatus
    ServiceOrder "1" --> "*" OrderDispatch
    ServiceOrder "1" --> "*" OrderActionLog
    OrderActionLog --> OrderActionType
    ServiceOrder ..> OrderType : OrderTypeId (loose ref)
    ServiceOrder "1" o-- "0..*" ServiceOrder : ParentOrderId / ChildOrderIds
```

Notes on the model, reflecting decisions made while hardening it:

- **`ParentOrderId` is the only persisted source of truth for hierarchy.**
  `ChildOrderIds` is a *derived* view, recomputed by every repository on every read —
  never write to it directly.
- **`FeatureSelectionSpec`** (on `SelectionSpec`) records which
  `IFeatureSelectionStrategy` populated `Features` and with what parameters, so the
  selection can be audited (see §5).
- **`OrderType`** carries two independent policy tables:
  `CreationPolicies` (who may create an order of this type) and `ActionPermissions`
  (per-action overrides, consulted by `ServiceOrderRules` — see §4).

### File map

| Concept | Path |
|---|---|
| Domain entity | `core/GeoAssets.Workflow/Orders/ServiceOrder.cs` |
| Domain interface | `core/GeoAssets.Workflow/Orders/IServiceOrder.cs` |
| Order type + policies | `core/GeoAssets.Workflow/Orders/OrderType.cs` |
| Order type catalogue | `core/GeoAssets.Workflow/Orders/OrderTypeRegistry.cs` |
| Status enum | `core/GeoAssets.Workflow/Orders/ServiceOrderStatus.cs` |
| Priority enum | `core/GeoAssets.Workflow/Orders/ServiceOrderPriority.cs` |
| Action enum | `core/GeoAssets.Workflow/Orders/OrderActionType.cs` |
| Dispatch record | `core/GeoAssets.Workflow/Orders/OrderDispatch.cs` |
| Audit log record | `core/GeoAssets.Workflow/Orders/OrderActionLog.cs` |
| State machine | `core/GeoAssets.Workflow/Orders/ServiceOrderTransitions.cs` |

---

## 4. Status lifecycle — the flow of a Service Order

Every legal status transition is defined in one place, `ServiceOrderTransitions.IsValid`,
and enforced at **every** write path that can change `Status` — the domain entity
(`ServiceOrder.Transition`), both repository implementations' `UpdateAsync`/
`AppendActionAsync`, and the `ValidatingServiceOrderRepository` decorator that wraps
any future implementation automatically.

```mermaid
stateDiagram-v2
    [*] --> Draft : Order created

    Draft --> Pending : Submit
    Draft --> Cancelled : Cancel

    Pending --> InProgress : Start work
    Pending --> Cancelled : Cancel

    InProgress --> OnHold : Suspend
    InProgress --> Completed : Complete
    InProgress --> Cancelled : Cancel

    OnHold --> InProgress : Resume
    OnHold --> Cancelled : Cancel

    Completed --> [*]
    Cancelled --> [*]

    note right of Completed
        Terminal state.
        Every outbound transition
        is rejected.
    end note
    note right of Cancelled
        Terminal state.
        Every outbound transition
        is rejected.
    end note
```

Any transition not shown above — skipping straight from `Draft` to `Completed`,
re-opening a `Completed`/`Cancelled` order, moving backward from `InProgress` to
`Pending` — throws `InvalidServiceOrderTransitionException` before anything is
mutated or persisted. Staying in the same status is always a legal no-op (used by
plain metadata updates that don't touch `Status`).

---

## 5. Authorization — the rules engine

`ServiceOrderRules` is a **deny-overrides** evaluator over a chain of
`IServiceOrderRule` instances (for actions on an existing order) and a parallel
chain of `IOrderCreationRule` instances (for creating a new order, which has no
`IServiceOrder` yet).

```mermaid
flowchart TD
    Start(["Evaluate(principal, action, order)"]) --> Anon{"principal anonymous?"}
    Anon -->|yes| DenyAnon["Deny — anonymous"]
    Anon -->|no| Resolve["ResolveRelationship(principal, order)<br/>Creator · Assignee · Dispatcher · OrgMember · GroupMember · DirectRecipient"]
    Resolve --> Loop["For each rule in chain (in order)"]
    Loop --> Verdict{"rule.Evaluate(...)"}
    Verdict -->|"false"| DenyRule["Deny — this rule wins,<br/>overrides every allow so far"]
    Verdict -->|"true"| MarkAllow["anyAllow = true"]
    Verdict -->|"null (abstain)"| Next
    MarkAllow --> Next["next rule"]
    Next --> More{"more rules?"}
    More -->|yes| Loop
    More -->|no| Final{"anyAllow?"}
    Final -->|yes| Allow["Allow"]
    Final -->|no| DenyDefault["Deny — fail closed,<br/>no rule granted it"]
```

### Built-in `IServiceOrderRule` chain (registration order)

| Rule | Grants | Notes |
|---|---|---|
| `CreatorRule` | View, Annotate to the creator; Cancel while `Draft`/`Pending` | Only status-aware built-in rule |
| `AssigneeRule` | View, Execute, Complete, Annotate to the assignee | |
| `DispatchRecipientRule` | View, Annotate to direct/group/org dispatch recipients | |
| `OrderTypeActionPermissionRule` | Per-`OrderType.ActionPermissions` override | **Overrides** the role-based default below when the order's type defines an entry for the action being evaluated; abstains otherwise |
| `RoleBasedActionRule` | Configurable role → action-set mapping (default: `Supervisor` → View/Approve/Reject/Assign/Dispatch/Cancel/Annotate; `Administrator` → everything) | Mapping is data, injected via `ServiceOrderRules`'s constructor — no code change needed to narrow or add a role tier |

### Built-in `IOrderCreationRule` chain

| Rule | Grants |
|---|---|
| `CreationPolicyRule` | Creation when the principal satisfies at least one `OrderType.CreationPolicies` entry (any-match), or unconditionally when none are defined. Abstains (not denies) when unsatisfied, so a custom creation rule can still grant access another way. |

`PolicyKind` (used by both `CreationPolicies` and `ActionPermissions`) matches on
`Role`, `Permission`, `Group`, or `Organization` against a `WorkflowPrincipal` — a
snapshot record deliberately decoupled from `GeoAssets.Identity`, so the workflow
core has no dependency on any specific identity system.

### File map

| Concept | Path |
|---|---|
| Engine | `core/GeoAssets.Workflow/Rules/ServiceOrderRules.cs` |
| Action-rule contract | `core/GeoAssets.Workflow/Rules/IServiceOrderRule.cs` |
| Creation-rule contract | `core/GeoAssets.Workflow/Rules/IOrderCreationRule.cs` |
| Principal snapshot | `core/GeoAssets.Workflow/Rules/WorkflowPrincipal.cs` |
| Relationship flags | `core/GeoAssets.Workflow/Rules/OrderUserRelationship.cs` |

---

## 6. Feature selection — populating an order

`FeatureSelectionRegistry` discovers `IFeatureSelectionStrategy` implementations via
MEF — built-in assemblies plus any `GeoAssets.Plugin.*.dll` dropped in a plugins
directory — so new strategies are added without modifying the registry.

| Strategy ID | Category | What it does |
|---|---|---|
| `bounding-box` | Spatial | Features inside a drawn rectangle |
| `nearby` | Spatial | Features within a radius of a point |
| `asset-type-filter` | Filter | All features of a given asset type |
| `manual` | Interactive | Explicit list of feature IDs |
| `topology-reachability` | Topology | Upstream/downstream/both from a seed feature |
| `inherit-parent` | Hierarchy | Copies (optionally filtered) the parent order's features |
| `inherit-children` | Hierarchy | Merges all direct child orders' features |
| *(abstract)* `BackgroundProcessSelectionStrategy` | — | Base class for long-running strategies with progress reporting |

Every call to `FeatureSelectionRegistry.SelectAsync` validates that the strategy's
`Parameters` are JSON-serializable **before** running it, so a non-serializable
parameter (a delegate, for instance) fails immediately at the call site instead of
much later when the resulting `FeatureSelectionSpec` is persisted.

---

## 7. Persistence

`IServiceOrderRepository` composes two segregated interfaces so a consumer that only
reads or only writes can depend on just that piece:

```mermaid
classDiagram
    class IServiceOrderReader {
        <<interface>>
        GetByIdAsync() GetAllAsync() GetRootsAsync()
        GetChildrenAsync() GetParentAsync()
        GetByStatusAsync() GetByAssigneeAsync() GetByCreatorAsync()
        GetByOrderTypeAsync() GetByDateRangeAsync() GetDispatchedToAsync()
    }
    class IServiceOrderWriter {
        <<interface>>
        AddAsync() UpdateAsync()
        AppendDispatchAsync() AppendActionAsync()
        DeleteAsync()
        OrderAdded OrderUpdated OrderStatusChanged OrderDeleted
    }
    class IServiceOrderRepository {
        <<interface>>
    }
    class InMemoryServiceOrderRepository
    class EFServiceOrderRepository
    class ValidatingServiceOrderRepository {
        -IServiceOrderRepository inner
    }

    IServiceOrderRepository --|> IServiceOrderReader
    IServiceOrderRepository --|> IServiceOrderWriter
    IServiceOrderRepository <|.. InMemoryServiceOrderRepository
    IServiceOrderRepository <|.. EFServiceOrderRepository
    IServiceOrderRepository <|.. ValidatingServiceOrderRepository
    ValidatingServiceOrderRepository o--> IServiceOrderRepository : wraps
```

- **`UpdateAsync`** persists scalar fields only (title, status, priority, assignee,
  schedule, attributes, features, hierarchy) — it never touches `Dispatches` or
  `ActionLog`.
- **`AppendDispatchAsync`** / **`AppendActionAsync`** insert a single new row each,
  independent of any other concurrent write — replacing an earlier design that tried
  to infer "what's new" by comparing collection lengths, which could silently drop
  an entry under concurrent writers.
- **`ValidatingServiceOrderRepository`** decorates any inner repository with
  transition-legality enforcement on `UpdateAsync`/`AppendActionAsync`, so a future
  implementation gets the guarantee automatically instead of having to reimplement
  it. `AddWorkflowInMemory()` and `AddWorkflowPersistence()` register it by default.

---

## 8. Notifications

`IOrderNotificationService` builds an `OrderStateChangedEvent` (enriched with the
resolved recipient list) and hands it to an `IOrderEventPublisher`:

| Publisher | Transport | Registration |
|---|---|---|
| `NullOrderEventPublisher` | No-op (default) | `AddWorkflowNotifications()` |
| `KafkaOrderEventPublisher` | Apache Kafka | `AddWorkflowKafka(opts => ...)` |
| `ServiceBusOrderEventPublisher` | Azure Service Bus | `AddWorkflowServiceBus(configuration)` |

The domain and rules layers have zero dependency on any messaging SDK — swapping
transports never touches call sites.

---

## 9. Use cases

```mermaid
flowchart LR
    Creator(("Requester<br/>(any authenticated user)"))
    Tech(("Field Technician<br/>(Assignee)"))
    Supervisor(("Supervisor"))
    Admin(("Administrator"))
    Sys(("Automated Process<br/>(background strategy)"))

    subgraph SO["Service Order System"]
        UC1(["Create Order"])
        UC2(["View Order"])
        UC3(["Select Features"])
        UC4(["Dispatch Order"])
        UC5(["Approve / Reject Order"])
        UC6(["Assign Order"])
        UC7(["Execute Order"])
        UC8(["Annotate Order"])
        UC9(["Complete Order"])
        UC10(["Cancel Order"])
        UC11(["Configure Order-Type Permissions"])
    end

    Creator --> UC1
    Creator --> UC2
    Creator --> UC8
    Creator --> UC10

    Tech --> UC2
    Tech --> UC7
    Tech --> UC8
    Tech --> UC9

    Supervisor --> UC2
    Supervisor --> UC4
    Supervisor --> UC5
    Supervisor --> UC6
    Supervisor --> UC8
    Supervisor --> UC10

    Admin --> UC2
    Admin --> UC4
    Admin --> UC5
    Admin --> UC6
    Admin --> UC7
    Admin --> UC8
    Admin --> UC9
    Admin --> UC10
    Admin --> UC11

    Sys --> UC3
    UC1 -.includes.-> UC3
```

Which actions a role may perform is not hardcoded per actor above the built-in
defaults — see §5. `UC11` (configuring `OrderType.ActionPermissions`) is what lets an
administrator narrow or extend any of the other use cases *per order type* without a
code change.

---

## 10. End-to-end flow — worked example

A Supervisor creates an inspection order, dispatches it, and a Field Technician
executes and completes it:

```mermaid
sequenceDiagram
    actor Supervisor
    actor Technician
    participant Rules as ServiceOrderRules
    participant Selection as FeatureSelectionRegistry
    participant Repo as IServiceOrderRepository
    participant Notify as IOrderNotificationService

    Supervisor->>Rules: CanCreate(principal, "inspection")
    Rules-->>Supervisor: allowed (CreationPolicyRule: role match)

    Supervisor->>Selection: SelectAsync("bounding-box", context)
    Selection-->>Supervisor: features + FeatureSelectionSpec

    Supervisor->>Repo: AddAsync(new ServiceOrder { Status = Draft }.WithFeatures(...))
    Repo-->>Notify: OrderAdded

    Supervisor->>Rules: Evaluate(principal, Dispatch, order)
    Rules-->>Supervisor: allowed (RoleBasedActionRule: Supervisor)
    Supervisor->>Repo: AppendDispatchAsync(orderId, dispatch to Technician)
    Repo-->>Notify: OrderUpdated

    Supervisor->>Repo: AppendActionAsync(orderId, Dispatch, ResultingStatus=Pending)
    Note over Repo: ServiceOrderTransitions.IsValid(Draft, Pending) → true
    Repo-->>Notify: OrderStatusChanged(Draft → Pending)

    Technician->>Rules: Evaluate(principal, Execute, order)
    Rules-->>Technician: allowed (AssigneeRule)
    Technician->>Repo: AppendActionAsync(orderId, Execute, ResultingStatus=InProgress)
    Note over Repo: ServiceOrderTransitions.IsValid(Pending, InProgress) → true
    Repo-->>Notify: OrderStatusChanged(Pending → InProgress)

    Technician->>Rules: Evaluate(principal, Complete, order)
    Rules-->>Technician: allowed (AssigneeRule)
    Technician->>Repo: AppendActionAsync(orderId, Complete, ResultingStatus=Completed)
    Note over Repo: ServiceOrderTransitions.IsValid(InProgress, Completed) → true
    Repo-->>Notify: OrderStatusChanged(InProgress → Completed)
```

If the Technician instead attempted `AppendActionAsync(orderId, Complete,
ResultingStatus=Completed)` while the order was still `Draft`, the repository would
throw `InvalidServiceOrderTransitionException` before mutating anything — the same
enforcement shown in §4 applies regardless of which action triggered the attempt.

---

## 11. Registering the module

```csharp
// In-memory (WASM hosts, tests) — no database required.
services.AddWorkflowInMemory();
services.AddOrderTypeRegistry();           // seeds "inspection", "maintenance", "emergency-repair"
services.AddWorkflowNotifications();       // no-op publisher by default

// EF Core-backed (server-side hosts).
services.AddWorkflowPersistence(o => o.UseSqlServer(connectionString));
services.AddWorkflowKafka(opts => { opts.BootstrapServers = "..."; opts.TopicName = "..."; });
// or: services.AddWorkflowServiceBus(configuration);
```

Both `AddWorkflowInMemory()` and `AddWorkflowPersistence()` register
`IServiceOrderRepository` as a `ValidatingServiceOrderRepository` wrapping the
concrete implementation, and separately register `IServiceOrderReader` /
`IServiceOrderWriter` pointing at that same instance.

---

## 12. Testing

`GeoAssets.Workflow.Tests` covers the module end to end — 177 test cases as of this
writing, with `InMemoryServiceOrderRepository`, `ServiceOrder` (transition logic),
`ServiceOrderTransitions`, `ServiceOrderRules`, `FeatureSelectionRegistry` (parameter
validation), and `ValidatingServiceOrderRepository` all at 100% line/branch coverage.
The full solution (`GeoAssets.Core.Tests` + `GeoAssets.Workflow.Tests` +
`GeoAssets.Commands.Tests`) runs 406 tests.

---

## 13. Known limitations

- **Not yet wired into any host.** No project under `apps/` references
  `GeoAssets.Workflow` — this module has no UI today. Confirmed intentional
  pre-`v0.1.0` sequencing (see the project README's roadmap), not a stalled
  integration — but worth knowing before assuming a page exists to click through.
- **`FeatureSelectionSpec.Parameters` doesn't survive a reload with full type
  fidelity.** Every parameter value comes back as a `System.Text.Json.JsonElement`
  after a save/reload cycle, regardless of its original CLR type — strategies with
  hard casts (`(GeoPoint)`, `(TraversalDirection)`) would throw if fed a reloaded
  spec instead of a freshly-built one. `Parameters` is safe for audit/display, not
  for literal replay.
- **No optimistic concurrency token.** Two concurrent `UpdateAsync` calls editing
  different scalar fields on the same order still resolve last-writer-wins with no
  conflict signal. The append-only writer methods (§7) close the sharper version of
  this problem (silently dropped audit-log rows); the general case is still open.
