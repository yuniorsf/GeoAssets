---
name: outer-loop-dag-orchestrator
description: >-
  Coordinate multi-agent subagents and parallel Jira/feature worktrees in Claude
  Code across any language stack using categorical DAG composition, monoidal
  disjoint-scope parallelization, ISP context projection, and localized
  self-healing rollback. Use when spawning multiple Claude subagents,
  decomposing multi-layer epics, or running /orchestrate-tickets.
---

# Claude Code Outer-Loop DAG Orchestrator (Language-Agnostic)

Applies categorical composition ($g \circ f$), monoidal parallelization ($A \otimes B$), and SOLID agent-coordination invariants whenever Claude Code decomposes work across parallel subagents (`.claude/agents/*` or `Agent` tool with `isolation: "worktree"`).

## Tooling & Policy Guardrails

- **Toolchain Config**: [`.harness/harness.env`](../../../.harness/harness.env) & [`harness.config.json`](../../../harness.config.json)
- **Branch Baseline**: Every worktree or branch **must** originate from `origin/develop`. Never touch `main` or `master` (`CLAUDE.md`).
- **Worktree Sandbox**: [`scripts/agent-sandbox.sh`](../../../scripts/agent-sandbox.sh) or Claude `isolation: "worktree"`.
- **Polyglot SOLID & Scope Oracle**: [`scripts/verify-solid-contracts.py`](../../../scripts/verify-solid-contracts.py)
- **3-Stage Build/Lint/Arch Oracle**: [`scripts/verify-agent-worktree.sh`](../../../scripts/verify-agent-worktree.sh)

## 1. Categorical Decomposition Rules

- **Sequential Composition ($g \circ f$)**: Check both formal Jira `Blocks` links and prose `"Depends on <TICKET-KEY>"` notes. When task $g$ consumes ports, schemas, or migrations created by $f$, synthesize and verify $f$ first (`src/core/` domain ports before outer-layer adapters).
- **Monoidal Parallelization ($A \otimes B$)**: Spawn parallel workers (`<= 5` concurrent workers) **only** when tasks have zero causal dependency and strictly disjoint mutation scopes (`scope(A) ∩ scope(B) = ∅`).

## 2. SOLID Agent-Coordination Invariants

- **SRP (Bounded Scope)**: Assign each subagent explicit `--allowed-scope` path globs and verify post-execution via `python3 scripts/verify-solid-contracts.py --target <worktree> --allowed-scope "<csv-globs>"`.
- **ISP (Minimal Context Projection $\pi_k$)**: Strip unrelated codebase state from subagent prompts; pass only frozen Shared Type/Port Contracts, assigned `[NEW]`/`[MODIFY]` paths, and relevant `.claude/skills/*` pointers.
- **LSP (Behavioral Equivalence)**: Every persona subagent must satisfy identical postconditions: zero linter/compiler suppressions and `Exit 0` across both Oracles.
- **DIP (Contract Mediation)**: Coordinate strictly via upfront type/interface contracts and verified Git diffs.
- **Pagination Invariant**: Any task generating or modifying UI tables or collection query ports must enforce pagination contracts (`page`, `pageSize`, `totalCount`, deterministic sort, and accessible pager controls across both branches).

## 3. Localized Self-Healing & Dual Rollback ($f^{op}$)

On non-zero Oracle exit (`10`, `20`, or `30`), execute up to `3` localized retries:
1. Snapshot pre-retry state: `SNAP=$(git -C <worktree> stash create)`.
2. Route `Exit 10` to `FORMAT_FIX_CMD`, `Exit 20` to compiler/typecheck/banned-API remediation (zero suppressions), and `Exit 30` to Clean/Hexagonal port extraction or domain invariant repair.
3. Rewind (`$SNAP`) if a retry compounds errors, and always re-run `./scripts/verify-agent-worktree.sh` + `UNIT_TEST_CMD` post-rebase onto `origin/develop`.
