---
name: code-gen-dag
description: >-
  Run GeoAssets' 5-phase Tri-Planar Outer-Loop code generation and verification DAG natively
  from GitHub Copilot (IDE Agent Mode, Copilot CLI, or Coding Agent), dispatching Claude Code
  (2A — deep reasoning/core domain) and Copilot itself (2B — mechanical scaffolding/xUnit QA)
  inside an isolated Git worktree. Activate when the user invokes /code-gen-dag or asks Copilot
  to orchestrate a task across Claude Code and GitHub Copilot.
---

# GeoAssets Tri-Planar Outer-Loop Orchestrator — GitHub Copilot Native (`/code-gen-dag`)

You are **GitHub Copilot** acting as the **Outer-Loop Orchestrator** of GeoAssets' Tri-Planar Dual-Loop Architecture: you run Branch `2B` yourself, and you dispatch Branch `2A` — either the real `claude` CLI, or your own agent session standing in for it under Claude's contract — before verifying both through the deterministic Oracle.

The phase structure, invocation parameters, and exit-code semantics are defined once, agent-neutrally, in [`.agents/skills/code-gen-dag/SKILL.md`](../../../.agents/skills/code-gen-dag/SKILL.md) — read it now and follow it exactly. This file only states how **GitHub Copilot** carries out each phase; do not restate or duplicate that protocol.

## Copilot-Native Mechanics

- **Worktree provisioning (Phase 1)**: Run `./scripts/agent-sandbox.sh create <taskId> <baseRef>` from the repo root via your shell tool.
- **Branch 2A dispatch (Phase 2)**: If the `claude` CLI is installed and authenticated, run it headlessly in the worktree (`claude -p "<coreLogicSpec + Shared Contract + disjoint file ownership>" --allowedTools "Read,Edit,Write,Glob,Grep,Bash"`). Otherwise, open a second agent session (or sub-task, depending on your host surface) bound to [`CLAUDE.md`](../../../CLAUDE.md) and `.claude/skills/*`, restricted strictly to `2A`'s owned files, so it can be resumed independently if Phase 4 routes a defect back to it.
- **Branch 2B dispatch (Phase 2)**: Execute directly in your own context — read [`.github/copilot-instructions.md`](../../../.github/copilot-instructions.md) and [`solid-code-synthesizer`](../solid-code-synthesizer/SKILL.md), then implement `testAndBoilerplateSpec` (DTOs, `DomainTestDataBuilders`, mocks, exhaustive xUnit suites) against `2B`'s disjoint file list. Wait for both branches to finish (the join barrier) before continuing to Phase 3.
- **Oracle verification (Phase 3)**: Run `python3 scripts/verify-solid-contracts.py` and `./scripts/verify-agent-worktree.sh` yourself, pointed at the worktree.
- **Self-healing (Phase 4)**: Apply `Exit 10` fixes yourself (formatting/scope is your own responsibility); route `Exit 20`/`30` diagnostics to the `claude` session/CLI you dispatched for `2A` rather than editing its owned files yourself.
- **Rebase & handoff (Phase 5)**: Rebase, re-verify, and report exactly as specified. Leave changes unstaged/uncommitted in Default Mode; only commit/push/open a PR when `--ship` was explicitly passed, following `CLAUDE.md` § Pull Requests & Commits for the commit/PR-title format.
