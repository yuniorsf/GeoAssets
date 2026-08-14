# Records vs. classes: a general type-shape guide

**Status: `current`**

**Source**: Microsoft Learn, ["Records — C# reference"](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record)
and ["Choose between class and record types"](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/choose-between-class-and-record).
Official documentation. `domain-driven-design.md` already covers this
specifically for DDD *value objects*; this file is the same decision for
every other type in the codebase, since GeoAssets already has both shapes in
active use outside the DDD-tagged code.

## The question isn't "immutable vs. mutable," it's "identity vs. value"

A `record` gives you value-based equality (two instances with the same
property values are `==`), a compiler-generated `ToString()`, and
`with`-expression non-destructive mutation for free — genuinely useful when
a type's identity *is* its data: two `OrderDispatch`es with identical fields
are the same dispatch as far as any consumer cares. A `class` keeps
reference equality by default and is the right choice when identity is
independent of current field values — a `GeoFeature` with the same
`Name`/`Geometry` as another `GeoFeature` is still a *different* feature if
its `Id` differs, and mutating one shouldn't spawn a value-equal twin.

## Decision rule

- **Data flows in fully formed, is read, and is done** (an event payload, an
  RPC/DTO shape, a query result, a rule-evaluation context passed down a call
  chain) → `record` (positional syntax when all state is in the primary
  constructor). Get `with`-expression copying and structural equality for
  free, and immutability by default guards against a shared context object
  being mutated by one consumer and surprising another.
- **The type has a lifecycle** — constructed once, then mutated in place
  over time via property setters, often because it's deserialized from JSON
  and edited interactively (`GeoFeature` in the map UI) or built up
  incrementally by different callers (`TopoEdge.Metadata` populated after
  construction) → `class`, mutable properties, default values via
  property initializers.
- **A tiny, cheap, frequently-copied value with no independent identity**
  (a coordinate pair, a small struct-shaped DTO passed by value in a hot
  loop) → consider `readonly record struct` instead of either — GeoAssets
  doesn't currently have a case for this, but it's the third point on the
  same spectrum, not a separate decision.

`sealed` is the right default modifier either way unless the type is
deliberately designed as an extension point — this repo already applies
`sealed` consistently to both its records and its classes.

## Where this would apply in GeoAssets

The repo already splits cleanly along this line, which makes it a good
place to check any new type against rather than a hypothetical:

- **Classes** (identity + lifecycle): `GeoFeature`/`GeoFeatureProperties`
  (`core/GeoAssets.Core/Models/GeoFeature.cs`) and `TopoEdge`
  (`core/GeoAssets.Core/Models/TopoEdge.cs`) are both `sealed class` with
  mutable, default-initialized properties (`= new()`, `= []`,
  `= string.Empty`) — correct, since both are deserialized from GeoJSON and
  then edited in place through the map UI's `AssetForm`/context-menu flow,
  and a `GeoFeature`'s identity (its `Id`) is independent of its current
  property values.
- **Records** (value/data-in-motion): `OrderDispatch`
  (`core/GeoAssets.Workflow/Orders/OrderDispatch.cs`), `RuleEvaluationContext`
  and `RuleEvaluationResult` (`core/GeoAssets.Workflow/Rules/`),
  `OrderStateChangedEvent` (`core/GeoAssets.Workflow/Notifications/`), and
  `WorkflowState`/`WorkflowTransition`/`OrderCreationPolicy` (all in
  `core/GeoAssets.Workflow/Orders/OrderType.cs`) are all `sealed record` —
  each one flows into or out of a call once, fully formed, and is never
  edited in place afterward, matching the "data flows in, is read, is done"
  half of the rule.
- A new type modeling a similar shape (another event, another rule-context
  object, another log-entry-style record) should default to `record`;
  a new type representing UI-editable domain state should default to
  `class`, following `GeoFeature`'s existing shape.
