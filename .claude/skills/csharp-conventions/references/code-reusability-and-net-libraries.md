# Code reusability, .NET libraries, and refactoring triggers

**Status: `current`** — general engineering practice; the .NET Standard vs.
target-framework guidance and the OpenAPI/Swashbuckle note are current as of
.NET 9+, which GeoAssets already meets.

**Source**: Gabriel Baptista & Francesco Abbruzzese, *Software Architecture with
C# 14 and .NET 10*, 5th Edition (Packt Publishing, 2026), Chapter 5
"Implementing Code Reusability in C#." Distilled and paraphrased.

## Reuse is a strategic decision, not a default

Reusability trades a higher up-front cost (stronger abstractions,
documentation, longer-term support obligation) for lower cost every time the
component is reused. That trade only pays off when something is genuinely
going to be reused — over-engineering a single-use piece of code into a
"reusable" library is pure cost with no payoff. Treat "should this be
reusable?" as an explicit architectural judgment call per component, not a
blanket policy.

## Copy-paste is not code reuse

Duplicating a method across several classes (rather than centralizing it
behind a shared abstraction) means every future fix or spec change has to be
found and applied N times — miss one and you have a silent, inconsistent
implementation living in production. This is exactly what the DRY principle
(Andrew Hunt & David Thomas) targets: not just "don't repeat yourself"
mechanically, but reuse *with confidence* that the one implementation is
correct everywhere it's used.

The centralization trade-off: once shared behavior lives behind one
abstraction, a bug or design flaw in it affects every consumer — which is
exactly why it's worth testing thoroughly and designing its interface
carefully **before** multiple call sites depend on it, not after.

## The reuse lifecycle

A repeatable process for deciding what becomes a reusable component, rather
than reuse happening ad hoc:

1. **Use** what already exists in the reusable library before writing
   something new.
2. **Identify** candidates — features likely to recur across products
   (auth, auditing, integration adapters) surfaced during requirements
   analysis.
3. **Modify the spec** to distinguish what the existing library already
   satisfies from what needs new development.
4. **Design** the component around a stable interface usable across multiple
   consuming projects — the interface is the part that has to be right, since
   consumers couple to it directly.
5. **Build** the consuming architecture against the new/updated library
   version, checking compatibility and performance.
6. **Document** it and make sure the team actually knows it exists — an
   unreused reusable component (because nobody knew about it) is wasted
   design cost.

## Targeting frameworks: .NET Standard vs. netX.0

.NET Standard was the historical mechanism for one library targeting multiple
runtimes (.NET Framework, Xamarin, older .NET Core) — a formal API-surface
specification, not a project type. For **new** libraries on unified .NET (5+,
including 10), treat .NET Standard as a **compatibility target for legacy
consumers only** — prefer targeting a concrete `netX.0` TFM (target framework
moniker) directly. .NET 10 is LTS (long-term support), which makes it a solid
default choice for new shared code today rather than something to hedge
against with .NET Standard.

## Object-oriented reuse mechanics

- **Inheritance/polymorphism**: a shared base class defines common state and
  a virtual method; subclasses override only what actually differs. Valid and
  effective for small, shallow hierarchies — but see the "Code quality
  metrics" reference (depth-of-inheritance) for when this tips into a
  liability. Composition over inheritance still applies once a hierarchy
  would otherwise grow deep.
- **Generics** (since C# 2.0): a placeholder type resolved at the consuming
  call site, letting one implementation serve many concrete types safely and
  fast (no boxing/casting, compile-time type checking). The `new()`
  constraint on a generic parameter is specifically for when the generic
  code needs to *instantiate* `T` itself — without it, the compiler won't
  allow `new T()`, and the caller would need to supply a factory or an
  existing instance instead.

## Signs code is *not* ready to be reused as-is

Reusability isn't binary — these are the concrete disqualifiers, and each
maps to a specific fix:

- **Untested** → a latent bug becomes a system-wide bug the moment it's
  reused. Test first.
- **Duplicated** → consolidate to one implementation before promoting it,
  otherwise you're promoting the duplication problem, not solving it.
- **Too complex to understand** → teams will avoid a reusable component they
  can't follow, and quietly re-implement it instead — defeating the point.
- **Tightly coupled** → a base class or a component wired to many concrete
  dependencies forces every consumer to adopt the same dependency graph.
  Interfaces (composition) travel much better across project boundaries than
  base classes do.

Refactoring is the path from any of these states to "actually reusable" —
but only safely with tests already in place to prove behavior didn't change;
refactor to eliminate duplication and reduce complexity, not to add features.

## Documentation and distribution

- XML doc comments (`///`) on the public surface are the .NET-native way to
  document a library, and tooling (including AI assistants) can generate
  them from the code and generate/update a README from that.
- NuGet is the standard distribution mechanism for a class library meant to
  be consumed by multiple projects — publishing is a `dotnet pack` +
  `dotnet nuget push` step, and a freshly published package can take up to an
  hour to finish indexing/security scanning before it's publicly searchable.
  For anything not meant to be broadly public, code obfuscation and
  key/credential-based usage authorization are worth considering at publish
  time, not after the fact.
- **API documentation**: for a Web API (as opposed to a class library),
  OpenAPI/Swagger is the standard, and **from .NET 9 onward, first-party
  OpenAPI document generation ships in ASP.NET Core itself**
  (`Microsoft.AspNetCore.OpenApi`) — `Swashbuckle.AspNetCore` is still valid
  and widely used, but it is **no longer the default in project templates**.
  This changes the *recommended default*, not the validity of either choice —
  worth knowing before defaulting to Swashbuckle out of habit.

## Where this would apply in GeoAssets

- The repo already follows the shape of the reuse lifecycle informally —
  `GeoAssets.Core`, `GeoAssets.Identity`, and `GeoAssets.Workflow` are exactly
  the kind of cross-cutting, multi-consumer libraries this chapter describes
  (used by both `apps/GeoAssets.Web` and, per `ServiceOrder.md` §9, the
  agent-orchestration module) — no NuGet publishing needed since they're
  in-repo project references, but the same "design a stable interface before
  multiple consumers depend on it" discipline applies.
- No project in the solution currently references `Swashbuckle.AspNetCore`
  or `Microsoft.AspNetCore.OpenApi` (checked — zero matches). If/when
  `GeoAssets.Server`'s REST endpoints get OpenAPI documentation (relevant
  once the Phase 1 authorization work in `Authorization.md` starts protecting
  those endpoints and documenting the permission model matters), the
  first-party `Microsoft.AspNetCore.OpenApi` package is the current
  Microsoft-recommended default for a .NET 9+ host like this one, not
  Swashbuckle.
