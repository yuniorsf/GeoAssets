# GeoAssets — Tri-Planar Multi-Agent Orchestration Blueprint

This directory contains the declarative workflow specification ([`code-gen-dag.json`](code-gen-dag.json)) for **Google Antigravity** acting as the Outer-Loop DAG Orchestrator over **Claude Code** and **GitHub Copilot**. It is Antigravity's own concrete implementation of the agent-neutral protocol defined in [`.agents/skills/code-gen-dag/SKILL.md`](../../.agents/skills/code-gen-dag/SKILL.md) — the same `/code-gen-dag` orchestration can also be run natively from [Claude Code](../../.claude/skills/code-gen-dag/SKILL.md) or [GitHub Copilot](../../.github/skills/code-gen-dag/SKILL.md).

## Architectural Model

```mermaid
flowchart TD
    subgraph ControlPlane["Outer Loop — Google Antigravity (Orchestrator)"]
        P1["Phase 1: Task Decomposition & Worktree Init\n(scripts/agent-sandbox.sh create <task-id>)"]
        P3["Phase 3: Deterministic Oracle Gatekeeper\n(scripts/verify-agent-worktree.sh)"]
        P4["Phase 4: Self-Healing Feedback Router\n(Max 3 Retries)"]
        P5["Phase 5: Rebase on origin/develop,\nPost-Rebase Re-Verify & Cleanup"]
    end

    subgraph ExecutionPlane["Inner Loop — Isolated Git Worktree (../worktrees/<task-id>)"]
        CC["Claude Code\n(Deep Reasoning, Domain Logic, Refactoring)"]
        GH["GitHub Copilot\n(Mechanical Boilerplate, xUnit Tests, Mocks)"]
    end

    P1 --> CC
    P1 --> GH
    CC --> P3
    GH --> P3
    P3 -- "Exit 0 (Pass)" --> P5
    P3 -- "Exit 10 (Format)" --> P4
    P3 -- "Exit 20 (Build/Analyzer)" --> P4
    P3 -- "Exit 30 (Arch/Test)" --> P4
    P4 -- "Exit 10" --> GH
    P4 -- "Exit 20 / 30" --> CC
```

## Phase Reference

1. **Phase 1 — Task Decomposition & Worktree Initialization**:
   - Validates Jira dependencies (`Blocks` links + prose `"Depends on"` references).
   - Creates an isolated worktree at `../worktrees/<task-id>` forked strictly from `origin/develop` (`main` is hard-blocked).
2. **Phase 2 — Parallel Dispatch**:
   - **Claude Code** implements core domain invariants, spatial/topology algorithms, and architectural wiring per `CLAUDE.md` and `.claude/skills/*`.
   - **GitHub Copilot** scaffolds DTOs, test builders, mocks, and xUnit tests per `.github/copilot-instructions.md`.
3. **Phase 3 — Deterministic Oracle (`scripts/verify-agent-worktree.sh`)**:
   - Stage 1 (`Exit 10`): `dotnet format --verify-no-changes`
   - Stage 2 (`Exit 20`): `dotnet build --no-incremental /warnaserror` (Roslyn `latest-all`, `AsyncFixer`, `Meziantou.Analyzer`, `BannedApiAnalyzers` with `BannedSymbols.txt`)
   - Stage 3 (`Exit 30`): `dotnet test --no-build` (Architecture & invariant tests)
4. **Phase 4 — Feedback Self-Healing Loop**:
   - Routes targeted diagnostic summaries back to Copilot (`Exit 10`) or Claude Code (`Exit 20` / `Exit 30`) for up to 3 bounded repair cycles without permitting `#pragma warning disable` or `<NoWarn>`.
5. **Phase 5 — Worktree Cleanup & Merge Preparation**:
   - Rebases onto `origin/develop`, re-runs the Oracle post-rebase, prepares the PR targeting `develop` with `(TICKET-KEY)` commit suffixes, and destroys the sandbox via `./scripts/agent-sandbox.sh destroy <task-id>` once merged.

