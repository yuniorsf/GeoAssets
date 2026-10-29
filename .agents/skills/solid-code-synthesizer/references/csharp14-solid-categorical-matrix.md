# SOLID & Categorical Target-Code Matrix for GeoAssets (C# 14 / .NET 10)

Adapted from `solid_category_code_synthesizer_dag.md` and reconciled with GeoAssets' Onion Architecture, Roslyn analyzers, and `csharp-conventions`.

## 1. Structural Invariant & Verification Table

| Principle | Categorical Interpretation | GeoAssets C# 14 Target-Code Rule | Deterministic Verification Gate |
|---|---|---|---|
| **SRP** (Single Responsibility) | Isolated subcategory $\mathbf{SubC} \subset \mathbf{TargetCode}$ with a single state-mutation family. | One reason to change per type; cyclomatic complexity $< 10$; zero EF Core / Npgsql / HTTP / UI references in `core/`; `.razor` markup separated from `.razor.cs` logic. | `scripts/verify-solid-contracts.py` (`SRP`/`RAZOR`), `ArchitectureTests.cs`, `ServerArchitectureTests.cs`. |
| **OCP** (Open / Closed) | Functorial extension $F: \mathbf{C} \to \mathbf{D}$ and coproduct injections ($A + B$). | Add behavior via `IServiceOrderRule`, `IProviderPlugin`, `IExternalProviderFactory`, `[ExportGeoCommand]`, or decorator chains rather than branching inside core services. | Exhaustive C# 14 `switch` expressions on closed enums/types; plugin discovery tests in `GeoAssets.Commands.Tests`. |
| **LSP** (Liskov Substitution) | Faithful subcategory inclusion $m: S \hookrightarrow T$ preserving commutative diagrams. | Every `IAssetProvider` / `IServiceOrderRepository` backend honors identical return, event, and exception semantics; zero `NotImplementedException` stubs. | Shared abstract `*ContractTests` (`GeoAssets.Workflow.TestKit`) + REST $\leftrightarrow$ Server contract tests + `verify-solid-contracts.py` (`LSP`). |
| **ISP** (Interface Segregation) | Limit projections $\pi_k: \prod X_i \to X_k$ exposing minimal role slices. | Narrow consumer-oriented interfaces (`<= 5` methods for new role ports; split `I*Reader` vs. `I*Writer`) so read-only consumers never implement write stubs or bloated mocks. | `scripts/verify-solid-contracts.py` (`ISP` method-count scanner). |
| **DIP** (Dependency Inversion) | Natural transformation $\alpha: F \Rightarrow G$ commuting across abstract port functors. | Primary/constructor DI over `core/` interfaces + `System.TimeProvider`; zero `new *DbContext()` / `new HttpClient()` / `DateTime.UtcNow` inside domain services. | `BannedSymbols.txt` (`RS0030`), `Meziantou.Analyzer`, `AsyncFixer`, `verify-solid-contracts.py` (`DIP`/`GUARDRAIL`). |

## 2. Reconciling Categorical Effect Monads with .NET 10 Idioms

Raw functional translations often force `Result<T, E>` onto every C# service method. In GeoAssets, effect totality is achieved **idiomatically without fighting .NET**:

1. **Maybe / Option Functor ($1 + A$) $\rightarrow$ Nullable Reference/Value Types (`T?`)**:
   - `<Nullable>enable</Nullable>` with `TreatWarningsAsErrors=true` makes nullability a compiler-verified sum type. Query ports (`GetByIdAsync`) return `Task<T?>`.
2. **Validation Applicative / Rule Result $\rightarrow$ Explicit Outcome Records**:
   - High-frequency, expected branching outcomes (attribute validation collectors, authorization rule evaluations) use explicit value records/results rather than control-flow exceptions.
3. **Failure / Exception Effect Across Network Boundaries $\rightarrow$ Bijective Isomorphism**:
   - In-process write ports throw typed domain exceptions when invariants fail.
   - Across HTTP boundaries (`apps/GeoAssets.Server` $\leftrightarrow$ `*.Rest` clients), a documented bijection maps `DomainException` $\to$ `(HttpStatusCode, StructuredErrorPayload)` $\to$ `DomainException`, preserving LSP across in-process and remote adapters.

## 3. Targeted Self-Correction Refactoring Moves

When `verify-solid-contracts.py` or `verify-agent-worktree.sh` flags a structural violation, apply the minimal local refactoring rather than regenerating upstream stages:

- **`SRP` / Bloat Advisory**: Apply *Extract Class* or split validation, domain transition, and persistence into dedicated collaborators.
- **`ISP` / `LSP` (`NotImplementedException` / `NotSupportedException`)**: Apply *Extract Role Interface* (`I*Reader` / `I*Writer`) so read-only adapters only implement the read projection.
- **`DIP` / `RS0030`**: Inject the port interface or `System.TimeProvider` (`FakeTimeProvider` in xUnit tests) through the constructor.
- **`AsyncFixer01`–`04` / `MA0004` / `MA0040`**: Forward `CancellationToken`, use `.ConfigureAwait(false)` in library layers, and avoid eliding `async`/`await` across `using` or `Activity` span scopes.

