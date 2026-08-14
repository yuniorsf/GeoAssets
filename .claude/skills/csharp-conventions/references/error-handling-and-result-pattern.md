# Error handling: exceptions vs. Result across a service boundary

**Status: `current`**

**Source**: Microsoft Learn, ["Exceptions and Exception
Handling"](https://learn.microsoft.com/en-us/dotnet/standard/exceptions/)
and ["Design Guidelines — Exception
throwing"](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/exception-throwing).
Official documentation. `domain-driven-design.md` already covers the
throw/error-collector/result-object menu for *entity validation specifically*
— this file generalizes the same decision to the app boundaries GeoAssets
now has: Core ↔ Provider, and (since XD01-8) Core ↔ REST client ↔ REST
server.

## In-process: exceptions for exceptional, not for control flow

Within a single process (Core calling a Provider directly, a Blazor
component calling a service), a thrown domain exception is the right tool
when the caller genuinely can't proceed — it's a control-flow shortcut that
unwinds straight to whoever's prepared to handle it, without every
intermediate frame needing to check and re-propagate a `Result`. Reserve
`Result<T>`/`OneOf`-style return types for cases that are *expected, frequent
outcomes* a caller is meant to branch on inline — not for genuine failures.
A `TryParse`-shaped API (already covered in
`code-quality-metrics-and-safe-coding.md`) is this same idea at its
simplest: "not found" is an expected outcome, not an exception.

## Across a network boundary, the exception *type* doesn't survive

An exception thrown server-side does not automatically reach a remote HTTP
client — it has to be deliberately translated to a status code (and usually
a small error envelope) on the way out, and deliberately reconstructed into
the matching exception on the way back in. Skipping either half breaks the
guarantee that calling code behaves the same regardless of which
`IServiceOrderRepository` implementation (in-process EF Core, or remote
REST) is wired in — and that guarantee is the entire point of coding against
an interface in the first place.

## The rule: pick a small, closed set of statuses and *document the mapping in both directions*

Don't invent a new envelope shape per endpoint. Settle on a status code +
minimal-payload convention per class of failure (not-found → 404, no body;
optimistic-concurrency conflict → 409, no body; validation failure → 400
with a structured payload the client can parse back into the specific
exception) and keep the server-side translation and the client-side
reconstruction next to each other in intent even though they live in
different projects — a docstring on one pointing at the other is enough; a
shared DTO type is overkill for a payload this small.

## Where this would apply in GeoAssets

This pattern is already implemented, not hypothetical — it's the reference
example, not a proposal:

- `ServiceOrdersRestApiExtensions.MapServiceOrdersApi`
  (`apps/GeoAssets.Server/ServiceOrdersRestApiExtensions.cs`) catches
  `ServiceOrderAttributeValidationException`, `InvalidServiceOrderTransitionException`,
  `ServiceOrderConcurrencyException`, and `KeyNotFoundException` at each
  minimal-API endpoint and maps them to `400`/`409`/`404` respectively, with
  `400` responses carrying just enough structured payload
  (`orderTypeId`/`errors`, or `from`/`to`) to reconstruct the specific
  exception client-side — not a generic "error message string."
- `RestServiceOrderRepository.EnsureWriteSuccessAsync`
  (`workflow/GeoAssets.Workflow.Rest/RestServiceOrderRepository.cs`) is the
  other half: it switches on `response.StatusCode` and re-throws the exact
  same exception types `EFServiceOrderRepository`/
  `ValidatingServiceOrderRepository` throw in-process, using the presence of
  specific JSON properties (`errors` vs. `from`/`to`) to disambiguate which
  `400` case it's looking at. Both files cross-reference each other in their
  doc comments — new domain exceptions added to `IServiceOrderWriter`'s
  contract must update *both* sides, or a REST-backed caller silently stops
  seeing the same exception a Postgres-backed caller sees for the identical
  failure.
- Read endpoints in the same file (`GetByIdAsync`, `GetParentAsync`) use the
  simpler, in-process-appropriate shape for a "not found" outcome — returning
  `null`/`Results.NotFound()` rather than throwing — consistent with the
  "expected outcome, not exceptional" guidance above; only the write path
  needs the fuller exception-translation machinery because writes are where
  the interesting failure modes (validation, transition rules, concurrency)
  live.
