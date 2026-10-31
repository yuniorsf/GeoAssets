---
name: plan-gen-dag
description: >-
  Run GeoAssets' 5-phase Tri-Planar Outer-Loop read-only planning and architecture verification
  DAG natively from GitHub Copilot, dispatching Claude Code (2A — architecture/domain reasoning)
  and Copilot itself (2B — C# 14 contracts/xUnit QA matrix) with zero code or working-tree
  changes. Activate when the user invokes /plan-gen-dag or asks Copilot for a multi-agent
  implementation plan without modifying code.
---

# GeoAssets Tri-Planar Read-Only Planning Orchestrator — GitHub Copilot Native (`/plan-gen-dag`)

You are **GitHub Copilot** acting as the **Outer-Loop Orchestrator** running the **Read-Only Planning** variant of the protocol: you produce Branch `2B`'s contracts/QA-matrix brief yourself, and you dispatch Branch `2A` — either the real `claude` CLI in a read-only mode, or your own read-only agent session standing in for it — then audit both against four deterministic gates.

> **Non-Negotiable Invariant — Zero Repository Changes**: never create, edit, stage, or delete files in the repository workspace, and never create branches, worktrees, or commits during this skill, unless the user explicitly passes `--out <path>` for Phase 5 export.

The phase structure, invocation parameters, and gate semantics are defined once, agent-neutrally, in [`.agents/skills/plan-gen-dag/SKILL.md`](../../../.agents/skills/plan-gen-dag/SKILL.md) — read it now and follow it exactly. This file only states how **GitHub Copilot** carries out each phase; do not restate or duplicate that protocol.

## Copilot-Native Mechanics

- **Baseline snapshot (Phase 1)**: Run `git status --porcelain && git rev-parse --short HEAD` yourself and read [`CLAUDE.md`](../../../CLAUDE.md), [`.github/copilot-instructions.md`](../../../.github/copilot-instructions.md), and [`solid-code-synthesizer`](../solid-code-synthesizer/SKILL.md).
- **Branch 2A dispatch (Phase 2)**: If the `claude` CLI is installed and authenticated, run it read-only (`claude -p "<architectureBrief>" --allowedTools "Read,Glob,Grep"`, never `Edit`/`Write`). Otherwise, open a second read-only agent session loaded with [`CLAUDE.md`](../../../CLAUDE.md), [`.claude/agents/arch-designer.md`](../../../.claude/agents/arch-designer.md), and [`.claude/agents/task-planner.md`](../../../.claude/agents/task-planner.md).
- **Branch 2B dispatch (Phase 2)**: Produce the `testAndScaffoldingBrief` directly in your own context — never make a mutating edit during this skill. Wait for both briefs before Phase 3.
- **Plan Oracle (Phase 3)**: Audit the combined plan against the four gates yourself — re-running `git status --porcelain` for Gate 0 is mandatory proof of zero mutation.
- **Self-healing (Phase 4)**: Revise both briefs yourself and re-run Phase 3, up to `maxSelfHealRetries`.
- **Artifact synthesis & handoff (Phase 5)**: Write the verified plan to `<artifactDir>/plan-<taskId>.md` and, only if `--out <path>` was explicitly passed, also write it there. Close with the one-command handoff: `/code-gen-dag <taskId> Implement verified plan from plan-<taskId>.md`.
