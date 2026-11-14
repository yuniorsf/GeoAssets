# Async Concurrency, Cancellation & Clock Abstraction

**Status: `current`** — language-agnostic determinism and concurrency discipline.

## 1. Never Read Global System Wall-Clock Directly

Direct calls to global clocks (`DateTime.Now`/`UtcNow`, `Date.now()`, `datetime.now()`, `time.Now()`, `Instant::now()`) inside domain or application services make time-dependent rules (expiration, retries, SLA windows, debounce, audit timestamps) non-deterministic and untestable without real sleeps:

- Define or use the runtime's standard **Clock / `TimeProvider`** abstraction and inject it explicitly.
- In automated tests, inject a **deterministic fake/manual clock** and advance virtual time instantaneously.

## 2. End-to-End Cancellation Propagation

Every asynchronous or I/O-bound operation must accept and forward the stack's native cancellation primitive (`context.Context` in Go, `AbortSignal` in TypeScript/JS, `CancellationToken` in C#/Rust, structured task cancellation in Python/Kotlin):

- Never drop a caller's cancellation token at an internal helper boundary.
- Respect cancellation promptly before expensive loops or outbound network/database calls.

## 3. Zero Sync-Over-Async or Unchecked Fire-and-Forget

- **Never block threads/event loops** waiting synchronously on async futures/promises/tasks (`.Result`, `.Wait()`, `asyncio.run` inside running loops, blocking I/O on async workers) — this causes thread-pool starvation and deadlocks.
- **Never spawn untracked fire-and-forget background tasks** without structured error boundaries, cancellation wiring, and trace-context propagation.
