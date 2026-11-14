# Polyglot SOLID & Categorical Target-Code Matrix

## 1. Universal Principle-to-Gate Mapping

| Principle | Categorical Model | Target-Code Structural Rule | Deterministic Verification Gate |
|---|---|---|---|
| **SRP** | Isolated subcategory $\mathbf{SubC} \subset \mathbf{TargetCode}$ | Single reason to change; cyclomatic complexity $< 10$; zero ORM/DB/HTTP/UI imports in `src/core/` or `src/domain/`. | `verify-solid-contracts.py` (`SRP` import & cohesion scan) + `ARCH_TEST_CMD`. |
| **OCP** | Functorial extension $F: \mathbf{C} \to \mathbf{D}$ & coproducts ($A + B$) | Extend via Strategy, Rule Chain, Decorator, or Plugin ports rather than editing stable core dispatchers. | Exhaustive pattern/switch matching checks (`BUILD_AND_LINT_CMD`). |
| **LSP** | Faithful subcategory inclusion $m: S \hookrightarrow T$ | Every port implementation honors identical return, error, and event contracts; zero `NotImplemented` / `todo!()` stubs. | Shared Contract Test Suites + `verify-solid-contracts.py` (`LSP` stub scan). |
| **ISP** | Limit projections $\pi_k: \prod X_i \to X_k$ | Narrow role interfaces/protocols/traits (`<= 5` cohesive methods; split `Reader` vs. `Writer`). | `verify-solid-contracts.py` (`ISP` interface/protocol/trait method count). |
| **DIP** | Natural transformation $\alpha: F \Rightarrow G$ | Constructor/parameter injection of abstract ports + Clock abstraction; zero concrete adapter `new` inside domain core. | `verify-solid-contracts.py` (`DIP` & `GUARDRAIL` scans). |

## 2. Idiomatic Realization Across Major Language Stacks

| Construct | TypeScript / Node | Python (`3.12+`) | Go | Rust | C# / .NET |
|---|---|---|---|---|---|
| **Value Objects (Category Objects)** | `Readonly<{ ... }>` / branded types | `@dataclass(frozen=True, slots=True)` | Immutable value `struct` | `#[derive(Clone, PartialEq, Eq)] struct` | `readonly record struct` / `sealed record` |
| **Segregated Ports (ISP)** | `interface OrderReader` | `class OrderReader(Protocol):` | Small consumer-side `interface` (1–3 methods) | Focused `trait OrderReader` | `public interface IOrderReader` |
| **Clock Abstraction (DIP)** | `Clock { now(): Date }` | `Clock(Protocol): def now() -> datetime` | `type Clock interface { Now() time.Time }` | `trait Clock { fn now(&self) -> DateTime<Utc>; }` | `System.TimeProvider` (`FakeTimeProvider`) |
| **Cancellation Propagation** | `AbortSignal` | `asyncio.CancelledError` / task scope | `ctx context.Context` (1st param) | `tokio_util::sync::CancellationToken` | `CancellationToken` (last param) |
| **Expected Absence ($1 + A$)** | `T \| null` (`strictNullChecks`) | `T \| None` (`mypy --strict`) | `(*T, bool)` or `(*T, error)` (`ErrNotFound`) | `Option<T>` | `T?` (`<Nullable>enable</Nullable>`) |
| **Failure / Boundary Effect** | Typed `Result<T, E>` or mapped Domain Error | Typed `Result` or mapped Domain Exception | Explicit `(T, error)` with `errors.Is/As` | `Result<T, DomainError>` | Typed Domain Exception + HTTP bijection (or `Result` for inline rules) |
