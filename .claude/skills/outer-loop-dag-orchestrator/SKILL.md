---
name: outer-loop-dag-orchestrator
description: >-
  Coordinate multi-agent subagents and parallel Jira/feature worktrees in Claude Code
  using categorical DAG composition, monoidal disjoint-scope parallelization, ISP
  context projection, and localized self-healing rollback. Use when spawning
  multiple Claude subagents, decomposing multi-layer epics, or running
  /orchestrate-tickets.
---

# Claude Code Outer-Loop DAG Orchestrator

Applies categorical composition ($g \circ f$), monoidal parallelization ($A \otimes B$), and SOLID agent-coordination invariants whenever Claude Code decomposes work across parallel subagents (`.claude/agents/*` or `Agent` tool with `isolation: "worktree"`).

## Tooling & Policy Guardrails

- **Branch Baseline**: Every worktree or branch **must** originate from `origin/develop`. Never branch from, rebase onto, or touch `main` or `master` (`CLAUDE.md` § Branching & Release Policy).
- **Worktree Sandbox**: [`scripts/agent-sandbox.sh`](../../../scripts/agent-sandbox.sh) or Claude `isolation: "worktree"` (`.claude/worktrees/<id>`).
- **SOLID & Scope Pre-Flight**: [`scripts/verify-solid-contracts.py`](../../../scripts/verify-solid-contracts.py)
- **3-Stage Build/Analyzer/Arch Oracle**: [`scripts/verify-agent-worktree.sh`](../../../scripts/verify-agent-worktree.sh)

---

## 1. Categorical Decomposition Rules

1. **Sequential Composition ($g \circ f$)**:
   - Inspect both formal Jira `Blocks` links and prose `"Depends on <TICKET-KEY>"` notes.
   - If task $g$ depends on contracts, entities, or migrations created by task $f$, **never** launch $f$ and $g$ concurrently in separate worktrees. Synthesize and verify $f$ first (`core/` ports before `providers/` / `workflow/` adapters).
2. **Monoidal Parallelization ($A \otimes B$)**:
   - Spawn parallel workers (`<= 5` concurrent workers by default) **only** when tasks have zero causal dependency and strictly disjoint mutation scopes (`scope(A) ∩ scope(B) = ∅`), e.g., independent Bounded Context verticals (`GeoAssets.Server` vs. `GeoAssets.IpBilling`) or production domain logic vs. disjoint test/DTO scaffolding against a frozen C# 14 interface contract.

---

## 2. SOLID Agent-Coordination Invariants

- **SRP (Bounded Mutation Scope)**: Assign each subagent an explicit list of allowed path prefixes/files (`--allowed-scope`). Verify post-execution via:
  ```bash
  python3 scripts/verify-solid-contracts.py --target <worktree> --allowed-scope "<csv-globs>"
  ```
- **ISP (Minimal Context Projection $\pi_k$)**: Strip unrelated codebase context from subagent prompts. Provide only:
  1. Target C# 14 interface/record contracts (`TimeProvider`, `CancellationToken` signatures).
  2. Assigned `[NEW]` / `[MODIFY]` paths.
  3. Relevant domain skill references (`csharp-conventions`, `observability-conventions`, `jira-conventions`).
- **LSP (Behavioral Subagent Equivalence)**: Any persona subagent (`ticket-worker`, `arch-designer`, `test-agent`) must satisfy the same postcondition gate: zero `#pragma warning disable` / `[SuppressMessage]` / `<NoWarn>`, `Exit 0` from `verify-solid-contracts.py`, and `Exit 0` from `verify-agent-worktree.sh`.
- **DIP (Contract Mediation)**: Subagents coordinate solely via upfront C# interface contracts and verified Git commits/diffs — never via implicit assumptions about sibling agent internals.

---

## 3. Localized Self-Healing & Dual Rollback ($f^{op}$)

When `verify-solid-contracts.py` or `verify-agent-worktree.sh` exits non-zero (`10`, `20`, or `30`):

1. **Checkpoint Before Retry**: Record `SNAP=$(git -C <worktree> stash create)` before each self-healing pass (max `3` retries).
2. **Targeted Error-Monad Routing**:
   - `Exit 10` (Formatting / `.razor.cs` split / scope drift) $\rightarrow$ Run `dotnet format style GeoAssets.sln` or split inline `@code` into `.razor.cs`.
   - `Exit 20` (Roslyn / `AsyncFixer` / `Meziantou` / `RS0030`) $\rightarrow$ Inject `System.TimeProvider`, propagate `CancellationToken`, remove blocking calls without suppressing warnings.
   - `Exit 30` (Onion layer leak / `NotImplementedException` / failed xUnit or `ArchitectureTests`) $\rightarrow$ Move infrastructure dependencies behind `core/` ports and repair domain invariants.
3. **Rewind ($f^{op}$) on Non-Convergence**: If a retry increases diagnostic count or breaks passing tests, restore `$SNAP` (`git checkout -- .` + `git stash apply $SNAP`) rather than stacking broken edits, and escalate after 3 attempts.
4. **Post-Rebase Re-Verification**: After `git fetch origin develop && git rebase origin/develop`, always re-run `./scripts/verify-agent-worktree.sh` and `dotnet test` in the worktree — never trust pre-rebase green results.

