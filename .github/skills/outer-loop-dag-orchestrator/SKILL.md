---
name: outer-loop-dag-orchestrator
description: >-
  Orchestrate multi-agent, multi-bounded-context software synthesis DAGs across
  any programming language using categorical composition, monoidal
  parallelization, SOLID agent-coordination invariants, and localized
  self-healing rollback. Use when decomposing complex epics, multi-service
  features, or multi-worktree agent pipelines across Antigravity, Claude Code,
  and GitHub Copilot.
---

# Outer-Loop Categorical & SOLID DAG Orchestrator (Language-Agnostic)

Governs macro-level task decomposition, minimal context projection, parallel dispatch, and localized self-healing across the **Tri-Planar Dual-Loop Architecture** (Antigravity Control Plane → Claude Code `2A` + GitHub Copilot `2B`).

- Single-ticket 2-branch execution: [`code-gen-dag`](../code-gen-dag/SKILL.md).
- Zero-mutation architectural planning: [`plan-gen-dag`](../plan-gen-dag/SKILL.md).
- Mathematical foundations (Monoidal Category $(\mathbf{Arch}, \otimes, I)$, Effect Monad $(T, \eta, \mu)$, Dual Rollback $f^{op}$): read [`references/categorical-dag-formalism.md`](references/categorical-dag-formalism.md) on demand.

## Reference Tooling

- **Active Toolchain & Rules**: [`.harness/harness.env`](../../../.harness/harness.env) & [`harness.config.json`](../../../harness.config.json)
- **Worktree Sandbox Manager**: [`scripts/agent-sandbox.sh`](../../../scripts/agent-sandbox.sh)
- **Polyglot SOLID & Disjoint-Scope Oracle**: [`scripts/verify-solid-contracts.py`](../../../scripts/verify-solid-contracts.py)
- **3-Stage Build/Lint/Arch Oracle**: [`scripts/verify-agent-worktree.sh`](../../../scripts/verify-agent-worktree.sh)

---

## 1. SOLID Invariants for Multi-Agent Coordination

1. **Single Responsibility (SRP — Bounded Mutation Scope)**:
   - Every agent node $N_i$ owns a strictly bounded directory/file scope (`scope(N_i)`), aligned to a single architectural layer or Bounded Context.
   - Verify post-run via `python3 scripts/verify-solid-contracts.py --target <worktree> --allowed-scope "<globs>"`. Out-of-scope edits trigger `Exit 10`.
2. **Open / Closed (OCP — Pluggable Stack & Domain Subcategories)**:
   - Adding a new language stack (`.harness/profiles/*.env`), adapter, plugin, or microservice adds a new subcategory/node without mutating the traversal engine or Oracle exit-code protocol (`0`, `2`, `10`, `20`, `30`).
3. **Liskov Substitution (LSP — Runtime & Agent Equivalence)**:
   - Substituting an external headless CLI (`claude`, `copilot`) with a native subagent (`claude-code-specialist`, `github-copilot-worker`) is valid iff the substitute honors identical postconditions: zero linter/compiler suppressions, bounded scope, and `Exit 0` across both Oracles.
4. **Interface Segregation (ISP — Minimal Context Projection $\pi_k$)**:
   - Strip unrelated global repository state from inner-loop prompts. Pass only: (1) frozen Shared Type/Port Contract, (2) owned `[NEW]`/`[MODIFY]` file paths, and (3) direct upstream port contracts + relevant domain skill references.
5. **Dependency Inversion (DIP — Contract-First Mediation)**:
   - Parallel branches coordinate exclusively through an upfront Shared Type/Port Contract and verified Git worktree artifacts — never by coupling to sibling agent prompt transcripts.
6. **Table & Query Pagination Invariant**:
   - Every DAG task synthesizing, refactoring, or reviewing a UI table or collection-returning query MUST enforce pagination contracts across both inner-loop branches (`2A`: domain pagination types & bounded query ports; `2B`: UI pager controls, touch targets, and LSP pagination contract test suites). Unbounded table dumps are prohibited.

---

## 2. Categorical DAG Execution Protocol

1. **Connected-Component Partitioning**:
   - **Sequential Composition ($g \circ f$)**: When node $g$ consumes types, schemas, or ports produced by node $f$ (including Jira `Blocks` and prose `"Depends on <KEY>"`), execute and verify $f$ before dispatching $g$.
   - **Monoidal Parallelization ($A \otimes B$)**: Run branches concurrently iff they have zero causal dependency and strictly disjoint mutation scopes (`scope(A) ∩ scope(B) = ∅`).
2. **Sandbox Lift ($\eta: A \to T(A)$)**:
   - Provision isolated worktrees strictly from `origin/develop` (`./scripts/agent-sandbox.sh create <task-id> origin/develop`).
3. **Dual-Oracle Gate**:
   - Run `python3 scripts/verify-solid-contracts.py --target ../worktrees/<task-id> --base origin/develop --scope-a "<2A>" --scope-b "<2B>" --json`, followed by `./scripts/verify-agent-worktree.sh ../worktrees/<task-id>`.
4. **Localized Self-Healing & Dual Rollback ($f^{op}$)**:
   - Capture `git -C ../worktrees/<task-id> stash create` before each retry (`1..3`).
   - Route `Exit 10` to Copilot (`FORMAT_FIX_CMD`) and `Exit 20`/`30` to Claude Code with structured diagnostic output.
   - If a retry increases diagnostics or regresses passing tests, rewind to the pre-retry tree snapshot ($f^{op}$) rather than compounding broken edits.
