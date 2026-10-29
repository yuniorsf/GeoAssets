# Categorical Formalism for GeoAssets Outer-Loop DAG Orchestration

Adapted from `outer_loop_dag_orchestrator_skill.md` for GeoAssets' .NET 10 / C# 14 Tri-Planar architecture.

## 1. Monoidal Architecture Category $(\mathbf{Arch}, \otimes, I)$

The Outer-Loop Orchestrator models multi-agent software synthesis as a symmetric monoidal category enriched over a failure/trace Effect Monad $(T, \eta, \mu)$:

| Categorical Construct | Mathematical Form | GeoAssets Concrete Realization |
|---|---|---|
| **Objects** | $A, B, C \in \text{Ob}(\mathbf{Arch})$ | Typed worktree states, C# 14 interface contracts, `.csproj` dependency graphs, and xUnit test suites. |
| **Morphisms** | $f: A \to B$ | Bounded agent transitions (`2A` domain synthesis, `2B` test synthesis, `dotnet format`, rebase onto `origin/develop`). |
| **Sequential Composition** | $g \circ f: A \to C$ | Ordered DAG phases where postconditions of $f$ (e.g., `core/` port compilation) satisfy preconditions of $g$ (adapter + REST wiring). |
| **Monoidal Tensor Product** | $A \otimes B$, $(f_1 \otimes f_2)$ | Concurrent execution across disjoint file sets (`scope(f_1) ∩ scope(f_2) = ∅`) or isolated worktrees (`../worktrees/<id>`). |
| **Unit Object** | $I$ | Clean baseline commit on `origin/develop` with `git status --porcelain == ""`. |
| **Effect Monad** | $(T, \eta, \mu)$ | Encapsulates compiler diagnostics (`CS*`, `AsyncFixer*`, `MA*`, `RS0030`), SOLID violations, and execution trace logs. |
| **Dual / Compensating Morphism** | $f^{op}: S_{i+1} \to S_i$ | Pre-retry tree snapshot restoration (`git stash create` / `git worktree` destroy) or compensating domain Saga transition. |

## 2. Monadic Laws in Self-Healing Loops

- **Unit ($\eta: A \to T(A)$)**: `scripts/agent-sandbox.sh create <task-id> origin/develop` lifts a static Jira/architectural specification $A$ into an isolated, restored Git worktree execution context $T(A)$.
- **Kleisli Composition ($f >=> g$)**: An downstream DAG stage executes **only** when the upstream `verify-solid-contracts.py` and `verify-agent-worktree.sh` gates emit `Exit 0`. Non-zero exit codes short-circuit forward traversal and route structured diagnostics into the bounded self-healing functor.
- **Multiplication ($\mu: T(T(A)) \to T(A)$)**: Flattens nested self-healing retries (`healAttempt 1..3`) into a single linear verification trace and clean working-tree diff ready for review or atomic commit.

## 3. Bounded Context Isolation (Connected Components)

GeoAssets enforces disconnected vertical & horizontal boundaries via `ArchitectureTests.cs` and `ServerArchitectureTests.cs`:

- **Core Purity (`core/*`)**: Zero outward edges to `Microsoft.EntityFrameworkCore`, `Npgsql`, `GeoAssets.Provider.*`, or `apps/*`.
- **Vertical Independence**: `GeoAssets.Server` (geographical features, attributes, topology) and `GeoAssets.IpBilling` (`GEOS_FAT_IP`) form distinct connected components at the core server boundary and can be synthesized via monoidal parallelization ($A \otimes B$) whenever shared `core/` contracts are stable.
- **Boundary Projections ($\pi_k: \text{Domain} \to \text{Contract}_k$)**: Cross-process boundaries (`apps/GeoAssets.Server` minimal APIs $\leftrightarrow$ `providers/GeoAssets.Provider.Rest` / `workflow/GeoAssets.Workflow.Rest`) communicate strictly via projected DTO records (`sealed record` + `JsonSerializerContext`) and explicit HTTP status/exception bijections.

