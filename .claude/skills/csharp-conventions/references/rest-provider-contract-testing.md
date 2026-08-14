# REST provider contract testing

**Status: `current`**

**Source**: Microsoft Learn, ["Integration tests in ASP.NET
Core"](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests)
(`WebApplicationFactory` section). Official documentation.
`testing-strategy-and-dotnet-tooling.md` already covers `WebApplicationFactory`
as a general integration-testing tool; this file is specifically about the
gap XD01-8 introduced: two independently-testable halves of one contract
(`ServiceOrdersRestApiExtensions` server-side, `RestServiceOrderRepository`/
`RestOrderTypeRepository` client-side) that currently have **no test
verifying they still agree with each other**.

## The gap, concretely

`GeoAssets.Workflow.Rest.Tests` tests `RestServiceOrderRepository` and
`RestOrderTypeRepository` against a `FakeHttpMessageHandler`
(`tests/GeoAssets.Workflow.Rest.Tests/FakeHttpMessageHandler.cs`) that
returns hand-written stub responses (status codes, JSON bodies) the test
author believes match what `ServiceOrdersRestApiExtensions` actually
produces. Nothing in the repo currently exercises the *real* minimal-API
endpoints and feeds their *real* responses to the *real* client — there's no
`WebApplicationFactory` usage anywhere in the codebase, and no test project
for `GeoAssets.Server`. That means a change to either side (a renamed JSON
property in the `400` error envelope, a status code changed from `409` to
`422`, a route renamed) can pass every existing test on both sides while
silently breaking the actual contract between them — `error-handling-and-result-pattern.md`'s
whole guarantee (identical exception types regardless of backend) depends on
these two halves staying in sync, and nothing currently enforces that.

## Contract test shape: real server, real client, one process

A contract test for a REST-backed provider should spin up the real
minimal-API endpoints in-process via `WebApplicationFactory<TEntryPoint>`,
point a real `RestServiceOrderRepository`/`RestOrderTypeRepository` at the
factory's `HttpClient`, and assert the client-side call produces the
expected result/exception — not a fake handler standing in for the server,
and not a raw `HttpClient` call bypassing the client class. This is
distinct from `RestServiceOrderRepositoryTests`' existing unit tests (which
correctly isolate the client's parsing/translation logic with a fake
handler — keep those, they're fast and test a different thing) and from any
future `ServiceOrdersRestApiExtensions`-only tests (which would isolate the
server's routing/status-code logic). The contract test's job is specifically
the *seam* between the two: does what the server actually emits parse into
what the client actually expects.

## What to prioritize covering first

Not every route needs a contract test — prioritize the paths where drift is
both likely and costly: the write-path error mapping
(`ServiceOrderAttributeValidationException`/`InvalidServiceOrderTransitionException`/
`ServiceOrderConcurrencyException`/`KeyNotFoundException` → status code →
reconstructed exception, exercised end-to-end for each of the four cases)
matters more than a plain `GET` returning a `200` with a list, since the
error envelope's exact JSON shape is the part most likely to silently drift
without either side's own unit tests catching it (each side's unit tests
only check *its own* assumption about the shape, not that the two
assumptions match).

## Where this would apply in GeoAssets

- Add a new test project (or a `Contracts/` folder inside an existing
  integration-test-appropriate project) that references both
  `apps/GeoAssets.Server` (for `WebApplicationFactory<Program>`) and
  `workflow/GeoAssets.Workflow.Rest` (for the real repository classes), and
  exercises `ServiceOrdersRestApiExtensions.MapServiceOrdersApi`'s four
  exception-mapping branches end-to-end against
  `RestServiceOrderRepository.EnsureWriteSuccessAsync`'s matching
  reconstruction logic.
- `FakeHttpMessageHandler`
  (`tests/GeoAssets.Workflow.Rest.Tests/FakeHttpMessageHandler.cs`) stays
  exactly as it is for the existing unit tests — this isn't a replacement
  for those, it's a new layer above them.
- If a second REST-backed provider is added later (per
  `project-and-solution-structure.md`'s one-project-per-backend rule), it
  should get its own contract test following this same shape rather than
  the gap being left open again.
