# Autonomous Claude Code Execution Directive: Implement All 59 Backlog Tickets in Project XD01 to Shipped State

**Target Agent**: Claude Code (`claude`)  
**Project**: GeoAssets (`GeoAssets.sln`)  
**Jira Cloud Instance**: `xdicor.atlassian.net`  
**Jira Project Key**: `XD01` (Team-Managed / Simplified Workflow)  
**Target Integration Branch**: `develop` (Strict Prohibition: Never branch from, commit directly to, or merge into `main`)  
**Methodology Blueprint**: Tri-Planar Outer-Loop DAG (`.agents/skills/code-gen-dag/SKILL.md`)  
**Working Directory**: `/Users/yuniorsf/WORK/GeoAssets`  

---

## 1. Executive Mission & Operating Context

You are operating as the autonomous lead software synthesis engineer (Claude Code) tasked with implementing all **59 Jira tickets remaining in "TO DO"** within the GeoAssets platform (`XD01` namespace) until every ticket reaches the rigorous **Shipped State**.

Of the 300 total tickets in the project namespace (`XD01-1` through `XD01-300`), 234 tickets are fully implemented, committed to `develop`, and verified with 2,660 passing unit and integration tests across 28 test projects. The remaining 59 tickets represent:
1. **2 Explicit Hardware / Packaging Blocked Tickets**:
   - `XD01-260`: MAUI Physical Camera & GPS UI capture (device sensor abstractions, camera viewfinder interop, and simulator fallback testing)
   - `XD01-293`: MAUI iOS/MacCatalyst `appsettings.json` bundling fix under strict `/warnaserror`
2. **57 Unstarted Backlog Tickets**:
   - Pilot Multi-Tenant Dedicated Silos: `XD01-217` (First dedicated silo tenant onboarding), `XD01-218` (Second dedicated silo tenant onboarding / multi-silo routing validation)
   - Core Architecture & Schema Foundation: `XD01-1`, `XD01-2`, `XD01-5`, `XD01-6`, `XD01-7`, `XD01-23`, `XD01-25`
   - UI Shell, Navigation & Theme: `XD01-72`, `XD01-77`, `XD01-78`, `XD01-79`, `XD01-86`, `XD01-137`
   - Map Styling, Vector Rendering & Topological Linking: `XD01-96`, `XD01-108`, `XD01-120`, `XD01-151`, `XD01-153`, `XD01-154`, `XD01-155`, `XD01-158`, `XD01-168`, `XD01-169`
   - Performance, Viewport Rendering & EF Core Optimizations: `XD01-122`, `XD01-126`, `XD01-149`, `XD01-165`, `XD01-166`, `XD01-167`
   - Project Subsystem & Domain Parent Epics: `XD01-136`, `XD01-175`, `XD01-194`, `XD01-195`, `XD01-196`, `XD01-197`, `XD01-198`
   - Field Service Order Engine & Sync Extensions: `XD01-225`, `XD01-227`, `XD01-231`, `XD01-232`, `XD01-234`, `XD01-235`, `XD01-295`
   - Contractor US Ledger & Measurement Reporting: `XD01-238`, `XD01-239`, `XD01-240`
   - Neutral Infrastructure Operator & Poste Justo: `XD01-253`, `XD01-254`, `XD01-255`, `XD01-256`, `XD01-257`, `XD01-268`
   - Security, Photo Evidence HMAC & Safety Rules: `XD01-262`, `XD01-290`, `XD01-299`

You must execute ticket implementation sequentially or in dependency-ordered clusters using isolated Git worktrees. Every implementation must maintain real domain state and produce real, verifiable behavior.

---

## 2. Non-Negotiable Definition of "Shipped State"

Every ticket is considered **Shipped** only when all five of the following objective gates are completely satisfied:

### 2.1 100% Genuine Production Code Implementation
- Full C# 14 / .NET 10 implementation in the authoritative projects under `core/`, `apps/`, `providers/`, `workflow/`, or `ipbilling/`.
- **Zero-Placeholder Guarantee**: The words `TODO`, `TBD`, `FIXME`, and exceptions of type `NotImplementedException` or `NotSupportedException` are strictly forbidden in production code.
- **No Dummy Facades**: Methods must compute, validate, project, mutate, or query real domain data. Returning hardcoded verification strings or empty dummy collections is an immediate disqualification.

### 2.2 Exhaustive Passing Automated Test Suites (`dotnet test`)
- Co-located xUnit test suites in `tests/` covering 100% of line and branch paths for newly introduced or modified logic.
- Tests must assert **behavior and invariants**, not implementation details. Every test must be non-vacuous (it must fail if domain logic is altered or inverted).
- Use `FakeTimeProvider` for time-dependent logic (`TimeProvider.System` injected in production).
- Zero skipped or ignored tests (`[Fact(Skip = "...")]` is strictly forbidden).

### 2.3 Clean Compilation with 0 Warnings Under `/warnaserror`
- Solution and touched projects must compile with zero errors and zero warnings:
  ```bash
  dotnet build <project.csproj> --no-incremental /warnaserror
  ```
- **Zero Suppression Rule**: Strictly forbidden to bypass `Directory.Build.props` or `BannedSymbols.txt` via:
  - `#pragma warning disable`
  - `[SuppressMessage(...)]`
  - `<NoWarn>...</NoWarn>` in `.csproj`
- Full compliance with Roslyn, `AsyncFixer`, `Meziantou.Analyzer`, and `BannedApiAnalyzers` rules:
  - `RS0030`: Never call `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `Task.Wait()`, `Task.Result`, or `Thread.Sleep()`.
  - `AsyncFixer01`: Do not wrap tasks in redundant `async`/`await` state machines unless inside a `using` scope or `try/catch`.
  - `AsyncFixer02`: Never perform blocking synchronous calls inside async methods.
  - `AsyncFixer03`: Never write `async void` except for Blazor/MAUI UI event handlers with explicit `try/catch`.
  - `MA0004` / `MA0040`: Always propagate `CancellationToken` and append `.ConfigureAwait(false)` in non-UI core/library projects.

### 2.4 Onion Architecture & SOLID Invariants
- `core/` projects (`GeoAssets.Core`, `GeoAssets.Workflow`, `GeoAssets.IpBilling`, `GeoAssets.Identity`, `GeoAssets.Domain.*`) must remain pure domain libraries:
  - Strictly **ZERO** references to `Npgsql`, `Microsoft.EntityFrameworkCore`, PostGIS, or raw HTTP/IO drivers in `core/`.
- Concrete persistence belongs strictly in `providers/GeoAssets.Provider.PostgreSQL/`, `workflow/GeoAssets.Workflow.EFCore/`, `ipbilling/GeoAssets.IpBilling.EFCore/`, or `apps/GeoAssets.Identity.EFCore/`.
- Segregate interfaces to cohesive role ports (`<= 5` methods per interface) following the Interface Segregation Principle (ISP).

### 2.5 Clean Git Hygiene & Integration Commit
- Work is executed in an isolated worktree created via `scripts/agent-sandbox.sh create <taskId> origin/develop`.
- Commits are formed on branch `develop` adhering strictly to repository conventional commit rules:
  - Subject format: `<type>(<scope>): <summary> (<TICKET-KEY>)`
  - Examples:
    - `feat(workflow): add SLA statutory deadline warning alerts (XD01-225)`
    - `fix(maui): resolve MacCatalyst appsettings.json bundling under warnaserror (XD01-293)`
- Clean working tree ready for pull request merge (`git status` reports nothing untracked or unstaged).

---

## 3. Autonomous Execution Protocol: 5-Phase Tri-Planar DAG

For each ticket (or sequential ticket cluster), execute the 5-phase code generation DAG from `.agents/skills/code-gen-dag/SKILL.md`:

```
┌────────────────────────────────────────────────────────┐
│ Phase 1: Task Decomposition & Worktree Initialization  │
│ - Parse ticket key, fetch requirements, inspect types  │
│ - Run ./scripts/agent-sandbox.sh create <taskId>       │
└───────────────────────────┬────────────────────────────┘
                            │
┌───────────────────────────▼────────────────────────────┐
│ Phase 2: Parallel Dual-Loop Synthesis                  │
│ - Branch 2A (Claude Code): Core domain models & ports   │
│ - Branch 2B (Copilot/QA): xUnit tests & test fixtures   │
└───────────────────────────┬────────────────────────────┘
                            │
┌───────────────────────────▼────────────────────────────┐
│ Phase 3: Deterministic Oracle Verification             │
│ - verify-solid-contracts.py                            │
│ - verify-agent-worktree.sh                             │
│ - dotnet test <touched-test-projects>                  │
└───────┬────────────────────────────────────────┬───────┘
        │ Exit != 0                              │ Exit 0
┌───────▼───────────────────────┐                │
│ Phase 4: Self-Healing Loop    │                │
│ (Max 3 retries)               │                │
│ Exit 10 -> format             │                │
│ Exit 20 -> Roslyn/Analyzers   │                │
│ Exit 30 -> NetArchTest/xUnit  │                │
└───────┬───────────────────────┘                │
        │ Re-run Phase 3                         │
        └────────────────────────────────────────┤
                                                 │
┌────────────────────────────────────────────────▼───────┐
│ Phase 5: Rebase, Post-Rebase Test Gate & Merge Ready   │
│ - git fetch origin develop && git rebase origin/develop│
│ - Post-rebase verify-agent-worktree.sh                 │
│ - Commit ending with (<TICKET-KEY>)                    │
│ - Stage PR metadata targeting develop                  │
└────────────────────────────────────────────────────────┘
```

### Self-Healing Decision Table (Phase 4)
| Exit Code | Failure Stage | Action |
|---|---|---|
| `Exit 10` | Code formatting drift | Execute `dotnet format style GeoAssets.sln` inside worktree. |
| `Exit 20` | Compiler or Roslyn Analyzer warning | Resolve Roslyn/`AsyncFixer`/`RS0030`/`Meziantou` diagnostics in source without `#pragma` suppressions. |
| `Exit 30` | `NetArchTest` architecture violation or xUnit test failure | Eliminate forbidden cross-layer dependency or fix broken domain logic assertion. |

---

## 4. Architectural Map & Bounded Contexts

GeoAssets is partitioned into four primary microservice / component tiers:

```
                                  ┌────────────────────────┐
                                  │   GeoAssets.Gateway    │
                                  │ (YARP Reverse Proxy)   │
                                  └───────────┬────────────┘
                                              │
             ┌────────────────────────────────┼────────────────────────────────┐
             │                                │                                │
┌────────────▼───────────┐      ┌─────────────▼───────────┐      ┌─────────────▼───────────┐
│   GeoAssets.Server     │      │ GeoAssets.IpBilling     │      │ GeoAssets.FieldService  │
│    (Core Platform)     │      │        .Server          │      │         .Server         │
│                        │      │ (Vertical Microservice) │      │ (Vertical Microservice) │
│ - Spatial queries      │      │                         │      │                         │
│ - TopoGraph engine     │      │ - GEOS_FAT_IP Ledger    │      │ - 12-state workflow     │
│ - OGC WFS / WMS        │      │ - Levantamento, Ofício  │      │ - Contractor US Ledger  │
│ - Asset Type catalog   │      │ - Fiscalização audit    │      │ - Offline Mobile Sync   │
│ - Identity / RBAC      │      │ - Municipal energy math │      │ - Regulatory Modules    │
└────────────┬───────────┘      └─────────────┬───────────┘      └─────────────┬───────────┘
             │                                │                                │
┌────────────▼───────────┐      ┌─────────────▼───────────┐      ┌─────────────▼───────────┐
│ GeoAssets.Provider     │      │ GeoAssets.IpBilling     │      │ GeoAssets.Workflow      │
│      .PostgreSQL       │      │         .EFCore         │      │         .EFCore         │
│ (PostGIS, RLS, ST_*)   │      │ (PostgreSQL, Ledger)    │      │ (PostgreSQL, Outbox)    │
└────────────────────────┘      └─────────────────────────┘      └─────────────────────────┘
```

1. **Core Platform (`apps/GeoAssets.Server`, `core/GeoAssets.Core`, `providers/GeoAssets.Provider.PostgreSQL`)**:
   - Master entity repository (`geo_entity`), PostGIS spatial query engine, directed topological graph (`TopoGraph`: Kahn sort, Dijkstra shortest path, BFS), OGC WFS 2.0 and WMS 1.3.0 raster rendering with SkiaSharp, and RBAC permission middleware.
2. **IP Billing Vertical Microservice (`apps/GeoAssets.IpBilling.Server`, `core/GeoAssets.IpBilling`, `ipbilling/GeoAssets.IpBilling.EFCore`)**:
   - Public lighting billing (`GEOS_FAT_IP`) engine supporting field luminaire surveys (*Levantamento*), municipal sample audits (*Fiscalização*), concessionaire notifications (*Ofício*), and retroactive municipal energy tariff ledgers (*Faturamento*).
3. **Field Service Vertical Microservice (`apps/GeoAssets.FieldService.Server`, `core/GeoAssets.Workflow`, `workflow/GeoAssets.Workflow.EFCore`)**:
   - 12-state field work state machine, geofencing (ST_DWithin <=30m), digital APR safety gates (NR-10, NR-35, NR-33), contractor *Unidades de Serviço* (US) ledger, *Espelho de Medição* dossier generator, supervisor inspection commits, and offline mobile sync gateway.
4. **Gateway & Multi-Tenancy SaaS (`apps/GeoAssets.Gateway`)**:
   - YARP dynamic reverse proxy, tenant isolation routing (shared multi-tenant cluster vs. Tier-2 dedicated silo by `X-Tenant-Domain`), and per-`OrganizationId` token-bucket rate limiter.
5. **Client Applications (`apps/GeoAssets.Web`, `apps/GeoAssets.Shared`, `apps/GeoAssets.MAUI`)**:
   - Blazor WebAssembly client and .NET MAUI multi-platform client featuring SQLCipher encrypted local outbox, GPS/compass metadata capture, and HMAC-SHA256 evidence sealing.

---

## 5. Authoritative Specification of All 59 Remaining TO DO Tickets

### Cluster 1: Platform & Mobile Hardware Incompletes (2 Tickets)

#### Ticket XD01-260: MAUI Physical Camera & GPS UI Capture with Device Simulation Fallback
- **Issue Key**: `XD01-260`
- **Issue Type**: `Historia`
- **Functional Requirements**:
  1. Implement device-independent camera and GPS sensor capture abstractions in `apps/GeoAssets.MAUI`.
  2. Implement `MauiCameraCaptureService` integrating with `.NET MAUI MediaPicker` / native camera viewfinders to capture high-resolution field photos.
  3. Implement `MauiGeolocationService` reading physical device GPS/compass sensor chips with accuracy filtering, timeout backoffs, and altitude capture.
  4. Provide `SimulatedCameraCaptureService` and `SimulatedGeolocationService` fallbacks that automatically activate when running in headless test harnesses, iOS simulators, or Android emulators lacking hardware camera/GPS sensors.
  5. Feed captured image bytes and coordinate metadata into `FieldPhotoEvidenceSealer` (`XD01-261`) to apply EXIF metadata, visual watermark burn-in, and HMAC-SHA256 tamper-evident sealing.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.MAUI/Services/MauiCameraCaptureService.cs` (New)
  - `apps/GeoAssets.MAUI/Services/MauiGeolocationService.cs` (New)
  - `apps/GeoAssets.MAUI/Services/SimulatedCameraCaptureService.cs` (New)
  - `apps/GeoAssets.MAUI/Services/SimulatedGeolocationService.cs` (New)
  - `apps/GeoAssets.MAUI/MauiProgram.cs` (Update DI registration)
  - `tests/GeoAssets.Shared.Tests/Services/CameraCaptureServiceTests.cs` (New)
- **Architectural Tier**: Client Applications (`GeoAssets.MAUI`, `GeoAssets.Shared`)
- **Acceptance Criteria**:
  - Camera service captures JPEG/PNG byte streams with valid dimensions and EXIF headers.
  - Geolocation service yields coordinates with dilution-of-precision (DOP) <= 5.0m or falls back to simulated coordinates cleanly.
  - Headless test suite passes without requiring physical hardware sensors.
- **Verification Commands**:
  ```bash
  dotnet build apps/GeoAssets.MAUI/GeoAssets.MAUI.csproj -f net10.0-android --no-incremental /warnaserror
  dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --filter "FullyQualifiedName~CameraCaptureServiceTests"
  ```

#### Ticket XD01-293: MAUI iOS/MacCatalyst `appsettings.json` Bundling Fix Under `/warnaserror`
- **Issue Key**: `XD01-293`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Resolve the MSBuild packaging error where `appsettings.json` is duplicated or omitted from bundle resources during iOS / MacCatalyst compilation under `/warnaserror`.
  2. Update `apps/GeoAssets.MAUI/GeoAssets.MAUI.csproj` item group targets: mark `appsettings.json` and environment-specific configuration files as `MauiAsset` with logical target paths, preventing conflict with default `Content` items.
  3. Ensure `ConfigurationBuilder.AddJsonStream` loads the bundled `appsettings.json` consistently across Android, iOS, MacCatalyst, and Windows targets.
  4. Verify that full solution compilation with `/warnaserror` succeeds for Apple targets without MSBuild warnings or errors.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.MAUI/GeoAssets.MAUI.csproj` (Update MSBuild item groups and asset packaging)
  - `apps/GeoAssets.MAUI/MauiProgram.cs` (Streamline configuration stream loader)
- **Architectural Tier**: Client Applications (`GeoAssets.MAUI`)
- **Acceptance Criteria**:
  - Clean MSBuild compilation of `apps/GeoAssets.MAUI/GeoAssets.MAUI.csproj` targeting `net10.0-maccatalyst` and `net10.0-ios` with 0 warnings under `/warnaserror`.
  - Configuration settings (e.g., `Gateway:BaseUrl`, `Sync:BatchSize`) deserialize correctly at runtime.
- **Verification Commands**:
  ```bash
  dotnet build apps/GeoAssets.MAUI/GeoAssets.MAUI.csproj -f net10.0-maccatalyst --no-incremental /warnaserror
  dotnet build apps/GeoAssets.MAUI/GeoAssets.MAUI.csproj -f net10.0-ios --no-incremental /warnaserror
  ```

---

### Cluster 2: Pilot Multi-Tenant Dedicated Silos (2 Tickets)

#### Ticket XD01-217: Pilot Dedicated Silos — First Dedicated Silo Tenant Onboarding (`silo-01`)
- **Issue Key**: `XD01-217`
- **Issue Type**: `Historia`
- **Functional Requirements**:
  1. Onboard the first Tier-2 dedicated silo pilot tenant (`tenant-silo-01`) using the Helm charts and Terraform modules delivered in `XD01-215` (`deploy/helm/dedicated-silo`, `deploy/terraform/silo`).
  2. Configure tenant routing in `apps/GeoAssets.Gateway`: register tenant domain `silo-01.geoassets.internal` in `GatewayRouteCatalog` mapped to dedicated backend cluster `http://silo-01-core:8080`.
  3. Validate PostgreSQL database provisioning: apply migrations for `silo-01` isolated database with Row-Level Security enabled and tenant claim assertions.
  4. Verify that requests bearing header `X-Tenant-Domain: silo-01.geoassets.internal` are dynamically proxied strictly to `silo-01` backend with statement timeout <= 5000ms.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.Gateway/Routing/GatewayRouteCatalog.cs`
  - `apps/GeoAssets.Gateway/appsettings.json`
  - `deploy/helm/dedicated-silo/values-silo-01.yaml` (New)
  - `tests/GeoAssets.Gateway.Tests/GatewayMultiTenantSiloRoutingTests.cs`
- **Architectural Tier**: Gateway & Multi-Tenancy SaaS (`GeoAssets.Gateway`, `deploy/`)
- **Acceptance Criteria**:
  - `GatewayMultiTenantSiloRoutingTests` verifies requests with `X-Tenant-Domain: silo-01.geoassets.internal` route exclusively to `silo-01` cluster.
  - Multi-tenant shared cluster requests remain isolated from `silo-01`.
- **Verification Commands**:
  ```bash
  dotnet build apps/GeoAssets.Gateway/GeoAssets.Gateway.csproj --no-incremental /warnaserror
  dotnet test tests/GeoAssets.Gateway.Tests/GeoAssets.Gateway.Tests.csproj --filter "FullyQualifiedName~GatewayMultiTenantSiloRoutingTests"
  ```

#### Ticket XD01-218: Pilot Dedicated Silos — Second Dedicated Silo Tenant Onboarding (`silo-02`) & Multi-Silo Routing Validation
- **Issue Key**: `XD01-218`
- **Issue Type**: `Historia`
- **Functional Requirements**:
  1. Onboard the second Tier-2 dedicated silo tenant (`tenant-silo-02`) in `GatewayRouteCatalog` to establish a multi-silo pilot cohort.
  2. Implement domain-value-aware routing verification: confirm `XD01-289` correctly discriminates between `silo-01.geoassets.internal` and `silo-02.geoassets.internal`, routing each to its distinct backend cluster.
  3. Execute automated cross-silo penetration test: verify that a tenant token belonging to `silo-01` is strictly rejected by `silo-02` backend with HTTP 403 Forbidden.
  4. Update `docs/security/XD01-216-cross-tenant-penetration-audit.md` documenting multi-silo isolation audit results.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.Gateway/Routing/GatewayRouteCatalog.cs`
  - `deploy/helm/dedicated-silo/values-silo-02.yaml` (New)
  - `tests/GeoAssets.Gateway.Tests/GatewayMultiTenantSiloRoutingTests.cs`
  - `tests/GeoAssets.Security.Tests/MultiSiloCrossTenantPenetrationTests.cs` (New)
- **Architectural Tier**: Gateway & Multi-Tenancy SaaS (`GeoAssets.Gateway`, `GeoAssets.Security.Tests`)
- **Acceptance Criteria**:
  - Zero cross-talk between `silo-01` and `silo-02` clusters under adversarial request spoofing.
  - 100% pass rate in `MultiSiloCrossTenantPenetrationTests`.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Gateway.Tests/GeoAssets.Gateway.Tests.csproj
  dotnet test tests/GeoAssets.Security.Tests/GeoAssets.Security.Tests.csproj --filter "FullyQualifiedName~MultiSiloCrossTenantPenetrationTests"
  ```

---

### Cluster 3: Core Workflow & Attribute Schema Foundation (7 Tickets)

#### Ticket XD01-1: Core Service Order Entity Base Model and Lifecycle Status Contracts
- **Issue Key**: `XD01-1`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Define the immutable domain contracts and base records for `ServiceOrder` in `core/GeoAssets.Workflow/Models/ServiceOrder.cs`.
  2. Enforce domain invariants: unique `OrderId` (Guid), non-empty `OrderNumber` string, `TenantId` (Guid), `CreatedAt` (`DateTimeOffset`), and initial status `ServiceOrderStatus.Draft`.
  3. Establish domain event types: `ServiceOrderCreatedDomainEvent`, `ServiceOrderStatusChangedDomainEvent`.
  4. Ensure pure domain modeling with zero EF Core or PostgreSQL references.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/Models/ServiceOrder.cs`
  - `core/GeoAssets.Workflow/Models/ServiceOrderStatus.cs`
  - `core/GeoAssets.Workflow/Events/ServiceOrderDomainEvents.cs`
  - `tests/GeoAssets.Workflow.Tests/Models/ServiceOrderModelTests.cs` (New)
- **Architectural Tier**: Core Workflow Engine (`GeoAssets.Workflow`)
- **Acceptance Criteria**:
  - Instantiating a `ServiceOrder` without required invariants throws `DomainValidationException`.
  - Status transitions are guarded by immutable state validation.
- **Verification Commands**:
  ```bash
  dotnet build core/GeoAssets.Workflow/GeoAssets.Workflow.csproj --no-incremental /warnaserror
  dotnet test tests/GeoAssets.Workflow.Tests/GeoAssets.Workflow.Tests.csproj --filter "FullyQualifiedName~ServiceOrderModelTests"
  ```

#### Ticket XD01-2: Dynamic Attribute JSON Schema Validation Engine
- **Issue Key**: `XD01-2`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement `IAttributeSchemaValidator` in `core/GeoAssets.Workflow/Validation/AttributeSchemaValidator.cs` using `JsonSchema.Net` (draft 2020-12).
  2. Validate arbitrary JSON object payloads (`JsonDocument` / `JsonElement`) against JSON Schema definitions specified per `OrderType`.
  3. Return structured validation results (`SchemaValidationResult`) containing exact JSON Pointer paths (`instanceLocation`) and descriptive failure messages.
  4. Support custom format checkers: Brazilian CPF/CNPJ, coordinate bounding boxes, and phone numbers.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/Validation/IAttributeSchemaValidator.cs` (New)
  - `core/GeoAssets.Workflow/Validation/AttributeSchemaValidator.cs` (New)
  - `core/GeoAssets.Workflow/Validation/SchemaValidationResult.cs` (New)
  - `tests/GeoAssets.Workflow.Tests/Validation/AttributeSchemaValidatorTests.cs` (New)
- **Architectural Tier**: Core Workflow Engine (`GeoAssets.Workflow`)
- **Acceptance Criteria**:
  - Valid payloads evaluate to `IsValid == true` with empty error list.
  - Invalid types, missing required fields, or format violations return precise JSON pointer paths.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Workflow.Tests/GeoAssets.Workflow.Tests.csproj --filter "FullyQualifiedName~AttributeSchemaValidatorTests"
  ```

#### Ticket XD01-5: Workflow Attribute Validation Integration on Order Creation/Mutation
- **Issue Key**: `XD01-5`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Integrate `IAttributeSchemaValidator` into the service order creation and update pipelines in `core/GeoAssets.Workflow/Services/ServiceOrderService.cs`.
  2. When an order is submitted or updated, lookup the registered `OrderType`'s schema and validate `ServiceOrder.Attributes`.
  3. Reject invalid attribute mutations before persistence, throwing `InvalidOrderAttributesException` containing structured errors.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/Services/ServiceOrderService.cs`
  - `core/GeoAssets.Workflow/Exceptions/InvalidOrderAttributesException.cs` (New)
  - `tests/GeoAssets.Workflow.Tests/Services/ServiceOrderAttributeValidationPipelineTests.cs` (New)
- **Architectural Tier**: Core Workflow Engine (`GeoAssets.Workflow`)
- **Acceptance Criteria**:
  - Orders with attributes violating their `OrderType` schema are rejected prior to calling `IServiceOrderRepository.SaveAsync`.
  - Valid attributes persist without data loss.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Workflow.Tests/GeoAssets.Workflow.Tests.csproj --filter "FullyQualifiedName~ServiceOrderAttributeValidationPipelineTests"
  ```

#### Ticket XD01-6: Service Order Pre-Validation Rules and Schema Enforcement
- **Issue Key**: `XD01-6`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement extensible pre-validation rule pipeline `IServiceOrderPreValidationRule` executing before state machine transition guards.
  2. Implement rules:
     - `AssigneeRoleRule`: Verify assigned user holds the required organizational role for the target order type.
     - `GeographicBoundRule`: Verify service location falls within the tenant's licensed municipal boundaries.
     - `ScheduleWindowRule`: Verify scheduled start and end timestamps satisfy operational window constraints.
  3. Wire rules into `ServiceOrderWorkflowEngine` and expose `POST /api/serviceorders/pre-validate` endpoint.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/Rules/IServiceOrderPreValidationRule.cs` (New)
  - `core/GeoAssets.Workflow/Rules/AssigneeRoleRule.cs` (New)
  - `core/GeoAssets.Workflow/Rules/GeographicBoundRule.cs` (New)
  - `apps/GeoAssets.Server/Endpoints/ServiceOrderEndpoints.cs`
  - `tests/GeoAssets.Workflow.Tests/Rules/ServiceOrderPreValidationRuleTests.cs` (New)
- **Architectural Tier**: Core Platform & Workflow (`GeoAssets.Workflow`, `GeoAssets.Server`)
- **Acceptance Criteria**:
  - Transition requests failing any pre-validation rule return HTTP 422 Unprocessable Entity with rule violation breakdown.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Workflow.Tests/GeoAssets.Workflow.Tests.csproj --filter "FullyQualifiedName~ServiceOrderPreValidationRuleTests"
  ```

#### Ticket XD01-7: Optimistic Concurrency Control and RowVersion Token Round-Trip
- **Issue Key**: `XD01-7`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement robust optimistic concurrency control across `ServiceOrder` persistence using byte array `RowVersion` / concurrency tokens.
  2. Configure `ServiceOrderConfiguration` in `GeoAssets.Workflow.EFCore`: map `RowVersion` as `[Timestamp]` / `IsRowVersion()` in PostgreSQL with xmin system column.
  3. Detect concurrency conflicts during `SaveChangesAsync`: catch `DbUpdateConcurrencyException`, map to domain `ServiceOrderConcurrencyException`, and return HTTP 409 Conflict with current database snapshot.
  4. Ensure client round-trips concurrency tokens in HTTP `If-Match` headers.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/Exceptions/ServiceOrderConcurrencyException.cs` (New)
  - `workflow/GeoAssets.Workflow.EFCore/Configurations/ServiceOrderConfiguration.cs`
  - `workflow/GeoAssets.Workflow.EFCore/Repositories/EFServiceOrderRepository.cs`
  - `tests/GeoAssets.Workflow.EFCore.Tests/Repositories/ServiceOrderConcurrencyTests.cs` (New)
- **Architectural Tier**: Workflow Persistence (`GeoAssets.Workflow.EFCore`)
- **Acceptance Criteria**:
  - Concurrent writes with stale `RowVersion` throw `ServiceOrderConcurrencyException`.
  - Sequential writes succeed and advance `RowVersion`.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Workflow.EFCore.Tests/GeoAssets.Workflow.EFCore.Tests.csproj --filter "FullyQualifiedName~ServiceOrderConcurrencyTests"
  ```

#### Ticket XD01-23: Federated Identity Authentication (External IdPs)
- **Issue Key**: `XD01-23`
- **Issue Type**: `Historia`
- **Functional Requirements**:
  1. Extend `GeoAssets.Identity` with external federated identity provider abstraction `IFederatedIdentityProvider`.
  2. Support OpenID Connect (OIDC) token exchange for Google, Microsoft personal, and SAML 2.0 / WS-Fed corporate IdPs.
  3. Implement claim mapping pipeline: translate external IdP claims (`sub`, `iss`, `email`, `preferred_username`) to internal `AppUser` identity and tenant affiliations.
  4. Implement JIT (Just-In-Time) user provisioning with domain allow-listing per tenant organization.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Identity/Services/IFederatedIdentityProvider.cs` (New)
  - `core/GeoAssets.Identity/Services/FederatedIdentityService.cs` (New)
  - `apps/GeoAssets.Identity.EFCore/Repositories/FederatedIdentityMappingRepository.cs` (New)
  - `tests/GeoAssets.Identity.Tests/Services/FederatedIdentityServiceTests.cs` (New)
- **Architectural Tier**: Identity & Access Management (`GeoAssets.Identity`, `GeoAssets.Identity.EFCore`)
- **Acceptance Criteria**:
  - Valid external OIDC tokens authenticate and link to internal `AppUser`.
  - Unregistered email domains outside tenant allow-lists are rejected with HTTP 403 Forbidden.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Identity.Tests/GeoAssets.Identity.Tests.csproj --filter "FullyQualifiedName~FederatedIdentityServiceTests"
  ```

#### Ticket XD01-25: Organization Resource Authorization Rules & RLS Policy Alignment
- **Issue Key**: `XD01-25`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Align ASP.NET Core authorization handlers with PostgreSQL Row-Level Security policies on `geo_entity` and `ServiceOrders`.
  2. Implement `OrganizationResourceAuthorizationHandler`: evaluate active tenant grants, user permissions, and resource ownership before dispatching database queries.
  3. Ensure `TenantTransactionInterceptor` injects `SET LOCAL app.current_tenant_id` consistently across pooled connections so RLS and application-layer authorization invariants are identical.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Identity/Authorization/OrganizationResourceAuthorizationHandler.cs`
  - `providers/GeoAssets.Provider.PostgreSQL/Interceptors/TenantTransactionInterceptor.cs`
  - `tests/GeoAssets.Server.Tests/Authorization/OrganizationResourceAuthorizationTests.cs` (New)
- **Architectural Tier**: Identity & Core Platform (`GeoAssets.Identity`, `GeoAssets.Server`, `GeoAssets.Provider.PostgreSQL`)
- **Acceptance Criteria**:
  - Users cannot access resources belonging to another organization even if direct entity IDs are known.
  - Zero SQL injection or session leakage under high connection churn.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Server.Tests/GeoAssets.Server.Tests.csproj --filter "FullyQualifiedName~OrganizationResourceAuthorizationTests"
  ```

---

### Cluster 4: UI Navigation, Shell Persistence & Theme (6 Tickets)

#### Ticket XD01-72: UI Theme and Catppuccin Mocha CSS Token Bridge Styling
- **Issue Key**: `XD01-72`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Build a CSS token bridge mapping Bootstrap 5 variables to Catppuccin Mocha color tokens in `apps/GeoAssets.Shared/wwwroot/css/geoassets.css`.
  2. Define theme tokens for Surface (`#1e1e2e`), Base (`#181825`), Mantle (`#11111b`), Crust (`#11111b`), Text (`#cdd6f4`), Subtext (`#a6adc8`), Blue (`#89b4fa`), Peach (`#fab387`), and Red (`#f38ba8`).
  3. Ensure contrast ratios satisfy WCAG 2.1 AA standards (>= 4.5:1 for normal text).
  4. Implement runtime theme switching between Dark (Catppuccin Mocha), Light (Catppuccin Latte), and System Preference.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.Shared/wwwroot/css/geoassets.css`
  - `apps/GeoAssets.Shared/Services/ThemeService.cs` (New)
  - `tests/GeoAssets.Shared.Tests/Services/ThemeServiceTests.cs` (New)
- **Architectural Tier**: UI & Front-end (`GeoAssets.Shared`)
- **Acceptance Criteria**:
  - Theme changes toggle CSS class on root `<html>` element and persist to local storage.
  - All UI elements maintain WCAG AA color contrast.
- **Verification Commands**:
  ```bash
  dotnet build apps/GeoAssets.Shared/GeoAssets.Shared.csproj --no-incremental /warnaserror
  dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --filter "FullyQualifiedName~ThemeServiceTests"
  ```

#### Ticket XD01-77: Left-Nav-Menu Data-Driven Item Tree Configuration
- **Issue Key**: `XD01-77`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Define navigation model hierarchy: `NavMenuItem`, `NavMenuCategory`, `NavMenuDivider`.
  2. Support metadata: route URL, icon class, badge counter, required permission code, and sort order.
  3. Implement JSON and C# configuration builders allowing microservices to supply navigation fragments at application boot.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Core/Navigation/NavMenuItem.cs` (New)
  - `core/GeoAssets.Core/Navigation/NavMenuCategory.cs` (New)
  - `core/GeoAssets.Core/Navigation/NavMenuConfiguration.cs` (New)
  - `tests/GeoAssets.Core.Tests/Navigation/NavMenuConfigurationTests.cs` (New)
- **Architectural Tier**: Core Platform (`GeoAssets.Core`)
- **Acceptance Criteria**:
  - Navigation tree organizes categories, items, and dividers in deterministic sort order.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Core.Tests/GeoAssets.Core.Tests.csproj --filter "FullyQualifiedName~NavMenuConfigurationTests"
  ```

#### Ticket XD01-78: Dynamic Navigation Registration & Permission-Gated Rendering
- **Issue Key**: `XD01-78`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement `INavigationRegistry` discovering registered navigation providers across loaded modules.
  2. Implement permission filtering: hide navigation items and categories if the authenticated user lacks the required `AppPermission`.
  3. Update `NavMenu.razor` in `apps/GeoAssets.Shared` to render the dynamic tree reactively.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Core/Navigation/INavigationRegistry.cs` (New)
  - `core/GeoAssets.Core/Navigation/NavigationRegistry.cs` (New)
  - `apps/GeoAssets.Shared/Components/Layout/NavMenu.razor`
  - `apps/GeoAssets.Shared/Components/Layout/NavMenu.razor.cs`
  - `tests/GeoAssets.Shared.Tests/Components/Layout/NavMenuTests.cs` (New)
- **Architectural Tier**: UI & Front-end (`GeoAssets.Shared`, `GeoAssets.Core`)
- **Acceptance Criteria**:
  - Navigation items requiring permissions the user lacks are completely omitted from the DOM.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --filter "FullyQualifiedName~NavMenuTests"
  ```

#### Ticket XD01-79: Navigation System Parent Epic: Extensible Left-Nav-Menu Architecture
- **Issue Key**: `XD01-79`
- **Issue Type**: `Epic`
- **Functional Requirements**:
  1. Umbrella epic governing `XD01-77`, `XD01-78`, `XD01-80`, `XD01-81`, `XD01-82`, `XD01-84`, and `XD01-85`.
  2. Validate complete end-to-end integration of the extensible left-nav-menu across both Blazor WebAssembly and .NET MAUI shell.
  3. Verify clean architectural decoupling: core defines contracts, UI consumes reactively, microservices inject items via DI.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.Shared/Components/Layout/NavMenu.razor`
  - `apps/GeoAssets.Web/Program.cs`
  - `apps/GeoAssets.MAUI/MauiProgram.cs`
  - `tests/GeoAssets.Shared.Tests/Integration/NavigationIntegrationTests.cs` (New)
- **Architectural Tier**: Cross-Cutting Epic
- **Acceptance Criteria**:
  - All child navigation tickets verified and passing in Web and MAUI hosts.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --filter "FullyQualifiedName~NavigationIntegrationTests"
  ```

#### Ticket XD01-86: UI Shell Persistence Across Navigation and Route Transitions
- **Issue Key**: `XD01-86`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement persistent layout shell (`MainLayout.razor`) that maintains active Leaflet/MapLibre map instance, WebGL contexts, and provider subscriptions alive when navigating to admin or service order routes.
  2. Prevent map re-initialization and tile re-fetching on route changes; use CSS visibility toggling (`display: none` / `block`) and trigger `IMapInterop.InvalidateSizeAsync` when returning to `/`.
  3. Preserve user viewport, zoom level, and active layer selections across navigations.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.Shared/Components/Layout/MainLayout.razor`
  - `apps/GeoAssets.Shared/Components/Layout/MainLayout.razor.cs`
  - `apps/GeoAssets.Shared/Components/Map/MapWorkspace.razor`
  - `tests/GeoAssets.Shared.Tests/Components/Layout/MainLayoutPersistenceTests.cs` (New)
- **Architectural Tier**: UI & Front-end (`GeoAssets.Shared`)
- **Acceptance Criteria**:
  - Navigating between `/` and `/admin/users` does not destroy or recreate the Leaflet JS map object.
  - `MapWorkspace` re-adjusts viewport size cleanly upon re-display.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --filter "FullyQualifiedName~MainLayoutPersistenceTests"
  ```

#### Ticket XD01-137: Project Session UI Dirty Tracking and Save/Discard Dialogs
- **Issue Key**: `XD01-137`
- **Issue Type**: `Historia`
- **Functional Requirements**:
  1. Implement dirty tracking in `IProjectSessionService`: track asset additions, geometry mutations, layer styling edits, and deletion flags against the loaded project baseline.
  2. Implement `DirtyStateGuard`: intercept route navigation, window close, and logout events when changes are unsaved.
  3. Render modal dialog: "You have unsaved changes. Save / Discard / Cancel".
  4. On "Save", invoke `IProjectSessionService.SaveAsync()`; on "Discard", revert dirty state; on "Cancel", abort navigation.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Core/Interfaces/IProjectSessionService.cs`
  - `apps/GeoAssets.Shared/Services/ProjectSessionService.cs`
  - `apps/GeoAssets.Shared/Components/Projects/UnsavedChangesDialog.razor` (New)
  - `apps/GeoAssets.Shared/Components/Projects/UnsavedChangesDialog.razor.cs` (New)
  - `tests/GeoAssets.Shared.Tests/Components/Projects/UnsavedChangesDialogTests.cs` (New)
- **Architectural Tier**: UI & Front-end (`GeoAssets.Shared`, `GeoAssets.Core`)
- **Acceptance Criteria**:
  - Unsaved mutations set `IsDirty == true`.
  - Closing dirty session triggers modal confirmation; discarding changes restores baseline.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --filter "FullyQualifiedName~UnsavedChangesDialogTests"
  ```

---

### Cluster 5: Map Styling, Vector Rendering & Topological Linking (10 Tickets)

#### Ticket XD01-96: Layer & Style REST Services and Dynamic Symbology Endpoints
- **Issue Key**: `XD01-96`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement REST endpoints in `apps/GeoAssets.Server`:
     - `GET /api/layers`: Return all configured layers for the tenant.
     - `POST /api/layers`: Create a new layer definition.
     - `PUT /api/layers/{id}/style`: Update symbology, colors, stroke width, and icon markers.
     - `POST /api/layers/{id}/rules`: Add rule-based styling predicates.
  2. Protect endpoints with `layers:read` and `layers:write` permission codes.
  3. Persist styles in PostgreSQL using `LayerConfiguration` and `LayerRuleConfiguration`.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.Server/Endpoints/LayerEndpoints.cs` (New)
  - `providers/GeoAssets.Provider.PostgreSQL/Configurations/LayerConfiguration.cs`
  - `tests/GeoAssets.Server.Tests/Endpoints/LayerEndpointsTests.cs` (New)
- **Architectural Tier**: Core Platform Server (`GeoAssets.Server`, `GeoAssets.Provider.PostgreSQL`)
- **Acceptance Criteria**:
  - Layer CRUD endpoints round-trip valid JSON styling definitions.
  - Unauthorized callers receive HTTP 403 Forbidden.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Server.Tests/GeoAssets.Server.Tests.csproj --filter "FullyQualifiedName~LayerEndpointsTests"
  ```

#### Ticket XD01-108: Map Styling Parent Epic: Type-Based & Rule-Based Styling Engine
- **Issue Key**: `XD01-108`
- **Issue Type**: `Epic`
- **Functional Requirements**:
  1. Umbrella epic governing `XD01-109`, `XD01-110`, `XD01-111`, `XD01-112`, `XD01-113`, and `XD01-114`.
  2. Verify end-to-end type-based and attribute-rule-based styling resolution: `LayerResolver` maps `GeoFeature` properties to CSS/Leaflet styling instructions.
  3. Validate fallback rendering when no rule matches.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Core/Services/LayerResolver.cs`
  - `apps/GeoAssets.Shared/Components/Map/MapContainer.razor.cs`
  - `tests/GeoAssets.Core.Tests/Services/LayerResolverTests.cs`
- **Architectural Tier**: Cross-Cutting Epic
- **Acceptance Criteria**:
  - Complete integration verified with passing tests across `GeoAssets.Core.Tests` and `GeoAssets.Shared.Tests`.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Core.Tests/GeoAssets.Core.Tests.csproj --filter "FullyQualifiedName~LayerResolverTests"
  ```

#### Ticket XD01-120: Topological Auto-Linking Multi-Candidate "Connect to..." Picker UI
- **Issue Key**: `XD01-120`
- **Issue Type**: `Historia`
- **Functional Requirements**:
  1. When a user creates or snaps a new topological asset (e.g. pole, transformer, valve) and 2 or more compatible connection candidates exist within snapping tolerance, display the "Connect to..." candidate picker dialog.
  2. Render candidate list showing candidate asset type, identifier, geodetic distance in meters, and electrical/hydraulic phase.
  3. On candidate selection, construct `TopoEdge` linking source and target features and persist to active provider.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.Shared/Components/Map/CandidatePickerModal.razor` (New)
  - `apps/GeoAssets.Shared/Components/Map/CandidatePickerModal.razor.cs` (New)
  - `apps/GeoAssets.Shared/Components/Map/CandidatePickerState.cs`
  - `tests/GeoAssets.Shared.Tests/Components/Map/CandidatePickerModalTests.cs` (New)
- **Architectural Tier**: UI & Front-end (`GeoAssets.Shared`)
- **Acceptance Criteria**:
  - Candidate picker displays candidates sorted by proximity.
  - Selecting candidate creates valid `TopoEdge` without page reload.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --filter "FullyQualifiedName~CandidatePickerModalTests"
  ```

#### Ticket XD01-151: Topological Candidate Picker Interaction Design and State Management
- **Issue Key**: `XD01-151`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement `CandidatePickerState` managing lifecycle of candidate selection: `Open()`, `SelectCandidate()`, `Dismiss()`, `Reset()`.
  2. Implement reactive event `OnStateChanged` notifying UI components when candidates are discovered or selected.
  3. Handle edge cases: zero candidates found (auto-dismiss), single candidate found (auto-link without modal), user cancellation (save feature without edge).
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.Shared/Components/Map/CandidatePickerState.cs`
  - `tests/GeoAssets.Shared.Tests/Components/Map/CandidatePickerStateTests.cs` (New)
- **Architectural Tier**: UI & Front-end (`GeoAssets.Shared`)
- **Acceptance Criteria**:
  - 100% branch coverage on candidate state transitions.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --filter "FullyQualifiedName~CandidatePickerStateTests"
  ```

#### Ticket XD01-153: TopoEdge Spatial Proximity Search and Snapping Threshold Calculation
- **Issue Key**: `XD01-153`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement `ITopologicalSnappingService` in `core/GeoAssets.Core/Services/TopologicalSnappingService.cs`.
  2. Calculate snapping radius dynamically based on map zoom level and utility domain standards (e.g., 5.0m for aerial electrical poles, 1.5m for underground telecom ducts).
  3. Query active `IAssetProvider` via `GetNearbyAsync` to retrieve candidate features within the calculated envelope.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Core/Interfaces/ITopologicalSnappingService.cs` (New)
  - `core/GeoAssets.Core/Services/TopologicalSnappingService.cs` (New)
  - `tests/GeoAssets.Core.Tests/Services/TopologicalSnappingServiceTests.cs` (New)
- **Architectural Tier**: Core Platform (`GeoAssets.Core`)
- **Acceptance Criteria**:
  - Returns only features whose spatial distance is <= snapping radius.
  - Filters out incompatible asset classes (e.g., cannot connect water valve to electric transformer).
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Core.Tests/GeoAssets.Core.Tests.csproj --filter "FullyQualifiedName~TopologicalSnappingServiceTests"
  ```

#### Ticket XD01-154: TopoEdge Database Persistence and Foreign Key Constraints
- **Issue Key**: `XD01-154`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement EF Core persistence for `TopoEdge` entities in `providers/GeoAssets.Provider.PostgreSQL`.
  2. Configure table `topo_edges`: `EdgeId` (PK), `SourceFeatureId` (FK -> `geo_entity`), `TargetFeatureId` (FK -> `geo_entity`), `EdgeType`, `TenantId`, `CustomAttributes` (`jsonb`), `RowVersion`.
  3. Enforce foreign key constraints with `ON DELETE CASCADE` or `RESTRICT` depending on edge classification.
  4. Enable Row-Level Security on `topo_edges` enforcing tenant isolation.
- **Affected Projects & File Targets**:
  - `providers/GeoAssets.Provider.PostgreSQL/Configurations/TopoEdgeConfiguration.cs` (New)
  - `providers/GeoAssets.Provider.PostgreSQL/PostgresAssetDbContext.cs`
  - `providers/GeoAssets.Provider.PostgreSQL/Migrations/AddTopoEdgesTable.cs` (New migration)
  - `tests/GeoAssets.Provider.PostgreSQL.Tests/Repositories/TopoEdgePersistenceTests.cs` (New)
- **Architectural Tier**: Core Persistence (`GeoAssets.Provider.PostgreSQL`)
- **Acceptance Criteria**:
  - TopoEdge records insert, query, and delete cleanly with tenant RLS isolation.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Provider.PostgreSQL.Tests/GeoAssets.Provider.PostgreSQL.Tests.csproj --filter "FullyQualifiedName~TopoEdgePersistenceTests"
  ```

#### Ticket XD01-155: TopoEdge Validation Rules: Cycle Prevention, Asset Compatibility & Directionality
- **Issue Key**: `XD01-155`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement `ITopoEdgeValidator` executing validation rules before edge creation:
     - `CompatibilityRule`: Validate voltage level or pipe diameter compatibility between connected nodes.
     - `CyclePreventionRule`: For directed radial distribution networks, verify that adding the edge does not introduce a directed cycle (using `TopoGraph.HasCycle`).
     - `SelfConnectionRule`: Reject edges where `SourceFeatureId == TargetFeatureId`.
  2. Throw `InvalidTopologyException` when a rule is violated.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Core/Validation/ITopoEdgeValidator.cs` (New)
  - `core/GeoAssets.Core/Validation/TopoEdgeValidator.cs` (New)
  - `tests/GeoAssets.Core.Tests/Validation/TopoEdgeValidatorTests.cs` (New)
- **Architectural Tier**: Core Platform (`GeoAssets.Core`)
- **Acceptance Criteria**:
  - Attempting to connect incompatible assets or introduce illegal cycles is blocked with informative validation errors.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Core.Tests/GeoAssets.Core.Tests.csproj --filter "FullyQualifiedName~TopoEdgeValidatorTests"
  ```

#### Ticket XD01-158: Provider Decorator Unwrapping and Caching Pipeline
- **Issue Key**: `XD01-158`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Extend `IProviderDecorator` interface allowing traversal through nested decorator chains: `ObservableAssetProvider` -> `ValidatingAssetProvider` -> `ActiveAssetProvider` -> `PostgresAssetProvider`.
  2. Implement `AssetProviderExtensions.UnwrapToConcrete<T>()`: iteratively unwrap decorators to locate the underlying concrete provider implementation.
  3. Implement caching pipeline decorator `CachingAssetProvider`: cache feature lookups by ID and spatial bounding boxes using sliding expiration, invalidating cache on `FeatureAdded`, `FeatureUpdated`, or `FeatureDeleted` events.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Core/Interfaces/IProviderDecorator.cs`
  - `core/GeoAssets.Core/Providers/AssetProviderExtensions.cs`
  - `core/GeoAssets.Core/Providers/CachingAssetProvider.cs` (New)
  - `tests/GeoAssets.Core.Tests/Providers/CachingAssetProviderTests.cs` (New)
- **Architectural Tier**: Core Platform (`GeoAssets.Core`)
- **Acceptance Criteria**:
  - `UnwrapToConcrete` resolves underlying target regardless of decorator depth.
  - Cached queries avoid redundant provider calls; cache invalidates on feature mutations.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Core.Tests/GeoAssets.Core.Tests.csproj --filter "FullyQualifiedName~CachingAssetProviderTests"
  ```

#### Ticket XD01-168: Vector Tile Caching and Client-Side Feature Invalidation
- **Issue Key**: `XD01-168`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement client-side spatial vector tile cache in `apps/GeoAssets.Shared/Services/VectorTileCacheService.cs`.
  2. Cache GeoJSON vector feature collections by zoom/x/y tile key using IndexedDB / LocalStorage.
  3. Subscribe to real-time provider change events: invalidate and refresh affected tiles immediately when an asset within that tile bounds is mutated.
  4. Enforce cache eviction policy (LRU, max 100MB).
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.Shared/Services/IVectorTileCacheService.cs` (New)
  - `apps/GeoAssets.Shared/Services/VectorTileCacheService.cs` (New)
  - `tests/GeoAssets.Shared.Tests/Services/VectorTileCacheServiceTests.cs` (New)
- **Architectural Tier**: UI & Front-end (`GeoAssets.Shared`)
- **Acceptance Criteria**:
  - Previously loaded tiles render instantly from client cache.
  - Asset updates trigger immediate invalidation of intersecting tile keys.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --filter "FullyQualifiedName~VectorTileCacheServiceTests"
  ```

#### Ticket XD01-169: Viewport Debounce and Supersession of In-Flight Bounding-Box Queries
- **Issue Key**: `XD01-169`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Optimize map pan and zoom responsiveness in `apps/GeoAssets.Shared/Components/Map/MapContainer.razor.cs`.
  2. Debounce `OnViewportChangedFromJs` calls by 150ms using `System.TimeProvider`.
  3. Link in-flight bounding-box queries (`GetInBoundsAsync`) to a `CancellationTokenSource`: cancel the active cancellation token immediately when a newer viewport pan arrives, preventing out-of-order rendering and redundant network I/O.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.Shared/Components/Map/MapContainer.razor.cs`
  - `tests/GeoAssets.Shared.Tests/Components/Map/ViewportDebounceTests.cs` (New)
- **Architectural Tier**: UI & Front-end (`GeoAssets.Shared`)
- **Acceptance Criteria**:
  - Rapid panning fires only one query after motion halts.
  - Superseded queries abort cleanly via `OperationCanceledException` without error logs.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --filter "FullyQualifiedName~ViewportDebounceTests"
  ```

---

### Cluster 6: Performance, Viewport Rendering & EF Core Optimizations (6 Tickets)

#### Ticket XD01-122: EF Core LINQ Query Optimization: CS9175 Collection-Expression in Expression-Trees
- **Issue Key**: `XD01-122`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Audit EF Core LINQ expressions across `providers/GeoAssets.Provider.PostgreSQL` and `ipbilling/GeoAssets.IpBilling.EFCore`.
  2. Resolve Roslyn warning/error `CS9175` (collection expressions cannot be used inside expression trees translated by EF Core).
  3. Replace inline C# 12 collection expressions (`[a, b]`) in `.Where(...)` / `.Select(...)` expression trees with explicit `.ToList()` or array variables evaluated outside the expression tree.
  4. Ensure all queries translate to valid PostgreSQL SQL without client-side evaluation fallback.
- **Affected Projects & File Targets**:
  - `providers/GeoAssets.Provider.PostgreSQL/Repositories/PostgresAssetRepository.cs`
  - `ipbilling/GeoAssets.IpBilling.EFCore/Repositories/EFIpBillingRepository.cs`
  - `tests/GeoAssets.Provider.PostgreSQL.Tests/Repositories/LinqExpressionOptimizationTests.cs` (New)
- **Architectural Tier**: Persistence (`GeoAssets.Provider.PostgreSQL`, `GeoAssets.IpBilling.EFCore`)
- **Acceptance Criteria**:
  - Zero `CS9175` warnings during compilation under `/warnaserror`.
  - EF Core queries generate parameterized SQL without client-side memory evaluation.
- **Verification Commands**:
  ```bash
  dotnet build providers/GeoAssets.Provider.PostgreSQL/GeoAssets.Provider.PostgreSQL.csproj --no-incremental /warnaserror
  dotnet test tests/GeoAssets.Provider.PostgreSQL.Tests/GeoAssets.Provider.PostgreSQL.Tests.csproj --filter "FullyQualifiedName~LinqExpressionOptimizationTests"
  ```

#### Ticket XD01-126: Repository Modernization: Complete Retirement of InMemory Defaults in Favor of EF Core Test Doubles
- **Issue Key**: `XD01-126`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Complete the decommissioning of legacy in-memory repository classes across `GeoAssets.Workflow` and `GeoAssets.Identity`.
  2. Migrate all unit tests to use SQLite in-memory databases (`Microsoft.Data.Sqlite`) or verified test doubles in `GeoAssets.Workflow.TestKit`.
  3. Verify that zero production DI extension methods register in-memory repositories by default; throw descriptive configuration exception if connection strings are missing.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/ServiceCollectionExtensions.cs`
  - `core/GeoAssets.Identity/IdentityServiceCollectionExtensions.cs`
  - `tests/GeoAssets.Workflow.TestKit/TestDatabaseFixture.cs`
  - `tests/GeoAssets.Workflow.Tests/Repositories/RepositoryDecommissioningTests.cs` (New)
- **Architectural Tier**: Workflow & Identity Architecture
- **Acceptance Criteria**:
  - All repository implementations execute against real relational providers or strict TestKit mocks.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Workflow.Tests/GeoAssets.Workflow.Tests.csproj
  ```

#### Ticket XD01-149: Project Capability Gating and Feature Authorization UI
- **Issue Key**: `XD01-149`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement capability-aware gating in `apps/GeoAssets.Shared`: check `IProjectSessionService.Capabilities` before rendering advanced GIS features (e.g., topology auto-linking, WFS export, bulk attribute editing).
  2. Display read-only badges or disable editing controls when the active project is opened in View-Only or Restricted mode.
  3. Ensure server API endpoints reject mutations on restricted projects with HTTP 403 Forbidden.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.Shared/Components/Projects/ProjectCapabilityGate.razor` (New)
  - `apps/GeoAssets.Shared/Components/Map/DrawToolbar.razor`
  - `tests/GeoAssets.Shared.Tests/Components/Projects/ProjectCapabilityGateTests.cs` (New)
- **Architectural Tier**: UI & Front-end (`GeoAssets.Shared`)
- **Acceptance Criteria**:
  - Restricted projects render UI in read-only mode with toolbars disabled.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --filter "FullyQualifiedName~ProjectCapabilityGateTests"
  ```

#### Ticket XD01-165: Viewport Rendering Dispatch Service with Cancellation Tokens
- **Issue Key**: `XD01-165`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement `IFeatureRenderPipeline` in `core/GeoAssets.Core/Services/FeatureRenderPipeline.cs`.
  2. Dispatch rendering tasks asynchronously: unwrap provider via `IProviderDecorator`, execute spatial bounds query, project geometry coordinates to Web Mercator / Leaflet coordinates, and forward chunks to JS interop.
  3. Propagate `CancellationToken` across all pipeline stages, guaranteeing immediate halt on cancellation.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Core/Interfaces/IFeatureRenderPipeline.cs`
  - `core/GeoAssets.Core/Services/FeatureRenderPipeline.cs`
  - `tests/GeoAssets.Core.Tests/Services/FeatureRenderPipelineTests.cs`
- **Architectural Tier**: Core Platform (`GeoAssets.Core`)
- **Acceptance Criteria**:
  - Pipeline renders features progressively without blocking the Blazor WebAssembly UI thread.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Core.Tests/GeoAssets.Core.Tests.csproj --filter "FullyQualifiedName~FeatureRenderPipelineTests"
  ```

#### Ticket XD01-166: Progressive Chunk-Based Vector Rendering and `requestAnimationFrame` Batching
- **Issue Key**: `XD01-166`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Optimize large feature set rendering in `apps/GeoAssets.Shared/wwwroot/js/geoassets.js`.
  2. Split GeoJSON feature arrays into chunks of 250 features; schedule chunk rendering across consecutive animation frames via `window.requestAnimationFrame`.
  3. Measure frame render time: if frame duration exceeds 16ms, yield to browser layout engine to maintain smooth 60 FPS scrolling and zooming.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.Shared/wwwroot/js/geoassets.js`
  - `tests/GeoAssets.Shared.Tests/Interop/JsRenderPerformanceTests.cs` (New)
- **Architectural Tier**: UI & Front-end (`GeoAssets.Shared`)
- **Acceptance Criteria**:
  - Rendering 5,000 vector features produces zero UI freezing or unresponsive script warnings.
- **Verification Commands**:
  ```bash
  dotnet build apps/GeoAssets.Shared/GeoAssets.Shared.csproj --no-incremental /warnaserror
  dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --filter "FullyQualifiedName~JsRenderPerformanceTests"
  ```

#### Ticket XD01-167: Spatial Index Query Optimization and PostGIS `ST_DWithin` Benchmarking
- **Issue Key**: `XD01-167`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Optimize PostGIS spatial index performance in `providers/GeoAssets.Provider.PostgreSQL`.
  2. Benchmark `ST_DWithin` queries against `geom` column with GiST indexes (`CREATE INDEX idx_geo_entity_geom ON geo_entity USING GIST (geom)`).
  3. Tune query plans for mixed spatial + tenant queries: add composite GiST + btree index (`USING GIST (geom, tenant_id)` via `btree_gist` extension).
  4. Document query latency baselines (< 10ms for 50,000 entities in 500m radius).
- **Affected Projects & File Targets**:
  - `providers/GeoAssets.Provider.PostgreSQL/PostgresAssetDbContext.cs`
  - `providers/GeoAssets.Provider.PostgreSQL/Migrations/AddBtreeGistSpatialIndexes.cs` (New migration)
  - `tests/GeoAssets.Provider.PostgreSQL.Tests/Performance/SpatialQueryBenchmarkTests.cs` (New)
- **Architectural Tier**: Core Persistence (`GeoAssets.Provider.PostgreSQL`)
- **Acceptance Criteria**:
  - Spatial queries execute with index scans (`Index Scan using idx_geo_entity_geom`), avoiding sequential scans.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Provider.PostgreSQL.Tests/GeoAssets.Provider.PostgreSQL.Tests.csproj --filter "FullyQualifiedName~SpatialQueryBenchmarkTests"
  ```

---

### Cluster 7: Project Subsystem & Parent Epics (7 Tickets)

#### Ticket XD01-136: Project Subsystem Parent Epic: Versioned JSON Project Domain Model & Persistence
- **Issue Key**: `XD01-136`
- **Issue Type**: `Epic`
- **Functional Requirements**:
  1. Umbrella epic governing `XD01-138`, `XD01-139`, `XD01-140`, `XD01-141`, `XD01-142`, `XD01-143`, `XD01-144`, `XD01-145`, `XD01-146`, `XD01-147`, `XD01-148`, and `XD01-150`.
  2. Validate complete project lifecycle: creation, JSON export/import, EF Core persistence in `ProjectDbContext`, dirty autosaving, and cross-organization access control.
- **Affected Projects & File Targets**:
  - `projects/GeoAssets.Projects.EFCore/ProjectDbContext.cs`
  - `core/GeoAssets.Core/Models/Project.cs`
  - `tests/GeoAssets.Projects.EFCore.Tests/ProjectPersistenceIntegrationTests.cs` (New)
- **Architectural Tier**: Cross-Cutting Epic
- **Acceptance Criteria**:
  - All project management child tickets verified and passing.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Projects.EFCore.Tests/GeoAssets.Projects.EFCore.Tests.csproj
  ```

#### Ticket XD01-175: IP Billing (`GEOS_FAT_IP`) Parent Epic: Public Lighting Vertical Microservice
- **Issue Key**: `XD01-175`
- **Issue Type**: `Epic`
- **Functional Requirements**:
  1. Umbrella epic governing `XD01-176` through `XD01-190`.
  2. Verify end-to-end public lighting billing workflows:
     - Survey upload (*Levantamento*), calculation engine, and memory of calculation.
     - Field audit (*Fiscalização*), discrepancy scoring, and municipal challenge.
     - Concessionaire notification (*Ofício*), lamp swap detection, and recalculation.
     - Double-entry municipal billing ledger (*Faturamento*).
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.IpBilling.Server/`
  - `core/GeoAssets.IpBilling/`
  - `ipbilling/GeoAssets.IpBilling.EFCore/`
  - `tests/GeoAssets.IpBilling.Tests/`
  - `tests/GeoAssets.IpBilling.EFCore.Tests/`
- **Architectural Tier**: Cross-Cutting Epic
- **Acceptance Criteria**:
  - 100% test pass rate across all 157 IP billing tests.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.IpBilling.Tests/GeoAssets.IpBilling.Tests.csproj
  dotnet test tests/GeoAssets.IpBilling.EFCore.Tests/GeoAssets.IpBilling.EFCore.Tests.csproj
  ```

#### Ticket XD01-194: Tier-1 Concessionaires Parent Epic: Large Electric & Water Utility Field Operations
- **Issue Key**: `XD01-194`
- **Issue Type**: `Epic`
- **Functional Requirements**:
  1. Umbrella epic governing field service management for large utilities (ANEEL electric concessionaires and ANA water/sanitation operators).
  2. Verify integration of ANEEL PRODIST continuity indicators (DIC/FIC/DMIC/DICRI), ANEEL REN 1.000 SLA clocks, and Sabesp ICERP repaving workflows.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Domain.Electrical/`
  - `core/GeoAssets.Domain.Sanitation/`
  - `apps/GeoAssets.FieldService.Server/`
  - `tests/GeoAssets.Domain.Electrical.Tests/`
  - `tests/GeoAssets.Domain.Sanitation.Tests/`
- **Architectural Tier**: Cross-Cutting Epic
- **Acceptance Criteria**:
  - All electrical and sanitation regulatory calculations pass validation suites.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Domain.Electrical.Tests/GeoAssets.Domain.Electrical.Tests.csproj
  dotnet test tests/GeoAssets.Domain.Sanitation.Tests/GeoAssets.Domain.Sanitation.Tests.csproj
  ```

#### Ticket XD01-195: Engineering Contractors Parent Epic: Contractor US Ledger & *Espelho de Medição*
- **Issue Key**: `XD01-195`
- **Issue Type**: `Epic`
- **Functional Requirements**:
  1. Umbrella epic governing contractor remuneration workflows: Unit of Service (US) catalog, work breakdown structures, photo evidence gating, and immutable ledger posting.
  2. Validate automated generation of *Espelho de Medição* dossier linking BoQ items, photos, and digital sign-offs.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/Contractors/`
  - `apps/GeoAssets.FieldService.Server/Endpoints/ContractorUsLedgerEndpoints.cs`
  - `tests/GeoAssets.Workflow.Tests/Contractors/`
- **Architectural Tier**: Cross-Cutting Epic
- **Acceptance Criteria**:
  - Complete contractor ledger and dossier generation verified with zero discrepancies.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Workflow.Tests/GeoAssets.Workflow.Tests.csproj --filter "FullyQualifiedName~Contractors"
  ```

#### Ticket XD01-196: Contractor Measurement Parent Epic: Work Breakdown & Physical Inspection Verification
- **Issue Key**: `XD01-196`
- **Issue Type**: `Epic`
- **Functional Requirements**:
  1. Umbrella epic governing physical inspection verification for contractor work.
  2. Verify single-DB ACID supervisor inspection sign-off commit (`FieldWorkInspectionRestApiExtensions`), ensuring order validation, contractor ledger posting, and authoritative asset geometry updates execute in a single database transaction.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/Services/InspectionCommitService.cs`
  - `tests/GeoAssets.Workflow.EFCore.Tests/Inspection/InspectionCommitTests.cs`
- **Architectural Tier**: Cross-Cutting Epic
- **Acceptance Criteria**:
  - ACID commit guarantees all changes persist or rollback together cleanly.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Workflow.EFCore.Tests/GeoAssets.Workflow.EFCore.Tests.csproj --filter "FullyQualifiedName~InspectionCommitTests"
  ```

#### Ticket XD01-197: Neutral Infrastructure Operators Parent Epic: Decreto 12.068/2024 "Poste Justo"
- **Issue Key**: `XD01-197`
- **Issue Type**: `Epic`
- **Functional Requirements**:
  1. Umbrella epic governing joint-use pole infrastructure under Brazilian regulatory framework (Decreto 12.068/2024, ANEEL/ANATEL Joint Resolution).
  2. Validate 6-slot attachment matrix, clandestine cable flagging, 30-day notice generation, corridor cleanup drives, and Poste Justo rental billing.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Domain.NeutralPole/`
  - `tests/GeoAssets.Domain.NeutralPole.Tests/`
- **Architectural Tier**: Cross-Cutting Epic
- **Acceptance Criteria**:
  - All neutral pole domain rules, slot models, and notice generators pass tests.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Domain.NeutralPole.Tests/GeoAssets.Domain.NeutralPole.Tests.csproj
  ```

#### Ticket XD01-198: Regional ISPs Parent Epic: FTTH Drop Installation & Optical Network Operations
- **Issue Key**: `XD01-198`
- **Issue Type**: `Epic`
- **Functional Requirements**:
  1. Umbrella epic governing FTTH installation and optical maintenance for regional ISPs.
  2. Validate drop-install state machine, nearest-CTO assignment, OPM optical power budget verification, CTO occupancy heatmaps, and OTDR .SOR trace fault dispatch.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Core/Isp/`
  - `apps/GeoAssets.FieldService.Server/`
  - `tests/GeoAssets.Core.Tests/Isp/`
- **Architectural Tier**: Cross-Cutting Epic
- **Acceptance Criteria**:
  - 100% pass rate across optical calculations, parser, and ISP field endpoints.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Core.Tests/GeoAssets.Core.Tests.csproj --filter "FullyQualifiedName~Isp"
  ```

---

### Cluster 8: Field Service Order Engine & Sync Extensions (7 Tickets)

#### Ticket XD01-225: Field Service SLA Alerts, Statutory Deadline Monitoring & Warning Webhooks
- **Issue Key**: `XD01-225`
- **Issue Type**: `Historia`
- **Functional Requirements**:
  1. Implement proactive SLA breach warning engine in `core/GeoAssets.Workflow/Services/SlaWarningEngine.cs`.
  2. Monitor statutory deadlines (e.g., ANEEL emergency restoration window, ANA sewer overflow cutoff): trigger warning events when elapsed time reaches 75% and 90% of statutory limit.
  3. Dispatch webhook notifications (`SlaBreachWarningWebhook`) to dispatch supervisors and field crew mobile clients.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/Services/ISlaWarningEngine.cs` (New)
  - `core/GeoAssets.Workflow/Services/SlaWarningEngine.cs` (New)
  - `core/GeoAssets.Workflow/Events/SlaBreachWarningEvent.cs` (New)
  - `tests/GeoAssets.Workflow.Tests/Services/SlaWarningEngineTests.cs` (New)
- **Architectural Tier**: Field Service Workflow (`GeoAssets.Workflow`)
- **Acceptance Criteria**:
  - Orders approaching deadlines trigger warning events deterministically.
  - Resolved orders cancel pending SLA alerts.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Workflow.Tests/GeoAssets.Workflow.Tests.csproj --filter "FullyQualifiedName~SlaWarningEngineTests"
  ```

#### Ticket XD01-227: Geofence Calibration: Configurable Radius, GPS Jitter Damping & Audit Logging
- **Issue Key**: `XD01-227`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Enhance arrival geofence validation (`ST_DWithin`) in `core/GeoAssets.Workflow/Services/GeofenceService.cs`.
  2. Make geofence arrival radius configurable per tenant and work type (default 30m; expandable up to 100m for rural power lines).
  3. Implement GPS jitter damping: require 3 consecutive GPS fixes within the boundary or a dilution-of-precision (DOP) <= 3.0 before confirming `Arrived` state transition.
  4. Record immutable audit log entry capturing crew coordinate, asset coordinate, and calculated geodetic delta.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/Services/GeofenceService.cs`
  - `core/GeoAssets.Workflow/Models/GeofenceAuditEntry.cs` (New)
  - `tests/GeoAssets.Workflow.Tests/Services/GeofenceServiceTests.cs` (New)
- **Architectural Tier**: Field Service Workflow (`GeoAssets.Workflow`)
- **Acceptance Criteria**:
  - Spurious GPS jumps outside geofence do not prematurely trigger arrival.
  - Valid arrival logs coordinate deltas accurately.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Workflow.Tests/GeoAssets.Workflow.Tests.csproj --filter "FullyQualifiedName~GeofenceServiceTests"
  ```

#### Ticket XD01-231: Sub-Order Dependency Orchestration & Cascading State Transitions
- **Issue Key**: `XD01-231`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement hierarchical sub-order dependency graph in `core/GeoAssets.Workflow/Services/OrderDependencyOrchestrator.cs`.
  2. Support parent-child relationships (e.g. main trench excavation order spawning child asphalt repaving sub-order `XD01-230`).
  3. Enforce cascading state rules: parent order cannot transition to `Closed` until all child sub-orders reach `Closed` or `Validated`.
  4. If a parent order is `Cancelled`, cascade cancellation to unstarted children.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/Services/IOrderDependencyOrchestrator.cs` (New)
  - `core/GeoAssets.Workflow/Services/OrderDependencyOrchestrator.cs` (New)
  - `tests/GeoAssets.Workflow.Tests/Services/OrderDependencyOrchestratorTests.cs` (New)
- **Architectural Tier**: Field Service Workflow (`GeoAssets.Workflow`)
- **Acceptance Criteria**:
  - Parent order closure is blocked while child sub-orders are active.
  - Cascading cancellations execute reliably.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Workflow.Tests/GeoAssets.Workflow.Tests.csproj --filter "FullyQualifiedName~OrderDependencyOrchestratorTests"
  ```

#### Ticket XD01-232: Field Service Asynchronous Event Dispatching Across Microservices
- **Issue Key**: `XD01-232`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement asynchronous event bus dispatcher bridging `GeoAssets.FieldService.Server` state transitions to Core Platform and IP Billing.
  2. Publish integration events (`FieldWorkOrderCompletedIntegrationEvent`, `AssetInspectedIntegrationEvent`) using transactional outbox pattern.
  3. Ensure reliable delivery with at-least-once semantics, idempotency keys, and exponential backoff retry.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.FieldService.Server/Outbox/FieldServiceOutboxDispatcher.cs` (New)
  - `core/GeoAssets.Workflow/Events/FieldWorkIntegrationEvents.cs` (New)
  - `tests/GeoAssets.FieldService.Server.Tests/Outbox/FieldServiceOutboxDispatcherTests.cs` (New)
- **Architectural Tier**: Field Service Host (`GeoAssets.FieldService.Server`, `GeoAssets.Workflow`)
- **Acceptance Criteria**:
  - State transitions persist outbox messages within the same DB transaction.
  - Outbox dispatcher publishes events to message bus and updates status to `Dispatched`.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.FieldService.Server.Tests/GeoAssets.FieldService.Server.Tests.csproj --filter "FullyQualifiedName~FieldServiceOutboxDispatcherTests"
  ```

#### Ticket XD01-234: Mobile Offline Queue: Priority Scheduling, Retry Backoff & Dead-Letter Handling
- **Issue Key**: `XD01-234`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Enhance SQLCipher encrypted offline mutation queue in `apps/GeoAssets.MAUI/Services/OfflineQueueManager.cs`.
  2. Implement priority scheduling: emergency rupture and safety incident reports sync ahead of routine inspection photos.
  3. Implement exponential backoff retry with jitter (1s, 2s, 4s, 8s, max 60s) for network transient failures.
  4. Move poison messages failing schema validation to a local dead-letter table after 5 failed attempts, alerting user.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.MAUI/Services/OfflineQueueManager.cs` (New)
  - `apps/GeoAssets.MAUI/Data/LocalOfflineDatabase.cs`
  - `tests/GeoAssets.Shared.Tests/Offline/OfflineQueueManagerTests.cs` (New)
- **Architectural Tier**: Mobile Client (`GeoAssets.MAUI`, `GeoAssets.Shared`)
- **Acceptance Criteria**:
  - High-priority items jump queue.
  - Poison items quarantine to dead-letter storage without blocking queue.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --filter "FullyQualifiedName~OfflineQueueManagerTests"
  ```

#### Ticket XD01-235: Mobile Differential Sync: Delta-Based Protocol for Bandwidth Reduction
- **Issue Key**: `XD01-235`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement differential synchronization protocol in `apps/GeoAssets.FieldService.Server` and `apps/GeoAssets.MAUI`.
  2. Track mutation vectors via `LastSynchronizedVersion` timestamps / sequence numbers.
  3. Mobile sync pull request sends last known version; server returns only modified entity deltas and deleted IDs rather than full snapshots.
  4. Apply gzip compression on sync payloads.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.FieldService.Server/Services/DifferentialSyncService.cs` (New)
  - `apps/GeoAssets.MAUI/Services/MauiSyncGateway.cs`
  - `tests/GeoAssets.FieldService.Server.Tests/Sync/DifferentialSyncTests.cs` (New)
- **Architectural Tier**: Field Service & Mobile (`GeoAssets.FieldService.Server`, `GeoAssets.MAUI`)
- **Acceptance Criteria**:
  - Repeated sync calls with zero server changes return HTTP 304 Not Modified or empty delta.
  - Sync payload bandwidth reduced by > 80% on partial updates.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.FieldService.Server.Tests/GeoAssets.FieldService.Server.Tests.csproj --filter "FullyQualifiedName~DifferentialSyncTests"
  ```

#### Ticket XD01-295: MAUI Sync Gateway UI: Visual Status Indicators, Manual Triggers & Error Modals
- **Issue Key**: `XD01-295`
- **Issue Type**: `Historia`
- **Functional Requirements**:
  1. Implement visual sync status indicator component in `apps/GeoAssets.MAUI` TopBar:
     - Green cloud icon: Synced (queue empty, online).
     - Yellow pulsing icon: Syncing (X items uploading).
     - Orange cloud with slash: Offline (mutations queued locally).
     - Red exclamation icon: Sync error (dead-letter items pending).
  2. Add manual "Sync Now" trigger button forcing immediate outbox flush.
  3. Add modal dialog displaying sync error details and allowing user to retry or discard quarantined mutations.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.Shared/Components/Sync/SyncStatusIndicator.razor` (New)
  - `apps/GeoAssets.Shared/Components/Sync/SyncStatusIndicator.razor.cs` (New)
  - `apps/GeoAssets.Shared/Components/Sync/SyncErrorModal.razor` (New)
  - `tests/GeoAssets.Shared.Tests/Components/Sync/SyncStatusIndicatorTests.cs` (New)
- **Architectural Tier**: UI & Mobile (`GeoAssets.Shared`, `GeoAssets.MAUI`)
- **Acceptance Criteria**:
  - UI updates reactively to `ConnectivitySyncTrigger` and outbox state changes.
  - Tapping "Sync Now" flushes queue.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --filter "FullyQualifiedName~SyncStatusIndicatorTests"
  ```

---

### Cluster 9: Contractor US Ledger & Measurement Reporting (3 Tickets)

#### Ticket XD01-238: Contractor Remuneration Rules: Dynamic Price Book & Contract Formula Calculation
- **Issue Key**: `XD01-238`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement dynamic contract price book calculator in `core/GeoAssets.Workflow/Contractors/ContractorRemunerationCalculator.cs`.
  2. Calculate total remuneration for completed BoQ items: multiply quantity by unit price from active contractor price agreement.
  3. Support formula adjustments: overtime multipliers (1.5x night, 2.0x holiday), difficult terrain surcharges, and hazardous duty bonuses.
  4. Validate calculations against Brazilian rounding rules (MidpointRounding.AwayFromZero to 2 decimal places).
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/Contractors/IContractorRemunerationCalculator.cs` (New)
  - `core/GeoAssets.Workflow/Contractors/ContractorRemunerationCalculator.cs` (New)
  - `tests/GeoAssets.Workflow.Tests/Contractors/ContractorRemunerationCalculatorTests.cs` (New)
- **Architectural Tier**: Field Service Workflow (`GeoAssets.Workflow`)
- **Acceptance Criteria**:
  - Remuneration formulas compute exact amounts matching financial test fixtures.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Workflow.Tests/GeoAssets.Workflow.Tests.csproj --filter "FullyQualifiedName~ContractorRemunerationCalculatorTests"
  ```

#### Ticket XD01-239: Contractor Ledger Immutable Audit Trail & Supervisory Adjustment Logging
- **Issue Key**: `XD01-239`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Enhance `ContractorUsLedgerEntry` schema with immutable supervisory adjustment tracking.
  2. If a supervisor modifies recorded work quantities during inspection, create a compensating ledger entry (`AdjustmentCredit` / `AdjustmentDebit`) referencing the original entry ID and reason code.
  3. Never overwrite original contractor entries; maintain append-only double-entry ledger integrity.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/Contractors/ContractorUsLedgerEntry.cs`
  - `workflow/GeoAssets.Workflow.EFCore/Repositories/EFContractorUsLedgerRepository.cs`
  - `tests/GeoAssets.Workflow.Tests/Contractors/ContractorLedgerAuditTrailTests.cs` (New)
- **Architectural Tier**: Field Service Workflow (`GeoAssets.Workflow`, `GeoAssets.Workflow.EFCore`)
- **Acceptance Criteria**:
  - Ledger maintains immutable history; balances reconcile correctly across adjustments.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Workflow.Tests/GeoAssets.Workflow.Tests.csproj --filter "FullyQualifiedName~ContractorLedgerAuditTrailTests"
  ```

#### Ticket XD01-240: Contractor Export Formats: CSV, Excel & PDF Dossier Generation for Billing
- **Issue Key**: `XD01-240`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement multi-format billing export generator in `core/GeoAssets.Workflow/Contractors/ContractorBillingExporter.cs`.
  2. Support export formats:
     - CSV: RFC 4180 compliant raw ledger data.
     - Excel (`.xlsx`): Formatted spreadsheet with summary headers, BoQ item breakdown, and remuneration totals.
     - PDF / HTML: Formal *Espelho de Medição* dossier printable format with embedded photo thumbnails, crew signatures, and GPS stamps.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/Contractors/IContractorBillingExporter.cs` (New)
  - `core/GeoAssets.Workflow/Contractors/ContractorBillingExporter.cs` (New)
  - `apps/GeoAssets.FieldService.Server/Endpoints/ContractorUsLedgerEndpoints.cs`
  - `tests/GeoAssets.Workflow.Tests/Contractors/ContractorBillingExporterTests.cs` (New)
- **Architectural Tier**: Field Service Workflow (`GeoAssets.Workflow`, `GeoAssets.FieldService.Server`)
- **Acceptance Criteria**:
  - Generates valid CSV and Excel files matching accounting templates.
  - Photo hashes in dossier match original evidence records.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Workflow.Tests/GeoAssets.Workflow.Tests.csproj --filter "FullyQualifiedName~ContractorBillingExporterTests"
  ```

---

### Cluster 10: Neutral Infrastructure Operator & Poste Justo (6 Tickets)

#### Ticket XD01-253: Aerial Pole Asset Catalog: Technical Specifications, Load Ratings & Materials
- **Issue Key**: `XD01-253`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement aerial pole catalog domain model in `core/GeoAssets.Domain.NeutralPole/Models/PoleCatalogItem.cs`.
  2. Model standard Brazilian distribution poles (ABNT NBR 8451 / NBR 8452): concrete (circular, DT), wooden, and fiberglass.
  3. Capture engineering attributes: nominal height (9m, 11m, 12m, 14m), nominal strength rating in daN (300 daN, 600 daN, 1000 daN), allowable telecom bending moment, and maximum allowable cable attachments.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Domain.NeutralPole/Models/PoleCatalogItem.cs` (New)
  - `core/GeoAssets.Domain.NeutralPole/Services/PoleLoadCalculator.cs` (New)
  - `tests/GeoAssets.Domain.NeutralPole.Tests/Models/PoleCatalogTests.cs` (New)
- **Architectural Tier**: Neutral Pole Domain (`GeoAssets.Domain.NeutralPole`)
- **Acceptance Criteria**:
  - Load calculator evaluates whether adding a new telecom cable exceeds the pole's daN mechanical limit.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Domain.NeutralPole.Tests/GeoAssets.Domain.NeutralPole.Tests.csproj --filter "FullyQualifiedName~PoleCatalogTests"
  ```

#### Ticket XD01-254: Clandestine Attachment Detection: Spatial Density Anomaly Algorithms
- **Issue Key**: `XD01-254`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement clandestine cable detection algorithm in `core/GeoAssets.Domain.NeutralPole/Services/ClandestineAnomalyDetector.cs`.
  2. Compare physical field survey cable counts against authorized occupant contracts in `PoleAttachmentSlotMatrix`.
  3. Detect anomalies: physical cable count > authorized slots, unauthorized drop cables, unlabelled fiber slack coils (*reservas técnicas*).
  4. Flag poles with anomaly score and auto-generate inspection work order.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Domain.NeutralPole/Services/IClandestineAnomalyDetector.cs` (New)
  - `core/GeoAssets.Domain.NeutralPole/Services/ClandestineAnomalyDetector.cs` (New)
  - `tests/GeoAssets.Domain.NeutralPole.Tests/Services/ClandestineAnomalyDetectorTests.cs` (New)
- **Architectural Tier**: Neutral Pole Domain (`GeoAssets.Domain.NeutralPole`)
- **Acceptance Criteria**:
  - Poles with uncontracted cables are flagged with discrepancy count and severity level.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Domain.NeutralPole.Tests/GeoAssets.Domain.NeutralPole.Tests.csproj --filter "FullyQualifiedName~ClandestineAnomalyDetectorTests"
  ```

#### Ticket XD01-255: Corridor Census Automation: Route-Based Batching & Mobile Survey Orchestration
- **Issue Key**: `XD01-255`
- **Issue Type**: `Historia`
- **Functional Requirements**:
  1. Implement automated corridor census planner in `core/GeoAssets.Domain.NeutralPole/Services/CorridorCensusPlanner.cs`.
  2. Group poles along electrical feeder lines into spatial batches of 50–100 poles for field survey crews.
  3. Sequence poles using Dijkstra / TSP pathfinder to minimize crew transit time.
  4. Generate batched service orders assigned to survey crews with pre-loaded offline maps.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Domain.NeutralPole/Services/ICorridorCensusPlanner.cs` (New)
  - `core/GeoAssets.Domain.NeutralPole/Services/CorridorCensusPlanner.cs` (New)
  - `tests/GeoAssets.Domain.NeutralPole.Tests/Services/CorridorCensusPlannerTests.cs` (New)
- **Architectural Tier**: Neutral Pole Domain (`GeoAssets.Domain.NeutralPole`)
- **Acceptance Criteria**:
  - Feeder poles are partitioned into contiguous route batches without orphan poles.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Domain.NeutralPole.Tests/GeoAssets.Domain.NeutralPole.Tests.csproj --filter "FullyQualifiedName~CorridorCensusPlannerTests"
  ```

#### Ticket XD01-256: Pole Attachment Dispute Workflow: Multi-Operator Mediation & SLA Tracking
- **Issue Key**: `XD01-256`
- **Issue Type**: `Historia`
- **Functional Requirements**:
  1. Implement dispute resolution state machine in `core/GeoAssets.Domain.NeutralPole/Services/DisputeResolutionService.cs`.
  2. Manage dispute lifecycle: `Flagged -> ContestedByOperator -> TechnicalAuditScheduled -> MediationHearing -> Resolved / EscalatedToAneelAnatel`.
  3. Track statutory resolution SLA (15 business days per Decreto 12.068/2024).
  4. Generate formal mediation dossier with photographic evidence, notice timestamps, and operator rebuttals.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Domain.NeutralPole/Services/IDisputeResolutionService.cs` (New)
  - `core/GeoAssets.Domain.NeutralPole/Services/DisputeResolutionService.cs` (New)
  - `tests/GeoAssets.Domain.NeutralPole.Tests/Services/DisputeResolutionServiceTests.cs` (New)
- **Architectural Tier**: Neutral Pole Domain (`GeoAssets.Domain.NeutralPole`)
- **Acceptance Criteria**:
  - Dispute state transitions obey regulatory workflow.
  - SLA breach triggers automatic escalation flag.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Domain.NeutralPole.Tests/GeoAssets.Domain.NeutralPole.Tests.csproj --filter "FullyQualifiedName~DisputeResolutionServiceTests"
  ```

#### Ticket XD01-257: BDGD PONNOT Ingestion: Authoritative ANEEL Distribution Grid Data Synchronization
- **Issue Key**: `XD01-257`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement BDGD (Base de Dados Georreferenciada da Distribuidora) PONNOT (Pontos Notáveis / Postes) ingestion engine in `core/GeoAssets.Core/Bdgd/BdgdPonnotImporter.cs`.
  2. Parse official ANEEL BDGD ESRI Shapefile and GeoPackage layers: extract pole coordinates, structural material, circuit code (`COD_ID`), and concessionaire asset tag.
  3. Project coordinates from SIRGAS 2000 UTM to WGS84 (`EPSG:4326`) and upsert into `geo_entity` with authoritative asset type `bdgd-poste`.
  4. Handle incremental updates without duplicating existing pole assets.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Core/Bdgd/IBdgdPonnotImporter.cs` (New)
  - `core/GeoAssets.Core/Bdgd/BdgdPonnotImporter.cs` (New)
  - `tests/GeoAssets.Core.Tests/Bdgd/BdgdPonnotImporterTests.cs` (New)
- **Architectural Tier**: Core Platform & BDGD (`GeoAssets.Core`)
- **Acceptance Criteria**:
  - Ingestion parses valid PONNOT features, reprojects coordinates accurately (< 0.01m error), and persists to provider.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Core.Tests/GeoAssets.Core.Tests.csproj --filter "FullyQualifiedName~BdgdPonnotImporterTests"
  ```

#### Ticket XD01-268: Poste Justo Lease Billing Adjustments: Retroactive Indexing & Municipal Fee Reconciliation
- **Issue Key**: `XD01-268`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement monthly lease billing adjustment engine in `core/GeoAssets.Domain.NeutralPole/Services/PosteJustoBillingService.cs`.
  2. Compute monthly rental charges per occupant: `OccupiedSlots * RegulatedPoleTariff`.
  3. Support annual tariff adjustments indexed by IPCA / IGPM inflation rates.
  4. Reconcile municipal rights-of-way fees (*taxa de ocupação de solo*), deducting municipal levies from concessionaire gross receipts.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Domain.NeutralPole/Services/IPosteJustoBillingService.cs` (New)
  - `core/GeoAssets.Domain.NeutralPole/Services/PosteJustoBillingService.cs` (New)
  - `tests/GeoAssets.Domain.NeutralPole.Tests/Services/PosteJustoBillingServiceTests.cs` (New)
- **Architectural Tier**: Neutral Pole Domain (`GeoAssets.Domain.NeutralPole`)
- **Acceptance Criteria**:
  - Monthly billing summaries compute exact financial values matching regulatory formulas.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Domain.NeutralPole.Tests/GeoAssets.Domain.NeutralPole.Tests.csproj --filter "FullyQualifiedName~PosteJustoBillingServiceTests"
  ```

---

### Cluster 11: Security, Photo Evidence HMAC & Safety Rules (3 Tickets)

#### Ticket XD01-262: Photo Evidence Integrity: Automated Server-Side Verification of HMAC-SHA256 Signatures
- **Issue Key**: `XD01-262`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement server-side evidence verification endpoint in `apps/GeoAssets.FieldService.Server`: `POST /api/evidence/verify`.
  2. Recompute HMAC-SHA256 signature from raw image bytes and EXIF payload using the tenant's master secret key.
  3. Validate against the embedded EXIF signature: confirm photo was not modified, GPS coordinates were not tampered with, and timestamp was not altered.
  4. Reject forged or re-compressed photos with HTTP 422 Unprocessable Entity.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.FieldService.Server/Endpoints/EvidenceVerificationEndpoints.cs`
  - `core/GeoAssets.Workflow/Evidence/FieldPhotoEvidenceVerifier.cs` (New)
  - `tests/GeoAssets.FieldService.Server.Tests/Endpoints/EvidenceVerificationEndpointTests.cs` (New)
- **Architectural Tier**: Field Service Server & Security (`GeoAssets.FieldService.Server`, `GeoAssets.Workflow`)
- **Acceptance Criteria**:
  - Legitimate photos signed by `FieldPhotoEvidenceSealer` return `IsValid == true`.
  - Photos with altered pixels, tampered GPS, or spoofed timestamps fail verification.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.FieldService.Server.Tests/GeoAssets.FieldService.Server.Tests.csproj --filter "FullyQualifiedName~EvidenceVerificationEndpointTests"
  ```

#### Ticket XD01-290: Gateway Tenant Isolation Auditing: Automated Periodic Validation of Silo Routing Rules
- **Issue Key**: `XD01-290`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement automated tenant isolation audit runner in `apps/GeoAssets.Gateway/Diagnostics/TenantIsolationAuditRunner.cs`.
  2. Execute periodic background verification probe (every 1 hour): send synthetic probe requests across all registered silo tenant domains and multi-tenant shared domains.
  3. Verify that zero probe requests leak into unintended backends.
  4. Emit OpenTelemetry metric `gateway_tenant_isolation_violations_total` and log alert on routing anomaly.
- **Affected Projects & File Targets**:
  - `apps/GeoAssets.Gateway/Diagnostics/TenantIsolationAuditRunner.cs` (New)
  - `apps/GeoAssets.Gateway/Program.cs`
  - `tests/GeoAssets.Gateway.Tests/Diagnostics/TenantIsolationAuditRunnerTests.cs` (New)
- **Architectural Tier**: Gateway Infrastructure (`GeoAssets.Gateway`)
- **Acceptance Criteria**:
  - Audit runner detects routing misconfigurations immediately during simulated fault injection.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Gateway.Tests/GeoAssets.Gateway.Tests.csproj --filter "FullyQualifiedName~TenantIsolationAuditRunnerTests"
  ```

#### Ticket XD01-299: Safety Checklist Dynamic Rules: Adaptive Rule Engine for Weather, Elevation & Hazard Zones
- **Issue Key**: `XD01-299`
- **Issue Type**: `Tarea`
- **Functional Requirements**:
  1. Implement dynamic safety rule engine in `core/GeoAssets.Workflow/Safety/DynamicSafetyRuleEngine.cs`.
  2. Dynamically inject safety checklist requirements into field orders based on environmental context:
     - If ambient wind speed > 30 km/h or rain detected, automatically mandate NR-35 adverse weather clearance.
     - If asset height > 4.0m, mandate full harness double-lanyard inspection.
     - If asset is located inside an electrical substation or high-voltage zone, mandate NR-10 arc-flash protective gear.
  3. Prevent `StartExecution` transition until all dynamically injected checklist gates are signed.
- **Affected Projects & File Targets**:
  - `core/GeoAssets.Workflow/Safety/IDynamicSafetyRuleEngine.cs` (New)
  - `core/GeoAssets.Workflow/Safety/DynamicSafetyRuleEngine.cs` (New)
  - `tests/GeoAssets.Workflow.Tests/Safety/DynamicSafetyRuleEngineTests.cs` (New)
- **Architectural Tier**: Field Service Workflow (`GeoAssets.Workflow`)
- **Acceptance Criteria**:
  - High-risk environmental conditions dynamically append safety gates to orders.
  - Crews cannot bypass dynamically mandated safety checks.
- **Verification Commands**:
  ```bash
  dotnet test tests/GeoAssets.Workflow.Tests/GeoAssets.Workflow.Tests.csproj --filter "FullyQualifiedName~DynamicSafetyRuleEngineTests"
  ```

---

## 6. Complete Verification Matrix & Shell Commands

### 6.1 Solution-Wide Build Gatekeeper
```bash
# Verify entire solution compiles with 0 errors and 0 warnings under /warnaserror
dotnet build GeoAssets.sln --no-incremental /warnaserror
```

### 6.2 Pre-Flight Architecture & Invariant Script
```bash
# Verify SOLID contracts, scope disjointness, and .razor.cs pairings
python3 scripts/verify-solid-contracts.py --target . --base origin/develop
```

### 6.3 Deterministic Worktree Gatekeeper
```bash
# Execute official repository 3-stage Oracle gatekeeper
./scripts/verify-agent-worktree.sh .
```

### 6.4 Comprehensive Automated Test Execution (By Subsystem)
```bash
# Core Domain, Algorithms & Spatial Queries
dotnet test tests/GeoAssets.Core.Tests/GeoAssets.Core.Tests.csproj --no-build --nologo

# Workflow Engine & State Machine
dotnet test tests/GeoAssets.Workflow.Tests/GeoAssets.Workflow.Tests.csproj --no-build --nologo
dotnet test tests/GeoAssets.Workflow.EFCore.Tests/GeoAssets.Workflow.EFCore.Tests.csproj --no-build --nologo

# IP Billing Microservice (GEOS_FAT_IP)
dotnet test tests/GeoAssets.IpBilling.Tests/GeoAssets.IpBilling.Tests.csproj --no-build --nologo
dotnet test tests/GeoAssets.IpBilling.EFCore.Tests/GeoAssets.IpBilling.EFCore.Tests.csproj --no-build --nologo

# Field Service Server & Endpoints
dotnet test tests/GeoAssets.FieldService.Server.Tests/GeoAssets.FieldService.Server.Tests.csproj --no-build --nologo

# PostgreSQL Provider & PostGIS Queries
dotnet test tests/GeoAssets.Provider.PostgreSQL.Tests/GeoAssets.Provider.PostgreSQL.Tests.csproj --no-build --nologo

# Gateway & Multi-Tenant Routing
dotnet test tests/GeoAssets.Gateway.Tests/GeoAssets.Gateway.Tests.csproj --no-build --nologo

# Security & Cross-Tenant Penetration Audits
dotnet test tests/GeoAssets.Security.Tests/GeoAssets.Security.Tests.csproj --no-build --nologo

# Regulatory Utility Verticals
dotnet test tests/GeoAssets.Domain.Electrical.Tests/GeoAssets.Domain.Electrical.Tests.csproj --no-build --nologo
dotnet test tests/GeoAssets.Domain.Sanitation.Tests/GeoAssets.Domain.Sanitation.Tests.csproj --no-build --nologo
dotnet test tests/GeoAssets.Domain.NeutralPole.Tests/GeoAssets.Domain.NeutralPole.Tests.csproj --no-build --nologo

# UI & Shared Components
dotnet test tests/GeoAssets.Shared.Tests/GeoAssets.Shared.Tests.csproj --no-build --nologo
```

---

## 7. Zero-Placeholder Audit & Final Pre-Flight Instructions

Prior to declaring any ticket complete or submitting a commit:
1. **Search for Forbidden Tokens**:
   ```bash
   git diff origin/develop | grep -nE "TODO|FIXME|TBD|NotImplementedException"
   ```
   *Expected output: Empty.* Any hit is an immediate failure.
2. **Verify Commit Subject Pattern**:
   ```bash
   git log -1 --pretty=format:"%s" | grep -E "\((XD01-[0-9]+)\)$"
   ```
   *Expected output: Commit subject must end with `(XD01-XXX)`.*
3. **Verify Branch Cleanliness**:
   ```bash
   git status --porcelain
   ```
   *Expected output: Clean working tree.*

Proceed through the 59 tickets methodically, maintaining the highest standard of engineering rigor. Every ticket implemented brings the GeoAssets platform to complete enterprise production readiness.
