# XD01-216 — Cross-tenant penetration test & audit report

**Scope:** the shared multi-tenant SaaS tier's full request path — YARP
Gateway routing (XD01-213), per-`OrganizationId` token-bucket rate limiting
(XD01-214), and Postgres Row-Level Security (XD01-204) — combined, not
just the database layer in isolation. Validates the full stack resists
cross-tenant access attempts end-to-end.

**Date:** 2026-11-01
**Method:** automated test suites run against real infrastructure (real
Postgres+PostGIS via Testcontainers, a real YARP pipeline, real Kestrel
backends) — no mocks of the systems under test. No external pentest
tooling (Burp/ZAP/nmap) was used: there is no publicly reachable endpoint
to point such tooling at in this environment (no CI/CD, no cloud host —
see `deploy/server/README.md`), so "penetration testing" here means
adversarial-input automated tests exercising the same code paths an
external attacker would reach, not a live network attack against a
deployed instance.

## Methodology / test matrix

| Risk | Automated coverage | Result |
|---|---|---|
| Tenant A's RLS session reads/writes tenant B's rows in `geo_entity`, `ServiceOrders`, `ContractorUsLedgerEntries`, `asset_type` | `tests/GeoAssets.Security.Tests/CrossTenantRowLevelSecurityTests.cs` — real Postgres, real `security_suite_scoped_role` (non-owner), raw ADO.NET SQL (not EF Core, so the DB-level policy itself is proven, not an app-layer filter) | **Pass** (11/11) |
| A newly added org-scoped table ships without an RLS policy and nobody notices | `EveryOrganizationScopedTable_HasRowLevelSecurityEnabledWithAtLeastOnePolicy` — reflects over every EF model in `GeoAssetsDbContext`/`ServiceOrderDbContext` for an `OrganizationId` property, fails loudly if its table lacks RLS | **Pass**, zero exclusions (previously excluded `asset_type` — see Findings) |
| A tenant claims another organization's id on INSERT/UPDATE via raw SQL (bypassing any app-layer validation) | `*_InsertUnderAnotherTenant_ViolatesRowSecurityPolicy` tests (per table) | **Pass** |
| A tenant mints a new "global" `asset_type` visible to every other tenant by claiming `OrganizationId = Guid.Empty` | `AssetType_InsertClaimingGlobalOwnership_ViolatesRowSecurityPolicy` | **Pass** |
| Gateway's silo-vs-shared route selection is spoofable by header value/casing | `tests/GeoAssets.Gateway.Tests/GatewayAdversarialInputTests.cs` | **Finding** — see below (not a data leak; tracked as XD01-289) |
| A SQL-injection-shaped header value crashes or otherwise misbehaves at the Gateway | `TenantDomainHeader_WithSqlInjectionLikeValue_IsHandledSafelyAndStillRoutesToSilo` | **Pass** — Gateway never interpolates header values into SQL |
| A malformed/garbage `Authorization` header crashes the rate limiter or bypasses partitioning | `MalformedOrGarbageAuthorizationHeader_FallsIntoUnresolvedBucket_WithoutCrashing` | **Pass** |
| One tenant exhausting its rate-limit bucket throttles another tenant | `tests/GeoAssets.Gateway.Tests/GatewayRateLimitingTests.cs` (XD01-214, re-verified as part of this audit) | **Pass** |
| Route precedence (silo vs. shared) holds across every proxied service prefix | `tests/GeoAssets.Gateway.Tests/GatewayRoutingTests.cs` (XD01-213, re-verified) | **Pass** |
| A Helm-deployed dedicated-silo pod never becomes healthy/reachable | Live `kind` cluster test during XD01-215 surfaced a real `/healthz`-under-auth bug; fixed and regression-tested (see that ticket). Re-confirmed here as in-scope for "gateway resists cross-tenant access attempts end-to-end" since an unreachable silo is itself a availability finding, not a leak | **Pass** (fixed in XD01-215) |

## Findings

### Finding 1 (remediated and re-verified) — `asset_type` had no RLS policy

**Severity at discovery: Critical** (XD01-204/XD01-288). `asset_type` had
an `OrganizationId` column but zero RLS policy — any session, with no
tenant isolation at all, could read or write every organization's asset
type catalog.

**Remediation:** `AddAssetTypeRowLevelSecurity` migration
(`providers/GeoAssets.Provider.PostgreSQL/Migrations/20261101034430_AddAssetTypeRowLevelSecurity.cs`)
adds the same `tenant_isolation_policy` shape used by `geo_entity`, with
one necessary difference: `asset_type` has real rows seeded with
`OrganizationId = Guid.Empty` (the generic Point/Line/Area defaults and the
domain starter catalog — see `AssetTypeRowConfiguration.HasData`), meant to
be visible to every tenant. The policy's `USING` clause allows a session's
own `OrganizationId` **or** `Guid.Empty`-owned rows; its `WITH CHECK`
clause does *not* grant that same exception, so no tenant can mint a new
"global" asset type visible to every other tenant.

**Re-verification:** `CrossTenantRowLevelSecurityTests.AssetType_*` (3 new
tests) plus the discovery test now running with zero exclusions — all pass
against a real Postgres container. XD01-288 is closed alongside this
ticket.

### Finding 2 (tracked, not remediated here) — Gateway silo routing is presence-only, not value-aware

**Severity: Medium**, not Critical — see reasoning below.
`GatewayRouteCatalog`'s dedicated-silo route matches
`HeaderMatchMode.Exists` on `X-Tenant-Domain` — any non-empty value routes
there, and each Gateway deployment has exactly one configured silo
destination, not one per tenant domain.

**Why this isn't rated Critical today:** whichever silo a request reaches,
that silo still enforces its own real bearer-token authentication
(`RequireAuthenticatedUser` FallbackPolicy — live-verified reachable and
correctly enforced in XD01-215) before any data is returned. Spoofing the
header redirects a caller's *traffic*, not their *authorization* — it
cannot be used to read another tenant's data through this path alone.

**Why it still matters:** there is currently at most one dedicated silo
provisioned anywhere (XD01-217, the first pilot, hasn't shipped yet), so
this gap has no live victim yet. It becomes a real routing-correctness
problem — "which of N tenants' silos does this request actually reach" —
the moment a second Tier-2 silo exists, which XD01-218 (onboarding a pilot
*cohort*) explicitly plans for.

**Disposition:** filed as **XD01-289**, linked as blocking XD01-217 and
XD01-218, not fixed under this ticket — redesigning route/destination
selection to be domain-value-aware is a scoped feature change in its own
right, not a one-line fix, and per this repo's own conventions a
pre-existing gap discovered during audit work is tracked, not silently
patched or silently left undocumented (same disposition XD01-288 got from
XD01-204's audit).

**Regression coverage for current behavior:**
`GatewayAdversarialInputTests.AnyNonEmptyTenantDomainHeaderValue_RoutesToSiloCluster_RegardlessOfValue`
documents today's actual behavior so XD01-289's eventual fix has a test to
update, not a behavior to rediscover.

## What was not verified

- **No real CIAM round-trip.** Every test here that needs an "authenticated
  caller" uses a no-op test authentication scheme or an unsigned JWT with
  the Gateway's claim-reading code — not a real Entra CIAM-issued,
  signature-validated token. The backend hosts' actual JWT bearer
  validation (issuer/audience/signature) was not exercised by this audit;
  it has no automated coverage anywhere in this repo today, a gap outside
  this ticket's scope to close.
- **No external network-level pentest tooling** (Burp, ZAP, nmap, a real
  TLS/cipher audit) — there is no deployed, network-reachable instance of
  this stack to point such tooling at (see `deploy/server/README.md`'s
  "What this doesn't solve").
- **`GeoIdentityDbContext`'s own tables** (`Groups`, etc.) are a different
  subsystem than the org-scoped `GeoAssetsDbContext`/`ServiceOrderDbContext`
  entities this audit's discovery test scans — explicitly out of scope,
  same as XD01-204 originally stated.
- **Tier-2 dedicated-silo cross-tenant isolation specifically** (as opposed
  to shared-tier isolation) — XD01-215's live `kind` test proved a silo
  *works*, but there was only ever one silo in that test; Finding 2 is the
  direct consequence of there being no second silo to test isolation
  *between* two silos against.

## Sign-off

Zero critical findings remain open. The one critical finding this audit's
own discovery test surfaced (`asset_type`'s missing RLS policy, XD01-288)
was remediated and re-verified before this ticket's sign-off, per XD01-216's
acceptance criteria. One medium finding (XD01-289) is tracked separately,
has no live victim today, and does not block this sign-off per the same
acceptance criteria's "or all findings remediated" branch applying only to
findings that need remediation *before* sign-off — Critical/High findings
in this project's practice, not every Medium one blocking every security
ticket indefinitely. Re-review XD01-289 before XD01-217/218 ship, per its
own linked Blocks relationship.
