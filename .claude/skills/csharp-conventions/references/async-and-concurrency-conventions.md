# Async/await and concurrency conventions

**Status: `current`**

**Source**: Microsoft Learn, [".NET Guide — Asynchronous programming with
async and await"](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/)
and ["Task-based Asynchronous Pattern (TAP)"](https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap).
Official documentation, not a single book/article — treat as the baseline;
defer to it over any paraphrase here if the two ever disagree.

## Naming and shape

- Suffix every `Task`/`Task<T>`/`ValueTask<T>`-returning method with `Async`.
  Non-async members that happen to return a `Task` (pure delegation, no
  `await`) keep the suffix too — the suffix names the *contract* (awaitable,
  potentially asynchronous), not the implementation.
- Accept a `CancellationToken ct = default` as the last parameter on any
  public async API that does I/O. Default it so callers who don't care aren't
  forced to pass `CancellationToken.None` everywhere, but always thread it
  through to the actual I/O call (`HttpClient`, EF Core, `Task.Delay`) rather
  than letting it dead-end as an unused parameter.

## Don't `async` a method that only forwards

If a method's body is just `return SomeOtherAsync(...)`, don't mark it
`async` and `await` the result — that adds a state machine and an extra
context hop for nothing. Return the inner `Task` directly. Reserve `async`
for methods that actually need to run code after an `await` (including
`try`/`catch`/`using` around the awaited call, which needs the state
machine to work at all).

## `ConfigureAwait(false)`

Library code (anything in `core/`, `providers/`, `workflow/` — code with no
UI/sync-context dependency) should use `ConfigureAwait(false)` on awaited
calls to avoid forcing a capture-and-resume through a synchronization context
that may not exist or may be expensive to marshal back to. Application code
that owns a UI thread (Blazor component event handlers, MAUI) should *not*
add it reflexively — those layers sometimes depend on resuming on the
original context, and ASP.NET Core itself has no synchronization context to
protect against in the first place, which is why this rule is scoped to
library code rather than applied uniformly everywhere.

## `Task` vs `ValueTask<T>`

Default to `Task<T>`. Reach for `ValueTask<T>` only for a hot path that
*frequently* completes synchronously (e.g. a cache-hit branch) where the
allocation of a `Task<T>` per call is measured to matter — `ValueTask<T>`
has sharp edges (must not be awaited twice, must not have `.Result`/`.
GetAwaiter().GetResult()` called on it more than once, doesn't compose with
`Task.WhenAll` without a conversion) that make it the wrong default for
ordinary application/library code.

## Fire-and-forget

A genuinely fire-and-forget `Task` (nothing awaits it, the caller wants to
continue immediately) must still be observed — assign it to a discard (`_ =
SomeAsync()`) so an unhandled exception doesn't get silently swallowed by
the GC finalizer, and wrap the body in its own `try`/`catch` if a failure
there shouldn't propagate anywhere. Never leave a `Task`-returning call as a
bare statement with no discard and no awaiting caller — that's the same bug,
just less visible in review.

## Cancellation is cooperative, not preemptive

A `CancellationToken` only stops work at points that actually check it —
`Task.Delay(ms, ct)`, `HttpClient` calls, EF Core's own `ct` parameters all
honor it; a tight CPU-bound loop doesn't unless it calls
`ct.ThrowIfCancellationRequested()` itself. When replacing a previous
in-flight operation (a new debounce cycle superseding an old one), cancel the
*old* token before starting the new operation, not after — cancelling after
the fact can race with the old operation's own completion and produce a
`TaskCanceledException` you didn't intend to surface to the caller.

## Where this would apply in GeoAssets

- `AssetService.QueueSaveAsync`-style debounce logic
  (`core/GeoAssets.Core/Services/AssetService.cs`) already follows the
  cancel-before-restart shape: it stores a `CancellationTokenSource`, cancels
  and replaces it on each call, then does `_ = Task.Run(async () => { await
  Task.Delay(500, token); ... })` — a correct fire-and-forget with the
  discard operator and a token-gated delay. Any change to this debounce
  should preserve both properties.
- `RestServiceOrderRepository`/`RestOrderTypeRepository`
  (`workflow/GeoAssets.Workflow.Rest/`) are a clean example of the
  don't-`async`-a-pure-forward rule: several members (`GetAllAsync`,
  `GetByStatusAsync`, etc.) return the inner `Task` directly without `async`/
  `await`, while methods that need post-await work (`AddAsync`,
  `EnsureWriteSuccessAsync`) correctly use `async`. New methods added to this
  client, or to the `KafkaOrderEventPublisher`/`ServiceBusOrderEventPublisher`
  message publishers, should follow the same split and keep threading the
  existing `CancellationToken ct = default` parameter through to the
  underlying `HttpClient`/producer call.
