# ADR-001 — GeoAssets.Server Domain Decoupling

| | |
|---|---|
| **Status** | Accepted (2026-11-18) — implementation tracked by Jira epics XD01-344 … XD01-351 (label `server-decoupling`) |
| **Scope** | `apps/GeoAssets.Server`, `core/GeoAssets.Workflow`, `workflow/GeoAssets.Workflow.EFCore`, `core/GeoAssets.Core`, `providers/GeoAssets.Provider.PostgreSQL`, `apps/GeoAssets.Shared` / Web / MAUI clients |
| **Amends** | `docs/service_order_research/microservices_intersection_evaluation.md` (§8 Amendment A) |
| **Supersedes** | — |

### Decisions recorded

| # | Decision | Where |
|---|---|---|
| D1 | Target topology is **one microservice per vertical** (Topology B of the intersection evaluation). The in-process module contract survives as the **order-type chassis** every vertical host is built on. | Part 3.1 |
| D2 | **Feature selection and the order→features target store stay in the kernel** as a **global target ledger** keyed by `(vertical, orderId)`, so exclusivity is enforced across verticals. | Part 3.4 |
| D3 | **`ServiceOrderRules` is the single authorization decision point for service orders.** `OrgResourceAuthorizationHandler` remains only for non-order resources (features, asset types). | Part 2, smell H |
| D4 | The ISP tenant-portal REST surface (`/api/isp/tenant-portal`) is **removed**; the portal UI uses the generic service-order routes. | Part 2, smell H |
| D5 | Feature-type semantics are expressed as **asset-type roles**; verticals publish **feature-action descriptors as data** — no vertical code runs in the kernel. | Part 1.1, Phase 3 |

## Context

`GeoAssets.Server` is meant to be a **domain-agnostic host** that does two things:
- holds a **pool of geographic asset providers**, renders their features on the map, and offers actions that depend on what each feature type means;
- holds **service-order type modules**, loading the order types and their behaviour when the application starts.

The audit (three code sweeps, key claims re-checked in source) found that the server works as a **composition root that knows every vertical domain by name**. The domains include:
- shared pole use and occupancy;
- ISP neutral-operator work (clandestine attachments, the tenant portal);
- IP billing and município grants;
- PostgreSQL/PostGIS as the only asset store.

This document has three parts: (1) a technical description of the target concept, (2) the code smells with file:line evidence, and (3) an evaluated refactoring roadmap. The roadmap is a staged migration that replaces the old code piece by piece (a "strangler" migration), and each stage can ship on its own.

---

## Part 1 — Technical description of the target concept

### 1.1 Kernel + two containers + a semantic bridge

```
┌──────────────────────────── GeoAssets Host Kernel ─────────────────────────────┐
│  Cross-cutting: AuthN/AuthZ engine, tenancy (ITenantContext), observability,     │
│  outbox/inbox transport, problem-details, module loader, bootstrap lifecycle     │
│                                                                                  │
│  ┌──────── Spatial Plane ────────┐   ┌──────── Workflow Plane ─────────────────┐ │
│  │  Provider Pool (named catalogs)│   │  Order-Type Module Container            │ │
│  │  IProviderPlugin → IAssetProvider│ │  IGeoAssetsModule → OrderType(s) +      │ │
│  │  + optional capability ports:  │   │   action handlers, guards, lifecycle    │ │
│  │   IAssetSpatialQuery, IAssetRender│ │   hooks, rules, endpoints, persistence, │ │
│  │   Query, IMutationBatchApplier,│   │   permissions, problem mappers          │ │
│  │   IOrganizationCatalogFactory  │   │  Generic engine: state machine, targets,│ │
│  │  Generic APIs: features, layers,│  │   selection, attributes-schema, rules   │ │
│  │   WFS, WMS (via ports only)    │   │  Generic APIs: /service-orders, /actions│ │
│  └───────────────┬───────────────┘   └───────────────┬─────────────────────────┘ │
│                  └──── Semantic Bridge (Feature Capabilities) ──┘                │
│       asset-type *roles* (e.g. "occupiable-support")  ⇄  order-type target policy│
│       feature-action descriptors → GET /features/{id}/actions (client renders)   │
└──────────────────────────────────────────────────────────────────────────────────┘
```

**Kernel.** The kernel holds only cross-cutting, domain-free concerns, and it must start up with **zero modules** loaded. It knows contracts, never concrete domains.

**Spatial plane.** This is a *pool of providers*, each one a named catalog (for example `primary`, `cadastre-wfs`, `isp-census`). A provider is built from configuration through `IProviderPlugin`, the same contract the Web and MAUI clients already use.
- The only required port is `IAssetProvider`.
- Faster paths are optional **capability ports**: spatial pushdown, render queries, mutation batches, per-organization catalogs. The kernel uses them when a provider offers them and falls back to generic implementations over `IAssetProvider` otherwise. `AssetProviderSpatialQuery` already works this way.

**Workflow plane.** This is a *container of order-type modules*. Under the chosen topology (one microservice per vertical, see Part 3), this container is a reusable **order-type service chassis**: the generic workflow engine plus the module contract. Each vertical microservice is built on the chassis and hosts its own modules. `GeoAssets.Server` keeps only the **Order-Type Registry**: a catalog of the order-type *descriptors* that each vertical publishes when it starts. The descriptors are data (types, states, target roles, feature actions, API base route), never code. Each module is a self-contained unit that contributes:

| Contribution | Contract (new or existing) |
|---|---|
| Order types (states, transitions, policies, schema) | `OrderTypeRegistry` callback (exists) |
| Domain actions on the generic `/actions` route | `IOrderActionHandler` keyed by `(orderTypeId, actionVerb)` |
| Transition preconditions | `ITransitionGuard`, invoked by `ValidatingServiceOrderRepository` |
| Lifecycle side-effects (approve, terminal) | `IOrderLifecycleHook` (replaces `ServiceOrderTargetRelease`) |
| Business rules | `IServiceOrderRule`, collected from DI (the port exists but isn't collected) |
| Extra REST resources | `IGeoAssetsModule.MapEndpoints` (behind the gateway at `/api/v/{vertical}/…` once extracted) |
| Own schema and migrations | A per-module `DbContext` with its own migrations assembly and history table |
| Permissions and role bundles | `IModulePermissionContributor`, seeded by the kernel's identity seeder |
| Domain exception → ProblemDetails | `IProblemDetailsMapper` (shared shape `WorkflowProblemResults.Create`) |
| Feature actions on the map | Feature-action descriptors published to the kernel registry |

**Semantic bridge.** Asset types carry **roles or capabilities**: tags such as `occupiable-support` or `isp:pole`, rather than GUIDs. Order types target **roles** through `OrderTargetPolicy`. When a feature is clicked, the kernel matches the feature's roles against the feature-action descriptors that verticals registered and returns a generic action list. The client renders that list without knowing any domain. This is how "act according to the semantics of the feature type" works without the kernel knowing what a pole is.

### 1.2 Bootstrap lifecycle (deterministic, ordered)

`Discover → Configure services → Build → Validate → Migrate → Seed → Map endpoints → Ready`

1. **Discover.** Read the module list from configuration (`GeoAssets:Modules`, `GeoAssets:Providers`). Built-in modules are resolved by assembly reference, and optional ones from a `plugins/` folder through `AssemblyLoadContext`. This step must run *before* the DI container is built: MEF `[ImportMany]` only composes after the build, which is too late for service registration.
2. **Configure.** Each module runs `ConfigureServices(IServiceCollection, IConfiguration)`. Each provider plugin registers its catalog and its capability ports.
3. **Validate.**
   - Contract checks: every action verb an order type declares has a handler or is a core verb.
   - Every role in a `TargetPolicy` is provided by some asset type.
   - No duplicate route prefixes and no duplicate permission codes.
   - Any failure stops startup (fail-fast).
4. **Migrate and seed.**
   - Kernel contexts go first (identity, workflow core, projects), then module contexts.
   - Seeding covers permissions, order types and asset-type roles.
   - The existing `LoadRegistryFromDbAsync` overlay stays.
5. **Map.** Kernel routes are mapped first, then each module's `MapEndpoints`.

### 1.3 Kernel invariants (to become fitness functions)

- **I1.** `GeoAssets.Server` (the kernel) has no reference to `GeoAssets.Provider.*`, vertical `Workflow.*` namespaces, `GeoAssets.Domain.*` or `GeoAssets.IpBilling*`. Only a thin *composition* project (or configuration) picks them.
- **I2.** The generic workflow engine (`Orders/Rules/Targets/Selection/Notifications`) and its EF context contain no vertical entities or tokens.
- **I3.** No generic route branches on a specific order type or action. Dispatch always goes through a registry.
- **I4.** No asset-type GUID literals outside a provider's seed data. Code refers to types by role.
- **I5.** The kernel boots and passes health checks with an empty module list.

---

## Part 2 — Code smells (current implementation)

Severity: **H** = blocks the target concept · **M** = significant friction · **L** = hygiene.

### A. Composition root acting as a domain registry (Divergent Change) — H
- `apps/GeoAssets.Server/Program.cs`:
  - `:12` imports `Workflow.Isp.NeutralOperator`.
  - `:82-89` hand-registers `ClandestineAttachmentOrderTypes`, `TenantAttachmentRequestOrderTypes` and `RegisterSharedUseOrderTypes`.
  - `:121-153` and `:164` register occupancy staging and commit, shared-use dispatch, the in-process mutation apply, `IClandestineAttachmentService`, SLA reporting and `MunicipioProvisioningService` one at a time.
  - `:249-262` and `:267` map domain routes by hand.
- Every new vertical means editing and redeploying the host. Every host (Server, FieldService.Server, IpBilling.Server, and even Web/MAUI) repeats its own hand-picked list.
- Some order types (`PoleCensusOrderTypes`, `CutAndRemoveOrderTypes`, `ScadaOutageOrderTypes`) are registered by no host at all.

### B. A generic pipeline special-cases one domain (OCP violation and Service Locator) — H
- **Verified:** `ServiceOrdersRestApiExtensions.cs:323-330`. Inside the generic `POST /service-orders/{id}/actions`, the route checks `if (OccupancyApprovalRoute.Handles(entry, orderType))` and then calls `RequestServices.GetRequiredService<IOccupancyCommitService>()`.
- `OccupancyApprovalRoute.cs:22-23, 52-63` maps the pole-domain `OccupancyConflictException` to a 409 with `poleFeatureIds`.

### C. Semantic hijack: "has targets" is treated as "is a pole order" — H
- **Verified:** `core/GeoAssets.Workflow/Occupancy/OccupancyApproval.cs:14` defines `Commits(orderType) => orderType?.TargetPolicy is not null`. As a result, *any* order type that targets assets gets pole-occupancy commit semantics when it is approved.
- The client mirrors this: `ServiceOrderDetail.razor:85-89` shows `OrderTargetOccupancyPanel` with `occupancy.*` and `sharedUse.*` captions for every targeted type.

### D. Closed vocabulary of domain verbs in the engine (Shotgun Surgery) — H
- **Verified:** the `core/GeoAssets.Workflow/Orders/OrderActionType.cs` enum mixes generic verbs (Approve, Assign, Complete…) with domain verbs: `Calculate` and `Validate` (IP billing), and `FlagClandestine` and `Dispute` (ISP).
- No `IOrderActionHandler` exists, so a new domain verb means editing the engine, the wire format and the database.

### E. Leaky provider abstraction: the server is PostGIS-only — H
- **Verified:** `WmsPostGisRenderer.cs:32` takes `IDbContextFactory<GeoAssetsDbContext>` and queries `GeoEntities`/`AssetTypes` with `ST_Intersects`. This bypasses `IAssetProvider`, `ValidatingAssetProvider`, the per-organization catalogs and `LayerResolver` styling.
  - `WmsEndpointExtensions.cs:53/93/166/194` injects the concrete renderer.
  - `RenderRow.cs:8` leaks the NTS storage type. `FeatureInfoRow` and `AssetTypeInfo` duplicate Core models.
- Program and the hosting extensions are wired to concrete Postgres types:
  - `Program.cs:6-8, 43, 50, 55, 59, 64, 69-70, 73`.
  - `OrganizationAssetCatalogHostExtensions.cs:2-3, 22, 41` resolves the concrete `PostgresOrganizationAssetCatalogs`.
  - `InProcessAssetMutationHostExtensions.cs:5-6, 57, 59, 83-84`.
  - `GeoAssetsDbContextFactory.cs` makes the server the `dotnet ef` design-time host for the provider's context.
- There is no configuration-driven choice of provider. The pool (`IProviderPool`, `IProviderPlugin`, `ActiveAssetProvider`) exists only on the client. `IExternalProviderFactory` has no consumer anywhere, and its doc comment is stale. Postgres has no `IProviderPlugin`.
- Clean today: `GeoAssetsRestApiExtensions.cs` and `WfsEndpointExtensions.cs` use only Core ports.

### F. Magic constants and primitive obsession for feature semantics — H
- "Pole" is defined by the GUID `00000000-0000-0000-0000-000000000004`, copied into three places:
  - `OrderOccupancyHostExtensions.cs:27`
  - `SharedUseOrderTypes.cs:29`
  - the provider-*internal* `DomainAssetTypeIds.cs:6`
- The config key `Workflow:Occupancy:PoleAssetTypeIds` is never set in `appsettings.json`, so production runs on the magic GUID.
- Attribute keys are hardcoded: `"barramento"` (`SharedUseOrderTypes.cs:32`) and `"occupancyClass"`.
- Static id registries: `Core/Bdgd/BdgdAssetTypeIds.cs`, `Core/Isp/IspAssetTypeIds.cs`.
- `AssetType` has no role or capability model, so feature-type semantics are not a first-class concept.

### G. Vertical endpoints live inside the host — M
- Occupancy routes: `ServiceOrderOccupancyRestApiExtensions.cs:59-163`, under `/service-orders/{id}/targets/{t}/occupancy*`.
- Shared-use routes: `SharedUseRestApiExtensions.cs:53-98`, under `/service-orders/{id}/shared-use/*`.
- ISP tenant portal: `IspTenantPortalRestApiExtensions.cs:33-147`, which hardcodes `TenantAttachmentRequestOrderTypes.AttachmentRequestId` and the `poleFeatureId` attribute.
- Município grants: `IdentityRestApiExtensions.cs:48, 665-684`, `MapMunicipioGrantsApi`.
- Domain DTOs and problem mappers: `SubmitAttachmentRequestRequest`, `SubmitDisputeRequest`, `OccupancyProblemResults`, `SharedUseProblemResults`.

### H. Two authorization models for order operations — H (raised from M after a deeper look)

**Model 1, the workflow decision point.** `ServiceOrderRules` (`core/GeoAssets.Workflow/Rules/ServiceOrderRules.cs`) runs in this order:
1. the organization gate (`OrderOrganizationGate`);
2. a rule chain where any deny wins: Creator, Assignee, DispatchRecipient, OrderTypeActionPermission, RoleBased, CrossOrgGrant;
3. `CanCreate` with `CreationPolicies`.

It is used by every `/api/workflow/service-orders*` route through `ServerWorkflowPrincipalFactory` and `ServiceOrderRouteAccess`, and the Blazor client evaluates the same engine.

**Model 2, the org-resource handler.** `OrgResourceAuthorizationHandler` (XD01-21, built for features and asset types) checks a permission code, then organization membership, then a grant. `IspTenantPortalRestApiExtensions.cs:28-29, 76, 132` uses it for *service orders*.

| Dimension | Model 1 (`/api/workflow`) | Model 2 (`/api/isp/tenant-portal`) |
|---|---|---|
| Unit of decision | action verb on the order (`serviceorders:{Action}`) | a fixed permission code |
| Dispute authorized by | rule chain for `OrderActionType.Dispute` | **`serviceorders:create`** (`:28-29, :110, :132`): a *create* permission authorizes a state change on an existing order |
| Per-order-type `ActionPermissions`, `CreationPolicies`, `RecipientAccess`/`RecipientActions` | honoured | **ignored** (the ISP order types declare none either, so the gap is invisible today) |
| Creation | `rules.CanCreate(principal, orderType)` (`ServiceOrdersRestApiExtensions.cs:199`) | no `CanCreate`: anyone with `serviceorders:create` creates attachment requests (`:46-56`) |
| Dispatch recipients, creator, assignee | gate condition 3 and the dedicated rules | unknown concept, so a dispatched-to organization is refused |
| Orders with no organization (`Guid.Empty`) | host option `AllowOrdersWithoutOrganization` | always allowed |
| Grant `ResourceType` match | `"ServiceOrder"`, case-insensitive (`CrossOrgGrantMatcher.cs:58`) | `resource.GetType().Name`, ordinal |
| Grant `AllowedActions` match | case-insensitive | ordinal |
| Refusal | **404** outside the organization, so a refusal never confirms an id exists (XD01-332, `ServiceOrderRouteAccess.cs:21-24`); 403 with the engine's reason inside | **403** `"Not authorized…"` (`:79-81, :135-137`): reveals that the order id exists |
| Write path | `EvaluateAppend` then `AppendActionAsync` | `ClandestineAttachmentService.DisputeAsync` calls `UpdateAsync` + `AppendActionAsync` directly (`ClandestineAttachmentService.cs:90-120`). **The rule chain is never consulted**; the precondition is a status check written in the service code |
| Client predictability | the client runs the same engine and shows the same buttons | the client cannot predict the decision |

**Consequences**
1. **Privilege mismatch.** `create` authorizes `Dispute`.
2. **The data-driven policy is bypassed.** Making order types configurable is undermined once a domain route skips the rules engine.
3. **Existence leak.** The 403 reveals ids, which contradicts XD01-332.
4. **The "single definition" claim no longer holds.** `CrossOrgGrantMatcher` describes itself as "the one definition… so the two can never drift apart". A third grant matcher with different case rules exists in `OrgResourceAuthorizationHandler`.
5. **The audit is inconsistent.** `RuleDenialCode` is not emitted for portal refusals.
6. **Under one microservice per vertical** this repeats per vertical unless the decision point is part of the chassis.

`OrgResourceAuthorizationHandler` is fine for features and asset types. The smell is using it for orders.

**To confirm with tests before fixing:**
- (a) A user who holds only `serviceorders:create` can dispute a notice.
- (b) Another organization's `GET /attachment-requests/{id}` returns 403 rather than 404.
- (c) An order dispatched to an organization is readable through `/api/workflow` but refused by the portal.

**Remediation (folded into Phase 2)**
- Make `ServiceOrderRules` the **only decision point for orders**. Expose it to ASP.NET as `ServiceOrderActionRequirement(verb)` and `ServiceOrderCreateRequirement(orderTypeId)` handlers that build the `WorkflowPrincipal` and map `OutsideOrganization` to 404 through `ServiceOrderRouteAccess.Refusal`.
- Dispute (and FlagClandestine, Regularize, ResolveDispute) become actions on the generic `POST /service-orders/{id}/actions`. They are handled by the NeutralOperator `IOrderActionHandler` *after* `EvaluateAppend`, and `ClandestineAttachmentService` stops writing without authorization.
- The ISP order types declare their policy as data: `CreationPolicies` (an ISP-tenant role), `ActionPermissions[Dispute]`, `RecipientAccess`.
- **Decided: remove the portal routes.** Delete `MapIspTenantPortalApi` (`IspTenantPortalRestApiExtensions.cs`), `SubmitAttachmentRequestRequest`, `SubmitDisputeRequest` and the `Program.cs:262` mapping. The Web client (`apps/GeoAssets.Web/Extensions/IspTenantPortalRestExtensions.cs`, `Pages/Isp/TenantPortal.razor`) switches to the generic routes:
  - **Create:** `POST /service-orders` with `orderTypeId = isp-attachment-request` and `attributes.poleFeatureId`. The attribute is validated by the order type's `AttributesSchemaJson`.
  - **List:** `GET /service-orders/by-type/{id}`.
  - **Flagged notices:** `GET /service-orders/by-type/{clandestine-notice}?status=cabo-a-revelia`. This needs a status filter on the by-type route, or the client filters.
  - **Dispute:** `POST /service-orders/{id}/actions` with `Dispute` and the evidence attribute.
  - These depend on the Phase 2 action handler being in place first.
- Unify grant matching: one Identity-level matcher, used by `CrossOrgGrantMatcher` and by `OrgResourceAuthorizationHandler` for non-order resources.
- Fitness test: no route that loads an `IServiceOrder` uses `OrgResourceRequirement`, and every route that changes an order evaluates `ServiceOrderRules`. Check this with the route-inventory test plus a test over the endpoint metadata.

### I. A "God" workflow DbContext and migrations owned by the host (Divergent Change) — H
- `workflow/GeoAssets.Workflow.EFCore/ServiceOrderDbContext.cs:52-62` holds vertical tables: contractor US ledger, US catalog, US postings, paving measurements, manhole audits, pole occupancies, and occupancy change sets.
- `AddWorkflowPersistence` (`WorkflowEFCoreServiceExtensions.cs:70-72`) always registers the contractor and pole-occupancy persistence.
- 13 vertical migrations sit in `apps/GeoAssets.Server/Migrations`: AddContractorUs\*, AddUsCatalogItems\*, AddPavingMeasurements\*, AddManholeAudits\*, AddPoleOccupancies\*, AddServiceOrderOccupancyChangeSets\*.
  - Some of these tables are used only by FieldService.Server, yet they are created in the Server's database.
- The RLS SQL embeds domain logic: `…DispatchRecipientRowLevelSecurity.cs:85-111` parses `AttributesJson->>'scopeId'`, and `AddPoleOccupanciesRowLevelSecurity.cs:18-22` is a pole-owner policy.
- A module cannot own its schema.

### J. The generic repository knows a domain (Feature Envy) — M
- `ServiceOrderTargetRelease.cs:50-57`, called from the generic `EFServiceOrderRepository` (`:192-206, 277-286`), discards staged *occupancy* change sets on every terminal transition.

### K. The engine assembly holds every vertical (Low Cohesion) — M
- `core/GeoAssets.Workflow` contains `Contractors/`, `Erp/` (SAP, Totvs, Sienge…), `FieldWork/`, `Infrastructure/`, `Isp/`, `Occupancy/`, `Scada/` and `SlaGuard/`, next to the generic `Orders/`, `Rules/`, `Targets/`, `Selection/` and `Notifications/` folders.
- Its csproj references `GeoAssets.Domain.Sanitation`.
- `GeoAssets.Core/Isp/NeutralOperator/*` leaks into Core.
- `WorkflowServiceExtensions.cs:192-286` registers shared-use and occupancy services next to generic ones.
- This contradicts the project's own Invariant 2 in `docs/service_order_research/microservices_intersection_evaluation.md`.

### L. Central, hardcoded permission catalog — M
- `apps/GeoAssets.Identity.EFCore/GeoIdentitySeeder.cs:70-157` hardcodes every permission, including 7 `ip-billing:*` codes and the role bundles. Modules cannot contribute permissions.
- `MunicipioGrantSyncRequest` and `MunicipioGrantRevokeRequest` live in `core/GeoAssets.Identity`.
- Role names are literals inside order types, for example `SharedUseOrderTypes.cs:58-61`.

### M. Extension points that exist but are dormant (Speculative Generality / dead seams) — L
- `GeoPluginContainer` (MEF) is never instantiated by any host.
- `IExternalProviderFactory` has no consumer.
- `AddServiceOrderRules` (`WorkflowServiceExtensions.cs:82-98`) never collects `IServiceOrderRule` from DI.
- MAUI's `AddGeoAssetsPostgres()` registration is dead.
- The outbox is generic but is wired from `SharedUseHostExtensions.AddServerWorkflowOutbox`, and the in-process relay reads the occupancy module's `CatalogInstanceId` (`InProcessAssetMutationHostExtensions.cs:27, 55`).

### N. The client mirrors the server's coupling — M
- `ServiceOrderDetail.razor:88, 139` hardcodes `OrderTargetOccupancyPanel` and `SharedUseDispatchDialog`.
- Web (`Program.cs:214-219`) and MAUI (`:180-181`) re-register `SharedUseOrderTypes` because code-seeded types never reach the client. That is a second source of truth.
- All IpBilling, ISP and regulatory menu items and pages live in the shell RCL, and `GeoAssets.Shared.csproj:15` references `GeoAssets.IpBilling`.
- `AttributesSchemaJson` on order types is never rendered: `ServiceOrderCreateForm` has fixed fields.
- The map context menu (`MapContextMenu.razor:7-19`) is generic, which is good, but it has no hook for feature-type actions.

### O. Architecture tests have blind spots — M
- `ServerArchitectureTests.cs:10-20` bans only `GeoAssets.IpBilling*`. Nothing stops the server from referencing `Provider.*`, Npgsql or EF.
- `MessagingArchitectureTests` Invariant02 bans tokens such as Sabesp and Sanitation, but not Pole, Occupancy, Isp, NeutralOperator, Paving, Manhole, Contractor, Municipio or Prefeitura. It also doesn't check assembly references, so the `Workflow → Domain.Sanitation` reference passes.
- No NetArchTest is in use, despite what CLAUDE.md's gate table implies.

### P. Risk to verify (not confirmed): WMS tenant scoping — M
- `AddGeoAssetsDbContextFactory` (`PostgresServiceExtensions.cs:78-87`) registers a singleton factory with no `TenantTransactionInterceptor`.
- `GeoAssetsDbContext`'s optional `ITenantContext?` constructor parameter is then resolved from the root provider, or falls back to `NullTenantContext`.
- WMS is anonymous by default (`Program.cs:233`).
- Possible outcomes: WMS shows only shared (Guid.Empty) features, or it resolves a scoped tenant context from the root provider. This should be confirmed with a test before Phase 4 changes the WMS path.

### What is already sound (keep)
- `GeoAssetsRestApiExtensions`, `WfsEndpointExtensions`, `ServiceOrderTargetsRestApiExtensions*`, `OrderTargetEndpointSupport`, `ServiceOrderRouteAccess`, `WorkflowProblemResults`, `ServerWorkflowPrincipalFactory`, `HttpTenantContext` and `ObservableAssetMutationBatchApplier`.
- The data-driven parts of `OrderType`: states, transitions, `CreationPolicies`, `ActionPermissions`, `AttributesSchemaJson`, `TargetPolicy`, `RecipientAccess` and `RecipientActions`.
- `IAssetSpatialQuery` with its generic fallback, `AssetMutationBatchApplier` and `IdempotentMutationBatchApplier` in Core, and `IProviderPlugin`/`ProviderPluginRegistry`.

---

## Part 3 — Refactoring evaluation

### 3.1 Options

| Criterion | **O1** Keep as is + add tests | **O2** Modular kernel (in-process modules) | **O3** One microservice per vertical (doc's "Topology B") |
|---|---|---|---|
| Domain-agnostic host | ✗ | ✓ | ✓ (per service) |
| Add a vertical without editing the host | ✗ | ✓ (config + package) | ✓ (new deployment) |
| Transactional consistency (order + occupancy + outbox) | ✓ | ✓ (shared connection/tx) | ✗ → sagas needed |
| Operational cost | low | low | high (N hosts, N DBs, N pipelines) |
| Migration risk | none | medium (staged) | high |
| Matches existing precedent | — | `FeatureSelectionRegistry` plugins, `IProviderPlugin` | `IpBilling.Server`, `FieldService.Server` |

**Decision (D1): O3, one microservice per vertical.** Product-owner decision of 2026-11-18; it follows Topology B in `microservices_intersection_evaluation.md`. O2's module contract survives as the internal *chassis* each vertical service is built on.

The resulting topology:
- **`GeoAssets.Server` becomes the Spatial & Platform Kernel.** It keeps the provider pool, features/layers/WFS/WMS, identity and authorization, projects, the Order-Type Registry (descriptors only), the generic feature-action aggregation, and the asset-mutation *consumer* (it applies `AssetMutationBatchRequestedV1` idempotently).
- **Each vertical runs on the order-type chassis in its own host and database:**
  - `NeutralOperator.Server`: shared-use, occupancy, clandestine attachments, tenant portal, pole census, cut-and-remove.
  - `FieldService.Server`: paving, manhole, contractor US.
  - `IpBilling.Server`: including the Município grant bridge.
  - A future `Scada`/`Erp` service if needed.
- **The two sides talk only through:**
  - versioned integration events on the existing outbox/inbox (Kafka or ServiceBus);
  - the kernel's public REST/OGC APIs, which verticals call as clients;
  - descriptor registration at bootstrap.
- **The client** reaches vertical APIs through a gateway (YARP route per vertical, `/api/v/{vertical}/…`) and renders feature actions from kernel-aggregated descriptors.

**What this costs.** The atomic "approve order + commit occupancy + stage asset mutation" transaction that exists today becomes a **saga**. A good part of the saga is already built:
- `AssetMutationBatchRequestedV1`
- `IdempotentMutationBatchApplier` and `IAppliedMutationBatchStore`
- the inbox and outbox tables
- `InProcessAssetMutationBatchPublisher`, which is the in-process version of this exact hand-off

What is missing: a compensating event (`AssetMutationBatchRejectedV1`), occupancy states that are *pending* until the kernel confirms the mutation, and saga timeouts.

### 3.2 Roadmap (each phase ships on its own, with no change in behaviour unless stated)

**Phase 0 — Guardrails (S, low risk) — epic XD01-344**
- Extend `ServerArchitectureTests`. The kernel types in `GeoAssets.Server` must not reference `Provider.*`, `Domain.*` or vertical `Workflow.*` namespaces. Start with an allowlist for the current offenders and shrink it in every later phase. This is the ratchet.
- Add Pole, Occupancy, Isp, NeutralOperator, Paving, Manhole, Contractor and Municipio to the Invariant02 tokens, also as an allowlisted ratchet.
- Add a route-inventory snapshot test that lists every mapped endpoint with its method, pattern and policies. It proves later phases keep behaviour.
- Write a test to confirm or refute smell P.

**Phase 1 — Chassis contract and quarantine inside the Server (M, low risk) — epic XD01-345**
This is the step that makes extraction mechanical later.
- New project `core/GeoAssets.Hosting.Abstractions`:
  - `IGeoAssetsModule { string Id; void ConfigureServices(IServiceCollection, IConfiguration); void MapEndpoints(IEndpointRouteBuilder); }`
  - optional `IModuleSeeder`
  - `builder.AddGeoAssetsModules(config)`, discovered from configuration before the container is built
- This contract is the order-type chassis that every vertical host uses.
- Move the existing registrations, unchanged, into modules in a `Modules/` folder of the Server:
  - `NeutralOperatorModule`: shared-use, occupancy, clandestine attachments, tenant portal, in-process mutation apply.
  - `IpBillingBridgeModule`: Município.
- Postgres wiring (Program.cs lines 42-73) moves into a `PostgresSpatialModule` in the kernel.
- Outbox wiring moves into a neutral `WorkflowMessagingHostExtensions`.
- Program.cs shrinks to kernel wiring plus a single module call.
- Each module is now a ready-to-lift unit for Phase 5.

**Phase 2 — Chassis extension points in the generic engine (M-L, medium risk) — epic XD01-346**
Every vertical service will use these. They must exist before extraction, or each vertical host would fork the generic route again.
- `IOrderActionHandler` registry keyed by `(orderTypeId, verb)`. The generic `/actions` route calls `handlers.TryHandle(...)` before the default append. `OccupancyApprovalRoute` becomes the NeutralOperator module's handler. This removes smells B and C.
- `OccupancyApproval.Commits` checks an explicit order-type capability (for example the `occupancy-commit` capability set by the module) instead of `TargetPolicy != null`.
- `IOrderLifecycleHook.OnTerminalAsync` replaces the `ServiceOrderTargetRelease` occupancy discard (smell J).
- `ITransitionGuard` is invoked by `ValidatingServiceOrderRepository`.
- `AddServiceOrderRules` collects `IEnumerable<IServiceOrderRule>`.
- `IProblemDetailsMapper` per module, chained by the kernel's exception handler.
- **Action vocabulary (smell D):** introduce an `OrderAction` string value object with well-known core constants, and keep the enum's numeric values as a compatibility mapping. *High-risk sub-step*: the enum is persisted in the action log and exchanged over JSON/Protobuf. It needs a dual-read period and a data migration.

**Phase 3 — Feature-type semantics (M, medium risk) — epic XD01-347**
- Add `Roles` (a string set) to `AssetType` (Core model, provider row, REST DTO), seeded alongside the existing domain asset types.
- `OrderTargetPolicy` can target roles as well as ids.
- Replace `OrderOccupancyOptions.IsPole` and the magic GUID (three copies) with a role lookup (smell F).
- **Feature actions as data.** Each vertical registers *feature-action descriptors* with the kernel when it starts (`POST /api/registry/order-types` with an idempotent upsert, or a `OrderTypeDescriptorPublishedV1` event). A descriptor holds: role → verb, label key, target order type, and vertical route.
  - `GET /api/geoassets/features/{id}/actions` resolves the feature's roles against those descriptors. No vertical code runs in the kernel.
  - `MapContextMenu` renders the list and invokes the vertical's route through the gateway.
- Roles are owned by asset types (spatial kernel data). Verticals only *reference* roles, so the kernel never learns what a pole is.

**Phase 4 — Server-side provider pool (L, medium risk) — epic XD01-348**
- Add a `PostgresProviderPlugin`.
- Add a server `ICatalogPool` of named catalogs built from `GeoAssets:Providers[]` through `IProviderPlugin`. The default catalog is the existing warmed Postgres provider. `CatalogInstanceId` becomes the pool key.
- Capability ports with generic fallbacks:
  - `IAssetRenderQuery`, so the WMS renderer works over the port. Postgres keeps its pushdown, and the fallback uses `GetInBoundsAsync` plus `LayerResolver`.
  - `IOrganizationCatalogFactory`, so `UseOrganizationAssetCatalogs` stops resolving concrete Postgres types.
  - `IAssetMutationBatchApplier` and `IAppliedMutationBatchStore`, registered by the provider module.
- Move `GeoAssetsDbContextFactory` (the design-time factory) into the provider project.
- Delete `IExternalProviderFactory` or fold it into `IProviderPlugin`.

**Phase 5 — Assembly split: engine vs verticals (L, medium risk) — epic XD01-349**
- Move vertical code out of `core/GeoAssets.Workflow` and `GeoAssets.Core/Isp` into `verticals/<Vertical>/GeoAssets.<Vertical>.Domain` and `…Application` assemblies that depend only on the engine and the abstractions.
- The engine keeps only `Orders/Rules/Targets/Selection/Notifications` plus the chassis.
- Drop the `Workflow → Domain.Sanitation` reference.
- Move the Município DTOs out of `core/GeoAssets.Identity`.
- `IModulePermissionContributor`: each vertical publishes its permission codes and role bundles to the kernel's identity service at startup (an idempotent upsert), replacing the hardcoded lists in `GeoIdentitySeeder` (smell L).

**Phase 6 — Persistence extraction per vertical (L, high risk) — epic XD01-350**
- Remove the vertical entities from the kernel's `ServiceOrderDbContext`. Each vertical host gets its own database and `DbContext`, extending the engine's base context with its own tables:
  - NeutralOperator: pole occupancies, change sets.
  - FieldService: paving, manhole, contractor US.
  - IpBilling: already done.
- `MigrationsAssembly` points at the vertical host, as `FieldService.Server` and `IpBilling.Server` already do.
- **Data move:** dual-write or a one-off copy into the vertical database, then a kernel migration that drops the tables. Handle the 13 vertical migrations listed under smell I.
- The RLS SQL that embeds domain rules (`scopeId`, pole owner) moves with the tables.

**Phase 7 — Vertical extraction and sagas (XL, high risk), one vertical at a time, NeutralOperator first — epic XD01-351**
- **Stand up `NeutralOperator.Server`** on the chassis, copying the `IpBilling.Server` template. Lift `NeutralOperatorModule` (Phase 1) as-is. Its order types, endpoints, problem mappers and handlers (Phase 2) go with it.
- **Turn approval into a saga:**
  1. The vertical approves.
  2. It stages `AssetMutationBatchRequestedV1` on its outbox.
  3. The kernel's inbox applies it through `IdempotentMutationBatchApplier`.
  4. The kernel emits `AssetMutationBatchAppliedV1` or `…RejectedV1`.
  5. The vertical finalizes or compensates, putting the occupancy back to `pending → committed | reverted`.
  - Retire `AddServerInProcessAssetMutationApply`.
- **Targets and selection:** the vertical calls the kernel's `selection/resolve` and feature APIs through the existing `GeoAssets.Provider.Rest` / `Workflow.Rest` clients (machine-to-machine auth). Organization grants go through the existing grant-sync outbox.
- **Gateway:** a YARP route `/api/v/neutral-operator/**` → `NeutralOperator.Server`. The Web/MAUI REST clients (`RestOrderOccupancyStagingService`, `RestSharedUseDispatchClient`, `IspTenantPortalRestExtensions`) are re-pointed to it.
- **Client UI:** a vertical RCL per vertical (menu items, order-detail panels through a `DynamicComponent` registry keyed by order-type capability), plus the `SchemaDrivenAttributeEditor` for `AttributesSchemaJson`. Clients stop re-registering order types and read the kernel registry instead (smell N). MAUI's single-file page exceptions stay as they are.
- **Repeat** for FieldService (already partially extracted) and IpBilling. Remove `MunicipioProvisioningService` from the Server.

### 3.3 Key risks and mitigations
- **Losing atomicity (approve → occupancy → asset mutation).** Mitigate with the saga plus idempotent consumers (already present), explicit pending and compensation states, saga timeouts with alerting (observability-conventions), and the inbox for exactly-once effects.
- **Read-your-writes on the map after approval.** Mitigate by having the client show a "pending" state from the vertical until `AssetMutationBatchAppliedV1` arrives, then refresh the WMS/WFS layer.
- **Distributed authorization drift**, where the vertical and the kernel disagree on who can act. Mitigate with a single policy engine in the kernel (`IGeoAuthorizationService`) that verticals query or cache through the grant-sync outbox, and fold smell H's second model into `ServiceOrderRules`.
- **Wire compatibility of the action vocabulary and the events.** Mitigate with versioned Protobuf contracts (`…V1`), contract tests, a dual-read period and numeric enum aliases.
- **Data migration out of the shared database.** Mitigate with per-table cut-over, rehearsal on a production snapshot, and dual-write behind a flag.
- **Operational cost** (N hosts, databases and pipelines). Mitigate with a shared chassis package, Docker Compose stacks per vertical under `deploy/`, and one OTel pipeline.
- **Scope creep.** The Phase 0 ratchet makes progress measurable: the allowlist size is the coupling metric.

### 3.4 Feature selection for service orders: who owns it

**Today: `GeoAssets.Server`, centrally and generically.** No vertical has its own selection provider. Only the Server registers it (`Program.cs:59, 121-123`); FieldService.Server and IpBilling.Server do not. It has three layers:
1. **Strategy (how features are picked).** `IFeatureSelectionStrategy` is discovered through MEF by `FeatureSelectionRegistry`. There are 9 domain-free built-ins in `core/GeoAssets.Workflow/Selection/Strategies`: bounding-box, polygon fence, manual, identifier list, nearby, asset-type filter, topology reachability, inherit parent/child, and background. More can be added as `GeoAssets.Plugin.*.dll` in `plugins/`.
2. **Spatial execution.** `IAssetSpatialQuery` pushes the query down to PostGIS and scopes it to the tenant (`AddPostgresAssetSpatialQuery`). The generic fallback is `AssetProviderSpatialQuery`. `BoundedFeatureSelector` enforces the size limits.
3. **Policy (what an order type may target).** `OrderTargetPolicy` is data on the `OrderType`: asset types, limit, activities, exclusivity, fan-out. It is applied by `OrderTargetSelectionService` and `TargetConflictEvaluator`, and targets are persisted in `ServiceOrderDbContext`.

The endpoints are in `ServiceOrderTargetsRestApiExtensions*.cs`: `/service-orders/{id}/targets/selection/preview|apply`, `/selection/resolve` (map gestures only, scoped to the organization), the targets CRUD routes, and `/targets/usages`. Clients call them through `RestMapSelectionClient` and `RestOrderTargetSelectionService`.

A vertical contributes **only data** here, through `TargetPolicy`. This is one of the parts that is already decoupled. Its one weakness is that target asset types are raw GUIDs (smell F); the Phase 3 roles fix that.

**Target under microservice-per-vertical: selection stays a kernel capability. Decided: a global target ledger in the kernel.**

What the decision means for the plan:
- In Phase 6, `service_order_targets` stays in the kernel database. Its order key becomes `(vertical, orderId)`.
- In Phase 7, verticals reserve, release and commit targets through the kernel's targets API or events, and stop writing to their own tables.
- `ServiceOrderTargetRelease`'s terminal-state cleanup becomes a ledger release triggered by the vertical's `OrderStateChangedV1`.
- **Why:**
  - It needs the spatial data, the PostGIS pushdown and the tenant scoping, which all live in the kernel.
  - **Exclusivity has to be checked across verticals**: for example, two verticals' orders both claiming the same pole for exclusive work. That check needs one global view of usage.
- **Kernel owns:**
  - strategy resolution (`/selection/resolve`, preview);
  - a **global target ledger**: `(vertical, orderId) → featureIds + activity + exclusivity`, with conflict checks and `/targets/usages`.
- **A vertical owns** its order lifecycle and *references* targets by ledger id. It reserves, releases and commits targets through the kernel API or events.
- **Domain-specific selection:**
  - Prefer declarative strategy parameters (role filter + topology + attribute predicates).
  - If code is unavoidable, prefer a *remote strategy* (the kernel calls a strategy endpoint the vertical registers in its descriptor) over a plugin DLL loaded inside the kernel.

### 3.5 Sequencing summary
`P0 → P1 → P2 → (P3 ∥ P4) → P5 → P6 → P7 (NeutralOperator) → P7 (FieldService, IpBilling)`

- Phases 0–4 deliver a domain-agnostic *in-process* kernel. Each is valuable even if extraction pauses.
- Phases 5–7 deliver the chosen topology (D1).

---

## Verification (per phase)
- `./scripts/verify-agent-worktree.sh` exits 0: format, build with `/warnaserror`, tests.
- The Phase 0 architecture tests and the route-inventory snapshot stay green, and the allowlist only shrinks.
- `dotnet ef migrations add Probe` for each context produces an empty diff after Phases 4 and 6.
- **Kernel-only boot:** with `GeoAssets:Modules=[]` and no vertical registered, the server starts, `/healthz` reports healthy, and features, WFS and WMS work (invariant I5).
- **End-to-end parity** on the docker-compose `server` stack, plus the vertical stacks after Phase 7: shared-use dispatch, occupancy stage → approve → asset mutation visible on WMS/WFS for a tenant user, and the ISP tenant portal through the gateway, compared before and after.
- **Saga fault injection:** kill the kernel consumer while an approval is in flight. The occupancy stays `pending`, the batch is applied exactly once on recovery, and a rejected batch compensates.

## Implementation tracking (Jira)

All tickets carry the label `server-decoupling` (`project = XD01 AND labels = server-decoupling`). Epics block each other in programme order; children are linked with `Blocks` in build order and state their dependencies in the description.

| Phase | Epic | Children (build order) |
|---|---|---|
| P0 Guardrails | XD01-344 | XD01-352 Server reference ratchet · XD01-353 Workflow token/assembly ratchet · XD01-354 Route-inventory snapshot · XD01-355 Characterization tests (smells H, P) |
| P1 Chassis & quarantine | XD01-345 | XD01-356 `IGeoAssetsModule` contract → XD01-357 neutral messaging wiring → XD01-358 NeutralOperatorModule → XD01-359 IpBillingBridge + PostgresSpatial modules, kernel-only boot |
| P2 Extension points & authz | XD01-346 | XD01-360 `IOrderActionHandler` → XD01-361 occupancy capability; XD01-362 lifecycle hook + transition guard; XD01-363 DI rules + problem mappers → XD01-364 single order-authz decision point → XD01-365 remove tenant-portal routes; XD01-366 extensible action vocabulary |
| P3 Feature semantics | XD01-347 | XD01-367 `AssetType.Roles` → XD01-368 role-based target policy → XD01-369 descriptor registry + `/features/{id}/actions` → XD01-370 context-menu actions |
| P4 Provider pool | XD01-348 | XD01-371 `PostgresProviderPlugin` + `ICatalogPool` → XD01-372 `IAssetRenderQuery` (WMS) · XD01-373 `IOrganizationCatalogFactory` → XD01-374 plugin-owned applier, drop Provider ref |
| P5 Assembly split | XD01-349 | XD01-375 NeutralOperator assemblies → XD01-376 FieldWork/ERP/SCADA out of engine · XD01-377 permission contributors → XD01-378 client vertical RCLs |
| P6 Persistence & ledger | XD01-350 | XD01-379 global target ledger → XD01-380 `NeutralOperatorDbContext` → XD01-381 FieldService tables out of kernel |
| P7 Extraction & saga | XD01-351 | XD01-382 `NeutralOperator.Server` → XD01-383 approval saga → XD01-384 gateway + client re-pointing → XD01-385 Município bridge to IpBilling.Server, ratchets at zero |

## Follow-ups not yet ticketed

- `apps/GeoAssets.FieldService.Server/Program.cs:85` registers `GeoAssetsDbContext` directly against the core `GeoAssets` connection string, contradicting Invariant 1 of the intersection evaluation ("only `GeoAssets.Server` may connect to `geo_entity`"). Out of this ADR's scope (FieldService host), but the same `IGeoCoreClient`/ledger route applies.
- `CLAUDE.md`'s gate table names NetArchTest; the repository's architecture tests are reflection-based (`GetReferencedAssemblies`). P0 extends those; adopting `NetArchTest.Rules` is optional.
