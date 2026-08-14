# Domain test-data builders

**Status: `current`**

**Source**: Microsoft Learn, ["Unit testing best practices with
.NET"](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)
(Arrange section — object-mother/builder guidance). Official documentation.
`testing-strategy-and-dotnet-tooling.md` already covers test anatomy
(Arrange/Act/Assert), xUnit/Moq mechanics, and TDD/BDD generally — this file
is specifically about *how test fixtures for GeoAssets' domain types get
constructed*, a gap that's visible in the test suite today: a repo-wide
search found 62 raw `new GeoPoint(...)`/`new GeoPolygon(...)`/
`new GeoLineString(...)` call sites across `tests/`, with no shared builder.

## When ad-hoc construction stops being fine

A single `new GeoPoint(1, 2)` inline in one test is fine — it's obvious and
needs no abstraction. The problem is scale: the same shape of construction
repeated across dozens of test files means any future change to how a
`GeoFeature`/`GeoPoint`/`TopoEdge` is validly constructed (a new required
property, a changed default) becomes a multi-file find-and-replace instead
of a one-place edit. The signal to promote to a builder is repetition across
*files*, not repetition within one file — a local private helper scoped to
one test class (see the existing `TopoGraphTests` example below) is often
the right-sized fix and doesn't need to become a shared builder until a
second test class wants the same shape.

## Builder shape: fluent `With...()` over a domain object, sensible defaults

Follow the same Builder pattern already described in
`design-patterns-and-dotnet.md` for production code — a test builder is the
same pattern applied to test fixtures. Give every property a valid default
so a caller only overrides what the specific test cares about
(`GeoFeatureBuilder.Default().WithGeometry(...).Build()`), rather than
requiring every test to specify every field. This is what keeps a test's
`Arrange` section readable — the properties that vary between test cases
are visible, the ones that don't are hidden in the default.

## Don't let a builder hide behavior under test

A builder should assemble *valid, uninteresting* fixture state — it should
never contain the logic under test. If a test is specifically about how
`TopoGraph.HasCycles` behaves, the graph's shape (which edges exist) belongs
in that test's `Arrange`, explicit and readable, even if it's built via a
shared builder's fluent calls — don't let the builder itself decide what
graph shape to produce based on a flag like `WithCycle(true)`, which moves
the interesting part of the test into code the reader of the test doesn't
see.

## Where this would apply in GeoAssets

- `TopoGraphTests` (`tests/GeoAssets.Core.Tests/Services/TopoGraphTests.cs`)
  already has the right instinct at file scope: private `F(id, targets)`/
  `Fw(id, edges)` helpers building a `GeoFeature` with a `Topology` list from
  a compact params syntax, instead of spelling out `new TopoEdge { ... }`
  inline per test. If a second test class (e.g. one for
  `InMemoryAssetRepository`'s topology query methods) needs the same shape,
  that's the trigger to promote `F`/`Fw` into a shared
  `core/GeoAssets.Core.Tests`-wide helper rather than duplicating them.
- The 62 raw `new GeoPoint(...)`/`new GeoPolygon(...)`/`new GeoLineString(...)`
  call sites spread across `tests/GeoAssets.Workflow.Tests/`,
  `tests/GeoAssets.Core.Tests/`, and others are the concrete case for a
  shared geometry builder — most construct simple, valid points/polygons for
  spatial-predicate or repository tests where the exact coordinates aren't
  the point of the test, which is exactly the "uninteresting fixture state"
  a builder should absorb.
- No shared builder exists yet for repository-pool / `IExternalRepositoryFactory`
  scenarios (no tests reference that interface today) — treat that as a
  builder to add *when* such tests are written, following the same shape,
  not a retrofit needed now.
