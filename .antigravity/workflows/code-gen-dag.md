# Tri-Planar Multi-Agent Code Generation Blueprint (`/code-gen-dag`)

Declarative workflow specification ([`code-gen-dag.json`](code-gen-dag.json)) for **Google Antigravity** acting as the Outer-Loop DAG Orchestrator over **Claude Code** and **GitHub Copilot** across any programming stack configured in [`.harness/harness.env`](../../.harness/harness.env).

This is Google Antigravity's own concrete implementation of the agent-neutral ControlPlane-OuterLoop protocol defined in [`.agents/skills/code-gen-dag/SKILL.md`](../../.agents/skills/code-gen-dag/SKILL.md); Claude Code and GitHub Copilot each run their own native adaptation of that same protocol from `.claude/skills/code-gen-dag/SKILL.md` and `.github/skills/code-gen-dag/SKILL.md` respectively.

## Invocation & Deterministic `taskId` Resolution

```text
/code-gen-dag [taskId | "<prompt>"] [description | prompt] [--base origin/develop] [--retries 3] [--ship] [--docker] [--e2e[=scope]] [--e2e-retries 2]
```

- **Prompt-Only Execution (Optional `taskId`)**: The command can be executed with only a natural language prompt without an explicit `taskId`.
- **Resolution Strategy**:
  - If a Jira ticket key is detected in the prompt (e.g. `XD01-179`), derives `<TICKET-KEY>-<slug>`.
  - If no ticket key is supplied, generates a kebab slug plus a 6-character collision-free hex hash: `task-<slug>-<short-hash>` (e.g. `task-member-pagination-a7f3b2`).
  - Prints `[ORCHESTRATOR] Resolved taskId: <taskId>` during Phase 1.

```mermaid
flowchart TD
    subgraph ControlPlane["Outer Loop — Google Antigravity (Orchestrator)"]
        P1["Phase 1: Task Decomposition & Worktree Init (scripts/agent-sandbox.sh)"]
        P3["Phase 3: Dual Deterministic Oracles (verify-solid-contracts.py + verify-agent-worktree.sh)"]
        P4["Phase 4: Bounded Self-Healing Router (Max 3 Retries + Tree Snapshot Rewind)"]
        P5["Phase 5: Rebase on origin/develop, Post-Rebase Re-Verify & Merge Prep"]
        P6["Phase 6: Ship & PR Merge to develop (with --ship)"]
        P7["Phase 7: Docker Redeploy & Health Check (with --docker)"]
        P8["Phase 8: Autonomous E2E Agentic QA & Bounded Self-Healing (with --e2e)"]
    end

    subgraph ExecutionPlane["Inner Loop — Isolated Git Worktree (../worktrees/<task-id>)"]
        CC["Claude Code (2A: Deep Reasoning, Domain Core, Clean/Hexagonal Boundaries)"]
        GH["GitHub Copilot (2B: DTO/Adapter Scaffolding, Builders, Unit & Contract Tests)"]
    end

    P1 --> CC
    P1 --> GH
    CC --> P3
    GH --> P3
    P3 -- "Exit 0 (Pass)" --> P5
    P3 -- "Exit 10 (Format/Scope)" --> P4
    P3 -- "Exit 20 (Build/Typecheck/Lint)" --> P4
    P3 -- "Exit 30 (SOLID/Arch/Test)" --> P4
    P4 -- "Exit 10" --> GH
    P4 -- "Exit 20 / 30" --> CC
    P5 --> P6 --> P7 --> P8
```
