---
name: code-gen-dag
description: >-
  Orchestrate GeoAssets' 5-phase Tri-Planar Outer-Loop code generation and verification DAG over
  Claude Code (deep reasoning/core domain) and GitHub Copilot (mechanical scaffolding/xUnit QA)
  inside an isolated Git worktree. Activate when the user invokes /code-gen-dag or asks Antigravity
  to orchestrate a task across Claude Code and GitHub Copilot.
---

# GeoAssets Tri-Planar Outer-Loop Orchestrator (`/code-gen-dag`)

Act as **Google Antigravity** in the **ControlPlane-OuterLoop** role defined in
[`.antigravity/workflows/code-gen-dag.json`](../../../.antigravity/workflows/code-gen-dag.json)
and [`.antigravity/workflows/code-gen-dag.md`](../../../.antigravity/workflows/code-gen-dag.md).

## Reference Contracts (Read on Demand)

- **DAG Blueprint**: [`.antigravity/workflows/code-gen-dag.json`](../../../.antigravity/workflows/code-gen-dag.json)
- **Sandbox Manager**: [`scripts/agent-sandbox.sh`](../../../scripts/agent-sandbox.sh)
- **Deterministic Oracle**: [`scripts/verify-agent-worktree.sh`](../../../scripts/verify-agent-worktree.sh)
- **Claude Code Contract (Branch 2A)**: [`CLAUDE.md`](../../../CLAUDE.md) and `.claude/skills/*/SKILL.md`
- **GitHub Copilot Contract (Branch 2B)**: [`.github/copilot-instructions.md`](../../../.github/copilot-instructions.md)
- **Static Guardrails**: [`Directory.Build.props`](../../../Directory.Build.props) and [`BannedSymbols.txt`](../../../BannedSymbols.txt)

---

## Invocation & Parameters

Expected invocation format:

```text
/code-gen-dag <taskId> [description | Jira key] [--base origin/develop] [--retries 3] [--ship]
```

1. **`taskId`** (required, regex `^[a-zA-Z0-9._-]+$`):
   - Examples: `XD01-195-spatial-buffer-cache`, `xd01-204-wfs-bbox-filter`.
   - If the user only supplies a prose description or bare Jira key (e.g., `XD01-195`), derive a normalized `<TICKET-KEY>-<kebab-slug>` `taskId`.
   - Hard-reject `main`, `master`, `develop`, or `origin/*` as a `taskId`.
2. **`baseRef`** (default: `origin/develop`):
   - **Non-negotiable branch policy**: `main`, `origin/main`, `master`, and `origin/master` are strictly forbidden at every phase.
3. **`maxSelfHealRetries`** (default: `3`, range: `1..5`).
4. **`--ship`** (optional flag, default: `false`):
   - When omitted, stop after Phase 5 verification and leave changes in `../worktrees/<taskId>` ready for user review with proposed commit/PR metadata.
   - When `--ship` is explicitly passed, stage, commit (ending with `(<TICKET-KEY>)`), push, and open a PR targeting `develop` (never `main`) only if 100% of build and test gates pass.

---

## Execution Protocol

Execute the 5 DAG phases strictly in order. Do not skip gates or suppress diagnostics.

### Phase 1 — Task Decomposition & Worktree Initialization

1. **Validate Dependencies & Context**:
   - Read [`CLAUDE.md`](../../../CLAUDE.md) and [`.github/copilot-instructions.md`](../../../.github/copilot-instructions.md), plus any relevant `.claude/skills/<domain>/SKILL.md` (`csharp-conventions`, `observability-conventions`, `jira-conventions`, `ui-ux-pro-max`).
   - If `taskId` references a Jira issue and Jira MCP tools are available, fetch the full issue description and verify both formal `Blocks` links and prose `"Depends on <KEY>"` dependencies before proceeding.
   - Inspect target files in the repository so interfaces and existing conventions are grounded in real code.
2. **Decompose with Disjoint File Ownership**:
   - Because Branch `2A` and Branch `2B` execute concurrently inside the same worktree (`../worktrees/<taskId>`), define an explicit **Shared Type/Interface Contract** upfront (exact namespaces, record/class names, method signatures, and `CancellationToken`/`TimeProvider` parameters) and assign **strictly disjoint file paths**:
     - **`coreLogicSpec` (Branch 2A — `claude-code`)**: Core domain models, invariants, spatial/topology algorithms, workflow/provider logic, and architectural wiring.
     - **`testAndBoilerplateSpec` (Branch 2B — `github-copilot`)**: DTOs/mappers (if disjoint), `DomainTestDataBuilders`, `NSubstitute`/`Moq` test doubles, `FakeTimeProvider` fixtures, and exhaustive xUnit `[Fact]`/`[Theory]` suites targeting 100% branch coverage.
3. **Provision Isolated Worktree**:
   - Run from the repository root:
     ```bash
     ./scripts/agent-sandbox.sh create <taskId> <baseRef>
     ```
   - *Note*: `../worktrees/<taskId>` sits outside the repository root directory. Attempt standard execution first; if blocked by workspace path sandboxing, retry that command outside the sandbox. Abort immediately on non-zero exit.

---

### Phase 2 — Parallel Inner-Loop Dispatch (`2A` + `2B`)

1. **Probe Inner-Loop Runtimes**:
   - Check CLI availability once (`command -v claude`, `command -v copilot`).
2. **Dispatch Branch 2A (`claude-code`) and Branch 2B (`github-copilot`) in Parallel** inside `../worktrees/<taskId>`:
   - **Branch 2A (`claude-code` — Deep Reasoning)**:
     - *External CLI mode* (if `claude` is installed and authenticated): Run headlessly in `../worktrees/<taskId>` (e.g., `claude -p "<coreLogicSpec + Shared Contract + disjoint file ownership>" --allowedTools "Read,Edit,Write,Glob,Grep,Bash"`).
     - *Native Subagent fallback* (if `claude` CLI is unavailable): Use `define_subagent` (`EnableWriteTools: true`) and `invoke_subagent` to launch a `claude-code-specialist` subagent bound to [`CLAUDE.md`](../../../CLAUDE.md) and `.claude/skills/*`, restricted strictly to `../worktrees/<taskId>` and `2A`'s owned files.
   - **Branch 2B (`github-copilot` — Mechanical & QA)**:
     - *External CLI mode* (if standalone agentic `copilot` CLI is installed and authenticated): Run headlessly in `../worktrees/<taskId>` with `<testAndBoilerplateSpec + Shared Contract + disjoint file ownership>`.
     - *Native Subagent fallback* (if agentic `copilot` CLI is unavailable): Use `define_subagent` (`EnableWriteTools: true`) and `invoke_subagent` to launch a `github-copilot-worker` subagent bound to [`.github/copilot-instructions.md`](../../../.github/copilot-instructions.md), restricted strictly to `../worktrees/<taskId>` and `2B`'s owned files.
3. **Mandatory Guardrails Injected into Both Branches**:
   - Inject `System.TimeProvider` (`FakeTimeProvider` in tests); never call `DateTime.Now`/`UtcNow` or `DateTimeOffset.Now`/`UtcNow` (`RS0030` / [`BannedSymbols.txt`](../../../BannedSymbols.txt)).
   - Preserve strict Onion Architecture: zero `Npgsql`, `Microsoft.EntityFrameworkCore`, or PostGIS references inside `core/` projects.
   - Propagate `CancellationToken` across async methods; zero `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`, or `Thread.Sleep`.
   - Zero `#pragma warning disable`, `[SuppressMessage]`, or `<NoWarn>`.
4. **Join Barrier**:
   - Wait until **both** Branch `2A` and Branch `2B` complete before entering Phase 3.

---

### Phase 3 — Deterministic Oracle Verification

1. Run the 3-stage Oracle gatekeeper from the repository root:
   ```bash
   ./scripts/verify-agent-worktree.sh ../worktrees/<taskId>
   ```
2. Map the exit code strictly:
   - **`Exit 0`**: Oracle passed (`dotnet format style --verify-no-changes`, `dotnet build --no-incremental /warnaserror`, and `NetArchTest` architecture invariants). Also execute the touched xUnit test project(s) in `../worktrees/<taskId>` (`dotnet test <test-csproj> --no-build --nologo`) to verify `2B`'s newly generated unit tests pass. If any unit test fails, treat as **`Exit 30`**. Otherwise advance to **Phase 5**.
   - **`Exit 2`**: Fatal environment or protected-branch violation. **Abort immediately** and surface the error.
   - **`Exit 10` / `Exit 20` / `Exit 30`**: Enter **Phase 4**.

---

### Phase 4 — Bounded Self-Healing Feedback Loop

Track `healAttempt` starting at `1` up to `maxSelfHealRetries` (default `3`):

- **`Exit 10` (`FormattingDrift`) → Route to `github-copilot`**:
  - Run `dotnet format style GeoAssets.sln` inside `../worktrees/<taskId>` without altering domain logic, then return to **Phase 3**.
- **`Exit 20` (`CompilerOrAnalyzerViolation`) → Route to `claude-code`**:
  - Feed the targeted Roslyn / `AsyncFixer` / `Meziantou.Analyzer` / `BannedApiAnalyzers` diagnostics emitted by `verify-agent-worktree.sh` to `claude-code` (or coordinate signature mismatches across `2A`/`2B`).
  - Fix every diagnostic in source **without** `#pragma warning disable`, `[SuppressMessage]`, or `<NoWarn>`, then return to **Phase 3**.
- **`Exit 30` (`ArchitectureOrInvariantFailure` / xUnit Test Failure) → Route to `claude-code`**:
  - Feed the failed assertion / stack trace summary to `claude-code` to repair layer coupling or domain defects (and update test setup via `github-copilot` only if the test expectation itself contradicted the spec), then return to **Phase 3**.

**Hard Escalation Rule**: If Phase 3 still fails after `maxSelfHealRetries` attempts, **halt immediately**, keep `../worktrees/<taskId>` intact (`preserveWorktree: true`), and report the exact remaining diagnostics to the user.

---

### Phase 5 — Rebase, Post-Rebase Verification & Merge Preparation

Execute only after Phase 3 achieves **`Exit 0`**:

1. **Rebase onto `origin/develop`**:
   - Inside `../worktrees/<taskId>`, run:
     ```bash
     git fetch origin develop && git rebase origin/develop
     ```
   - If a **mechanical** rebase conflict occurs, route conflict resolution to `claude-code` with full worktree context. If a **semantic** conflict is detected, run `git rebase --abort` and escalate to the user.
2. **Post-Rebase Re-Verification (Never Trust Pre-Rebase Green)**:
   - Re-run `./scripts/verify-agent-worktree.sh ../worktrees/<taskId>` (must return `Exit 0`) and run `dotnet test GeoAssets.sln --no-build --nologo` inside `../worktrees/<taskId>`. Any test failure here is a hard stop.
3. **Prepare Merge Artifact (or Ship if `--ship`)**:
   - Inspect `git status` and `git diff --stat` inside `../worktrees/<taskId>`.
   - **Default Mode (No `--ship`)**: Do not commit or push. Report:
     - Worktree path (`../worktrees/<taskId>`) & branch name
     - Files modified by Branch `2A` (`claude-code`) vs. Branch `2B` (`github-copilot`)
     - Oracle & test suite verification status + number of self-healing iterations used
     - Ready-to-use commit message (`<type>(<scope>): <summary> (<TICKET-KEY>)`) and PR title (`<TICKET-KEY>: <summary>` targeting `develop`)
     - Cleanup command for after merge: `./scripts/agent-sandbox.sh destroy <taskId>`
   - **`--ship` Mode**: Commit using repo conventions ending with `(<TICKET-KEY>)`, push branch (`git push -u origin <branch>`), and open a PR targeting `develop` (**never** `main`) via `gh pr create`. Remind the user to run `./scripts/agent-sandbox.sh destroy <taskId>` once the PR is merged into `develop`.

