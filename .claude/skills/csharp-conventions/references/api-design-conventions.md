# REST API design conventions (minimal APIs)

**Status: `current`**

**Source**: Microsoft Learn, ["Minimal APIs
overview"](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)
and ["Web API design best
practices"](https://learn.microsoft.com/en-us/azure/architecture/best-practices/api-design).
Official documentation.

## Minimal APIs, grouped in a `MapXApi` extension method per domain

GeoAssets exposes REST endpoints exclusively as minimal APIs via
`IEndpointRouteBuilder` extension methods — never MVC controllers (there are
none in the codebase). Each domain gets its own static extension class
(`MapGeoAssetsApi`, `MapServiceOrdersApi`, `MapWfsApi`, `MapWmsApi`) taking
an optional `string prefix` parameter defaulted to that domain's mount
point, called once from the host's `Program.cs`. Keep this shape for any new
REST surface: a new domain gets a new `MapXApi` extension, not new routes
folded into an existing one, and not a controller.

## Route prefix is the domain boundary, not a version number

Each domain mounts at its own path prefix under `/api/` (`/api/geoassets`,
`/api/workflow`) — the prefix separates *domains* sharing one origin, it
isn't doing API versioning. GeoAssets has no versioning scheme yet (no
`/v1/` segment, no `Accepts` header negotiation); if that need arises, treat
it as a decision to make deliberately (URL segment vs. header-based) rather
than retrofitting version numbers into the existing domain prefixes.

## Resource-oriented routes, verbs map to HTTP methods

`GET /{resource}`, `GET /{resource}/{id}`, `POST /{resource}` (create,
`201 Created` with a `Location`-shaped path in the body-less response),
`PUT /{resource}/{id}` (full update, `204 No Content` on success),
`DELETE /{resource}/{id}` (`204 No Content`). Sub-resource actions that
aren't plain CRUD (`POST /service-orders/{id}/dispatch`,
`POST /service-orders/{id}/actions`) stay verb-named nouns under the parent
resource's path rather than becoming query parameters or a generic
`/actions` endpoint — the URL should describe *what* changed, the HTTP verb
describes *how*.

## Validate the request shape before touching the repository

Check for a missing/undeserializable body (`Results.BadRequest(...)`) and
route/body ID mismatches (`if (order.Id != id) return
Results.BadRequest(...)`) *before* calling into the repository — cheap,
synchronous checks fail fast and keep the repository call's own exception
handling focused on genuine domain failures, not malformed input.

## Error shape: status code carries the category, body carries just enough to act on

Don't return a generic `500` with a stack trace, and don't return a bare
`200` with an error flag buried in the body. Pick the HTTP status that
matches the failure category (`400` malformed/invalid, `404` missing,
`409` conflict) and, only where the client needs to act on specifics
beyond the status code, add a small structured JSON body — see
`error-handling-and-result-pattern.md` for the full write-up of how this
maps back to domain exceptions across the REST boundary.

## Where this would apply in GeoAssets

- `ServiceOrdersRestApiExtensions.MapServiceOrdersApi`
  (`apps/GeoAssets.Server/ServiceOrdersRestApiExtensions.cs`) and
  `GeoAssetsRestApiExtensions.MapGeoAssetsApi` (same folder) are the two
  reference examples of this whole file — same `prefix`-parameter shape,
  same resource-oriented route naming (`/service-orders/{id}/dispatch`,
  `/service-orders/{id}/actions` as sub-resource actions rather than query
  params), same request-validate-before-repository-call ordering. A new
  domain's REST surface should be reviewed against these two files directly.
- `Program.cs` (`apps/GeoAssets.Server/Program.cs`) calling
  `app.MapGeoAssetsApi()` then `app.MapServiceOrdersApi()` after a single
  `app.UseCors()` is the current composition pattern — a new domain adds one
  more `app.MapXApi()` call here, not a change to the CORS/pipeline setup.
- `GeoAssetsRestApiExtensions` mounting `MapWfsApi`/`MapWmsApi` under its own
  prefix (`routes.MapWfsApi(route: $"{prefix}/wfs")`) shows the pattern
  nesting cleanly — a sub-domain within a domain gets its own `MapXApi`
  extension mounted at a sub-path, rather than being inlined into the
  parent's extension method.
