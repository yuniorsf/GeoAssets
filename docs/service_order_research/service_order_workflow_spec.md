# GeoAssets Service Order (Ordem de Serviço) Workflow Functional Specification

**Document Identifier**: `GEOASSETS-SPEC-OS-2026-01`  
**Target Platform**: GeoAssets Geospatial Asset & Field Service Platform (.NET 10 LTS, C# 14, PostgreSQL 16 + PostGIS, NetTopologySuite 2.6, Blazor WebAssembly, .NET MAUI)  
**Author**: Engineering Architecture Team (`worker_functional_spec_1`)  
**Status**: Production-Grade Functional Specification  
**Date**: October 2026  
**Reference Codebase**: `/Users/yuniorsf/WORK/GeoAssets`  

---

## Executive Summary

This document defines the production-grade functional specification and engineering architecture for the **Service Order (*Ordem de Serviço - OS*) Workflow Engine** within the `GeoAssets` platform. 

`GeoAssets` currently provides a modular, multi-tenant enterprise foundation featuring:
1. A decoupled domain workflow model (`IServiceOrder`, `ServiceOrder`, `OrderType`, `ServiceOrderTransitions`, `ServiceOrderRules`).
2. Robust spatial persistence utilizing PostgreSQL with the PostGIS extension (`GeoAssetsDbContext`, `geo_entity`, NetTopologySuite 2.6).
3. Directed topological graph processing (`TopoGraph`) with native support for radial electrical circuits and municipal hydraulic networks.
4. Autonomous agent orchestration through Microsoft Agent Framework (`GeoAssets.Workflow.Agents`).
5. Multi-platform delivery across Blazor WebAssembly (`GeoAssets.Web`) and .NET MAUI hybrid clients (`GeoAssets.MAUI`).

This specification bridges the current codebase state with the stringent regulatory, operational, and commercial requirements of Brazilian infrastructure utilities across three critical domains:
* **Electrical Power Distribution Assets**: Regulated by ANEEL (REN nº 1.000/2021, PRODIST Módulos 8 e 10, Base de Dados Geográfica da Distribuidora - BDGD), Ministério do Trabalho (NR-10, NR-35), and concessionaire operational standards (Neoenergia Coelba DIS-NOR-068, Enel Ceará JoiN-AS, CEMIG RDA / US billing, CPFL GED-94).
* **Water and Wastewater Sanitation Utilities (*Saneamento Básico*)**: Regulated by the Federal Sanitation Framework (Lei Federal nº 14.026/2020), Agência Nacional de Águas e Saneamento Básico (ANA Resolução nº 211/2024 / NR nº 9/2024, NR nº 8/2024, NR nº 15/2025), state regulators (ARSESP, ARCE, AGENERSA), municipal paving compliance (Sabesp ICERP, Sanepar SGM/MOS), and safety norms (NR-33).
* **Telecommunications Wire & Fiber-Optic Distribution**: Regulated by ANATEL and ANEEL (Resolução Conjunta nº 004/2014, Resolução nº 1.044/2023), Federal Decree nº 12.068/2024 (Artigo 16 - compulsory transfer of pole management to neutral entities), ABNT NBR 15214, FTTH wholesale models (V.tal, ABRINT), and high-precision optical restoration standards (RNP Redecomep, Telebras).

---

# 1. End-to-End Service Order State Machine & Actor Model

## 1.1 State Machine Graph & Formal Lifecycle

The core workflow engine in `GeoAssets` (`core/GeoAssets.Workflow/Orders/ServiceOrderTransitions.cs`) currently implements a foundational 6-state graph: `Draft`, `Pending`, `InProgress`, `OnHold`, `Completed`, and `Cancelled`. 

To support field operations across utility networks where safety handshakes, crew transit, on-site check-ins, quality audits, regulatory verifications, and offline concurrency collisions must be deterministically managed, the state machine is formally extended into a **12-State Production Workflow Graph**. This model is fully compatible with `OrderType.States` (`WorkflowState`) and `OrderType.Transitions` (`WorkflowTransition`).

```text
                     ┌───────────────────────────┐
                     │           Draft           │
                     └─────────────┬─────────────┘
                                   │ Submit / Create
                                   ▼
                     ┌───────────────────────────┐
                     │      Pending/Created      │◄────────────────────────┐
                     └─────────────┬─────────────┘                         │
                                   │ Schedule                              │ Reopen /
                                   ▼                                       │ Rework
                     ┌───────────────────────────┐                         │
                     │         Scheduled         │                         │
                     └─────────────┬─────────────┘                         │
                                   │ Dispatch                              │
                                   ▼                                       │
                     ┌───────────────────────────┐                         │
                     │        Dispatched         │                         │
                     └─────────────┬─────────────┘                         │
                                   │ Acknowledge & Start Travel            │
                                   ▼                                       │
                     ┌───────────────────────────┐                         │
                     │          EnRoute          │                         │
                     └─────────────┬─────────────┘                         │
                                   │ On-Site Geofence / Check-In           │
                                   ▼                                       │
                     ┌───────────────────────────┐                         │
                     │          OnSite           │                         │
                     └─────────────┬─────────────┘                         │
                                   │ Safety Gate (APR/NR-33 Passed)        │
                                   ▼                                       │
 ┌──────────────┐     Suspend      ┌───────────────────────────┐           │
 │  Suspended   │◄─────────────────┤   InProgress / Executing  │           │
 │   (OnHold)   │─────────────────►└─────────────┬─────────────┘           │
 └──────────────┘     Resume                     │ Complete Field Work     │
                                                 ▼                         │
                                   ┌───────────────────────────┐           │
                                   │         Completed         │           │
                                   └───────┬───────────▲───────┘           │
                                           │           │ Approve           │
                                           │ Audit &   │ (Validate Field)  │
                                           │ Inspect   │                   │
                                           ▼           │                   │
                                   ┌──────────────┐    │                   │
                                   │  Inspected   │    │                   │
                                   │  (Audited)   │    │                   │
                                   │ (Success-    │    │                   │
                                   │  Terminal)   │    │                   │
                                   └──────────────┘    │                   │
                                                       │                   │
                                  Offline Sync         │                   │
   ┌──────────────────────────┐   Structural Collision │                   │
   │ Any Active / Cancelled   │──────────────────────┐ │                   │
   │ (Offline Completed vs    │                      ▼ │                   │
   │  Cancelled/Reassigned)   │            ┌──────────────────┐            │
   └──────────────────────────┘            │  ConflictReview  │            │
                                           └─────────┬────────┘            │
                                                     │                     │
                                              Reject │ Rework /            │ Reject
                                            (Uphold) │ Re-dispatch         │ (Audit Non-
                                                     │                     │  Conformity)
                                                     ▼                     │
 [Any Active State except Inspected] ──► ┌──────────────────────────┐      │
                                         │        Cancelled         │◄─────┘
                                         │    (Failed-Terminal)     │
                                         └──────────────────────────┘
```

### Detailed State Definitions

1. **`Draft`**:
   - *Description*: The initial authoring state. The order is being drafted by a human dispatcher, an external system webhook, or an AI agent. 
   - *Invariants*: Modifiable without optimistic concurrency restrictions. Features can be added, removed, or spatially queried. No field crew or vehicle resources are reserved.
2. **`Pending` (`Created`)**:
   - *Description*: Formally submitted to the operational backlog. It awaits automated or human triage, priority reassessment, material reservation, and scheduling.
   - *Invariants*: Immutable asset geometry (`FeatureIds`). Read-accessible to dispatchers and automated routing algorithms.
3. **`Scheduled`**:
   - *Description*: Allocated to a specific operational calendar window, shift, and service sector. Materials and vehicle slots are tentatively locked.
   - *Invariants*: `ScheduledAt` timestamp is populated. Target SLA deadline (`SlaTargetTimestamp`) is locked and countdown begins.
4. **`Dispatched`**:
   - *Description*: Formally assigned and transmitted to a specific technician, field crew lead (`AssignedTo`), or contractor company.
   - *Invariants*: Pushed to the target technician's local offline sync queue in .NET MAUI. An `OrderDispatch` record is appended to `ServiceOrder.Dispatches`.
5. **`EnRoute`**:
   - *Description*: The field crew acknowledges the dispatch and starts moving toward the physical coordinates of the asset.
   - *Invariants*: Real-time GPS breadcrumb tracking activates on the mobile client. Distance and estimated time of arrival (ETA) are continuously recalculated.
6. **`OnSite`**:
   - *Description*: The field crew arrives at the designated asset location.
   - *Invariants*: Automatic check-in verified by GPS geofencing ($\le 30\text{ m}$ radius from target `GeoFeature` coordinate) or manual override with mandatory photographic justification. Safety checklists (APR / NR-10 / NR-35 / NR-33) become mandatory and interactive.
7. **`InProgress` (`Executing`)**:
   - *Description*: Physical intervention is underway (e.g., live-line transformer replacement, water main clamp installation, fusion splicing).
   - *Invariants*: Safety checklist has been signed digitally. Elapsed execution time is measured.
8. **`Suspended` (`OnHold`)**:
   - *Description*: Field execution is temporarily halted due to an external impediment.
   - *Invariants*: Mandatory impediment classification:
     - `IMP_WEATHER_RAIN` (Severe rain / lightning hazard for high voltage).
     - `IMP_PERMIT_TRAFFIC` (Municipal transit authority permit pending).
     - `IMP_CLIENT_ABSENT` (Consumer absent for indoor meter inspection).
     - `IMP_GAS_HAZARD` (Atmospheric gas concentration threshold violated in manhole).
     - `IMP_THIRD_PARTY_INTERFERENCE` (Unmapped gas or fiber line intersecting water trench).
     SLA clock rules dictate whether the regulatory countdown pauses or continues.
9. **`Completed`**:
   - *Description*: Physical field work is finished. All mandatory measurements (optical power dBm, water pressure bar, conductor tension), before/after geotagged photos, and consumed inventory have been recorded.
   - *Invariants*: `CompletedAt` timestamp is recorded. The order is locked against field edits and moves to the audit queue.
10. **`Inspected` (`Audited`)**:
    - *Description*: The terminal success state. A field supervisor, concessionaire quality auditor, or regulatory validation daemon reviews the evidence (e.g., asphalt ICERP test, BDGD asset delta, OTDR trace) and formally signs off.
    - *Invariants*: `IsSuccess = true`. Triggers contractor remuneration (Unidades de Serviço - US), updates the central GIS production layers, and exports regulatory compliance records.
11. **`Cancelled`**:
    - *Description*: Terminal aborted state. The order is terminated prior to completion.
    - *Invariants*: Allowed from any active state prior to `Inspected`. Mandatory cancellation reason code and audit comment must be recorded. Any reserved truck inventory is returned to stock.
12. **`ConflictReview`** (`Quarantine / Administrative Adjudication`):
    - *Description*: An administrative hold and reconciliation state entered exclusively when offline sync reconciliation detects an irreconcilable structural collision (e.g., dispatcher cancelled or reassigned an order while an offline crew physically executed it).
    - *Invariants*:
      - The order is locked against automated scheduling, dispatch, or mobile field modifications.
      - All submitted field evidence (geotagged photos, GPS coordinates, timestamps, consumed inventory, and technician signatures) is immutably sealed in `OrderActionLog` as an unverified revision batch.
      - An urgent adjudication ticket is enqueued on the Field Supervisor / Operational Controller dashboard.
      - SLA regulatory countdown is suspended under ANEEL PRODIST / ARSESP audit trail rules pending formal resolution.
      - Out-degree is strictly $\ge 1$: the order can be transitioned to `Completed` (adjudication approval), `Cancelled` (adjudication rejection), `Pending` (re-dispatch/rework), or `InProgress` (re-authorization).

### State Transition Matrix (Table 1.1)

| From State | To State | Trigger Action (`OrderActionType`) | Permitted Actors | Mandatory Preconditions | System Side Effects |
|---|---|---|---|---|---|
| `Draft` | `Pending` | `Approve` / `Execute` | Dispatcher, AI Agent, External System | Title, OrderTypeId, and $\ge 1$ `FeatureId` bound | Validates `Attributes` against `AttributesSchemaJson` |
| `Draft` | `Cancelled` | `Cancel` | Creator, Dispatcher, Administrator | Cancellation comment supplied | Releases unpersisted drafts; frees temporary reservations |
| `Pending` | `Scheduled` | `Assign` | Dispatcher, AI Agent | Valid work window and target date defined | Computes SLA target countdown; sets `ScheduledAt` |
| `Scheduled` | `Dispatched` | `Dispatch` | Dispatcher, AI Agent | Valid target technician / crew assigned | Creates `OrderDispatch`; enqueues delta for MAUI sync |
| `Dispatched` | `EnRoute` | `Accept` | Field Crew Lead, Technician | Mobile device online or local sync active | Records transit start timestamp; starts GPS tracking |
| `EnRoute` | `OnSite` | `Execute` | Field Crew Lead, Technician | GPS proximity $\le 30\text{ m}$ of asset or override photo | Records arrival timestamp; unlocks safety checklists. (Note: `Annotate` is decoupled and does NOT trigger arrival) |
| `OnSite` | `InProgress` | `Execute` | Field Crew Lead | Safety checklist (APR/NR-33) 100% compliant | Stamps work start time; locks asset in GIS topology |
| `InProgress` | `Suspended` | `Suspend` *(or `Execute` with `ImpedimentPayload`)* | Field Crew Lead, Supervisor | Valid impediment code (`IMP_*`) and explanation provided | Notifies CCO/COD; evaluates regulatory SLA pause rule; freezes work timer. (Standard `Annotate` calls NEVER trigger suspension) |
| `Suspended` | `InProgress` | `Execute` | Field Crew Lead, Supervisor | Impediment cleared; secondary safety check passed | Resumes work timer; logs resumption in `ActionLog` |
| `InProgress` | `Completed` | `Complete` | Field Crew Lead | All mandatory photos, checklists, and items captured | Stamps `CompletedAt`; enqueues order for supervisor audit |
| `Completed` | `Inspected` | `Validate` | Field Supervisor, Concessionaire Auditor | ICERP/As-Built/OTDR thresholds verified | Triggers US billing; updates central GIS production layer |
| `Completed` | `Pending` | `Reject` | Field Supervisor, Concessionaire Auditor | Audit rejection reason and rework specifications logged | Reopens order as rework child or resets status; alerts crew |
| `ConflictReview` | `Completed` | `Approve` | Field Supervisor, Concessionaire Auditor | Supervisor validates physical field evidence (GPS, photos, APR) despite prior cancellation or reassignment | Unfreezes order; accepts Crew A evidence; triggers standard inspection and contractor remuneration |
| `ConflictReview` | `Cancelled` | `Reject` / `Cancel` | Field Supervisor, Operations Manager | Supervisor upholds cancellation; field evidence deemed invalid, unauthorized, or redundant | Order permanently cancelled; inventory returned or investigated; contractor notified |
| `ConflictReview` | `Pending` | `Reject` | Field Supervisor, Central Dispatcher | Field intervention was incomplete or aborted; physical asset still requires resolution | Resets order to backlog (`Pending`) for re-scheduling; preserves audit evidence of failed attempt |
| `ConflictReview` | `InProgress` | `Execute` | Field Supervisor, Central Dispatcher | Supervisor resolves reassignment dispute in favor of on-site crew who continues active work | Re-authorizes crew's mobile session; restores work timer; updates `AssignedTo` |
| `Cancelled` / `Dispatched` / `InProgress` | `ConflictReview` | `Execute` *(Internal Sync Collision)* | GeoAssets Sync Engine (System Actor) | Concurrency collision: offline field work submitted against cancelled order or conflicting `AssignedTo` | Locks order in quarantine; seals offline evidence batch in `OrderActionLog`; pauses SLA; alerts supervisor |
| *Any Active State* | `Cancelled` | `Cancel` | Supervisor, Dispatcher, Administrator | Mandatory cancellation code provided | Logs cancellation in `ActionLog`; aborts SLA countdown; recalls field dispatches |

### 1.1.1 Action Type Decoupling & Suspension Architecture

To prevent verb overloading and unintended side effects, the workflow engine enforces an explicit decoupling between routine informational notes (`OrderActionType.Annotate`) and workflow state transitions:

#### Core Enum Definition (`OrderActionType.Suspend`)
In `core/GeoAssets.Workflow/Orders/OrderActionType.cs`:
```csharp
namespace GeoAssets.Workflow.Orders;

public enum OrderActionType
{
    View,
    Approve,
    Reject,
    Assign,
    Accept,
    Dispatch,
    Execute,
    Complete,
    Cancel,

    /// <summary>
    /// Add non-mutating comments, notes, or attachments to the order.
    /// Invariant: Annotate calls MUST NEVER alter <see cref="IServiceOrder.Status"/>.
    /// </summary>
    Annotate,

    /// <summary>
    /// Temporarily suspend active execution due to an external impediment, safety hazard,
    /// or transit permit requirement. Moves the order from InProgress to Suspended.
    /// Distinct from <see cref="Annotate"/> (which records audit information without state change).
    /// </summary>
    Suspend,

    Calculate,
    Validate,
}
```

#### Typed Impediment Payload Specification
Transitioning an order into `Suspended` requires the strongly typed `ImpedimentPayload`:
```csharp
namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Mandatory payload required to transition an order into the Suspended state.
/// </summary>
public sealed record ImpedimentPayload(
    [property: JsonPropertyName("impedimentCode")] string ImpedimentCode,
    [property: JsonPropertyName("reasonDescription")] string ReasonDescription,
    [property: JsonPropertyName("slaClockPauseRequested")] bool SlaClockPauseRequested,
    [property: JsonPropertyName("externalPermitProtocol")] string? ExternalPermitProtocol = null,
    [property: JsonPropertyName("expectedResolutionTime")] DateTimeOffset? ExpectedResolutionTime = null);
```

#### Status Invariant Gate in `ServiceOrder.cs`
`ServiceOrder.RecordAction()` explicitly prevents `Annotate` from mutating order status:
```csharp
if (action == OrderActionType.Annotate && resultingStatus is not null)
{
    throw new InvalidOperationException(
        "OrderActionType.Annotate is strictly an informational action and cannot specify a resultingStatus. " +
        "Use OrderActionType.Suspend to suspend an active order.");
}
```

---

## 1.2 Multi-Tier Actor Model & Roles

GeoAssets enforces a strict Role-Based and Attribute-Based Access Control (RBAC/ABAC) model via `GeoIdentityDbContext` and `core/GeoAssets.Workflow/Rules/ServiceOrderRules.cs`. Operations are executed across five distinct classes of human, system, and autonomous actors:

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                                   ACTOR TAXONOMY                                       │
├───────────────────────────────┬────────────────────────────────────────────────────────┤
│ 1. Dispatcher / Controller    │ Centro de Operações (COD / CCO / COI / NOC).           │
│    (Human Central Control)    │ Triage, emergency rerouting, manual overrides.         │
├───────────────────────────────┼────────────────────────────────────────────────────────┤
│ 2. Field Crew Lead / Tech     │ Encarregado, Eletricista, Encanador, Cabista.          │
│    (Human Field Mobility)     │ Mobile offline execution, APR, measurements, photos.   │
├───────────────────────────────┼────────────────────────────────────────────────────────┤
│ 3. Field Supervisor / Auditor │ Fiscal de Campo, Engenheiro, Auditor Concessionária.   │
│    (Human Quality Gate)       │ In-situ inspection, ICERP approval, rework dispatch.   │
├───────────────────────────────┼────────────────────────────────────────────────────────┤
│ 4. External System Trigger    │ SCADA, OMS, Telemetria VRP, SAC 195/116, Webhooks.     │
│    (System Integration)       │ Automated fault creation, municipal license ingestion. │
├───────────────────────────────┼────────────────────────────────────────────────────────┤
│ 5. AI Agent Executors         │ GeoAssets.Workflow.Agents (Microsoft Agent Framework). │
│    (Autonomous Intelligence)  │ Spatial clustering, auto-dispatch, VRPTW optimization. │
└───────────────────────────────┴────────────────────────────────────────────────────────┘
```

### 1. Dispatcher / Controller (*Despachante / Operador CCO / COD / COI / NOC*)
- **Domain Context**: Operates inside the distribution control centers (e.g., Centro de Operação da Distribuição - COD at Neoenergia Coelba or Enel Ceará; Centro de Controle Operacional - CCO at Sabesp; Network Operations Center - NOC at an FTTH operator).
- **Core Responsibilities**:
  - Continuous operational monitoring of open backlogs across municipal districts and feeder zones.
  - Multi-order spatial triage: reviewing incoming alarms from SCADA/OMS or citizen portals and correlating them with known grid topology.
  - Resource assignment: matching work orders to field crews based on vehicle equipment (e.g., bucket truck / caminhão guindauto vs. motorcycle technician), live-line qualifications, and geographical proximity.
  - Emergency interruption management: initiating load transfers or valve closures that impact downstream consumers.

### 2. Field Crew Lead / Field Technician (*Encarregado de Equipe / Eletricista / Encanador / Cabista*)
- **Domain Context**: Front-line field operators working in mobile teams (typically 2 to 3 workers per vehicle: e.g., electrical maintenance team composed of lead electrician and safety assistant; water maintenance team equipped with backhoe and mechanical tools; telecom fiber splice team).
- **Core Responsibilities**:
  - Mobile operation using ruggedized tablets or smartphones running `GeoAssets.MAUI`.
  - Mandatory pre-work safety execution: executing the digital APR (Análise Preliminar de Risco) in electricity, or atmospheric gas testing in sewer manholes (NR-33).
  - Geofenced arrival verification and step-by-step technical checklist execution.
  - Hardware asset tag reconciliation: scanning barcodes/QR codes of newly installed assets (transformers, hydrometers, ONTs) and recording retired asset tags.
  - Photographic and physical evidence collection: capturing high-resolution, georeferenced photos before, during, and after intervention.
  - Customer interaction: collecting on-screen digital signatures for commercial connection or meter changeout orders.

### 3. Field Supervisor / Quality Inspector (*Fiscal de Campo / Auditor de Qualidade*)
- **Domain Context**: Concessionaire engineering supervisors or independent auditing engineers (e.g., auditing road repaving quality for Sabesp under ARSESP standards; conducting pole attachment audits under ANEEL/ANATEL norms; verifying live-line safety compliance).
- **Core Responsibilities**:
  - Sample-based or 100% auditing of completed work orders.
  - Geometric and structural verification: checking road paving smoothness and grade alignment ($m^2$ calculation for ICERP), inspecting pole plumbness and conductor sagging, or validating bidirectional OTDR splice loss ($\le 0.05\text{ dB}$).
  - Validation or rejection: approving the order to trigger contractor remuneration (Unidades de Serviço - US) or rejecting it with mandatory deficiency codes, forcing an automatic rework cycle.

### 4. External System / Citizen Trigger (*Gatilhos Externos / Barramento de Integração*)
- **Domain Context**: Upstream automated systems feeding work orders into GeoAssets via REST/Kafka/AMQP:
  - *Citizen Portals & Omnichannel Bots*: Municipal SAC, WhatsApp bots (e.g., Coelba WhatsApp, Sabesp 195 Digital), and web portals reporting low water pressure, sewer overflows, or fallen tree limbs.
  - *SCADA & OMS (Outage Management Systems)*: Substation breaker trip alarms, automated recloser lockouts, or hydraulic telemetry alert indicating an abrupt pressure drop downstream of a Pressure Reducing Valve (VRP).
  - *Smart Metering (AMI / AMR)*: Telemetry alarms flagging reverse water flow (tampering), meter battery death, or smart electrical meter "last gasp" outage notifications.
  - *Regulatory Webhooks*: Notifications from municipal street excavation agencies (e.g., CONVIAS in São Paulo) granting or rejecting trenching permits.

### 5. AI Agent Autonomous Executors (`GeoAssets.Workflow.Agents`)
- **Domain Context**: Microsoft Agent Framework integration proven in `workflow/GeoAssets.Workflow.Agents/Executors/EmergencyRepairAgentWorkflow.cs`. AI Agents participate as first-class actors carrying a `WorkflowPrincipal` with `ActorKind.Agent`.
- **Core Responsibilities**:
  - *Spatial Outage Clustering*: When multiple customer calls flood the system within a 5-minute window, the AI Agent analyzes the topological graph (`TopoGraph.cs`), identifies the common upstream distribution transformer (`UNTRD`) or isolation valve, and merges redundant individual tickets into a single parent Emergency Repair Service Order (`ParentOrderId`).
  - *Autonomous Dispatch Optimization*: Evaluates crew qualifications, current GPS positions, traffic delays, and SLA deadlines to dispatch the optimal crew without human dispatcher intervention (`DispatchServiceOrderExecutor.cs`).
  - *Predictive Anomaly Detection*: Identifies repeat failure clusters (e.g., water pipes with abnormal leak recurrence rates, or fiber spans with chronic microbending) and automatically generates preventive inspection orders.

---

## 1.3 Concurrency Control & Optimistic Locking Architecture

Field service management for utility networks operates in fundamentally asynchronous, intermittently connected environments. A field crew working in a rural valley or an underground vault may spend hours offline. During this window, a central dispatcher might re-assign the order, adjust its priority, or attempt to cancel it due to an upstream network reconfiguration.

To prevent data corruption, lost updates, and operational race conditions without taking blocking database locks, GeoAssets enforces **Optimistic Concurrency Control (OCC)** using a database-maintained concurrency token (`RowVersion`).

### Database-Level Concurrency Token Mechanics

1. **Schema Definition**:
   In `workflow/GeoAssets.Workflow.EFCore/Entities/ServiceOrderRecord.cs`:
   ```csharp
   public byte[] RowVersion { get; set; } = [];
   ```
   In PostgreSQL, the `RowVersion` column is mapped to a byte array (`bytea`) backed by an automatic trigger or sequence that changes on every row mutation:
   ```sql
   -- ============================================================================
   -- GeoAssets Optimistic Concurrency Control (OCC): RowVersion DDL
   -- Database: PostgreSQL 14+ with pgcrypto extension
   -- ============================================================================

   -- 1. Ensure the pgcrypto extension is installed for SHA-256 binary digests
   CREATE EXTENSION IF NOT EXISTS pgcrypto;

   -- 2. Create the immutable RowVersion trigger function
   CREATE OR REPLACE FUNCTION touch_service_orders_row_version()
   RETURNS TRIGGER AS $$
   BEGIN
       -- Converts composite row identity, timestamp, and UUID into valid UTF-8 bytea
       -- and computes a 32-byte cryptographic SHA-256 digest matching C# byte[32].
       NEW."RowVersion" = digest(
           convert_to(
               NEW."Id"::text || '|' || 
               clock_timestamp()::text || '|' || 
               gen_random_uuid()::text, 
               'UTF8'
           ),
           'sha256'
       );
       NEW."UpdatedAt" = clock_timestamp();
       RETURN NEW;
   END;
   $$ LANGUAGE plpgsql;

   -- 3. Idempotent trigger binding covering both INSERT and UPDATE mutations
   DROP TRIGGER IF EXISTS trg_service_orders_row_version ON "ServiceOrders";

   CREATE TRIGGER trg_service_orders_row_version
   BEFORE INSERT OR UPDATE ON "ServiceOrders"
   FOR EACH ROW
   EXECUTE FUNCTION touch_service_orders_row_version();
   ```

#### Alternative Native PostgreSQL `xmin` Architecture (Zero Triggers / EF Core Npgsql)
PostgreSQL maintains an internal 32-bit transaction identifier (`xmin`) in every row's `HeapTupleHeaderData`. EF Core and Npgsql provide built-in first-class optimistic concurrency mapping to `xmin`:

1. **Entity Definition** (`ServiceOrderRecord.cs`):
   ```csharp
   internal sealed class ServiceOrderRecord
   {
       /// <summary>
       /// Optimistic concurrency token mapped to PostgreSQL's native xmin transaction column.
       /// Automatically updated by PostgreSQL MVCC on every UPDATE with zero trigger overhead.
       /// </summary>
       public uint Version { get; set; }
   }
   ```

2. **EF Core Configuration** (`ServiceOrderRecordConfiguration.cs`):
   ```csharp
   builder.Entity<ServiceOrderRecord>(b =>
   {
       // Instructs Npgsql to bind the property to PostgreSQL's system column "xmin"
       b.Property(o => o.Version)
        .IsRowVersion();
   });
   ```

3. **Generated SQL Execution**:
   ```sql
   UPDATE "ServiceOrders" 
   SET "Status" = @p0, "UpdatedAt" = @p1 
   WHERE "Id" = @id AND "xmin" = @p2;
   ```
   If another transaction touched the row, `xmin` differs, EF Core detects 0 rows updated, and automatically throws `DbUpdateConcurrencyException` (translated to `HTTP 409 Conflict`).

2. **EF Core Configuration**:
   In `workflow/GeoAssets.Workflow.EFCore/ServiceOrderDbContext.cs`:
   ```csharp
   builder.Entity<ServiceOrderRecord>(b =>
   {
       b.Property(o => o.RowVersion)
        .IsRowVersion()
        .IsConcurrencyToken();
   });
   ```

3. **REST API Concurrency Protocol**:
   All state transition and update endpoints in `GeoAssets.Server` enforce optimistic validation via standard HTTP ETag / `If-Match` headers:
   - When a client reads an order: `ETag: W/"[Base64 RowVersion]"`.
   - When updating or transitioning: client must submit `If-Match: W/"[Base64 RowVersion]"` or include the `RowVersion` field in the JSON payload.
   - If the database detects a mismatch between the provided `RowVersion` and the current record in the database, EF Core throws a `DbUpdateConcurrencyException`, translated by the server into `HTTP 409 Conflict`.

### The Field-Preservation Reconciliation Strategy

In utility field operations, a hard rejection of field data is unacceptable: **a field crew that spent 3 hours digging a trench, repairing a high-voltage cable, and taking photos cannot have their evidence silently discarded simply because a dispatcher updated the order notes in the central office.**

GeoAssets implements a deterministic **3-Way Reconciliation Strategy**:

```
                       Field Client Sync Submission
                    (Local mutations + cached RowVersion)
                                    │
                                    ▼
                     Server Database State Evaluation
                                    │
              ┌─────────────────────┴─────────────────────┐
              ▼                                           ▼
      RowVersion Matches                      RowVersion Mismatch (409)
   (No concurrent server edit)                            │
              │                                           ▼
              ▼                          Conflict Resolution Rule Evaluation
       Apply Changes Directly                             │
       Generate new RowVersion           ┌────────────────┼────────────────┐
       Return HTTP 200 OK                ▼                ▼                ▼
                                Scalar Conflict    Status Conflict   Reassignment Conflict
                              (Description edit) (Cancelled vs Done) (AssignedTo Mismatch)
                                        │                 │                │
                                        ▼                 ▼                ▼
                            Field-Priority Merge   ConflictReview     3-Case Policy
                            Checklist/photos kept. Evidence sealed.  Recall / Quarantine /
                            Server notes preserved.Supervisor alert. Custody Adjudication
```

1. **Non-Colliding Field Merges**:
   If the concurrent edit only affected non-conflicting operational metadata (e.g., central dispatcher updated customer phone number or priority notes, while the field technician filled in the safety checklist and consumed materials), the server executes a field-priority attribute merge, persists the field actions, increments `RowVersion`, and returns the merged entity.

2. **Structural / Status Semantic Collisions**:
   If the dispatcher marked the order `Cancelled` while the field crew marked it `Completed` while offline:
   - The order enters the administrative quarantine state: `ConflictReview`.
   - The field crew's physical evidence (photos, GPS coordinates, used materials, technician signatures) is **never deleted or overwritten**; it is committed to the append-only `OrderActionLog` as an immutable sealed revision.
   - An urgent adjudication ticket is enqueued on the supervisor dashboard to approve (`ConflictReview -> Completed`) or reject (`ConflictReview -> Cancelled`).

3. **Technician Reassignment Race Conditions (`AssignedTo` Mismatch)**:
   In Brazilian utility operations (e.g., rural feeders in Coelba/Enel Ceará, or subterranean sewers in Sabesp), crews spend hours disconnected from cellular coverage. Central dispatchers frequently reassign orders to backup crews (`Crew B`) when emergency response windows tighten, creating an asynchronous race condition when offline `Crew A` submits completed work.

#### The Reassignment Race Condition Topology
```
Time   Central Dispatcher / CCO                   Offline Field Crew A             Online Backup Crew B
 ───   ────────────────────────                   ────────────────────             ────────────────────
 T0    Dispatches Order #1001 to Crew A           Receives dispatch; enters
       (AssignedTo = Crew A, RowVersion = V1)     offline dead zone (valley/vault)
                                                  
 T1    Dispatcher notices SLA risk;               Offline (No signal)              Crew B receives dispatch
       Reassigns Order #1001 to Crew B            Arrives at physical asset;       Acknowledges and starts
       (AssignedTo = Crew B, RowVersion = V2)     executes APR & repairs           transit (EnRoute)
                                                  
 T2    Central DB has:                            Finishes physical repair;        Crew B driving to site
       AssignedTo = Crew B, Status = EnRoute      Clicks "Complete" offline;
                                                  Local SQLite stores V1 payload
                                                  
 T3    Crew A reconnects to cellular tower;
       Mobile MAUI client sends Sync Payload:
       (Base RowVersion = V1, Status = Completed, Evidence = Photos/GPS/Inventory)
       ───► Server detects RowVersion Mismatch (V1 != V2) AND AssignedTo Mismatch (Crew A != Crew B)
```

#### Deterministic 3-Case Resolution Policy
The reconciliation engine deterministically resolves `AssignedTo` mismatches across three operational scenarios:

| Operational Scenario | Crew B Current State | Physical Reality | Deterministic System Resolution | State Transition & Side Effects |
|---|---|---|---|---|
| **Case 1: Crew B In Transit (No Work Done)** | `Dispatched` or `EnRoute` | Crew A already completed physical repair. Crew B has performed zero physical work. | **Automated Crew B Recall & Field Work Acceptance** | 1. Accept Crew A's field evidence and mark Crew A as executing crew.<br>2. Transmit high-priority MQTT/Push recall to Crew B: *"Order completed by Crew A while offline. Return to available pool."*<br>3. Reset Crew B to `Available` with zero SLA penalty.<br>4. Move order to `Completed` (or `ConflictReview` for 1-click supervisor sign-off if tenant policy requires). |
| **Case 2: Dual On-Site Intervention** | `OnSite`, `InProgress`, or `Completed` | Life-safety conflict! Both crews were physically on site or claimed physical intervention. | **Immediate Safety Freeze & Quarantine** | 1. Order immediately frozen in `ConflictReview`.<br>2. Immediate audible safety alert pushed to both crews: *"Dual intervention detected on asset. Stand down and contact CCO immediately."*<br>3. Both crews' evidence batches sealed side-by-side in `OrderActionLog`.<br>4. Routed to Supervisor for mandatory physical adjudication (split, attribute, or investigate). |
| **Case 3: Crew A Partial / Impediment Submission** | `Dispatched`, `EnRoute`, or `OnSite` | Crew A attempted work but encountered an impediment or is still on site, while Crew B is en route. | **Transit Halt & Sector Custody Adjudication** | 1. Push immediate halt alert to Crew B.<br>2. Order transitions to `ConflictReview`.<br>3. Dispatcher/Supervisor determines which crew retains physical custody of the work site. |

#### Data Contracts for Concurrency Adjudication
In `core/GeoAssets.Workflow/Sync`:
```csharp
namespace GeoAssets.Workflow.Sync;

public enum ReassignmentConflictType
{
    None,
    CrewBRecalled_FieldWorkAccepted,
    DualIntervention_QuarantineRequired,
    PartialWorkCollision_CustodyDispute
}

public sealed record ReassignmentConflictRecord(
    Guid ConflictId,
    string OrderId,
    string OriginalCrewId,
    string ReassignedCrewId,
    ReassignmentConflictType ConflictType,
    string CrewAEvidenceBatchId,
    string? CrewBEvidenceBatchId,
    DateTimeOffset DetectedAtUtc,
    string? AdjudicatedBy = null,
    DateTimeOffset? AdjudicatedAtUtc = null,
    string? AdjudicationResolution = null);
```

#### Production Reconciliation Algorithm Implementation
The reconciliation algorithm executed inside `ServiceOrderSyncEngine.cs`:
```csharp
public async Task<SyncReconciliationResult> ReconcileSyncSubmissionAsync(
    ServiceOrderRecord currentServerOrder,
    MobileSyncSubmission incomingSubmission,
    CancellationToken ct)
{
    // 1. Detect if this is an AssignedTo Reassignment Collision
    bool isReassignmentCollision = 
        !string.Equals(currentServerOrder.AssignedTo, incomingSubmission.CrewId, StringComparison.Ordinal)
        && incomingSubmission.BaseRowVersion < currentServerOrder.RowVersion;

    if (!isReassignmentCollision)
    {
        // Standard scalar or status conflict handling
        return await HandleStandardConcurrencyConflictAsync(currentServerOrder, incomingSubmission, ct);
    }

    string crewA = incomingSubmission.CrewId;
    string crewB = currentServerOrder.AssignedTo;

    // 2. Evaluate Crew B's current operational state
    var crewBStatus = currentServerOrder.Status;

    if (crewBStatus is ServiceOrderStatus.Dispatched or "EnRoute")
    {
        // CASE 1: Crew B has not started physical work. Recall Crew B and accept Crew A's work.
        // Step A: Seal Crew A's field evidence
        var evidenceBatchId = await _evidenceStore.PersistEvidenceBatchAsync(incomingSubmission.Evidence, ct);
        
        currentServerOrder.ActionLog.Add(new OrderActionLog(
            Action: OrderActionType.Execute,
            PerformedBy: crewA,
            PerformedAt: incomingSubmission.SubmittedAtUtc,
            Comment: $"Offline execution accepted under 3-way reconciliation (Reassigned from {crewB} while offline). BatchId: {evidenceBatchId}",
            ResultingStatus: ServiceOrderStatus.Completed));

        // Step B: Set primary executor to Crew A for billing and audit
        currentServerOrder.AssignedTo = crewA;
        currentServerOrder.Status = ServiceOrderStatus.Completed;
        currentServerOrder.CompletedAt = incomingSubmission.CompletedAtUtc ?? DateTime.UtcNow;

        // Step C: Send real-time recall notification to Crew B
        await _notificationService.SendHighPriorityAlertAsync(
            targetCrewId: crewB,
            title: "Ordem Reatribuída - Retorno à Base",
            message: $"A OS #{currentServerOrder.Id} foi concluída pela equipe {crewA} em modo offline. Seu deslocamento foi cancelado sem penalidade de SLA.",
            ct);

        // Step D: Reset Crew B's operational shift status
        await _crewManager.SetCrewStatusAsync(crewB, CrewOperationalStatus.Available, ct);

        await _repository.UpdateAsync(currentServerOrder, ct);

        return new SyncReconciliationResult(
            Success: true,
            ResolvedStatus: ServiceOrderStatus.Completed,
            ConflictType: ReassignmentConflictType.CrewBRecalled_FieldWorkAccepted,
            Message: "Trabalho da equipe offline aceito com sucesso. Equipe substituta desmobilizada.");
    }
    else
    {
        // CASE 2: Dual Physical Intervention (Crew B is OnSite, InProgress, or Completed)
        // Step A: Immediately quarantine order to ConflictReview
        var crewABatchId = await _evidenceStore.PersistEvidenceBatchAsync(incomingSubmission.Evidence, ct);
        
        currentServerOrder.Status = "ConflictReview";
        currentServerOrder.ActionLog.Add(new OrderActionLog(
            Action: OrderActionType.Execute,
            PerformedBy: "SYSTEM_SYNC",
            PerformedAt: DateTime.UtcNow,
            Comment: $"CRITICAL DUAL INTERVENTION CONFLICT: Offline Crew {crewA} synced completion, but Crew {crewB} is already {crewBStatus}. Evidence quarantined under Batch {crewABatchId}.",
            ResultingStatus: "ConflictReview"));

        // Step B: Immediate life-safety halt alerts to BOTH crews
        await _notificationService.SendHighPriorityAlertAsync(
            targetCrewId: crewA,
            title: "ALERTA DE SEGURANÇA: Intervenção Dupla",
            message: $"Detectada duplicidade de atendimento na OS #{currentServerOrder.Id}. Afaste-se do ativo e contate o COD imediatamente.",
            ct);

        await _notificationService.SendHighPriorityAlertAsync(
            targetCrewId: crewB,
            title: "ALERTA DE SEGURANÇA: Intervenção Dupla",
            message: $"Detectada duplicidade de atendimento na OS #{currentServerOrder.Id}. Afaste-se do ativo e contate o COD imediatamente.",
            ct);

        // Step C: Register conflict for supervisor adjudication
        var conflictRecord = new ReassignmentConflictRecord(
            ConflictId: Guid.NewGuid(),
            OrderId: currentServerOrder.Id,
            OriginalCrewId: crewA,
            ReassignedCrewId: crewB,
            ConflictType: ReassignmentConflictType.DualIntervention_QuarantineRequired,
            CrewAEvidenceBatchId: crewABatchId,
            CrewBEvidenceBatchId: currentServerOrder.LatestEvidenceBatchId,
            DetectedAtUtc: DateTimeOffset.UtcNow);

        await _conflictRepository.RecordConflictAsync(conflictRecord, ct);
        await _repository.UpdateAsync(currentServerOrder, ct);

        return new SyncReconciliationResult(
            Success: false,
            ResolvedStatus: "ConflictReview",
            ConflictType: ReassignmentConflictType.DualIntervention_QuarantineRequired,
            Message: "Conflito crítico de intervenção dupla detectado. Ordem movida para ConflictReview e equipes alertadas.");
    }
}
```

---

# 2. Common Core vs Domain-Specific Data Attributes & Field Checklists

## 2.1 Common Core Schema & Extensible Attribute Engine

GeoAssets enforces an explicit separation between immutable platform core attributes and flexible domain extensions. The core schema is standardized across all utilities, while domain-specific variations are enforced via JSON Schema (Draft 2020-12) validation.

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                              GeoAssets Core Service Order                              │
├────────────────────────────────────────────────────────────────────────────────────────┤
│ - Id: string (GUID)                                                                    │
│ - Title: string                                                                        │
│ - Description: string                                                                  │
│ - OrderTypeId: string (FK to OrderType)                                                │
│ - Status: string (Default "Draft", governed by ServiceOrderTransitions)                │
│ - Priority: ServiceOrderPriority (Low=0, Normal=1, High=2, Critical=3)                 │
│ - CreatedBy: string (UserId / AgentId)                                                 │
│ - AssignedTo: string? (Technician UserId / CrewId)                                     │
│ - OrganizationId: Guid (Multi-tenant partition, implements IOrgOwnedResource)          │
│ - CreatedAt: DateTime (UTC)                                                            │
│ - UpdatedAt: DateTime? (UTC)                                                           │
│ - ScheduledAt: DateTime? (UTC)                                                         │
│ - CompletedAt: DateTime? (UTC)                                                         │
│ - ParentOrderId: string? (Hierarchical clustering / Rework link)                       │
│ - FeatureIds: string[] (Array of bound GeoFeature GUIDs)                               │
│ - SelectionSpec: FeatureSelectionSpec? (Dynamic spatial query specification)           │
│ - Attributes: IReadOnlyDictionary<string, string> (Extensible key-value store)         │
│ - Dispatches: IReadOnlyList<OrderDispatch> (Audit trail of assignment events)          │
│ - ActionLog: IReadOnlyList<OrderActionLog> (Append-only immutable event journal)       │
│ - RowVersion: byte[] (Optimistic concurrency token)                                    │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

### JSON Schema Dynamic Validation

The `Attributes` dictionary in `IServiceOrder` stores domain fields as strings (or serialized JSON for structured objects). Validation is executed by `ServiceOrderAttributeValidator.cs` using `JsonSchema.Net` against `OrderType.AttributesSchemaJson`. 

Every domain-specific extension defined below is realized as a concrete `OrderType` with an enforced schema, guaranteeing data integrity at the API boundary without requiring database schema migrations for new utility attributes.

---

## 2.2 Electrical Distribution Extensions (ANEEL BDGD, PRODIST, Safety & US Billing)

### Asset Binding & ANEEL BDGD Hierarchy
Brazilian electrical distribution concessionaires are legally mandated to maintain their geographical network inventory in strict accordance with ANEEL's Base de Dados Geográfica da Distribuidora (BDGD). Service Orders in this domain bind directly to `GeoFeature` instances representing BDGD feature layers:

```
  Subestação / Barra AT (BAY / SS)
                 │
                 ▼
     Alimentador MT (SEGCON_MT)
                 │
        ┌────────┴────────┐
        ▼                 ▼
Chave Fusível (UNREMT)   Poste / Suporte (PONNOT)
        │                 │
        ▼                 ▼
Transformador MT/BT (UNTRD)
        │
        ▼
   Rede Aérea BT (SEGCON_BT)
        │
        ▼
  Ramal de Ligação (RAMLIG)
        │
        ▼
Unidade Consumidora / Medidor (UC)
```

| BDGD Layer | Description | Key Attributes Tracked in Service Order |
|---|---|---|
| `PONNOT` | Postes e Pontos Notáveis | Material (Concreto, Madeira, Aço), Altura (m), Esforço Nominal (daN), Posição UTM/WGS84. |
| `UNTRD` | Unidade Transformadora de Distribuição | Código Operacional, Potência Nominal (kVA), Tensão Primária/Secundária (kV), Tombamento. |
| `UNREMT` / `UNREBT` | Chaves Seccionadoras, Fusíveis, Religadores | Tipo (Faca, Fusível, A Óleo, A Vácuo), Corrente Nominal (A), Estado Operacional (NA/NF). |
| `SEGCON` | Trechos de Condutores MT / BT | Bitola (AWG/MCM/$mm^2$), Material (Alumínio CA/CAA, Cobre), Disposição (Espaçador, Cruzeta). |
| `UC` | Unidade Consumidora / Ponto de Medição | Número da Instalação, Classe (Residencial, Comercial, Industrial), Tipo de Medidor. |

### Technical Field Checklists & Regulatory Safety

#### 1. Digital APR (Análise Preliminar de Risco) with NR-10 & NR-35 Compliance
Field execution cannot transition to `InProgress` without the lead technician submitting a digitally signed APR checklist via the mobile application:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "ElectricalDistributionAPRChecklist",
  "description": "Análise Preliminar de Risco (APR) mandatória para liberação de intervenções em redes elétricas aéreas de média e baixa tensão conforme NR-10 e NR-35.",
  "type": "object",
  "additionalProperties": false,
  "required": [
    "nr10_deenergization",
    "nr10_lockout_tagout",
    "nr10_voltage_absence_verified",
    "nr10_temporary_grounding_installed",
    "nr35_pole_structural_integrity",
    "nr35_harness_inspected",
    "weather_conditions_safe"
  ],
  "properties": {
    "nr10_deenergization": {
      "type": "boolean",
      "const": true,
      "description": "Confirmação mandatória de seccionamento e desenergização do circuito primário/secundário (NR-10 Item 10.5.1a)."
    },
    "nr10_lockout_tagout": {
      "type": "boolean",
      "const": true,
      "description": "Confirmação mandatória de impedimento de reenergização com bloqueio mecânico e etiquetagem LOTO (NR-10 Item 10.5.1b)."
    },
    "nr10_voltage_absence_verified": {
      "type": "boolean",
      "const": true,
      "description": "Constatação mandatória de ausência de tensão efetuada com detector acústico/luminoso aprovado e testado (NR-10 Item 10.5.1c)."
    },
    "nr10_temporary_grounding_installed": {
      "type": "boolean",
      "const": true,
      "description": "Instalação mandatória de conjunto de aterramento temporário com equipotencialização a montante e a jusante (NR-10 Item 10.5.1d)."
    },
    "nr35_pole_structural_integrity": {
      "type": "string",
      "const": "APPROVED",
      "description": "Avaliação estrutural da base e fuste do poste antes da escalada. Obrigatoriamente APPROVED para liberação da subida (NR-35 Item 35.4.1.3)."
    },
    "nr35_harness_inspected": {
      "type": "boolean",
      "const": true,
      "description": "Cinto paraquedista, talabarte duplo com absorvedor de energia e trava-quedas inspecionados sem desgastes (NR-35 Item 35.5.2)."
    },
    "weather_conditions_safe": {
      "type": "boolean",
      "const": true,
      "description": "Ausência mandatória de chuvas, descargas atmosféricas ou ventos fortes que impeçam o trabalho seguro em altura (NR-35 Item 35.4.2)."
    }
  }
}
```

#### Structural Defect Impediment Schema (`ElectricalPoleDefectImpedimentPayload`)
When a pole fails visual or mechanical inspection (e.g. decayed wood or cracked concrete), climbing is prohibited. The crew submits an impediment payload that halts work (`OnSite -> Suspended`) and triggers an emergency pole replacement child order:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "ElectricalPoleDefectImpedimentPayload",
  "description": "Payload submetido pela equipe de campo quando o poste é reprovado na inspeção visual/mecânica de base, acionando suspensão imediata e criação de ordem emergencial de substituição.",
  "type": "object",
  "additionalProperties": false,
  "required": [
    "structural_defect_type",
    "defect_severity",
    "immediate_hazard_flag",
    "defect_photo_asset_ids",
    "recommended_remediation"
  ],
  "properties": {
    "structural_defect_type": {
      "type": "string",
      "enum": [
        "REJECTED_ROTTEN_WOOD",
        "REJECTED_CRACKED_CONCRETE",
        "REJECTED_EXPOSED_REBAR",
        "REJECTED_COLLISION_DAMAGE",
        "REJECTED_SEVERE_LEAN"
      ],
      "description": "Tipo de defeito estrutural constatado no poste."
    },
    "defect_severity": {
      "type": "string",
      "enum": ["IMMINENT_COLLAPSE", "STRUCTURAL_COMPROMISE", "UNSAFE_TO_CLIMB"],
      "description": "Classificação da severidade do risco de colapso estrutural."
    },
    "immediate_hazard_flag": {
      "type": "boolean",
      "description": "Indica se há risco iminente de tombamento sobre transeuntes, veículos ou edificações vizinhas."
    },
    "defect_photo_asset_ids": {
      "type": "array",
      "items": { "type": "string", "format": "uuid" },
      "minItems": 1,
      "description": "Lista de identificadores de fotos georreferenciadas obrigatórias comprovando o defeito estrutural."
    },
    "recommended_remediation": {
      "type": "string",
      "enum": ["REPLACE_POLE_EMERGENCY", "SCHEDULE_POLE_REPLACEMENT", "CIVIL_DEFENSE_ISOLATION"],
      "description": "Ação recomendada pelo encarregado da equipe de campo."
    }
  }
}
```

#### Application-Level Safety Gate Architecture: `IAprSafetyRuleEvaluator`
To provide defense-in-depth beyond syntactic JSON Schema validation and generate authoritative Brazilian Portuguese regulatory diagnostics (citing exact NR-10, NR-35, and NR-33 clauses), `GeoAssets.Workflow` incorporates a compiled domain safety evaluator:

```csharp
namespace GeoAssets.Workflow.Rules;

using GeoAssets.Workflow.Orders;

/// <summary>
/// Domain-specific occupational safety gate evaluator. Enforces mandatory Brazilian regulatory
/// requirements (NR-10, NR-35, NR-33, ANEEL/ANATEL) before work activation is authorized.
/// </summary>
public interface IAprSafetyRuleEvaluator
{
    /// <summary>
    /// Evaluates whether the attributes submitted for a service order meet all mandatory life-safety
    /// criteria required to transition into <paramref name="targetStatus"/>.
    /// </summary>
    AprEvaluationResult Evaluate(
        IServiceOrder order, 
        string targetStatus, 
        IReadOnlyDictionary<string, string> attributes);
}

public sealed record AprEvaluationResult(
    bool IsPermitted,
    IReadOnlyList<SafetyViolation> Violations,
    SafetyImpedimentAction RecommendedAction
)
{
    public static AprEvaluationResult Approved() =>
        new(true, Array.Empty<SafetyViolation>(), SafetyImpedimentAction.None);

    public static AprEvaluationResult Denied(
        IReadOnlyList<SafetyViolation> violations, 
        SafetyImpedimentAction action = SafetyImpedimentAction.AbortToSuspended) =>
        new(false, violations, action);
}

public sealed record SafetyViolation(
    string RuleCode,
    string FieldName,
    string RegulatoryNorm,
    SafetySeverity Severity,
    string PortugueseMessage
);

public enum SafetySeverity
{
    Major,
    Critical,
    Fatal
}

public enum SafetyImpedimentAction
{
    None,
    AbortToSuspended,
    TriggerEmergencyChildOrder
}

public sealed class AprSafetyRuleEvaluator : IAprSafetyRuleEvaluator
{
    public AprEvaluationResult Evaluate(
        IServiceOrder order, 
        string targetStatus, 
        IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(attributes);

        if (!string.Equals(targetStatus, ServiceOrderStatus.InProgress, StringComparison.Ordinal))
        {
            return AprEvaluationResult.Approved();
        }

        var violations = new List<SafetyViolation>();
        var action = SafetyImpedimentAction.None;
        var orderTypeId = order.OrderTypeId.ToLowerInvariant();

        // 1. Electrical Distribution Orders (NR-10 & NR-35)
        if (orderTypeId.Contains("electrical") || orderTypeId.Contains("rede-eletrica") || orderTypeId.Contains("eletric"))
        {
            if (!attributes.TryGetValue("nr10_deenergization", out var deenergized) || !bool.TryParse(deenergized, out var isDeenergized) || !isDeenergized)
            {
                violations.Add(new SafetyViolation("NR10_DEENERGIZATION_MISSING", "nr10_deenergization", "NR-10 Item 10.5.1(a)", SafetySeverity.Fatal, "Seccionamento e desenergização do circuito não confirmados."));
            }
            if (!attributes.TryGetValue("nr10_lockout_tagout", out var loto) || !bool.TryParse(loto, out var isLoto) || !isLoto)
            {
                violations.Add(new SafetyViolation("NR10_LOTO_MISSING", "nr10_lockout_tagout", "NR-10 Item 10.5.1(b)", SafetySeverity.Critical, "Bloqueio e etiquetagem LOTO não confirmados."));
            }
            if (!attributes.TryGetValue("nr10_voltage_absence_verified", out var voltAbs) || !bool.TryParse(voltAbs, out var isVoltAbs) || !isVoltAbs)
            {
                violations.Add(new SafetyViolation("NR10_VOLTAGE_ABSENCE_MISSING", "nr10_voltage_absence_verified", "NR-10 Item 10.5.1(c)", SafetySeverity.Fatal, "Ausência de tensão não constatada com detector testado."));
            }
            var hasGrounding = (attributes.TryGetValue("nr10_temporary_grounding_installed", out var ground) && bool.TryParse(ground, out var g) && g)
                || (attributes.TryGetValue("nr10_temp_grounding_installed", out var altGround) && bool.TryParse(altGround, out var ag) && ag);
            if (!hasGrounding)
            {
                violations.Add(new SafetyViolation("NR10_TEMP_GROUNDING_MISSING", "nr10_temporary_grounding_installed", "NR-10 Item 10.5.1(d)", SafetySeverity.Fatal, "Conjunto de aterramento temporário com equipotencialização ausente."));
            }
            if (!attributes.TryGetValue("nr35_pole_structural_integrity", out var poleStatus))
            {
                violations.Add(new SafetyViolation("NR35_POLE_INTEGRITY_MISSING", "nr35_pole_structural_integrity", "NR-35 Item 35.4.1.3", SafetySeverity.Fatal, "Avaliação estrutural da base e fuste do poste não preenchida."));
            }
            else if (!string.Equals(poleStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                action = SafetyImpedimentAction.TriggerEmergencyChildOrder;
                violations.Add(new SafetyViolation("NR35_POLE_UNSOUND", "nr35_pole_structural_integrity", "NR-35 Item 35.4.1.3", SafetySeverity.Fatal, $"Poste reprovado na inspeção estrutural ({poleStatus}). Escalada estritamente proibida."));
            }
            if (!attributes.TryGetValue("nr35_harness_inspected", out var harness) || !bool.TryParse(harness, out var isHarness) || !isHarness)
            {
                violations.Add(new SafetyViolation("NR35_HARNESS_MISSING", "nr35_harness_inspected", "NR-35 Item 35.5.2", SafetySeverity.Critical, "EPIs de proteção contra quedas não inspecionados ou reprovados."));
            }
            if (!attributes.TryGetValue("weather_conditions_safe", out var weather) || !bool.TryParse(weather, out var isWeather) || !isWeather)
            {
                violations.Add(new SafetyViolation("NR35_WEATHER_UNSAFE", "weather_conditions_safe", "NR-35 Item 35.4.2", SafetySeverity.Major, "Condições meteorológicas adversas impedem o trabalho seguro em altura."));
            }
        }

        return violations.Count == 0 ? AprEvaluationResult.Approved() : AprEvaluationResult.Denied(violations, action);
    }
}

public sealed class AprSafetyServiceOrderRule(IAprSafetyRuleEvaluator safetyEvaluator) : IServiceOrderRule
{
    public string Name => "AprSafetyRuleEvaluator";

    public bool? Evaluate(OrderActionType action, RuleEvaluationContext context)
    {
        if (action == OrderActionType.Execute)
        {
            var result = safetyEvaluator.Evaluate(context.Order, ServiceOrderStatus.InProgress, context.Order.Attributes);
            if (!result.IsPermitted) return false; // Explicit Deny overrides permission allows
        }
        return null;
    }
}
```

#### 2. Transformer Replacement & Tag Reconciliation Checklist (MCPSE Compliance)
When a distribution transformer (`UNTRD`) is replaced or overhauled, the technician must reconcile physical assets against accounting records (Manual de Controle Patrimonial do Setor Elétrico - MCPSE):

```json
{
  "transformer_tag_reconciliation": {
    "old_transformer": {
      "serial_number": "TR-102948-BA",
      "asset_tag_plaqueta": "PLQ-849201",
      "removal_condition": "BURNT_OVERLOAD",
      "oil_status": "SEALED_CONTAINER_TRANSPORT"
    },
    "new_transformer": {
      "serial_number": "WEG-2026-99481",
      "asset_tag_plaqueta": "PLQ-950112",
      "rated_kva": 75.0,
      "primary_voltage_kv": 13.8,
      "secondary_voltage_v": "380/220",
      "winding_connection": "DELTA_ESTRELA",
      "insulating_oil_type": "MINERAL_INHIBITED",
      "tap_position": "3_13800V"
    }
  }
}
```

#### 3. Conductor Sagging, Tensioning & Vegetation Clearing
* **Sagging & Tensioning (*Flecha e Tração*)**: For cable replacement orders on aerial spans (`SEGCON`), recording the ambient temperature ($^\circ\text{C}$), dynamometer line tension ($\text{daN}$), and measured ground clearance at mid-span ($\text{m}$) against minimum standards (ABNT NBR 15688 / NBR 5422: $\ge 6.0\text{ m}$ for highways, $\ge 5.5\text{ m}$ for urban streets).
* **Vegetation Management (*Poda e Faixa de Servidão*)**: Mandatory record of tree clearance distance achieved (minimum $1.5\text{ m}$ for low voltage, $3.0\text{ m}$ for medium voltage), volume of pruned branches ($m^3$), and authorized eco-disposal site receipt ID.

### Commercial Remuneration: Unidade de Serviço (US) Measurement Engine
In Brazilian distribution contracting (e.g., CEMIG Processo 530-LS20453, Neoenergia Coelba DIS-NOR-068), contractor billing is strictly itemized by **Unidades de Serviço (US)**. GeoAssets incorporates an itemized Bill of Quantities (BoQ) calculation inside the `ServiceOrder`:

$$\text{Total Order US} = \sum_{i=1}^{n} \left( \text{ItemQuantity}_i \times \text{FactorUS}_i \times \text{RegionalMultiplier} \right)$$

```json
{
  "us_measurement_sheet": {
    "contractor_id": "EMPREITEIRA_NORDESTE_ELETRICA_LTDA",
    "contract_number": "CT-2025-0842",
    "items": [
      {
        "item_code": "US-EL-014",
        "description": "Substituição de poste de concreto circular 11m/600daN com caminhão guindauto",
        "unit": "UN",
        "quantity": 1.0,
        "base_us_factor": 185.50,
        "total_us": 185.50
      },
      {
        "item_code": "US-EL-089",
        "description": "Lançamento e tensionamento de condutor de alumínio multiplexado BT 3x1x70+50mm²",
        "unit": "M",
        "quantity": 240.0,
        "base_us_factor": 0.45,
        "total_us": 108.00
      },
      {
        "item_code": "US-EL-102",
        "description": "Instalação de transformador de distribuição aéreo até 112.5 kVA",
        "unit": "UN",
        "quantity": 1.0,
        "base_us_factor": 142.00,
        "total_us": 142.00
      }
    ],
    "total_us_accumulated": 435.50,
    "financial_conversion_rate_brl": 3.85,
    "total_remuneration_brl": 1676.68
  }
}
```

---

## 2.3 Water & Sewage Sanitation Extensions (ANA, Sabesp ICERP, Cagece, SGM & NR-33)

### Asset Binding & Sanitation Network Hierarchy
Under the Marco Legal do Saneamento (Lei 14.026/2020) and ANA Reference Norms, water and sewage Service Orders bind to pressurized hydraulic networks and gravity collection systems:

```
[Água Potável]
Captação / ETA ──► Adutora Água Bruta/Tratada ──► Reservatório ──► VRP / Macromedidor
                                                                        │
                                                                        ▼
                                                             Rede Distribuição (RDA)
                                                                        │
                                                                        ▼
                                                             Ramal Predial & Cavalete
                                                                        │
                                                                        ▼
                                                                Hidrômetro Residencial

[Esgotamento Sanitário]
Economia / Ramal Predial ──► Rede Coletora (RCE) ──► Poço de Visita (PV) ──► Coletor Tronco / Interceptor
                                                                                    │
                                                                                    ▼
                                                                           Estação Elevatória (EEE) ──► ETE
```

| Asset Layer | Engineering Class | Critical Physical Attributes |
|---|---|---|
| `RDA` / `ADUTORA` | Tubulação de Água Sob Pressão | Diâmetro Nominal (DN 20 a 1200 mm), Material (PEAD, PVC Defofo, FoFo Dúctil, Aço), Classe de Pressão (PN 6, 10, 16), Profundidade. |
| `VRP` | Válvula Redutora de Pressão | Pressão de Entrada ($P_{in}$ bar), Pressão de Saída ($P_{out}$ bar), Diâmetro, Estado Piloto. |
| `HIDROMETRO` | Micromedição Comercial | Número de Série, Marca, Vazão Nominal ($Q_n$ $m^3/h$), Diâmetro (1/2", 3/4"), Ano Inmetro, Lacre. |
| `RCE` / `COLTRONCO` | Tubulação de Esgoto por Gravidade | Diâmetro (DN 100 a 1500 mm), Material (Cerâmica, Concreto, PVC Ocre, PEAD Corrugado), Declividade ($m/m$). |
| `PV` | Poço de Visita de Esgoto | Cota de Tampa ($m$), Cota de Fundo ($m$), Profundidade, Material do Tampão, Presença de Sedimentos. |

### Technical Field Checklists & Safety Standards

#### 1. NR-33 Confined Space Entry & Atmospheric Testing Checklist
Field intervention inside manholes (`PV`), deep trenches ($> 1.5\text{ m}$), or pump station wet wells (`EEE`) requires atmospheric gas validation before descent:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "WaterSanitationNR33ConfinedSpaceChecklist",
  "description": "Validação de atmosfera e equipamentos para entrada em poços de visita (PV), valas profundas (>1.5m) e poços úmidos de elevatórias (EEE) conforme NR-33.",
  "type": "object",
  "additionalProperties": false,
  "required": [
    "gas_detector_calibrated",
    "oxygen_percentage",
    "h2s_ppm",
    "carbon_monoxide_ppm",
    "lel_combustible_percentage",
    "mechanical_ventilation_active",
    "tripod_winch_installed",
    "surface_watchman_present"
  ],
  "properties": {
    "gas_detector_calibrated": {
      "type": "boolean",
      "const": true,
      "description": "Detector de 4 gases calibrado com teste de resposta (bump test) diário válido (NR-33 Item 33.3.2.1)."
    },
    "oxygen_percentage": {
      "type": "number",
      "minimum": 19.5,
      "maximum": 23.0,
      "description": "Concentração de Oxigênio (O2) segura entre 19.5% e 23.0% (NR-33 Tabela 1)."
    },
    "h2s_ppm": {
      "type": "number",
      "minimum": 0.0,
      "maximum": 8.0,
      "description": "Concentração de Gás Sulfídrico (H2S) não-negativa e estritamente abaixo de 8.0 ppm (NR-15/NR-33)."
    },
    "carbon_monoxide_ppm": {
      "type": "number",
      "minimum": 0.0,
      "maximum": 25.0,
      "description": "Concentração de Monóxido de Carbono (CO) não-negativa e estritamente abaixo de 25 ppm (NR-15/NR-33)."
    },
    "lel_combustible_percentage": {
      "type": "number",
      "minimum": 0.0,
      "maximum": 10.0,
      "description": "Limite Inferior de Explosividade (LEL) de gases inflamáveis entre 0.0% e 10.0% (NR-33)."
    },
    "mechanical_ventilation_active": {
      "type": "boolean",
      "const": true,
      "description": "Insuflador de ar mecânico forçado em operação contínua durante 100% da descida e permanência (NR-33 Item 33.3.2.4)."
    },
    "tripod_winch_installed": {
      "type": "boolean",
      "const": true,
      "description": "Tripé de resgate com guincho mecânico e linha de vida acoplada ao cinto do operador (NR-33 Item 33.3.2.5)."
    },
    "surface_watchman_present": {
      "type": "boolean",
      "const": true,
      "description": "Vigia externo treinado posicionado na superfície durante 100% da operação (NR-33 Item 33.3.4.2)."
    }
  }
}
```

#### 2. Water Leak Repair & Loss Calculation Checklist
```json
{
  "water_leak_repair": {
    "detection_method": "ACOUSTIC_CORRELATOR_GEOPHONE",
    "leak_location": "PAVED_ROAD_CARRIAGEWAY",
    "leak_classification": "PIPE_BODY_LONGITUDINAL_CRACK",
    "affected_pipe_dn_mm": 150,
    "pipe_material": "PVC_DEFOFO",
    "operating_pressure_bar": 3.8,
    "repair_method": "STAINLESS_STEEL_BIPARTITE_REPAIR_CLAMP",
    "clamp_serial_number": "CLAMP-SS316-DN150-300MM",
    "estimated_loss_flow_m3_h": 4.2,
    "total_water_lost_estimated_m3": 21.0
  }
}
```

#### 3. Pavement Recomposition & ICERP Mathematical Area Calculation
In contracts governed by Sabesp / ARSESP Deliberação nº 1.739/2025 and municipal concession standards, street trenching mandates pavement restoration audited by the **ICERP (Indicador de Conformidade na Execução da Reposição de Pavimento)**:

$$\text{ICERP (\%)} = \left(\frac{\text{Aprovadas}}{\text{Amostradas}}\right) \times 100 \ge 95\%$$

The technician records trench dimensions. GeoAssets calculates surface area ($m^2$) and automatically spawns a linked paving sub-order (`ParentOrderId` link):

$$\text{Trench Surface Area } (m^2) = \text{Length } (m) \times \text{Width } (m)$$
$$\text{Excavated Volume } (m^3) = \text{Length } (m) \times \text{Width } (m) \times \text{Depth } (m)$$

```json
{
  "pavement_recomposition": {
    "surface_type": "ASPHALT_CBUQ",
    "trench_dimensions": {
      "length_meters": 3.20,
      "width_meters": 1.10,
      "depth_meters": 1.45,
      "calculated_area_sqm": 3.52,
      "calculated_volume_cbm": 5.10
    },
    "compaction_layers": {
      "subgrade_compacted": true,
      "crushed_stone_base_cm": 25.0,
      "binder_tack_coat_applied": true,
      "cbuq_hot_mix_thickness_cm": 5.0
    },
    "grade_alignment_tolerance_mm": 3.0,
    "icerp_audit_status": "APPROVED",
    "municipal_convia_clearance_id": "ALV-SP-2026-9042"
  }
}
```

#### 4. Sewage Unblocking & Hydro-Jetting Checklist
* **Blockage Cause**: `SOLIDIFIED_COOKING_GREASE`, `SAND_SEDIMENTATION`, `TREE_ROOT_INTRUSION`, `STRUCTURAL_PIPE_COLLAPSE`, `FOREIGN_OBJECT_VANDALISM`.
* **Execution Parameters**: Combined jet/vacuum truck ID, jet nozzle type (e.g., torpedo, rotating vibrator), water injection pressure ($\text{bar}$), vacuum volume extracted ($m^3$), CCTV robot inspection executed (`true`/`false`), and street surface lime desinfection completed (`true`/`false`).

---

## 2.4 Telecom Wire & Optical Fiber Extensions (ANATEL, Pole Sharing, FTTH & OTDR)

### Asset Binding & Optical Network Hierarchy
Telecom distribution encompasses passive optical network (PON) infrastructure, customer drop cables, and shared joint-use poles:

```
Central Office / POP (OLT)
          │
          ▼
Cabo Óptico Troncal / Feeder (12 a 288 FO)
          │
          ▼
Caixa de Emenda Óptica (CEO / FOSC)
          │
          ▼
Cabo de Distribuição Óptica (12 a 72 FO)
          │
          ▼
Caixa de Terminação Óptica (CTO / Splitter 1:8 / 1:16) ──[Poste Compartilhado / Faixa 50cm]
          │
          ▼
Cabo Drop Compacto Monomodo (G.657.A2)
          │
          ▼
Ponto de Terminação Óptica (PTO / Roseta)
          │
          ▼
Terminal de Usuário / ONT (GPON / XGS-PON)
```

| Asset Layer | Component Type | Tracked Attributes in Service Order |
|---|---|---|
| `POSTE_COMPARTILHADO` | Ponto de Suporte em Rede Elétrica | ID do Poste Distribuidora, Posição na Faixa de 500 mm, Número de Ocupantes, Altura de Vão Livre. |
| `CABO_OPTICO` | Tronco / Distribuição Aérea | Contagem de Fibras, Fabricante, Índice de Refração ($IOR$), Fator de Helicoidização ($F_{helix}$). |
| `CTO` | Caixa de Terminação Óptica | Capacidade de Portas (8 ou 16), Razão de Divisão do Splitter (1:8, 1:16), Potência Óptica na Entrada ($dBm$). |
| `CEO` | Caixa de Emenda Óptica | Tipo de Bandeja, Capacidade de Fusões, Código de Cores das Fibras (ABNT NBR 14106). |
| `DROP_CLIENTE` | Cabo Drop de Acesso Residencial | Comprimento Lançado ($m$), Tipo de Ancoragem (BAP, Esticador Cunha), Identificação de Plaqueta. |
| `ONT` | Optical Network Terminal | Fabricante, Modelo, Serial Number (FSAN), Endereço MAC, Potência RX/TX ($dBm$). |

### Technical Field Checklists & Quality Standards

#### 1. Optical Power Meter (OPM) & Fusion Attenuation Checklist
Field rules enforce strict insertion loss and link budget boundaries (Teleco / RNP Redecomep standards):

$$\text{Allowable Fusion Splice Loss} \le 0.05\text{ dB}$$
$$\text{CTO Port Optical Power Level} \in [-15.0\text{ dBm}, -22.0\text{ dBm}]$$
$$\text{Customer PTO Power Level} \in [-15.0\text{ dBm}, -25.0\text{ dBm}]$$
$$\text{Drop Insertion Loss } \Delta P = P_{CTO} - P_{PTO} \le 0.5\text{ dB}$$

```json
{
  "optical_power_checklist": {
    "opm_wavelength_nm": 1490,
    "cto_splitter_port_number": 4,
    "cto_port_measured_power_dbm": -18.25,
    "drop_cable_length_meters": 68.0,
    "customer_pto_measured_power_dbm": -18.60,
    "calculated_drop_loss_db": 0.35,
    "optical_link_budget_valid": true,
    "fusion_splices": [
      { "tray_id": 1, "fiber_number": 1, "color": "VERDE", "splice_loss_db": 0.02 },
      { "tray_id": 1, "fiber_number": 2, "color": "AMARELO", "splice_loss_db": 0.03 }
    ]
  }
}
```

#### 2. Joint-Use Pole Sharing & Regulatory Tagging Checklist
Enforces compliance with **Resolução Conjunta ANEEL/ANATEL nº 004/2014**, **Decreto Presidencial nº 12.068/2024 (Art. 16 - Entidade Exploradora Neutra)**, and utility specifications (Neoenergia Coelba DIS-NOR-056 / Enel Ceará JoiN-AS):

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "PoleSharingComplianceChecklist",
  "description": "Checklist de conformidade de ocupação compartilhada de postes com rede elétrica conforme Resolução Conjunta ANEEL/ANATEL nº 004/2014 e Decreto nº 12.068/2024.",
  "type": "object",
  "additionalProperties": false,
  "required": [
    "pole_distributor_id",
    "within_500mm_band",
    "clearance_to_secondary_power_m",
    "midspan_ground_clearance_m",
    "standard_yellow_tag_present",
    "unauthorized_cables_detected",
    "occupant_count_total"
  ],
  "properties": {
    "pole_distributor_id": {
      "type": "string",
      "minLength": 3,
      "description": "Identificador gravado no poste pela concessionária elétrica (ex: P-19402)."
    },
    "within_500mm_band": {
      "type": "boolean",
      "description": "Cabos de telecomunicações fixados estritamente dentro da faixa regulamentar de ocupação de 500 mm."
    },
    "clearance_to_secondary_power_m": {
      "type": "number",
      "minimum": 0.60,
      "description": "Distância vertical à rede secundária de baixa tensão da distribuidora (mínimo normativo de 0.60 m)."
    },
    "midspan_ground_clearance_m": {
      "type": "number",
      "minimum": 4.50,
      "description": "Vão livre mínimo do cabo ao solo no centro da via/calçada (>= 5.0m em vias, >= 4.5m em passeios)."
    },
    "standard_yellow_tag_present": {
      "type": "boolean",
      "description": "Presença da plaqueta plástica amarela com Razão Social, CNPJ, Contato e Licença SCM."
    },
    "unauthorized_cables_detected": {
      "type": "boolean",
      "description": "Existência de cabos clandestinos sem plaqueta, fiação frouxa ou cabos mortos no ponto de fixação."
    },
    "occupant_count_total": {
      "type": "integer",
      "minimum": 0,
      "maximum": 6,
      "description": "Número total de ocupantes de telecomunicações fixados no poste (máximo normativo típico de 5 a 6 operadoras)."
    }
  }
}
```

#### 3. ONT Provisioning & Physical Verification
* Barcode / QR scan of ONT serial number (`FSAN`: e.g., `ALCLB4920A12`) and `MAC` address.
* Operational confirmation: PON LED solid green, LOS LED off, customer Wi-Fi benchmark test completed.

---

## 2.5 OTDR Trace Correlation Algorithm: Exact Mathematical Specification

When an optical cable cut or severe degradation occurs (`OrderTypeId: emergency-fiber-repair`), emergency crews connect an Optical Time-Domain Reflectometer (OTDR) at the Central Office (OLT/DGO) or an accessible splice enclosure (`CEO`). 

The OTDR detects a reflection or attenuation event at a one-dimensional optical propagation distance $D_{opt}$ (meters). GeoAssets implements a deterministic algorithm to map this scalar distance onto a multi-dimensional PostGIS `LineString` representing the georeferenced cable route, factoring in optical parameters, mechanical excess factors, and physical technical reserve coils (*cruzetas / raquetes de sobra*).

```
[Central Office / OLT]
          │
          │ Optical Signal Propagation (Group Refractive Index: n_eff)
          ▼
   Cable Route on Aerial Poles
   (Contains mechanical slack coils: S_station, S_reserve)
          │
          ▼
   [Fiber Cut / Event] ──► Calculated 1D Distance ──► 2D Interpolation on PostGIS LineString
                                                               │
                                                               ▼
                                                  Exact Lat/Lon Coordinates
                                                  Pinpointed Pole Span (P-450 to P-451)
```

### Mathematical Derivation & Spatial Projection

1. **Optical Distance Calculation**:
   The OTDR calculates the round-trip propagation time $t_{flight}$ of a laser pulse:
   $$D_{opt} = \frac{c \cdot t_{flight}}{2 \cdot n_{eff}}$$
   where:
   * $c = 299,792,458\text{ m/s}$ (speed of light in vacuum).
   * $n_{eff}$ = Effective group index of refraction of the fiber core (configured per wavelength, e.g., $IOR = 1.4677$ at $1310\text{ nm}$, $1.4682$ at $1550\text{ nm}$ for ITU-T G.652.D).

2. **Cable Sheath Length Conversion ($L_{sheath}$)**:
   Optical fibers are stranded inside loose buffer tubes with a helical excess length to protect the glass from tensile strain:
   $$L_{sheath} = \frac{D_{opt}}{F_{helix}}$$
   where $F_{helix}$ is the cable stranding/helix factor ($1.010 \le F_{helix} \le 1.025$, per manufacturer specification, e.g., Furukawa or Prysmian).

3. **Station Slack Boundary Condition ($L_{sheath} \le S_{station}$)**:
   If the mechanical sheath length does not exceed the Central Office / Substation manhole slack:
   $$L_{sheath} \le S_{station} \implies \begin{cases}
   L_{geo} = 0.0\text{ m} \\
   P_{fault} = G_{cable}(0) \\
   \text{LocationType} = \text{StationSlack} \\
   \Delta_{upstream} = 0.0\text{ m} \\
   \text{SlackPenetration} = L_{sheath}
   \end{cases}$$

4. **Sequential Slack Deduction & Topological Classification**:
   For routes exiting the Central Office, let initial remaining length be $L_{rem} = L_{sheath} - S_{station}$ and initial accumulated geographical distance be $L_{geo} = 0.0$.
   For each intermediate slack loop $s_i \in \mathcal{S}$ sorted by route position $s_i.p$ with loop length $s_i.l$:
   - Span to coil entrance: $\Delta_{span} = s_i.p - L_{geo}$.
   - **Case A: Break in Aerial Span before Coil** ($L_{rem} < \Delta_{span}$):
     $$L_{geo} \leftarrow L_{geo} + L_{rem}, \quad \text{LocationType} = \text{AerialSpan}, \quad L_{rem} \leftarrow 0 \quad (\text{Halt})$$
   - **Case B: Traverse Span to Coil** ($L_{rem} \ge \Delta_{span}$):
     $$L_{geo} \leftarrow s_i.p, \quad L_{rem} \leftarrow L_{rem} - \Delta_{span}$$
     - If $L_{rem} \le s_i.l$:
       $$P_{fault} = G_{cable}(s_i.p), \quad \Delta_{upstream} = 0.0\text{ m}, \quad \text{LocationType} = s_i.\tau, \quad \text{SlackPenetration} = L_{rem} \quad (\text{Halt})$$
       where $s_i.\tau \in \{\text{TechnicalReserveLoop}, \text{SpliceClosure}\}$.
     - Else ($L_{rem} > s_i.l$):
       $$L_{rem} \leftarrow L_{rem} - s_i.l \quad (\text{Continue})$$
   - **Case C: Break in Aerial Span after Final Coil** ($L_{rem} > 0$):
     $$L_{geo} \leftarrow L_{geo} + L_{rem}, \quad \text{LocationType} = \text{AerialSpan}$$

5. **Metric CRS Planar Projection (SIRGAS 2000 UTM)**:
   Because GeoAssets stores geographic geometries in decimal degrees (EPSG:4326), linear referencing directly with metric distances requires forward planar projection into the appropriate SIRGAS 2000 UTM Zone (EPSG 31978–31985):
   $$\text{Zone} = \left\lfloor \frac{\lambda_{origin} + 180^\circ}{6^\circ} \right\rfloor + 1, \quad \text{SRID} = 31960 + \text{Zone}$$
   $$G_{metric} = \mathcal{T}_{4326 \to \text{UTM}}(G_{cable})$$
   $$P_{metric} = \text{LengthIndexedLine}(G_{metric}).\text{ExtractPoint}(L_{geo})$$
   $$P_{fault} = \mathcal{T}_{\text{UTM} \to 4326}(P_{metric})$$
   When planar projection is not configured, the engine utilizes geodetic metric linear referencing along the vertex sequence:
   $$d_k = \text{HaversineMeters}(V_k, V_{k+1}), \quad L_{total} = \sum_{k=0}^{N-2} d_k, \quad \alpha = \frac{L_{geo} - D_m}{d_m}$$
   $$P_{fault} = V_m + \alpha (V_{m+1} - V_m)$$

### C# Implementation Service (`OtdrFaultCorrelationService`)

```csharp
namespace GeoAssets.Core.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;

/// <summary>
/// Categorizes the physical infrastructure asset or plant section where an optical break is located.
/// </summary>
public enum FaultLocationType
{
    /// <summary>Fault located in the suspended open aerial span between two utility poles.</summary>
    AerialSpan,

    /// <summary>Fault located inside a coiled technical reserve loop (*cruzeta / raquete de sobra*) on a pole.</summary>
    TechnicalReserveLoop,

    /// <summary>Fault located within the Central Office / Substation entrance vault or ODF slack coil.</summary>
    StationSlack,

    /// <summary>Fault located inside an optical splice enclosure (*Caixa de Emenda Óptica - CEO* or *CTO*).</summary>
    SpliceClosure
}

/// <summary>
/// Models a physical cable reserve coil or splice enclosure along a fiber route.
/// </summary>
/// <param name="PositionAlongRouteMeters">Geographical metric distance along route where the coil is installed.</param>
/// <param name="SlackLengthMeters">Length of extra cable coiled inside the loop (meters).</param>
/// <param name="Description">Descriptive label (e.g. "Cruzeta de sobra no poste P-14").</param>
/// <param name="SlackId">Unique identifier of the coil asset (e.g. "CR-104", "CEO-012").</param>
/// <param name="SlackType">Classification of the coil (Reserve loop vs Splice closure).</param>
public sealed record SlackEntity(
    double PositionAlongRouteMeters,
    double SlackLengthMeters,
    string Description,
    string? SlackId = null,
    FaultLocationType SlackType = FaultLocationType.TechnicalReserveLoop);

/// <summary>
/// Spatial and topological diagnostic result of correlating an OTDR trace to a fiber route.
/// </summary>
public sealed record OtdrCorrelationResult(
    Coordinate FaultCoordinate,
    double EffectiveGeographicalDistanceMeters,
    string UpstreamPoleId,
    string DownstreamPoleId,
    double DistanceFromUpstreamPoleMeters,
    FaultLocationType LocationType,
    string? DiagnosticDescription = null,
    string? SlackEntityId = null,
    double? PenetrationDistanceIntoSlackMeters = null);

/// <summary>
/// Service abstraction for projecting between geographic (EPSG:4326) and planar metric Coordinate Reference Systems.
/// </summary>
public interface ICrsProjectionService
{
    /// <summary>
    /// Determines the appropriate SIRGAS 2000 UTM Zone EPSG code (31978..31985) for the given longitude in Brazil.
    /// </summary>
    int DetermineSirgas2000UtmSrid(double longitude);

    /// <summary>
    /// Projects a geographic LineString into planar metric coordinates.
    /// </summary>
    (LineString ProjectedLine, int Srid) ProjectToPlanarMetric(LineString geographicLine, int? targetSrid = null);

    /// <summary>
    /// Projects a planar metric coordinate back to geographic EPSG:4326 (lon, lat).
    /// </summary>
    Coordinate ProjectToGeographic(Coordinate planarCoordinate, int sourceSrid);
}

/// <summary>
/// Standalone reference implementation of SIRGAS 2000 UTM projection for Brazilian utility infrastructure.
/// Supports UTM Zones 18S through 25S (EPSG 31978 to 31985) using high-precision Transverse Mercator equations.
/// </summary>
public sealed class Sirgas2000UtmProjectionService : ICrsProjectionService
{
    private const double A = 6378137.0; // GRS80 / WGS 84 semi-major axis
    private const double F = 1.0 / 298.257222101; // GRS80 flattening
    private const double K0 = 0.9996; // UTM central scale factor
    private static readonly double B = A * (1.0 - F);
    private static readonly double E2 = (A * A - B * B) / (A * A);
    private static readonly double EPrime2 = (A * A - B * B) / (B * B);

    public int DetermineSirgas2000UtmSrid(double longitude)
    {
        int zone = (int)Math.Floor((longitude + 180.0) / 6.0) + 1;
        zone = Math.Clamp(zone, 18, 25);
        return 31960 + zone; // EPSG 31978 (Zone 18S) to 31985 (Zone 25S)
    }

    public (LineString ProjectedLine, int Srid) ProjectToPlanarMetric(LineString geographicLine, int? targetSrid = null)
    {
        ArgumentNullException.ThrowIfNull(geographicLine);
        int srid = targetSrid ?? DetermineSirgas2000UtmSrid(geographicLine.Coordinates[0].X);
        int zone = srid - 31960;

        var transformedCoords = new Coordinate[geographicLine.Coordinates.Length];
        for (int i = 0; i < geographicLine.Coordinates.Length; i++)
        {
            var c = geographicLine.Coordinates[i];
            var (x, y) = LatLonToUtm(c.X, c.Y, zone);
            transformedCoords[i] = new Coordinate(x, y);
        }

        var factory = new GeometryFactory(new PrecisionModel(), srid);
        return (factory.CreateLineString(transformedCoords), srid);
    }

    public Coordinate ProjectToGeographic(Coordinate planarCoordinate, int sourceSrid)
    {
        ArgumentNullException.ThrowIfNull(planarCoordinate);
        int zone = sourceSrid - 31960;
        var (lon, lat) = UtmToLatLon(planarCoordinate.X, planarCoordinate.Y, zone);
        return new Coordinate(lon, lat);
    }

    private static (double Easting, double Northing) LatLonToUtm(double lon, double lat, int zone)
    {
        double lon0 = (zone - 1) * 6 - 180 + 3;
        double lon0Rad = lon0 * (Math.PI / 180.0);
        double latRad = lat * (Math.PI / 180.0);
        double lonRad = lon * (Math.PI / 180.0);

        double sinLat = Math.Sin(latRad);
        double cosLat = Math.Cos(latRad);
        double tanLat = Math.Tan(latRad);

        double n = A / Math.Sqrt(1.0 - E2 * sinLat * sinLat);
        double t = tanLat * tanLat;
        double c = EPrime2 * cosLat * cosLat;
        double aCoeff = cosLat * (lonRad - lon0Rad);

        double m = A * (
            (1.0 - E2 / 4.0 - 3.0 * E2 * E2 / 64.0 - 5.0 * E2 * E2 * E2 / 256.0) * latRad
            - (3.0 * E2 / 8.0 + 3.0 * E2 * E2 / 32.0 + 45.0 * E2 * E2 * E2 / 1024.0) * Math.Sin(2.0 * latRad)
            + (15.0 * E2 * E2 / 256.0 + 45.0 * E2 * E2 * E2 / 1024.0) * Math.Sin(4.0 * latRad)
            - (35.0 * E2 * E2 * E2 / 3072.0) * Math.Sin(6.0 * latRad)
        );

        double x = K0 * n * (
            aCoeff
            + (1.0 - t + c) * Math.Pow(aCoeff, 3) / 6.0
            + (5.0 - 18.0 * t + t * t + 72.0 * c - 58.0 * EPrime2) * Math.Pow(aCoeff, 5) / 120.0
        ) + 500000.0;

        double y = K0 * (
            m
            + n * tanLat * (
                Math.Pow(aCoeff, 2) / 2.0
                + (5.0 - t + 9.0 * c + 4.0 * c * c) * Math.Pow(aCoeff, 4) / 24.0
                + (61.0 - 58.0 * t + t * t + 600.0 * c - 330.0 * EPrime2) * Math.Pow(aCoeff, 6) / 720.0
            )
        );

        if (lat < 0) y += 10000000.0; // Southern hemisphere false northing
        return (x, y);
    }

    private static (double Lon, double Lat) UtmToLatLon(double x, double y, int zone)
    {
        double xPrime = x - 500000.0;
        double yPrime = y - 10000000.0; // Southern hemisphere

        double e1 = (1.0 - Math.Sqrt(1.0 - E2)) / (1.0 + Math.Sqrt(1.0 - E2));
        double m = yPrime / K0;
        double mu = m / (A * (1.0 - E2 / 4.0 - 3.0 * E2 * E2 / 64.0 - 5.0 * E2 * E2 * E2 / 256.0));

        double phi1 = mu
            + (3.0 * e1 / 2.0 - 27.0 * Math.Pow(e1, 3) / 32.0) * Math.Sin(2.0 * mu)
            + (21.0 * e1 * e1 / 16.0 - 55.0 * Math.Pow(e1, 4) / 32.0) * Math.Sin(4.0 * mu)
            + (151.0 * Math.Pow(e1, 3) / 96.0) * Math.Sin(6.0 * mu)
            + (1097.0 * Math.Pow(e1, 4) / 512.0) * Math.Sin(8.0 * mu);

        double sinPhi1 = Math.Sin(phi1);
        double cosPhi1 = Math.Cos(phi1);
        double tanPhi1 = Math.Tan(phi1);

        double n1 = A / Math.Sqrt(1.0 - E2 * sinPhi1 * sinPhi1);
        double t1 = tanPhi1 * tanPhi1;
        double c1 = EPrime2 * cosPhi1 * cosPhi1;
        double r1 = A * (1.0 - E2) / Math.Pow(1.0 - E2 * sinPhi1 * sinPhi1, 1.5);
        double d = xPrime / (n1 * K0);

        double latRad = phi1 - (n1 * tanPhi1 / r1) * (
            d * d / 2.0
            - (5.0 + 3.0 * t1 + 10.0 * c1 - 4.0 * c1 * c1 - 9.0 * EPrime2) * Math.Pow(d, 4) / 24.0
            + (61.0 + 90.0 * t1 + 298.0 * c1 + 45.0 * t1 * t1 - 252.0 * EPrime2 - 3.0 * c1 * c1) * Math.Pow(d, 6) / 720.0
        );

        double lon0 = (zone - 1) * 6 - 180 + 3;
        double lon0Rad = lon0 * (Math.PI / 180.0);

        double lonRad = lon0Rad + (
            d
            - (1.0 + 2.0 * t1 + c1) * Math.Pow(d, 3) / 6.0
            + (5.0 - 2.0 * c1 + 28.0 * t1 - 3.0 * c1 * c1 + 8.0 * EPrime2 + 24.0 * t1 * t1) * Math.Pow(d, 5) / 120.0
        ) / cosPhi1;

        return (lonRad * (180.0 / Math.PI), latRad * (180.0 / Math.PI));
    }
}

/// <summary>
/// High-precision deterministic correlation engine mapping 1D OTDR optical traces to georeferenced cable geometries.
/// Resolves station slack boundary cases, classifies discrete coil locations, and guarantees metric CRS projection.
/// </summary>
public static class OtdrFaultCorrelationEngine
{
    private const double EarthRadiusMeters = 6378137.0;

    /// <summary>
    /// Correlates a 1D OTDR optical break distance to an exact 2D/3D WGS 84 geographic coordinate along a fiber route.
    /// </summary>
    /// <param name="cableGeometry">Georeferenced LineString of the cable route (EPSG:4326 or projected metric).</param>
    /// <param name="otdrOpticalDistanceMeters">1D optical event distance measured by OTDR (meters).</param>
    /// <param name="helixFactor">Cable stranding/helix factor (> 0, typically 1.010..1.025).</param>
    /// <param name="stationSlackMeters">Length of cable coiled in Central Office / Substation vault (meters).</param>
    /// <param name="intermediateSlacks">Catalog of intermediate reserve loops and splice closures along route.</param>
    /// <param name="polePositions">Catalog of utility poles with their route positions and IDs.</param>
    /// <param name="crsProjectionService">Optional projection service; if null, geodetic metric referencing is used.</param>
    public static OtdrCorrelationResult LocateFault(
        LineString cableGeometry,
        double otdrOpticalDistanceMeters,
        double helixFactor,
        double stationSlackMeters,
        IReadOnlyList<SlackEntity> intermediateSlacks,
        IReadOnlyList<(string PoleId, double PositionMeters)> polePositions,
        ICrsProjectionService? crsProjectionService = null)
    {
        ArgumentNullException.ThrowIfNull(cableGeometry);
        ArgumentNullException.ThrowIfNull(intermediateSlacks);
        ArgumentNullException.ThrowIfNull(polePositions);

        if (helixFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(helixFactor), "Helix factor must be strictly positive.");
        if (otdrOpticalDistanceMeters < 0.0)
            throw new ArgumentOutOfRangeException(nameof(otdrOpticalDistanceMeters), "OTDR optical distance cannot be negative.");
        if (stationSlackMeters < 0.0)
            throw new ArgumentOutOfRangeException(nameof(stationSlackMeters), "Station slack cannot be negative.");

        var sortedPoles = polePositions.OrderBy(p => p.PositionMeters).ToList();
        string firstPoleId = sortedPoles.Count > 0 ? sortedPoles[0].PoleId : "P-0";

        // Step 1: Convert optical fiber length to physical cable sheath length
        double cableSheathLength = otdrOpticalDistanceMeters / helixFactor;

        // Step 2: Station Slack Boundary Check (Eliminates negative distance defect)
        if (cableSheathLength <= stationSlackMeters)
        {
            return new OtdrCorrelationResult(
                FaultCoordinate: cableGeometry.StartPoint.Coordinate,
                EffectiveGeographicalDistanceMeters: 0.0,
                UpstreamPoleId: "CO-ODF-ORIGIN",
                DownstreamPoleId: firstPoleId,
                DistanceFromUpstreamPoleMeters: 0.0,
                LocationType: FaultLocationType.StationSlack,
                DiagnosticDescription: $"Optical break inside Central Office / Substation slack vault (ODF/DGO). Sheath length: {cableSheathLength:F2}m <= Station slack: {stationSlackMeters:F2}m.",
                SlackEntityId: null,
                PenetrationDistanceIntoSlackMeters: cableSheathLength);
        }

        // Step 3: Traverse intermediate slack reserves sequentially
        double remainingDistance = cableSheathLength - stationSlackMeters;
        double accumulatedGeoDistance = 0.0;
        var sortedSlacks = intermediateSlacks.OrderBy(s => s.PositionAlongRouteMeters).ToList();

        FaultLocationType locationType = FaultLocationType.AerialSpan;
        string? matchedDescription = null;
        string? matchedSlackId = null;
        double? penetrationIntoSlack = null;

        foreach (var slack in sortedSlacks)
        {
            double segmentSpan = slack.PositionAlongRouteMeters - accumulatedGeoDistance;

            if (remainingDistance < segmentSpan)
            {
                // Fault occurs in open aerial span before this slack coil
                accumulatedGeoDistance += remainingDistance;
                remainingDistance = 0.0;
                break;
            }

            // Advance through aerial span to the slack coil
            accumulatedGeoDistance += segmentSpan;
            remainingDistance -= segmentSpan;

            if (remainingDistance <= slack.SlackLengthMeters)
            {
                // Fault is located inside the coiled slack loop or splice closure itself!
                locationType = slack.SlackType;
                matchedDescription = slack.Description;
                matchedSlackId = slack.SlackId;
                penetrationIntoSlack = remainingDistance;
                remainingDistance = 0.0;
                break;
            }

            // Consume the coiled slack length and proceed to next span
            remainingDistance -= slack.SlackLengthMeters;
        }

        if (remainingDistance > 0.0)
        {
            accumulatedGeoDistance += remainingDistance;
        }

        // Step 4: Spatial coordinate extraction using Metric CRS or Geodetic Fallback
        Coordinate faultCoordinate;
        if (crsProjectionService != null)
        {
            // Mode A: Planar metric projection via ICrsProjectionService
            var (metricLine, metricSrid) = crsProjectionService.ProjectToPlanarMetric(cableGeometry);
            var lengthIndexedLine = new LengthIndexedLine(metricLine);
            double clampedTarget = Math.Clamp(accumulatedGeoDistance, 0.0, metricLine.Length);
            var metricPoint = lengthIndexedLine.ExtractPoint(clampedTarget);
            faultCoordinate = crsProjectionService.ProjectToGeographic(metricPoint, metricSrid);
        }
        else if (cableGeometry.SRID != 4326 && cableGeometry.SRID != 0)
        {
            // Mode B: Cable geometry is already in a planar metric CRS (e.g. UTM)
            var lengthIndexedLine = new LengthIndexedLine(cableGeometry);
            double clampedTarget = Math.Clamp(accumulatedGeoDistance, 0.0, cableGeometry.Length);
            faultCoordinate = lengthIndexedLine.ExtractPoint(clampedTarget);
        }
        else
        {
            // Mode C: Direct Geodetic Metric Interpolation on EPSG:4326 (Prevents degree clamping bug)
            faultCoordinate = InterpolateGeodeticPoint(cableGeometry, accumulatedGeoDistance);
        }

        // Step 5: Identify nearest upstream and downstream poles & compute span offset
        var upstream = sortedPoles.LastOrDefault(p => p.PositionMeters <= accumulatedGeoDistance);
        var downstream = sortedPoles.FirstOrDefault(p => p.PositionMeters > accumulatedGeoDistance);

        string upId = upstream.PoleId ?? "CO-ODF-ORIGIN";
        string downId = downstream.PoleId ?? "END-OF-LINE";
        double distFromUp = (locationType == FaultLocationType.TechnicalReserveLoop || locationType == FaultLocationType.SpliceClosure)
            ? 0.0
            : (upstream.PoleId != null ? (accumulatedGeoDistance - upstream.PositionMeters) : accumulatedGeoDistance);

        if (locationType == FaultLocationType.AerialSpan)
        {
            matchedDescription = $"Aerial span fiber break between poles {upId} and {downId} ({distFromUp:F1}m downstream from {upId}).";
        }

        return new OtdrCorrelationResult(
            FaultCoordinate: faultCoordinate,
            EffectiveGeographicalDistanceMeters: accumulatedGeoDistance,
            UpstreamPoleId: upId,
            DownstreamPoleId: downId,
            DistanceFromUpstreamPoleMeters: distFromUp,
            LocationType: locationType,
            DiagnosticDescription: matchedDescription,
            SlackEntityId: matchedSlackId,
            PenetrationDistanceIntoSlackMeters: penetrationIntoSlack);
    }

    /// <summary>
    /// Performs high-precision geodetic linear referencing along an EPSG:4326 LineString using Haversine segment accumulation.
    /// </summary>
    private static Coordinate InterpolateGeodeticPoint(LineString line, double targetDistanceMeters)
    {
        var coords = line.Coordinates;
        if (coords.Length == 0) throw new InvalidOperationException("LineString has zero coordinates.");
        if (coords.Length == 1 || targetDistanceMeters <= 0.0) return coords[0];

        double accumulatedDistance = 0.0;
        for (int i = 0; i < coords.Length - 1; i++)
        {
            double segmentDist = CalculateHaversineMeters(coords[i], coords[i + 1]);
            if (accumulatedDistance + segmentDist >= targetDistanceMeters)
            {
                double fraction = segmentDist > 0.0 ? (targetDistanceMeters - accumulatedDistance) / segmentDist : 0.0;
                double lon = coords[i].X + fraction * (coords[i + 1].X - coords[i].X);
                double lat = coords[i].Y + fraction * (coords[i + 1].Y - coords[i].Y);
                return new Coordinate(lon, lat);
            }
            accumulatedDistance += segmentDist;
        }

        return coords[^1]; // Clamp to final vertex
    }

    private static double CalculateHaversineMeters(Coordinate c1, Coordinate c2)
    {
        double lat1 = c1.Y * (Math.PI / 180.0);
        double lat2 = c2.Y * (Math.PI / 180.0);
        double dLat = (c2.Y - c1.Y) * (Math.PI / 180.0);
        double dLon = (c2.X - c1.X) * (Math.PI / 180.0);

        double a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                   Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);
        double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        return EarthRadiusMeters * c;
    }
}
```

---

# 3. Offline/Mobile Field Execution, Georeferenced Evidence, and SLA Compliance

## 3.1 Mobile Architecture & Offline-First Sync Queue (.NET MAUI)

Technicians in Brazilian utility networks regularly execute work orders in remote rural regions (e.g., Sertão baiano in Neoenergia Coelba's concession, semi-arid interior of Ceará), underground vaults, or densely populated peri-urban valleys lacking reliable 4G/5G mobile connectivity. The mobile runtime (`GeoAssets.MAUI`) must operate strictly **Offline-First**.

```
┌────────────────────────────────────────────────────────────────────────┐
│                        .NET MAUI Client Architecture                   │
├────────────────────────────────────────────────────────────────────────┤
│  Blazor Hybrid WebView (GeoAssets.Shared Razor UI)                     │
│  - ServiceOrders.razor / ServiceOrderDetail.razor                      │
│  - CameraCaptureComponent.razor                                        │
│  - DigitalSignaturePad.razor                                           │
├────────────────────────────────────────────────────────────────────────┤
│  Local Application Services                                            │
│  - IOfflineServiceOrderRepository                                      │
│  - ILocalMediaStorageService                                           │
│  - ISyncEngineService                                                  │
├────────────────────────────────────────────────────────────────────────┤
│  Local Encrypted Persistence (SQLCipher / SQLite)                      │
│  - local_service_orders (Cached records, status, attributes)           │
│  - local_action_outbox (Queued offline actions to push)                │
│  - local_media_cache (Staged encrypted photos & hashes)                │
│  - local_vector_tiles (Pre-cached GeoJSON / MBTiles for map view)      │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                         Bi-Directional Delta Sync
                     (HTTPS REST / JSON / Multipart)
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                        GeoAssets ASP.NET Core Server                   │
├────────────────────────────────────────────────────────────────────────┤
│  - POST /api/workflow/service-orders/sync/push                         │
│  - GET  /api/workflow/service-orders/sync/pull?since={timestamp}       │
│  - POST /api/workflow/service-orders/{id}/media                        │
└────────────────────────────────────────────────────────────────────────┘
```

### Local SQLite Schema & Outbox Pattern

The mobile client maintains a transactional outbox queue (`local_action_outbox`) tracking every operation executed offline. When network connectivity is re-established (monitored by `Microsoft.Maui.Networking.Connectivity.Current`), the background synchronization daemon flushes the queue sequentially.

```sql
-- Local SQLite Mobile Schema
CREATE TABLE local_service_orders (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    order_type_id TEXT NOT NULL,
    status TEXT NOT NULL,
    priority INTEGER NOT NULL,
    assigned_to TEXT,
    attributes_json TEXT NOT NULL,
    feature_ids_json TEXT NOT NULL,
    row_version BLOB NOT NULL,
    local_modified_at TEXT NOT NULL,
    is_dirty INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE local_action_outbox (
    outbox_id TEXT PRIMARY KEY,
    order_id TEXT NOT NULL,
    action_type TEXT NOT NULL,
    performed_at TEXT NOT NULL,
    resulting_status TEXT,
    comment TEXT,
    payload_json TEXT NOT NULL,
    retry_count INTEGER NOT NULL DEFAULT 0,
    sync_status TEXT NOT NULL DEFAULT 'QUEUED', -- QUEUED, SENDING, ACKNOWLEDGED, FAILED
    error_message TEXT
);

CREATE TABLE local_media_staging (
    media_id TEXT PRIMARY KEY,
    order_id TEXT NOT NULL,
    file_path TEXT NOT NULL,
    sha256_hash TEXT NOT NULL,
    latitude REAL NOT NULL,
    longitude REAL NOT NULL,
    altitude REAL,
    azimuth REAL,
    captured_at TEXT NOT NULL,
    upload_status TEXT NOT NULL DEFAULT 'PENDING'
);
```

---

## 3.2 Georeferenced Media Capture & Anti-Tamper Integrity

Brazilian public procurement notices and utility contracts (e.g., Sabesp ICERP, Neoenergia DIS-NOR-068, CEMIG RDA) universally mandate that photographic evidence must be tamper-proof to prevent fraud, staged repairs, or reuse of historical photos.

### Mobile Device Hardware Integration
1. **Manifest Permissions**:
   * Android (`Platforms/Android/AndroidManifest.xml`):
     ```xml
     <uses-permission android:name="android.permission.INTERNET" />
     <uses-permission android:name="android.permission.CAMERA" />
     <uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
     <uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />
     <uses-feature android:name="android.hardware.camera" android:required="true" />
     <uses-feature android:name="android.hardware.location.gps" android:required="true" />
     ```
   * iOS (`Platforms/iOS/Info.plist`):
     ```xml
     <key>NSCameraUsageDescription</key>
     <string>GeoAssets requires camera access to capture georeferenced work order evidence.</string>
     <key>NSLocationWhenInUseUsageDescription</key>
     <string>GeoAssets requires high-precision GPS to verify technician location at the asset.</string>
     ```

2. **Hardware Shutter Interlock & EXIF Watermark Engine**:
   When the technician triggers camera capture inside `GeoAssets.MAUI`, the application queries the device sensors directly via `Microsoft.Maui.Devices.Sensors`:
   - High-precision GPS reading: Latitude, Longitude, Altitude, Horizontal Accuracy ($\text{accuracy} \le 10\text{ m}$ threshold enforced; capture blocked if accuracy is degraded).
   - Compass sensor: Heading azimuth angle ($0^\circ - 360^\circ$).
   - Network Time Protocol (NTP) timestamp: Verified against server time delta to prevent manual device clock tampering.

3. **Cryptographic Seal (SHA-256 HMAC)**:
   The raw image byte stream is combined with sensor metadata and signed:
   $$\text{EvidenceHash} = \text{HMAC-SHA256}(\text{RawImageBytes} \parallel \text{Lat} \parallel \text{Lon} \parallel \text{UtcTimestamp} \parallel \text{OrderId}, \text{DeviceSecret})$$
   The resulting hash is embedded in the image EXIF header tag `ImageDescription` and recorded in the server database record. Any byte modification of the photo or metadata invalidates the cryptographic signature during supervisor audit.

4. **Visual Watermark Inscription**:
   Before saving, the client burns an immutable text banner directly into the lower 120 pixels of the image:
   `[GeoAssets OS: {OrderId}] | Date: 2026-10-28 14:32:01 UTC-3 | Lat: -12.9714° Lon: -38.5014° | Acc: 3.2m | Tech: ELEC-4819 | Coelba BDGD UNTRD-9041`

---

## 3.3 Regulatory SLA Monitoring Engine & Outage Forecasting

GeoAssets provides a real-time regulatory compliance engine that monitors Service Order execution against legal thresholds established by ANEEL, ANA, and state concession contracts.

### Brazilian Utility Regulatory SLA Matrix

| Regulatory Norm | Utility Domain | Service Category / SLA Description | Mandatory Time Window | Financial / Legal Penalty for Breach |
|---|---|---|---|---|
| **ANEEL REN 1.000/2021** | Energia Elétrica | Religação de Urgência (Área Urbana) | **4 horas** | Compensação financeira direta na fatura do consumidor (Art. 362). |
| **ANEEL REN 1.000/2021** | Energia Elétrica | Religação Normal (Área Urbana) | **24 horas** | Compensação financeira calculada por hora de atraso. |
| **ANEEL REN 1.000/2021** | Energia Elétrica | Atendimento Emergencial (Cabo Partido / Risco à Vida) | **Despacho Imediato** | Auto de Infração e multas regulatórias da ANEEL / ARCE. |
| **PRODIST Módulo 8** | Energia Elétrica | Limites Contínuos de Duração (DMIC) e Frequência (FIC) | **Metas Anuais / Mensais** | Ressarcimento financeiro obrigatório por violação de DMIC. |
| **Sabesp / ARSESP Del. 1739** | Saneamento Água | Vazamento Visível de Água em Leito Viário | **6 a 12 horas** | Redução do Fator Q / Índice Geral de Qualidade (IGQ). |
| **Sabesp / ARSESP Del. 1739** | Saneamento Esgoto| Extravasamento de Esgoto em Via Pública | **4 a 6 horas** | Multa ambiental municipal e penalidade regulatória ARSESP. |
| **Sabesp / ARSESP ICERP** | Saneamento Obras | Recomposição de Pavimento Asfáltico (Vias Arteriais) | **48 horas** | Reprovação de lote no ICERP; retenção de medição de empreiteira. |
| **RNP / Telebras / Wholesale**| Telecom Fibra | Rompimento de Cabo Óptico Tronco (MTTR Backbone) | **4 horas** | Penalidade contratual de Service Level Credit ($1\% - 5\%$ da fatura/h). |

### Predictive Outage Impact Simulation using `TopoGraph`

When an electrical circuit breaker or a water district valve must be operated, GeoAssets utilizes its built-in topological engine (`core/GeoAssets.Core/Services/TopoGraph.cs`) to forecast customer and regulatory impacts **prior to dispatch**:

```csharp
public sealed record OutageImpactForecast(
    int AffectedCustomerUnitsCount,
    double TotalContractedPowerKva,
    double ForecastedDmicHours,
    decimal EstimatedCompensationRiskBrl);

public static class RegulatoryOutagePredictor
{
    /// <summary>
    /// Executes a downstream traversal from an isolation point, identifying all downstream
    /// customers and computing expected regulatory impact under PRODIST Módulo 8.
    /// </summary>
    public static OutageImpactForecast PredictOutage(
        TopoGraph networkGraph,
        string isolationAssetId,
        IReadOnlyDictionary<string, double> assetLoadsKva,
        IReadOnlyList<string> registeredConsumerUnitIds,
        TimeSpan plannedDuration)
    {
        // 1. Traverse downstream graph to find all isolated nodes
        var isolatedAssets = networkGraph.GetDescendants(isolationAssetId).ToHashSet();
        isolatedAssets.Add(isolationAssetId);

        // 2. Filter affected Consumer Units (UCs)
        int affectedUcs = registeredConsumerUnitIds.Count(ucId => isolatedAssets.Contains(ucId));

        // 3. Compute total interrupted kVA
        double totalKva = isolatedAssets
            .Where(assetLoadsKva.ContainsKey)
            .Sum(id => assetLoadsKva[id]);

        // 4. Calculate DMIC impact (Duration in hours)
        double dmicHours = plannedDuration.TotalHours;

        // 5. Estimate financial compensation risk based on ANEEL standard formula
        // Valor = (VUC * Kva * dmicHours) / FatorProrroga
        decimal estimatedRisk = (decimal)(affectedUcs * 14.50 * dmicHours);

        return new OutageImpactForecast(affectedUcs, totalKva, dmicHours, estimatedRisk);
    }
}
```

---

# 4. Prioritized Gap Analysis & Implementation Roadmap for GeoAssets

## 4.1 Prioritized Gap Analysis Matrix

| Platform Capability | Current Codebase State in GeoAssets | Required Production Capability | Priority | Effort |
|---|---|---|---|---|
| **Core Domain Model** | `IServiceOrder.cs` defines Id, Status, Priority, Attributes, Features, Dispatches, ActionLog. | Production-ready foundation; add `SlaTargetTimestamp`, `ImpedimentCode`, and `ParentOrderId` hierarchy. | High | 1 week |
| **State Machine Engine** | `ServiceOrderTransitions.cs` supports 6 basic states (`Draft`, `Pending`, `InProgress`, `OnHold`, `Completed`, `Cancelled`). | Expand to the 12-State Workflow Graph (`Scheduled`, `Dispatched`, `EnRoute`, `OnSite`, `ConflictReview`, `Inspected`) with dedicated `Suspend` action. | Critical | 1.5 weeks |
| **Map-to-OS UI Integration** | Leaflet map is completely hidden on `/service-orders` (`MainLayout.razor:45,50`). No map asset picker. | Embed interactive Leaflet map in `/service-orders`; enable spatial selection of assets during order creation; render active order pins on map. | Critical | 2 weeks |
| **Mobile Offline Database** | MAUI client uses `RestServiceOrderRepository` which requires constant connectivity. No offline DB. | Implement local SQLite offline store (`SQLCipher`) in MAUI with transactional outbox queue and delta sync. | Critical | 3 weeks |
| **Georeferenced Photo Capture** | Zero camera integration; zero permissions in Android/iOS manifests; no EXIF GPS extraction. | Integrate `MediaPicker` and `Geolocation` in MAUI; implement EXIF GPS watermark stamp and SHA-256 HMAC anti-tamper seal. | Critical | 2 weeks |
| **Domain Asset Schemas** | Generic `AssetType` (Poste, Transformador). Demo circuits only. | Implement ANEEL BDGD layers (`PONNOT`, `UNTRD`, `SEGCON`), Water (`RDA`, `PV`, `VRP`), and Telecom (`CTO`, `CEO`, `Drop`). | High | 2 weeks |
| **Digital Field Checklists** | Key-value string dictionary without UI form renderers. | Dynamic JSON Schema form renderer in Razor supporting APR, NR-10/35, NR-33, ICERP, and OPM tests. | High | 2.5 weeks |
| **OTDR Fault Localization** | NetTopologySuite available, but no OTDR distance-to-geometry algorithm. | Implement `OtdrFaultCorrelationEngine` with IOR, helix factor, and slack loop traversal. | Medium | 1.5 weeks |
| **Regulatory SLA Engine** | Basic timestamps only. No regulatory countdown timers. | Real-time SLA engine tracking ANEEL REN 1.000, Sabesp ISAP, and PRODIST DIC forecasting using `TopoGraph`. | High | 2 weeks |
| **BoQ & Billing (US / ICERP)** | IP Billing catalog exists (`GeoAssets.IpBilling.EFCore`), but not integrated with Service Order. | Implement Unidades de Serviço (US) itemized contractor calculation and ICERP paving measurement sheets. | Medium | 2 weeks |

---

## 4.2 4-Phase Implementation Roadmap

```
2026 Q4 ───────────────► 2027 Q1 ───────────────► 2027 Q2 ───────────────► 2027 Q3
┌──────────────────────┐ ┌──────────────────────┐ ┌──────────────────────┐ ┌──────────────────────┐
│       PHASE 1        │ │       PHASE 2        │ │       PHASE 3        │ │       PHASE 4        │
│ Domain Schemas,      │ │ Mobile Hardware,     │ │ Offline-First        │ │ Field Checklists,    │
│ Catalog Seeds &      │ │ Camera Geotagging &  │ │ SQLite Outbox &      │ │ OTDR Algorithm &     │
│ Map-to-OS UI Link    │ │ Anti-Tamper Media    │ │ Delta Sync Engine    │ │ Regulatory SLA Engine│
└──────────────────────┘ └──────────────────────┘ └──────────────────────┘ └──────────────────────┘
```

### Phase 1: Domain Schemas, Regulatory Catalog Seeds & Map-to-OS UI Integration
* **Deliverables**:
  1. Expand `ServiceOrderTransitions.cs` and `OrderActionType.cs` to support the full 12-State lifecycle (`Scheduled`, `Dispatched`, `EnRoute`, `OnSite`, `ConflictReview`, `Inspected`) and dedicated `OrderActionType.Suspend`.
  2. Seed production `OrderType` records and JSON schemas for:
     - `electrical-emergency` (ANEEL REN 1.000).
     - `electrical-maintenance-us` (CEMIG/Coelba US model).
     - `water-leak-repair` (Sabesp/Cagece ICERP model).
     - `sewer-unblocking` (NR-33 sanitation model).
     - `telecom-ftth-activation` (V.tal/Anatel drop activation).
     - `telecom-pole-audit` (Decreto 12.068/2024 / Res. 004/2014).
  3. **Map-to-OS UI Integration**:
     - Modify `MainLayout.razor` to retain Leaflet map visibility in a split/docked view on `/service-orders`.
     - Update `ServiceOrderCreateForm.razor` to support spatial picking: clicking one or more features on the Leaflet map automatically binds their GUIDs to `ServiceOrder.FeatureIds`.
     - Add an interactive Service Order layer to Leaflet showing active orders colored by priority and status.

### Phase 2: Mobile Hardware Integration, Media & Georeferenced Camera in MAUI
* **Deliverables**:
  1. Configure camera and fine-location permissions in `GeoAssets.MAUI` for Android and iOS.
  2. Implement `ICameraMediaService` in `GeoAssets.Shared`:
     - Cross-platform capture via `Microsoft.Maui.Media.MediaPicker`.
     - Automatic retrieval of GPS coordinates (WGS 84), altitude, heading azimuth, and UTC network time.
     - Watermark rendering on the image bitmap (Order ID, Coordinates, Timestamp, Technician ID).
     - Generation of SHA-256 HMAC anti-tamper signature embedded in EXIF and saved to database payload.
  3. Build `MediaGalleryComponent.razor` inside `ServiceOrderDetail.razor` to display before/after photos with interactive thumbnail zoom and map location pin.

### Phase 3: Offline-First SQLite Storage & Store-and-Forward Sync Queue
* **Deliverables**:
  1. Implement encrypted local SQLite storage (`local_service_orders`, `local_action_outbox`, `local_media_staging`) in `GeoAssets.MAUI` using `sqlite-net-pcl`.
  2. Replace `RestServiceOrderRepository` in the mobile project with `OfflineFirstServiceOrderRepository`:
     - Reads are served instantaneously from local SQLite.
     - Writes append to `local_action_outbox` and immediately transition local state.
  3. Implement `SyncDaemonService`:
     - Monitors network connectivity (`Connectivity.Current`).
     - Batches pending outbox actions and pushes to `/api/workflow/service-orders/sync/push`.
     - Handles HTTP 409 Concurrency Conflicts using the Field-Preservation Reconciliation Strategy.
     - Pulls updated orders assigned to the technician via delta queries.

### Phase 4: Domain Field Checklists, OTDR Engine & Regulatory SLA Monitoring
* **Deliverables**:
  1. Implement `DynamicFormRenderer.razor` driven by `OrderType.AttributesSchemaJson` to present interactive checklists for APR (NR-10/35), Confined Space (NR-33), and Optical Budget.
  2. Implement `OtdrFaultCorrelationEngine` in `GeoAssets.Core.Services` and expose REST endpoint `/api/topology/telecom/correlate-otdr` to pinpoint fiber breaks on map routes.
  3. Integrate the Unidade de Serviço (US) itemized catalog and ICERP paving area calculation into order completion and audit screens.
  4. Implement `RegulatorySlaEngine`:
     - Real-time countdown clocks rendered in `ServiceOrderList.razor` with color-coded escalation alerts (75% yellow, 90% orange, 100% breach).
     - Outage simulation leveraging `TopoGraph.GetDescendants` to project affected consumer units and ANEEL DIC/DMIC financial exposure prior to feeder isolation.

---

# 5. Verification & Acceptance Criteria

To verify this specification against technical and regulatory mandates:

1. **State Machine Conformance**:
   - Verify that all 12 states and their transitions conform to `IServiceOrder.Status` and can be represented in `OrderType.States` and `OrderType.Transitions`.
   - Validate that `RowVersion` optimistic concurrency prevents lost updates between dispatcher and field clients, returning HTTP 409 on collision.
2. **Domain Regulatory Compliance**:
   - *Electrical*: Checklists enforce NR-10 and NR-35 pre-work locks; asset models map to ANEEL BDGD layers (`UNTRD`, `PONNOT`, `SEGCON`, `UNREMT`); US measurement matches CEMIG/Coelba contractor BoQ formulas.
   - *Water/Sanitation*: Checklists enforce NR-33 gas limits ($O_2 \ge 19.5\%$, $H_2S \le 8\text{ ppm}$); paving measurement calculates trench area ($m^2$) for Sabesp ICERP compliance; sewage unblocking records hydro-jetting parameters.
   - *Telecom*: Optical checks enforce power levels ($-15\text{ to }-25\text{ dBm}$) and fusion loss $\le 0.05\text{ dB}$; pole checklists validate 500 mm band occupancy under Decreto 12.068/2024; OTDR correlation algorithm correctly locates break coordinates from optical distances.
3. **Offline Mobile & Evidence Integrity**:
   - Verify that all mobile transactions function with disconnected network adapters, persisting to local SQLite outbox.
   - Verify that captured photos contain mandatory GPS EXIF tags, burnt-in watermarks, and verifiable SHA-256 cryptographic signatures.
4. **SLA Monitoring**:
   - Verify that real-time timers track ANEEL REN 1.000 (4h/24h) and Sabesp leak SLAs (6h/12h/24h), triggering escalation alerts at defined thresholds.
