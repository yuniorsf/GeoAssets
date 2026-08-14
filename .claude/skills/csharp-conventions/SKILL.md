---
name: csharp-conventions
description: >-
  C# and .NET engineering conventions, design patterns, and language-feature
  guidance distilled from external books and articles, curated for the
  GeoAssets codebase (.NET 9). Use when writing or reviewing C# code,
  choosing between design patterns or type-hierarchy shapes, or evaluating
  whether a new C#/.NET language feature is safe to adopt.
---

# C# Conventions

Distilled, source-attributed engineering guidance — not a restatement of
things you already know. Each reference file below traces back to a specific
book or article; when a claim from a source turns out to be wrong or
superseded, fix it at the source file, don't just override it in a
conversation.

## How this skill is organized

- `references/` holds one file per topic. Read only the file relevant to the
  task at hand — don't load the whole set speculatively.
- Every entry records **status**: `current` (usable today on .NET 9 / C# 13)
  or `future` (depends on a language/runtime version GeoAssets doesn't
  target yet).

## Before recommending anything from a `future` entry

GeoAssets targets **.NET 9**. Any entry marked `future` describes a
preview or unshipped feature. Do not:
- suggest it as the fix for a current code review or design question,
- write code that uses it,
- treat its syntax/behavior as final — preview features change shape before GA.

Do surface it when the user is explicitly asking about roadmap, upcoming
language features, or planning a future migration.

## References

| File | Topic | Status |
|---|---|---|
| [future-language-features.md](references/future-language-features.md) | Closed hierarchies, union types, closed enums (C# 15 / .NET 11) | `future` — GA targeted November 2026 |
| [code-quality-metrics-and-safe-coding.md](references/code-quality-metrics-and-safe-coding.md) | Cyclomatic complexity, coupling, IDisposable, static analysis tooling | `current` |
| [code-reusability-and-net-libraries.md](references/code-reusability-and-net-libraries.md) | DRY, the reuse lifecycle, .NET Standard vs. netX.0, refactoring triggers, NuGet/OpenAPI | `current` |
| [design-patterns-and-dotnet.md](references/design-patterns-and-dotnet.md) | SOLID, GoF patterns, dependency injection lifetimes, .NET Generic Host, AI-agent patterns | `current` |
| [domain-driven-design.md](references/domain-driven-design.md) | Bounded contexts, entities/value objects/aggregates, Onion architecture, repository/UoW, CQRS | `current` |
| [testing-strategy-and-dotnet-tooling.md](references/testing-strategy-and-dotnet-tooling.md) | Unit/integration/acceptance tiers, TDD/BDD, xUnit/Moq, subcutaneous-test pitfalls | `current` |
| [entity-framework-core-and-onion-architecture.md](references/entity-framework-core-and-onion-architecture.md) | EF Core configuration/migrations, global query filters, EF-entity-vs-aggregate tension | `current` |
| [async-and-concurrency-conventions.md](references/async-and-concurrency-conventions.md) | async/await naming, `ConfigureAwait`, `Task` vs `ValueTask`, cancellation, fire-and-forget | `current` |
| [nullable-reference-types.md](references/nullable-reference-types.md) | `?`/`!`/`required` usage, nullable-oblivious boundaries, entity/persistence nullability | `current` |
| [error-handling-and-result-pattern.md](references/error-handling-and-result-pattern.md) | Exceptions vs. Result, translating domain exceptions across a REST boundary | `current` |
| [records-vs-classes.md](references/records-vs-classes.md) | General type-shape decision rule: identity/lifecycle vs. value/data-in-motion | `current` |
| [project-and-solution-structure.md](references/project-and-solution-structure.md) | Folder-as-dependency-lane rules, namespace-mirrors-path, one project per backend | `current` |
| [configuration-and-options-pattern.md](references/configuration-and-options-pattern.md) | Options classes, `SectionName` convention, `IOptions` vs `IOptionsSnapshot`/`Monitor` | `current` |
| [api-design-conventions.md](references/api-design-conventions.md) | Minimal API grouping, route/verb conventions, request validation, error-shape conventions | `current` |
| [domain-test-data-builders.md](references/domain-test-data-builders.md) | When/how to promote ad-hoc geometry/topology test construction into shared builders | `current` |
| [rest-provider-contract-testing.md](references/rest-provider-contract-testing.md) | Closing the untested seam between the REST server endpoints and REST client repository | `current` |

As more books/articles are added, list them here with topic and status so
this table stays the single index of what's been curated.
