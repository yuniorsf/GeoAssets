# Gateway rate limits — the request-count model behind the defaults (XD01-393)

`apps/GeoAssets.Gateway` rate-limits every request before it reaches a backend (`GatewayRateLimiting`). This document
is where the default bucket sizes in `apps/GeoAssets.Gateway/appsettings.json` come from, and how to re-measure them.

## What is limited, and per whom

| Policy | Requests | Configuration | Default bucket |
|---|---|---|---|
| `tiles` | `/wms`, `/wfs` (the kernel's OGC endpoints; Leaflet tile requests are anonymous) | `RateLimiting:Tiles:{TokenLimit,TokensPerPeriod,ReplenishmentPeriodSeconds}` | 1000 burst, refilled 100/s |
| `api` | everything else except `/healthz` | `RateLimiting:Api:{…}` (the pre-XD01-393 flat `RateLimiting:{TokenLimit,TokensPerPeriod,ReplenishmentPeriodSeconds}` override it when set) | 500 burst, refilled 10/s |

Each policy has its own bucket **per caller**, so exhausting the tile bucket never rejects an API call, and one caller
never spends another's tokens. The caller (partition key) is, first match wins:

1. `org:<id>` — the bearer token's `RateLimiting:OrganizationIdClaimType` claim (unset by default: Entra CIAM emits no
   organization claim). **Once set, the API bucket is shared by the whole organization — scale it by the organization's
   concurrent active users.**
2. `sub:<id>` — the token's subject, `oid` then `sub`.
3. `ip:<address>` — the client IP (IPv4-mapped IPv6 as IPv4, IPv6 canonical, no scope id). Tile requests carry no
   token, so tiles are always per IP.

Claim values over 128 characters are keyed by their SHA-256.

### Which token's claims count (XD01-398)

With `AzureAdCiam:Instance` and `AzureAdCiam:TenantId` set (the same tenant as the kernel's; or
`AzureAdCiam:MetadataAddress`, by default `{Instance}/{TenantId}/v2.0/.well-known/openid-configuration`), the claims key a
bucket only when the token's **signature, issuer and lifetime** validate against the tenant's signing keys
(`RateLimiting:TokenValidation:ClockSkewSeconds`, default 300). The audience is not checked against a list — the gateway
fronts several APIs, each validating its own audience — but the token must carry one. A forged token can no longer
name someone else's subject or organization and spend *their* bucket: it lands in its sender's client-IP bucket.
The gateway still authorizes nothing and **never rejects a request because of its token**; backends decide.

| The bearer token | Validation outcome | Bucket |
|---|---|---|
| none, or not a JWT | `absent` | `ip:` |
| validates | `valid` | `org:` → `sub:` → `ip:` |
| bad signature or unknown key, wrong issuer, expired / not yet valid, no audience, malformed | `invalid` | `ip:` |
| signing keys unobtainable (metadata unreachable, or backing off after a failure) | `unavailable` | `ip:` |
| `AzureAdCiam` not configured: decoded only, as before XD01-398 | `unvalidated` | `org:` → `sub:` → `ip:` |

Every request except `/healthz` increments `geoassets.gateway.rate_limit_partition` (meter `GeoAssets.Gateway`), tagged
`policy=api|tiles`, `source=org|sub|ip|unresolved` and `validation=absent|valid|invalid|unavailable|unvalidated` — never
the key. A rising `validation=invalid` share is forged or stale tokens; any `unavailable` means the tenant's metadata
can't be reached. With `AzureAdCiam` not configured the gateway logs one **warning at startup** ("rate limits key on
unvalidated token claims…"): a forged token can then name another caller's bucket, as before XD01-398.

**Cost.** Validation is a local signature check against keys held in memory: the metadata document and its JWKS are
fetched once and cached by `ConfigurationManager` (refreshed on its own schedule, 12 h by default). A token signed by an
unknown `kid` (the tenant rolled its keys, or a forgery) asks for a refetch, which the manager grants at most once per
its `RefreshInterval` (5 min by default), so a stream of forged `kid`s cannot make the gateway hammer the tenant. A
failed fetch makes every token `unavailable` (IP-keyed) for `RateLimiting:TokenValidation:MetadataFailureBackoffSeconds`
(default 30) without trying again, logged as a warning each time it fails.

**Behind a load balancer** the connection's address is the load balancer's, so every caller without a token would share
one bucket. List the load balancer in `RateLimiting:TrustedProxies` (IP addresses) or `RateLimiting:TrustedNetworks`
(CIDRs, e.g. `10.0.0.0/8`). Only those hops' `X-Forwarded-For` is believed: ASP.NET's forwarded-headers middleware runs
first in the pipeline, walks `X-Forwarded-For` from the right while the appending hop is trusted, and makes the first
untrusted entry the client. Only `X-Forwarded-For` is processed (never `-Proto`/`-Host`). YARP's `X-Forwarded: Set`
route transform runs later, at forward time, and sets `X-Forwarded-For` from that resolved client address. Backends
therefore see the real client behind a trusted proxy, and never an address the caller chose. With nothing configured
(the default) the middleware isn't added at all.

**A rejected request** gets `429` at once (no queue), `Retry-After: <seconds>` (the bucket's own estimate, at least 1),
and an `application/problem+json` body: `{"title":"Too many requests","status":429,"detail":"…","code":"rate-limit.exceeded","policy":"tiles"}`.
Each rejection increments the counter `geoassets.gateway.rate_limited` (meter `GeoAssets.Gateway`), tagged `policy=api|tiles`.
The partition key is never a tag, to keep cardinality bounded.

## The model

These are the parameters of `tools/GeoAssets.Tools.GatewayLoadProfile`'s `LoadProfileModel`, read from the client code.

**Map session, per user (Blazor Web, desktop)**

- Viewport: a 1920×1080 screen minus the 340 px sidebar and the browser/top-bar chrome gives a 1580×1020 px map. WMS
  layers are `L.tileLayer.wms` with 256 px tiles (`geoassets.js` `addWmsLayer`). One full repaint of one layer is
  (⌈1580/256⌉+1)×(⌈1020/256⌉+1) = 8×5 = **40 tiles**. The +1 counts the partial tiles at both edges.
- 2 WMS layers on at once. Half the actions are zooms (a full repaint) and half are pans (revealing about half the
  viewport). That makes 2×40×(0.5+0.5×0.5) = **60 tile requests per pan/zoom**.
- An actively exploring user makes 30 pans/zooms per minute, one every 2 s. That is 60×30/60 = **30 tile req/s** at peak.
- A scroll-wheel flick crosses 5 zoom levels in a second or two: 5×2×40 = **400 tile requests in a burst**.
- On every settled pan/zoom, `MapContainer.OnViewportChangedFromJs` makes one `features/bounds` call per open vector
  provider entry (`RestAssetProvider` splits it 2×2 only above 50 deg², which doesn't happen at street level). With 2
  entries that is **1 API req/s** at peak, and 5×2 = **10 API requests** per zoom flick.

**Mobile photo sync, per technician (MAUI)**

- After an inspection, the evidence backlog syncs at once: 100 photos × 2 requests (the upload and its evidence record)
  + 20 other requests (checklist, status, refreshes) = **220 API requests in a burst**.

## The defaults, with their headroom

| Setting | Value | Derivation |
|---|---|---|
| `Tiles:TokenLimit` | 1000 | the 400-tile zoom burst × 2.5: two users behind one address (an office NAT) zooming at once, plus 25 % |
| `Tiles:TokensPerPeriod` / `ReplenishmentPeriodSeconds` | 100 / 1 | 30 tile req/s × 3, rounded up to 50: three users behind one address exploring at peak, or about ten at a typical third of that pace |
| `Api:TokenLimit` | 500 | (220-request sync + 10-request map burst) × 2, rounded up to 100 |
| `Api:TokensPerPeriod` / `ReplenishmentPeriodSeconds` | 10 / 1 | 1 map API req/s × 10: a user's own map traffic never comes close, and an upload backlog beyond the burst still drains at about 9 req/s |

A 1 s replenishment period refills smoothly, and `Retry-After` comes out as 1 s rather than the old 10 s steps. The
previous single shared bucket (100 tokens per 10 s, shared by everyone without an organization claim, which meant
everyone) was below one user's single zoom flick.

**When to raise them:** set an organization claim (the API bucket becomes per organization, so multiply the API values by
its concurrent users); a NAT with many more than about 10 active map users behind one address (raise `Tiles`); larger
photo backlogs (raise `Api:TokenLimit` to about 2 × the backlog's request count). Watch `geoassets.gateway.rate_limited`
by `policy`: a steady non-zero `tiles` rate during office hours means a shared address is too busy for its bucket.

## Re-measuring

```bash
# The model and the recommendations it derives (change any parameter to see its effect):
dotnet run --project tools/GeoAssets.Tools.GatewayLoadProfile -- model
dotnet run --project tools/GeoAssets.Tools.GatewayLoadProfile -- model --wms-layers 3 --actions-per-minute 40 --photos 300

# Replay the profile against a gateway and report req/s, 2xx and 429 per class (map tiles, map API, upload).
# An API token, if any, comes from the environment, never the command line. Tiles are always sent anonymously.
GATEWAY_LOAD_PROFILE_TOKEN="<bearer>" dotnet run --project tools/GeoAssets.Tools.GatewayLoadProfile -- \
  replay --base-url http://localhost:8090 --scenario all --duration-seconds 60
```

`replay` paces the map session at the model's actions per minute (zooms and pans alternate), with 6 concurrent
connections like a browser. It sends the upload burst as fast as `--upload-concurrency` (4) allows, as
`application/octet-stream` POSTs of `--upload-bytes` to `--upload-path`. Point that at an endpoint that accepts the
payload when the backend's answer matters. The rate limiter answers before any backend, so its 429s are counted
either way. To check that the defaults hold for one user, replay against the gateway with its defaults: there should
be no 429 in any class. To find a limit, replay with `--scenario map` and more `--actions-per-minute` until `tiles`
shows 429s.
