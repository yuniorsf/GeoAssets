# Nullable reference types

**Status: `current`**

**Source**: Microsoft Learn, ["Nullable reference
types"](https://learn.microsoft.com/en-us/dotnet/csharp/nullable-references)
and ["Nullable reference types: attributes on API
signatures"](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis).
Official documentation, not a single book/article.

Every GeoAssets project has `<Nullable>enable</Nullable>` set (verified
across all `.csproj` files in the repo) — this isn't an aspirational goal,
it's already the compiler mode every line of code is written under. This
file is about using that mode well, not about whether to turn it on.

## `?` states a real possibility, not a formality

A `string?` return type is a promise to callers: "read this contract, you
must handle null." Don't reach for `?` reflexively on every reference-type
member — reserve it for members that can *genuinely* be absent (an optional
lookup result, an unset configuration value). A member that's always
populated by the time it's observable (set in the constructor, defaulted to
`new()`/`string.Empty`/`[]`) should stay non-nullable — that's what lets a
`?` elsewhere carry real information instead of being noise the reader has
learned to ignore.

## `!` (null-forgiving) is a documented exception, not a silencer

Every `!` is a claim to the compiler "I know more than static analysis
does here" — and every one of those claims is a place the compiler can no
longer catch you if you're wrong. Acceptable uses: right after a guard
clause the analyzer can't follow (`Dictionary.TryGetValue` patterns before
C# added the nullable-aware overloads, or a `Debug.Assert` immediately
before), or in test code building known-valid fixtures. Not acceptable: using
`!` to make a warning disappear without checking why the compiler thought
the value could be null — that's turning off the exact protection nullable
reference types exist to provide.

## `required` over constructor boilerplate for mandatory init state

For a type whose valid state demands certain members be set, prefer
`required` properties (`public required string Name { get; init; }`) over
a constructor with matching parameters, when the type is a simple
data-holder rather than one enforcing invariants beyond "these fields are
present." `required` gives the same compile-time guarantee with less
ceremony; reach for a constructor instead when construction needs to run
actual validation logic or compute derived state.

## Nullable-oblivious boundaries

The moment nullable annotations leave GeoAssets' own code — deserializing
untrusted JSON, reading data an EF Core provider fetched from Postgres,
crossing into a NuGet package built without nullable annotations (an
"nullable-oblivious" assembly) — the compiler's guarantee stops. Treat data
crossing those boundaries as possibly-null even if the static type says
otherwise, and validate/guard at the boundary rather than trusting the
annotation propagates through external I/O.

## Don't let annotations drift from the shape of an entity/persistence layer

An EF Core entity mapped to a nullable database column must be annotated
`?` to match — annotating it non-nullable just to silence a warning creates
a lie the compiler will happily repeat back as fact. When a provider or
entity's nullability doesn't match its true persistence shape, fix the
annotation to match reality rather than suppressing the warning.

## Where this would apply in GeoAssets

- `GeoFeature.Geometry` (`core/GeoAssets.Core/Models/GeoFeature.cs`) is
  already a correct example of the "real possibility" rule: it's `GeoGeometry?`
  because a feature genuinely can exist without geometry attached yet, while
  `GeoFeature.Properties`/`Topology` on the same class are non-nullable and
  default-initialized (`= new()`, `= []`) because they're never legitimately
  absent — new nullable annotations added to this file or sibling model types
  should follow that same split rather than defaulting every reference type
  to `?`.
- The Postgres provider boundary (`providers/GeoAssets.Provider.PostgreSQL/`)
  and the REST client (`workflow/GeoAssets.Workflow.Rest/`) are exactly the
  "nullable-oblivious boundary" case — data crossing from Npgsql/EF Core or
  from a deserialized HTTP response should be treated as untrusted regardless
  of what the local C# type says, since neither the database nor the wire
  format enforces C#'s nullable contract.
