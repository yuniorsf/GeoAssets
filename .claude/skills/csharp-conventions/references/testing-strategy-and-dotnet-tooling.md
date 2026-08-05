# Testing strategy: unit/integration/acceptance, TDD/BDD, and .NET tooling

**Status: `current`** — general testing methodology plus current xUnit/Moq/
`Microsoft.AspNetCore.Mvc.Testing`/Selenium tooling.

**Source**: Gabriel Baptista & Francesco Abbruzzese, *Software Architecture with
C# 14 and .NET 10*, 5th Edition (Packt Publishing, 2026), Chapter 9 "Testing
Your Enterprise Application." Distilled and paraphrased.

## Why three test tiers, not one

The combinatorial argument: if method A has 3 execution paths and method B has
4, testing them *together* needs up to 3×4 = 12 input combinations to cover
every path pair; testing them *separately* only needs 3 + 4 = 7. That gap
grows multiplicatively with every additional module tested together — which is
the concrete reason to test small and isolated first, then verify interaction
separately with far fewer cases:

- **Unit tests** — one class/method at a time, free of external dependencies
  (DB, network), aiming for near-complete path coverage. Cheap because each
  method has few paths compared to the whole app.
- **Integration tests** — run after units pass; verify modules interact
  correctly. Don't need to re-cover every execution path (units already did
  that) — just the interaction patterns between modules.
- **Acceptance tests** — run at the end of a sprint/before release; verify the
  build satisfies functional requirements (**functional tests**) and
  non-functional/performance requirements (**performance tests**) —
  functional-test failures mean *what* the system does is wrong; performance-
  test failures mean *how* it does it is wrong. Different problems, different
  fixes.

A useful heuristic for picking representative inputs for one interaction
pattern: **equivalence classes**. For an array-shaped input, `null`, empty,
single-element, and many-element aren't 4 arbitrary examples — they're a
small set that stands in for the entire space of possible arrays. Reach for
this shape of thinking any time an input type has structurally distinct
categories, not just "pick a few examples."

## Anatomy of an automated test

Every framework structures a test the same way regardless of vendor:

1. **Arrange** — build the environment/data a test (or several) needs; when
   this preparation is expensive or shared, factor it out (see Fixtures
   below) instead of repeating it per test.
2. **Act/Assert** — invoke the method under test, compare actual vs. expected
   results.
3. **Tear-down** — clean up so one test can never influence another.

**Mocking** only matters for collaborators that have real *behavior* — a pure
data class with no methods can't introduce a bug into the class under test,
so it doesn't need mocking. Only interfaces (or things abstracted behind one)
can be fully mocked; that's a concrete reason to inject dependencies through
interfaces rather than concrete classes even outside of DI/DIP concerns.

## TDD: why writing the test first actually catches bugs

TDD treats unit tests as **example-based specifications** written *before* the
code. The counterintuitive part: both the test and the code could independently
be wrong — but the probability of making the *exact same* mistake in both,
written from two different angles, is low. That's the actual mechanism that
makes TDD effective: a real defect shows up almost immediately as *some*
failing test, not because the tests are a perfect spec, but because
code/test errors rarely coincide.

Picking test inputs doesn't require infinite or random inputs — it requires
one representative example per *distinct execution path*, forecast **before**
writing real code (extreme/edge cases first), with new cases added only when
coding reveals a path you didn't anticipate.

**Red → Green → Refactor loop**:
1. **Red** — write the test(s) against a not-yet-implemented method (it
   should fail, ideally because the method doesn't exist yet or throws
   `NotImplementedException`).
2. **Green** — write the *minimum* code that makes all current tests pass.
   Don't over-build past what the tests currently demand.
3. **Refactor** — clean up now that behavior is pinned down by passing tests.
   This step often surfaces new edge cases/paths you hadn't tested yet — loop
   back to Red for those. The loop ends when a refactor pass makes no further
   changes.

## BDD: TDD's specs made stakeholder-legible

**Behavior-Driven Development** applies the same "two independent
descriptions rarely share the same mistake" principle as TDD, but writes the
example in a **Given/When/Then** (Gherkin) syntax expressed in the target
user's language instead of code — so specs can be understood and validated by
stakeholders directly, not just other developers, and stay independent of
implementation choices. This pays off specifically for **functional tests**,
where stakeholder legibility is the whole point. For unit tests on
already-concrete classes, plain TDD usually isn't worth trading for BDD's
extra translation overhead — BDD earns its keep more clearly when specifying
the behavior of an *abstract* interface with multiple implementations, where
a shared, implementation-independent spec has real value.

## .NET test tooling shape

- **xUnit/NUnit/MSTest** are structurally similar — they differ mainly in
  attribute and assertion naming, not in capability. None include mocking out
  of the box.
- **`[Fact]`** = a single, non-parameterized test. **`[Theory]`** = the same
  test logic run once per data tuple, supplied via `[InlineData]`, a class
  implementing `IEnumerable` (via `[ClassData]`), or a static member (via
  `[MemberData]`) — the point is avoiding near-duplicate test methods that
  only differ in their input values.
- **Fixtures**: xUnit creates a fresh test-class instance *per test method*,
  so expensive one-time setup (e.g. opening a DB connection) can't live in the
  constructor — it has to be factored into a separate fixture class the test
  class receives via `IClassFixture<T>` (shared across one class) or a
  collection-definition marker (shared across a whole named collection of
  test classes).
- **Moq**: `new Mock<IInterface>()` + `.Setup(x => x.Method(...)).Returns(...)`
  defines behavior per input (including `It.IsAny<T>()` wildcards);
  `.ReturnsAsync(...)` for async members; `.Verify(x => x.Method(...),
  Times.AtLeast(n))` asserts the mock was actually *called* the expected
  number of times — useful for asserting on interaction, not just on a return
  value.
- **AI-generated tests** (Copilot or similar) are best used as a completeness
  *check* against tests you wrote manually under TDD, or to backfill coverage
  on legacy code that has none — not as a substitute for TDD's actual
  mechanism, since a generator can only validate against code that already
  exists, never against intended behavior the code might have gotten wrong.

## Functional/web testing specifics

Two independent decisions shape how a functional test for a web app gets
built:

1. **Does the endpoint return HTML, or structured data?** Structured
   data (JSON/XML) just needs an `HttpClient`. HTML needs a browser-driving
   tool (Selenium, Playwright) to interact with and inspect the DOM.
2. **Controlled environment vs. staging environment?** A controlled
   environment (an app instance spun up per test) gives full control of
   initial state — good for edge cases, fast, in-memory-DB-friendly — but is
   further from real production behavior. A staging environment (the real
   deployed app) is closer to reality but its initial state can't be
   controlled per test. Practical split: **controlled for edge cases,
   staging for average-case realism checks** — neither one alone covers both
   needs well.

**Subcutaneous tests** — calling application logic directly (e.g. invoking a
controller action method) instead of going through the UI/HTTP layer — trade
full input control for realism, and have one sharp limitation worth
internalizing: **they bypass the entire request pipeline**, which for
ASP.NET Core means authentication, authorization, and CORS middleware are
never exercised. A subcutaneous test proves the *action method's* logic is
correct; it proves nothing about whether the pipeline in front of it actually
enforces auth/authz — that requires a real HTTP round-trip test instead.

`Microsoft.AspNetCore.Mvc.Testing`'s `WebApplicationFactory<TEntryPoint>` is
the standard way to spin up an in-process ASP.NET Core server + `HttpClient`
for controlled-environment functional tests that *do* go through the real
pipeline. One concrete gotcha: the generic type argument must be a **public**
entry-point class — a top-level-statements `Program.cs` compiles that class
as `internal` by default, so it needs an explicit
`public partial class Program { }` marker added, or `WebApplicationFactory`
has nothing it can reference.

**Resetting a real test database** between runs has four options, none
universally best: drop/recreate via scripts or EF migrations (always
correct, slow, needs elevated DB privileges); a fresh Docker container per
test (fast reset, but setup effort and still not instant); disabling
constraints and clearing all tables in any order (fast, but sometimes fails
and still needs elevated privileges); or maintaining an explicit,
dependency-ordered delete list (no elevated privileges needed, and the list
doubles as a useful artifact for fixing real production migration issues —
but fails on circular foreign-key references and needs upkeep as the schema
grows).

## Where this would apply in GeoAssets

- GeoAssets already follows the unit-test-first, near-complete-coverage
  discipline this chapter describes: `GeoAssets.Workflow.Tests` and
  `GeoAssets.Workflow.Agents.Tests` follow a stated 100%-line/branch-coverage
  convention (`ServiceOrder.md`), and the XD01-4 implementation
  (`Authorization.md` §6) added 9 tests alongside the fix itself, including
  explicit non-leakage proofs — the "prove the negative case too" instinct
  this chapter's equivalence-class thinking argues for.
- Checked: no project in the solution references Moq or NSubstitute, and
  nothing uses `WebApplicationFactory` yet — makes sense today, since
  `ServiceOrderRules`' tests operate on plain `WorkflowPrincipal`/`ServiceOrder`
  objects with no behavior worth mocking, and `GeoAssets.Server` has no
  middleware pipeline yet to test through.
- **This becomes directly load-bearing once `Authorization.md`'s Phase 1
  work starts** (XD01-13 the AuthZ bridge, XD01-15 endpoint protection): the
  subcutaneous-test pitfall above is exactly the trap to avoid there — a test
  that calls a REST endpoint's handler method directly will never exercise
  the `IAuthorizationHandler`/JWT-validation middleware that Phase 1 adds, so
  it can pass while the actual authorization is broken or missing. Those
  tickets' tests need a real `WebApplicationFactory<TEntryPoint>`-style
  round-trip (with the `public partial class Program` marker, since
  `GeoAssets.Server`'s `Program.cs` currently uses top-level statements) —
  or, for the endpoint list itself, `Microsoft.AspNetCore.OpenApi`
  (per the code-reusability reference) plus this testing approach are worth
  planning together rather than as separate concerns.
