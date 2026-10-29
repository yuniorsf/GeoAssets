---
name: outer-loop-dag-orchestrator
description: >-
  Orchestrate multi-agent, multi-bounded-context software synthesis DAGs using
  categorical composition, monoidal parallelization, SOLID agent-coordination
  invariants, and localized self-healing rollback. Use when decomposing complex
  epics, multi-service/vertical features, or multi-worktree agent pipelines
  across Antigravity, Claude Code, and GitHub Copilot.
---

# Outer-Loop Categorical & SOLID DAG Orchestrator

Governs macro-level task decomposition, context projection, parallel dispatch, and localized self-healing across GeoAssets' **Tri-Planar Dual-Loop Architecture** (Antigravity Control Plane → Claude Code `2A` + GitHub Copilot `2B`).

- For single-ticket 2-branch worktree execution, invoke [`code-gen-dag`](../code-gen-dag/SKILL.md).
- For zero-mutation architectural planning, invoke [`plan-gen-dag`](../plan-gen-dag/SKILL.md).
- For mathematical axioms (Monoidal Category $(\mathbf{Arch}, \otimes, I)$, Effect Monad $(T, \eta, \mu)$, Dual Rollback $f^{op}$), read [`references/categorical-dag-formalism.md`](references/categorical-dag-formalism.md) on demand.

## Reference Tooling

- **Worktree Sandbox Manager**: [`scripts/agent-sandbox.sh`](../../../scripts/agent-sandbox.sh)
- **SOLID & Disjoint-Scope Pre-Flight Oracle**: [`scripts/verify-solid-contracts.py`](../../../scripts/verify-solid-contracts.py)
- **3-Stage Build/Analyzer/Arch Oracle**: [`scripts/verify-agent-worktree.sh`](../../../scripts/verify-agent-worktree.sh)

---

## 1. SOLID Invariants for Multi-Agent Coordination

Enforce SOLID as structural execution constraints across every node in the agent DAG:

1. **Single Responsibility (SRP — Bounded Mutation Scope)**:
   - Every agent node $N_i$ owns a strictly bounded file/directory mutation scope (`scope(N_i)`), aligned to a single Onion layer or Bounded Context (`core/`, `providers/`, `workflow/`, `ipbilling/`, `projects/`, `apps/`).
   - Verify post-run via `python3 scripts/verify-solid-contracts.py --target <worktree> --allowed-scope "<globs>"`. Out-of-scope file mutations trigger `Exit 10`.
2. **Open / Closed (OCP — Pluggable Subcategories)**:
   - Extend orchestration to new providers, MEF command plugins (`GeoAssets.Plugin.*`), or messaging transports (`Kafka`, `ServiceBus`) by adding leaf DAG nodes conforming to existing port contracts without mutating core traversal gates.
3. **Liskov Substitution (LSP — Runtime & Agent Equivalence)**:
   - Substituting an external headless CLI (`claude`, `copilot`) with a native Antigravity subagent (`claude-code-specialist`, `github-copilot-worker`) is valid if and only if the substitute honors identical behavioral postconditions: zero analyzer suppressions, disjoint file scope, and `Exit 0` across both Oracles.
4. **Interface Segregation (ISP — Minimal Context Projection $\pi_k$)**:
   - Never dump unrelated global repository state into inner-loop prompts. Project workspace context down to:
     1. Exact shared C# 14 type/interface signatures (`namespace`, types, `TimeProvider`, `CancellationToken`).
     2. Owned target file paths (`[NEW]` / `[MODIFY]`).
     3. Direct upstream port contracts and relevant `.claude/skills/<domain>/SKILL.md` references.
5. **Dependency Inversion (DIP — Contract-First Mediation)**:
   - Parallel agents coordinate exclusively through an upfront **Shared C# Contract Specification** and worktree filesystem artifacts — never by coupling to another agent's internal prompt transcript.

---

## 2. Categorical DAG Execution Protocol

### Step 1 — Connected-Component Bounded Context Partitioning
1. Build the task dependency graph (including Jira `Blocks` links and prose `"Depends on <KEY>"` references).
2. Extract **Sequential Morphisms ($g \circ f$)**:
   - Whenever node $g$ consumes types or ports authored by node $f$ (e.g., `core/GeoAssets.Workflow` domain contracts $\to$ `workflow/GeoAssets.Workflow.EFCore` & `Rest` adapters), execute $f$ first and verify its contract postconditions before dispatching $g$.
3. Extract **Monoidal Parallel Branches ($A \otimes B$)**:
   - Run nodes concurrently if and only if they have zero causal dependency and strictly disjoint mutation scopes (`scope(A) ∩ scope(B) = ∅`), such as:
     - Disjoint Bounded Context verticals (e.g., `GeoAssets.Server` core GIS vs. `GeoAssets.IpBilling`, enforced by `ServerArchitectureTests`).
     - Intra-worktree `Branch 2A` (production logic) $\otimes$ `Branch 2B` (DTOs, `DomainTestDataBuilders`, xUnit suites) sharing a frozen upfront C# interface contract.

### Step 2 — Isolated Sandbox Provisioning ($\eta: A \to T(A)$)
- Provision isolated Git worktrees forked strictly from `origin/develop` (`main` and `master` are permanently forbidden):
  ```bash
  ./scripts/agent-sandbox.sh create <task-id> origin/develop
  ```

### Step 3 — Dual-Oracle Verification Gate
After parallel branches join, execute both deterministic verifiers in order:
1. **Fast Structural & Scope Pre-Flight (< 1s)**:
   ```bash
   python3 scripts/verify-solid-contracts.py --target ../worktrees/<task-id> --base origin/develop --scope-a "<2A-globs>" --scope-b "<2B-globs>" --json
   ```
2. **Full Compiler, Analyzer & Architecture Oracle**:
   ```bash
   ./scripts/verify-agent-worktree.sh ../worktrees/<task-id>
   ```

### Step 4 — Localized Self-Healing & Dual Morphism Rewind ($f^{op}$)
When a node fails verification (`Exit 10`, `20`, or `30`), execute up to `maxRetries` (default `3`) localized repair cycles:
1. **Snapshot Before Retry**: Capture the worktree state (`git stash create` inside `../worktrees/<task-id>`) before dispatching a repair prompt.
2. **Error Monad Injection**: Feed only the structured JSON/diagnostic lines (`verify-solid-contracts.py --json` or `verify-agent-worktree.sh` summary) to the specific responsible agent (`Exit 10` formatting/scope $\to$ Copilot/`2B`; `Exit 20`/`30` analyzer/SOLID/domain $\to$ Claude/`2A`).
3. **Dual Rollback ($f^{op}$) on Regression**: If a retry introduces *more* diagnostics or corrupts previously passing tests, restore the pre-retry tree snapshot (`git checkout -- .` / `git reset --hard <snapshot-tree>` if uncommitted) rather than compounding broken edits, and refine or escalate.
4. **Topological Merge Serialization**: Rebase verified worktrees onto `origin/develop` in topological order and re-run both Oracles post-rebase before merge preparation.

