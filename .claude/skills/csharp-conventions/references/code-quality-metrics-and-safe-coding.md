# Code quality metrics, safe coding, and static analysis

**Status: `current`** — general C#/.NET engineering practice, not gated by a
language or runtime version. Applies as-is on .NET 9 / C# 13 and GeoAssets'
configured `LangVersion 14`.

**Source**: Gabriel Baptista & Francesco Abbruzzese, *Software Architecture with
C# 14 and .NET 10*, 5th Edition (Packt Publishing, 2026), Chapter 4 "Best
Practices in Coding C#." Distilled and paraphrased, not a reproduction — verify
exact tool menu paths/thresholds against current Visual Studio docs before citing
them as authoritative.

## Code metrics as decision tools, not a scoreboard

The point of these metrics isn't to chase a number — it's that each one names a
*specific* maintenance risk, so a bad score tells you what to actually fix:

- **Maintainability index** (0–100, Visual Studio's Code Metrics tool): above 75
  is healthy, 50–75 is borderline, below 50 needs refactoring. Driven by single
  responsibility, low duplication, and short methods — it's a composite, not an
  independent lever.
- **Cyclomatic complexity** (McCabe): count of independent paths through a
  method. Keep it under 10. Nested `if`/`else` inside every `switch` arm, loops
  inside loops, and repeated per-case logic are the usual causes. The fix is
  usually one of: an `enum` instead of string/int discriminators, one method per
  case (extracted, or via inheritance/interface implementing a shared contract),
  or a `Dictionary<TKey, Action/Func>` / `switch` *expression* replacing a
  `switch` *statement* full of side effects.
- **Depth of inheritance**: the more classes between a type and its root, the
  harder any change to a base class is to reason about — a change ripples down
  through every descendant. Deep chains are a signal to reach for **composition
  over inheritance**.
- **Class coupling**: how many other types a class directly depends on. Recent
  Microsoft guidance suggests keeping this under ~9. The concrete fix is
  extracting an interface for the varying part and depending on the interface
  instead of concrete collaborators — this doesn't reduce the *number* of
  relationships but decouples the dependent class from concrete implementations,
  which is what actually costs you at change time.
- **Lines of code**: not a complexity measure by itself, but a smell — a class
  over ~1,000 lines (1 KLOC) usually means responsibilities have been merged
  that shouldn't be.

The architectural point: low coupling + high cohesion (each class's own methods
and data are tightly related to each other, and only loosely related to other
classes') is the target shape these metrics are all indirectly measuring.

## Try-catch, try-finally/using, and IDisposable

- **Never leave a `catch` block empty.** An empty catch converts a real failure
  into silent wrong behavior — worse than crashing, because it hides the
  problem until much later, somewhere unrelated. Catch the *specific* exception
  type you can actually handle; catching bare `Exception` at a low level hides
  bugs you didn't anticipate. Reserve broad catches for a single top-level
  boundary handler, not scattered through the codebase.
- Prefer `TryParse`-style APIs over throwing/catching for expected failure paths
  (e.g. `int.TryParse` instead of `Convert.ToInt32` wrapped in try-catch) —
  exceptions are computationally expensive whether thrown or caught, so reserve
  them for genuinely exceptional conditions, not routine validation.
- **Anything holding an unmanaged or I/O resource the GC doesn't manage**
  (file handles, sockets, and — by extension — any class that *owns* a
  long-lived `IDisposable` member) needs `using`/`try-finally`, or, at the class
  level, needs to implement `IDisposable` itself and dispose its owned members.
  The GC will eventually collect the managed wrapper, but not promptly, and it
  won't release the unmanaged resource behind it.
- If a class is never meant to be subclassed, seal it — a sealed disposable
  class only needs the plain `Dispose()` method, not the full
  `Dispose(bool disposing)` + finalizer pattern IDE tooling scaffolds by
  default. Reach for the full pattern only when a class genuinely owns
  unmanaged resources *and* is designed to be extended.

## Three groups of "safe code" defaults

Useful as a checklist frame, not just a list — each group targets a different
failure mode:

1. **Safety & correctness** (reduce defect risk): null-check before use / lean
   on nullable reference types; avoid `unsafe`; no empty or over-broad
   catches; dispose what you create even when the GC *could* eventually get to
   it; `switch` needs a `default` (or, in an expression, a `_` arm) so an
   unexpected value fails loudly instead of silently falling through.
2. **Maintainability & structure** (control future cost): cyclomatic
   complexity under 10; flag methods over ~50 lines as probably doing too
   much; use the narrowest correct member visibility (`init`-only setters for
   properties that should be immutable after construction, available since
   C# 9 — already usable on GeoAssets' target); no duplicated logic.
3. **Readability & communication** (value to the *next* reader): names that
   make a comment unnecessary; named constants/enums instead of magic
   numbers or literal strings; comment the *public* surface, since that's what
   consumers outside the type actually rely on.

## Static analysis tooling shape

Three layers, each updated and enforced differently — worth knowing which is
which when deciding where a rule should live:

| Layer | What it covers | How it's kept consistent across a team |
|---|---|---|
| Code-style analyzers | Formatting/naming/`using` ordering | `.editorconfig`, checked into the repo |
| Code-quality analyzers | Built into the .NET SDK — correctness, reliability, some performance patterns | Versioned with the SDK; teams pin via `global.json` |
| Third-party analyzers (e.g. SonarQube/SonarAnalyzer) | Security, domain-specific, stricter style rules | **Prefer the NuGet package over the IDE extension** — a NuGet-referenced analyzer travels with the project and applies to every contributor's build; an IDE-only extension only helps whoever remembered to install it |

The one enforcement point worth calling out explicitly: **configuring code
style in the IDE alone doesn't guarantee team-wide consistency.** Setting
`TreatWarningsAsErrors` (and pinning analyzer versions) moves enforcement from
"whatever this developer's IDE happens to be configured to flag" to "the build
itself fails" — the same violation is caught in CI regardless of who wrote it
or what their local settings are.

AI-assisted review (Copilot or similar) is a *second, complementary* layer on
top of static analysis, not a replacement for it — it's better at contextual,
explanatory feedback ("this could be a switch expression, here's why"); static
analyzers are better at exhaustive, deterministic, CI-enforceable rules. Use
both; don't substitute one for the other.

## Where this would apply in GeoAssets

`Directory.Build.props` at the repo root currently only sets `LangVersion 14` —
there's no `.editorconfig`, no `TreatWarningsAsErrors`, and no analyzer package
referenced solution-wide. Per the enforcement point above, that means any
code-style/quality rule the team follows today is only as consistent as each
contributor's own IDE configuration. If this project wants build-enforced
consistency (matching the discipline already established for test coverage —
see the 100%-branch-coverage convention noted across `GeoAssets.Workflow.Tests`
and `ServiceOrder.md`), the concrete next step would be a repo-root
`.editorconfig` plus a shared analyzer package reference in
`Directory.Build.props`, not per-developer IDE settings.
