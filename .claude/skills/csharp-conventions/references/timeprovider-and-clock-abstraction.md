# Clock abstraction: `TimeProvider`, not `DateTime.Now`/`Stopwatch`/raw timers

**Status: `current`** — `TimeProvider` has been in the BCL since .NET 8, no
shim needed on GeoAssets' `net10.0` target.

**Source**: Microsoft Learn, ["Generate consistent DateTime assertions with
TimeProvider"](https://learn.microsoft.com/en-us/dotnet/core/extensions/timeprovider-overview)
and ["Testing with `TimeProvider` and
`FakeTimeProvider`"](https://learn.microsoft.com/en-us/dotnet/core/extensions/timeprovider-testing).
Official documentation.

## The rule

**Never call `DateTime.Now`/`DateTime.UtcNow`, `DateTimeOffset.Now`/`.UtcNow`,
`Stopwatch.StartNew()`/`new Stopwatch()`, `Task.Delay(ms)`,
`new PeriodicTimer(...)`, or `new CancellationTokenSource(timeout)` directly
in production code.** Inject `TimeProvider` (constructor injection, same as
any other dependency — see the DI guidance in
`design-patterns-and-dotnet.md`) and call its members instead:

| Instead of | Use |
|---|---|
| `DateTime.UtcNow` | `timeProvider.GetUtcNow()` (returns `DateTimeOffset`) |
| `Stopwatch.StartNew()` + `.Elapsed` | `timeProvider.GetTimestamp()` + `timeProvider.GetElapsedTime(start)` |
| `Task.Delay(ms, ct)` | `Task.Delay(TimeSpan, timeProvider, ct)` (the `TimeProvider`-aware overload) |
| `new PeriodicTimer(period)` | `timeProvider.CreateTimer(callback, state, dueTime, period)` — `PeriodicTimer` itself has no `TimeProvider`-aware constructor, so periodic work needs `CreateTimer`, not a wrapped `PeriodicTimer` |
| `new CancellationTokenSource(timeout)` | `timeProvider.CreateCancellationTokenSource(timeout)` |

Register the real clock once at each composition root —
`services.AddSingleton(TimeProvider.System)` — and let everything else
receive it through DI. Nothing in the repo does this yet (no
`TimeProvider` registration exists anywhere today); this is the target
shape, not a description of current state — see **Migration status** below.

## Why this is a correctness rule, not a style preference

A direct call to the system clock/timer inside a class makes that class's
time-dependent behavior untestable without a real wall-clock wait — the
same problem `code-quality-metrics-and-safe-coding.md`'s testability
guidance already flags for other hidden dependencies, just specific to
time. `TimeProvider` fixes this the same way any other DI abstraction fixes
a hidden dependency: a test substitutes
`Microsoft.Extensions.TimeProvider.Testing`'s `FakeTimeProvider` (not yet a
package reference anywhere in the repo — add it to the relevant test
project when the first `TimeProvider`-consuming class needs a test) and
advances virtual time deterministically (`fakeTimeProvider.Advance(...)`)
instead of actually sleeping.

## Two categories of default-timestamp property need different fixes

A model with `public DateTime CreatedAt { get; set; } = DateTime.UtcNow;`
can't simply take a constructor-injected `TimeProvider` if it's constructed
by JSON deserialization rather than `new T(...)` — a property initializer
runs regardless of how the object is materialized, so DI never gets a
chance to supply the clock. Two different fixes apply depending on how the
type is actually constructed:
- **Constructor-built types** (records, DDD entities built via `new`/a
  factory) — inject `TimeProvider` into the constructor/factory and stamp
  the timestamp there. Straightforward.
- **Deserialization-built types** — property-initializer defaults can't be
  intercepted this way. This needs either moving the timestamp assignment to
  wherever the object is *added* to a repository/collection (not where it's
  deserialized), or a different design entirely. Don't invent a fix here —
  this is an open, unresolved design decision for GeoAssets specifically
  (see **Migration status** below); flag it rather than silently picking an
  approach when touching one of these types.

## Migration status — read before assuming this is already done

This rule describes the target state GeoAssets is actively migrating
toward, tracked as Jira epic **XD01-34** (5 child tickets: XD01-35 DI
bootstrap, XD01-36 behavior-critical call sites, XD01-37 domain audit
timestamps, XD01-38 plain-model default timestamps — the deserialization
problem above — XD01-39 test infra/`FakeTimeProvider` coverage). As of this
writing, implementation (Phase 3) has not started: a repo-wide check found
zero `TimeProvider` usages anywhere and ~28 files still calling
`DateTime.Now`/`.UtcNow`/`Stopwatch`/`Task.Delay`/`PeriodicTimer` directly.
Apply this rule to **new code and any file you're already touching for
another reason** — don't go out of your way to bulk-migrate unrelated files
as a drive-by; that's XD01-34's own scoped work. Check the epic's child
tickets in Jira before starting dedicated migration work on any of them, since
scope may have shifted since last recorded.

## Where this would apply in GeoAssets

- `SessionTimeoutService` (`apps/GeoAssets.Web/Services/Session/SessionTimeoutService.cs`)
  is a concrete, currently-untested case of two violations at once:
  `_lastActivity = DateTime.UtcNow` and `using var timer = new
  PeriodicTimer(TimeSpan.FromSeconds(1))` — exactly the behavior-critical
  session-timeout logic XD01-36 scopes in.
- `ObservableDecoratorBase` (`apps/GeoAssets.Shared/Services/Observability/ObservableDecoratorBase.cs`)
  calls `Stopwatch.StartNew()` at three separate call sites for latency
  measurement — the `GetTimestamp()`/`GetElapsedTime()` replacement above
  applies directly, and is explicitly in XD01-36's scope per the epic.
- `WmsPostGisRenderer.QueryTimeout` (`apps/GeoAssets.Server/WmsPostGisRenderer.cs`)
  constructs `new CancellationTokenSource(QueryTimeout)` at three call
  sites — the `CreateCancellationTokenSource(timeout)` replacement applies
  here.
- `GeoFeature.CreatedAt`/`UpdatedAt` and `GeoFeatureCollection.CreatedAt`
  (`core/GeoAssets.Core/Models/GeoFeature.cs`,
  `.../GeoFeatureCollection.cs`) are the deserialization-built case from
  above verbatim — both default via `= DateTime.UtcNow` property
  initializers, and `GeoFeature` is built mainly through
  `System.Text.Json` deserialization rather than `new GeoFeature(...)`,
  which is exactly why XD01-38 marks this an open design decision rather
  than a mechanical find-and-replace.
- `AssetService`'s 500ms auto-save debounce
  (`core/GeoAssets.Core/Services/AssetService.cs`, also referenced in
  `async-and-concurrency-conventions.md`) uses `Task.Delay(500, token)`
  directly and currently has zero test coverage — the motivating example
  for why this rule exists at all, per the epic's own stated rationale.
