---
name: plan-gen-dag
description: >-
  Run GeoAssets' 5-phase Tri-Planar Outer-Loop read-only planning and architecture verification
  DAG natively inside Claude Code, dispatching Claude Code itself (2A — architecture/domain
  reasoning) and GitHub Copilot (2B — C# 14 contracts/xUnit QA matrix) with zero code or
  working-tree changes. Activate when the user invokes /plan-gen-dag or asks Claude Code for a
  multi-agent implementation plan without modifying code.
---

# GeoAssets Tri-Planar Read-Only Planning Orchestrator — Claude Code Native (`/plan-gen-dag`)

You are Claude Code acting as the **Outer-Loop Orchestrator** running the **Read-Only Planning** variant of the protocol: you produce Branch `2A`'s architecture brief yourself, and you dispatch Branch `2B` — either the real `copilot` CLI in a non-mutating mode, or a read-only Claude subagent standing in for it — then audit both against four deterministic gates.

> **Non-Negotiable Invariant — Zero Repository Changes**: never create, edit, stage, or delete files in the repository workspace, and never create branches, worktrees, or commits during this skill, unless the user explicitly passes `--out <path>` for Phase 5 export.

The phase structure, invocation parameters, and gate semantics are defined once, agent-neutrally, in [`.agents/skills/plan-gen-dag/SKILL.md`](../../../.agents/skills/plan-gen-dag/SKILL.md) — read it now and follow it exactly. This file only states how **Claude Code** carries out each phase; do not restate or duplicate that protocol.

## Claude-Native Mechanics

- **Baseline snapshot (Phase 1)**: Run `git status --porcelain && git rev-parse --short HEAD` yourself and read [`CLAUDE.md`](../../../CLAUDE.md), [`.github/copilot-instructions.md`](../../../.github/copilot-instructions.md), [`outer-loop-dag-orchestrator`](../outer-loop-dag-orchestrator/SKILL.md), [`solid-code-synthesizer`](../solid-code-synthesizer/SKILL.md), and any other relevant `.claude/skills/<domain>/SKILL.md`.
- **Branch 2A dispatch (Phase 2)**: Produce the `architectureBrief` directly in your own context. Never grant yourself `Edit`/`Write` or a mutating `Bash` call during this skill.
- **Branch 2B dispatch (Phase 2)**: If a read-only, non-mutating `copilot` CLI invocation is available, use it to produce the `testAndScaffoldingBrief`. Otherwise, spawn a read-only subagent (e.g. the `Plan` or `Explore` agent type) loaded with [`.github/copilot-instructions.md`](../../../.github/copilot-instructions.md) and [`.claude/agents/test-agent.md`](../../../.claude/agents/test-agent.md). Wait for both briefs before Phase 3.
- **Plan Oracle (Phase 3)**: Audit the combined plan against the four gates yourself — re-running `git status --porcelain` for Gate 0 is mandatory proof of zero mutation.
- **Self-healing (Phase 4)**: Revise both briefs yourself (you authored `2A` directly and reviewed `2B`'s output) and re-run Phase 3, up to `maxSelfHealRetries`.
- **Artifact synthesis & handoff (Phase 5)**: Write the verified plan to `<artifactDir>/plan-<taskId>.md` (or this session's working notes) and, only if `--out <path>` was explicitly passed, also write it there. Close with the one-command handoff: `/code-gen-dag <taskId> Implement verified plan from plan-<taskId>.md`.
