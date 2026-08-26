# Closed hierarchies, union types, closed enums

**Status: `future` — do not use or recommend yet.** Ships in C# 15 with the
.NET 11 preview cycle; GA targeted **November 2026**. .NET 11 is an STS
release, not LTS — GeoAssets targets **.NET 10**. Revisit this file and
re-evaluate the codebase once .NET 11 reaches GA (see Jira ticket tracking
this).

**Source**: Bharat Chandera, ["The Inheritance Problem C# Has Had Since Day
One Just Got Fixed"](https://medium.com/@bharat.chandera/the-inheritance-problem-c-has-had-since-day-one-just-got-fixed-7fd5ce01cbac),
Medium, 2026. Single-author account of an in-flux preview design, not
official Microsoft documentation — verify against the live Roslyn/C# design
proposals before relying on exact syntax or error codes.

## The problem

C# has only ever had two inheritance modes: `abstract` (open to any
consumer) or `sealed` (closed to all). There was no way to say "extendable,
but only by the types I explicitly designed for" — so exhaustive `switch`
over an `abstract` hierarchy always needs a `_` / default arm, because the
compiler can't prove no other assembly added a subtype.

## The three features

1. **`closed` hierarchies** — modifier restricting subclassing to the
   declaring assembly.
   - Implicitly `abstract`; cannot combine with `sealed`, `static`, or an
     explicit `abstract` modifier.
   - Contextual keyword — `@closed` escapes a genuine naming collision.
   - **Not transitive**: closing the root only restricts *direct* children.
     Descendants of an already-open child remain open unless marked
     individually. Deliberate — lets a library close the root for
     exhaustiveness while leaving specific branches open for legitimate
     extension (plugins, strategy pattern).
   - Enables real exhaustiveness checking in `switch` — no `_` arm needed,
     and adding a case without updating a switch is a compile error, not a
     runtime `InvalidOperationException`.
   - Applies to classes and record classes, not structs.

2. **`union` types** (`.NET 11 Preview 2+`) — declare a closed set of
   *unrelated* types with no shared ancestor required:
   ```csharp
   public union Pet(Cat, Dog, Bird);
   ```
   - Compiles to a struct with one constructor per case and a single
     `object? Value`; value-type cases get boxed.
   - Pattern matching unwraps `Value` automatically (`Dog d`, not
     `pet.Value is Dog d`). `var`/`_` bind the whole union.
   - If any case type is nullable, every switch needs a `null` arm — a
     union's default state is maybe-null.
   - Escape hatch for hot paths: a hand-written type with `[Union]` +
     `Value`/`HasValue`/`TryGetValue<T>` is recognized by the compiler
     without boxing.
   - **Known gap**: "union member providers" (calling a member shared
     across all case types directly on the union) aren't implemented yet —
     still need `.Value` + a switch even for a shared property.
   - Not interoperable with F# discriminated unions at the CLR level.

3. **Closed enums** — blocks constructing/casting arbitrary int values into
   an enum that don't correspond to a declared member (e.g. `(DayOfWeek)99`
   currently compiles and is a landmine). Least detailed of the three in the
   source article.

## Decision rule (once adoptable)

- Cases **share a conceptual identity** → `closed` hierarchy.
- Cases are **fundamentally unrelated types** being composed for one purpose
  → `union`.
- Extension point is meant for **consumers you don't control** (plugin
  architecture) → keep plain `abstract`. Don't retrofit every abstract class
  — the value is highest for small, known, rarely-changing case sets (state
  machines, result types, domain events).

## Where this would apply in GeoAssets (evaluate at GA, not before)

`GeoGeometry` → `GeoPoint` / `GeoLineString` / `GeoPolygon`
(`core/GeoAssets.Core/Models/Geometry/`) is a small, fixed, known-in-advance
set of subtypes within a single assembly — the shape `closed` targets. Any
adoption decision should happen only after .NET 11 GA and a check that the
rest of the stack (EF Core provider, Blazor WASM, MAUI) supports the
target framework.

## Preview rough edges (won't apply post-GA, kept for context)

- Early preview SDKs may throw `CS0656: Missing compiler required member
  'System.Runtime.CompilerServices.ClosedAttribute..ctor'` — attribute name
  drifted between CLI and VS toolsets (`ClosedAttribute` vs
  `IsClosedTypeAttribute`) before the BCL stabilized.
- Treat the union shape as "close to final, not frozen" per the source
  article — the design team was still debating member providers.
