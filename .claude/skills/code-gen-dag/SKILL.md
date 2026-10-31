---
name: code-gen-dag
description: >-
  Run GeoAssets' 5-phase Tri-Planar Outer-Loop code generation and verification DAG natively
  inside Claude Code, dispatching Claude Code itself (2A — deep reasoning/core domain) and
  GitHub Copilot (2B — mechanical scaffolding/xUnit QA) in parallel inside an isolated Git
  worktree. Activate when the user invokes /code-gen-dag, asks Claude Code to orchestrate a
  task across Claude Code and GitHub Copilot, or wants the Tri-Planar DAG run without Google
  Antigravity.
---

# GeoAssets Tri-Planar Outer-Loop Orchestrator — Claude Code Native (`/code-gen-dag`)

You are Claude Code acting as the **Outer-Loop Orchestrator** of GeoAssets' Tri-Planar Dual-Loop Architecture: you run Branch `2A` yourself, and you dispatch Branch `2B` — either the real `copilot` CLI, or a Claude subagent standing in for it — before verifying both through the deterministic Oracle.

The phase structure, invocation parameters, and exit-code semantics are defined once, agent-neutrally, in [`.agents/skills/code-gen-dag/SKILL.md`](../../../.agents/skills/code-gen-dag/SKILL.md) — read it now and follow it exactly. This file only states how **Claude Code** carries out each phase; do not restate or duplicate that protocol.

## Claude-Native Mechanics

- **Worktree provisioning (Phase 1)**: Run `./scripts/agent-sandbox.sh create <taskId> <baseRef>` from the repo root via `Bash`. If that script is unavailable in this environment, use the `Agent` tool's `isolation: "worktree"` instead — it provisions an equivalent isolated worktree under `.claude/worktrees/<id>`.
- **Branch 2A dispatch (Phase 2)**: Execute directly in your own context inside the worktree. Read [`CLAUDE.md`](../../../CLAUDE.md), [`solid-code-synthesizer`](../solid-code-synthesizer/SKILL.md), [`csharp-conventions`](../csharp-conventions/SKILL.md), and any other relevant `.claude/skills/*/SKILL.md`, then implement `coreLogicSpec` against the Shared Type/Interface Contract and `2A`'s disjoint file list.
- **Branch 2B dispatch (Phase 2)**:
  - If the `copilot` CLI is installed and authenticated, run it headlessly in the worktree with `<testAndBoilerplateSpec + Shared Contract + disjoint file ownership>`.
  - Otherwise, spawn a subagent via the `Agent` tool (`isolation: "worktree"`, pointed at the **same** worktree) bound to [`.github/copilot-instructions.md`](../../../.github/copilot-instructions.md), restricted strictly to `2B`'s owned files. Give it a stable `name` (e.g. `"<taskId>-2b"`) so it stays addressable later via `SendMessage`.
  - Wait for both branches to go idle (the join barrier) before continuing to Phase 3.
- **Oracle verification (Phase 3)**: Run `python3 scripts/verify-solid-contracts.py` and `./scripts/verify-agent-worktree.sh` yourself via `Bash`, pointed at the worktree.
- **Self-healing (Phase 4)**: Apply `Exit 10`/`20`/`30` fixes yourself — you own both `2A`'s code and the routing decision. If the defect lives in files owned by `2B`'s subagent, `SendMessage` to it by its `name` rather than editing those files directly.
- **Rebase & handoff (Phase 5)**: Rebase, re-verify, and report exactly as specified. In Default Mode, leave the worktree's changes unstaged/uncommitted for human review; only commit/push/open a PR when `--ship` was explicitly passed, following `CLAUDE.md` § Pull Requests & Commits for the commit/PR-title format.
