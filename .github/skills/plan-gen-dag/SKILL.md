---
name: plan-gen-dag
description: >-
  Orchestrate the 5-phase Tri-Planar Outer-Loop read-only planning and
  architecture verification DAG over Claude Code (architecture/domain reasoning)
  and GitHub Copilot (type contracts/100%-branch QA matrix) with zero code or
  working-tree changes. Activate when the user invokes /plan-gen-dag or asks
  for a multi-agent implementation plan without modifying code.
---

# Tri-Planar Read-Only Planning Orchestrator (`/plan-gen-dag`)

You are **GitHub Copilot** acting as the **ControlPlane-OuterLoop Orchestrator (Read-Only Planning)** for this project's Tri-Planar Dual-Loop Architecture — the shared, agent-neutral protocol defined in [`.agents/skills/plan-gen-dag/SKILL.md`](../../../.agents/skills/plan-gen-dag/SKILL.md). This file adapts that protocol to GitHub Copilot's own mechanics: a read-only Agent Mode session restricted to read/search tools. Google Antigravity's own concrete implementation of the same protocol lives in [`.antigravity/workflows/plan-gen-dag.json`](../../../.antigravity/workflows/plan-gen-dag.json) / [`.md`](../../../.antigravity/workflows/plan-gen-dag.md); Claude Code's lives in [`.claude/skills/plan-gen-dag/SKILL.md`](../../../.claude/skills/plan-gen-dag/SKILL.md).

> Copilot's primary role in this repository is Branch `2B` (mechanical/QA) — see [`.github/copilot-instructions.md`](../../../.github/copilot-instructions.md). Use this file only when Copilot itself is asked to drive the full read-only planning orchestration end-to-end.

> **Non-Negotiable Invariant — Zero Repository Changes:**
> Never create, edit, stage, or delete files in the repository workspace, and never create branches, worktrees, or commits during `/plan-gen-dag` (unless `--out <path>` is explicitly passed). All inner-loop agents **must** run strictly read-only.

## Reference Contracts (Read on Demand)

- **Active Toolchain & Rules**: [`.harness/harness.env`](../../../.harness/harness.env) and [`harness.config.json`](../../../harness.config.json)
- **Planning & Execution DAGs**: [`.antigravity/workflows/plan-gen-dag.json`](../../../.antigravity/workflows/plan-gen-dag.json) and [`.antigravity/workflows/code-gen-dag.json`](../../../.antigravity/workflows/code-gen-dag.json)
- **Categorical & SOLID Contracts**: [`../outer-loop-dag-orchestrator/SKILL.md`](../outer-loop-dag-orchestrator/SKILL.md) and [`../solid-code-synthesizer/SKILL.md`](../solid-code-synthesizer/SKILL.md)
- **Claude Code Personas (`2A`)**: [`CLAUDE.md`](../../../CLAUDE.md), [`.claude/agents/arch-designer.md`](../../../.claude/agents/arch-designer.md), [`.claude/agents/task-planner.md`](../../../.claude/agents/task-planner.md), and `.claude/skills/*/SKILL.md`
- **GitHub Copilot Personas (`2B`)**: [`.github/copilot-instructions.md`](../../../.github/copilot-instructions.md), [`.github/skills/solid-code-synthesizer/SKILL.md`](../../../.github/skills/solid-code-synthesizer/SKILL.md), and [`.claude/agents/test-agent.md`](../../../.claude/agents/test-agent.md)

---

## Invocation & Parameters

```text
/plan-gen-dag <taskId | description | ticket key> [--base origin/develop] [--retries 3] [--out <path>]
```

- Deliver the verified plan as an **Antigravity Conversation Artifact** in `<artifactDir>/plan-<taskId>.md` (`UserFacing: true`, `RequestFeedback: true`). Only export to `<path>` when `--out <path>` is explicitly passed.

---

## Execution Protocol

### Phase 1 — Read-Only Baseline Snapshot & Scope Grounding
1. Capture `baselineGitStatus` (`git status --porcelain && git rev-parse --short HEAD`) to prove zero workspace mutations.
2. Read `.harness/harness.env`, `harness.config.json`, `CLAUDE.md`, `.github/copilot-instructions.md`, and relevant `.claude/skills/*`. If a Jira key is provided, inspect full issue details (`Blocks` + prose `"Depends on"`) without transitioning ticket status.
3. Locate real modules, interfaces, and test suites in the workspace to ground `architectureBrief` (`2A`) and `testAndScaffoldingBrief` (`2B`).

### Phase 2 — Parallel Read-Only Specialist Dispatch (`2A ⊗ 2B`)
1. **Branch 2A (`claude-code`, strictly read-only)**: Run headless `claude -p ...` in read-only mode if the CLI is available in the environment, to design Clean/Hexagonal boundaries (`domainCoreDirs` free of ORM/DB/HTTP/UI dependencies), Clock & cancellation propagation, OpenTelemetry context propagation, sequenced tasks, risk matrix, and `coreLogicSpec` file list (`[NEW]` / `[MODIFY]`). This repo's docs do not establish a Copilot-native mechanism for driving another agent's session, so treat this branch as an external call.
2. **Branch 2B (`github-copilot`, strictly read-only — the current Agent Mode session restricted to read/search tools)**:
   - Draft concrete target-language type & segregated interface contracts (`<= 5` cohesive methods per port), file touch manifest (`testAndBoilerplateSpec`), `TestDataBuilders`, narrow mocks, shared LSP contract test design, and 100%-branch-coverage unit test matrix.
3. Wait at the join barrier until both branches complete.

### Phase 3 — Deterministic Plan Oracle Verification
Audit the combined `2A` + `2B` plan against four gates:
- **Gate 0 (`Code 2`)**: `git status --porcelain` matches `baselineGitStatus` identically.
- **Gate 1 (`Code 10`)**: Every `[MODIFY]` file exists on disk; `pairedFileRules` in `harness.config.json` are honored; `2A ∩ 2B = ∅` (100% disjoint file ownership).
- **Gate 2 (`Code 20`)**: Proposed code snippets contain zero `bannedApiPatterns` (raw wall-clock, blocking sync-over-async) and zero `suppressionPatterns` (`eslint-disable`, `@ts-ignore`, `# noqa`, `#[allow(`, `//nolint`, `#pragma warning disable`, `<NoWarn>`).
- **Gate 3 (`Code 30`)**: Zero `forbiddenCoreImports` inside `domainCoreDirs`; ISP role segregation (`Reader` vs. `Writer`) and LSP boundary error parity respected; zero unimplemented stubs; every proposed branch and guard clause in `2A` has test coverage in `2B`; any tabular display or collection-returning query specifies mandatory pagination contracts (`page`, `pageSize`, `totalCount`, deterministic sort, and accessible UI pager controls).

### Phase 4 — Bounded Plan Self-Healing Loop (`1..maxSelfHealRetries`)
- `Code 10` → Route to `2B` planner to fix paths and re-partition overlapping files.
- `Code 20` → Route to `2A` planner to replace banned APIs/suppressions with injected Clock and cancellation-aware signatures, then sync with `2B`.
- `Code 30` → Route to `2A` & `2B` planners to introduce clean domain ports and fill test coverage gaps.

### Phase 5 — Conversation Artifact Synthesis & `/code-gen-dag` Handoff
Write `<artifactDir>/plan-<taskId>.md` containing: (1) Executive Summary & Dependencies, (2) Clean/Hexagonal Mermaid Diagram, (3) Shared Type & Port Contract, (4) Disjoint `2A` vs. `2B` Manifest, (5) 100%-Branch Test & Contract Matrix, (6) Risks & Observability Notes, and (7) Ready-to-run `/code-gen-dag <taskId> ...` handoff command. Confirm `git status --porcelain` remains untouched.
