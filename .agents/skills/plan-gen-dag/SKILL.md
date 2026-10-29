---
name: plan-gen-dag
description: >-
  Orchestrate GeoAssets' 5-phase Tri-Planar Outer-Loop read-only planning and architecture
  verification DAG over Claude Code (architecture/domain reasoning) and GitHub Copilot
  (C# 14 contracts/xUnit QA matrix) with zero code or working-tree changes. Activate when the
  user invokes /plan-gen-dag or asks for a multi-agent implementation plan without modifying code.
---

# GeoAssets Tri-Planar Read-Only Planning Orchestrator (`/plan-gen-dag`)

Act as **Google Antigravity** in the **ControlPlane-OuterLoop (Read-Only Planning)** role defined in
[`.antigravity/workflows/plan-gen-dag.json`](../../../.antigravity/workflows/plan-gen-dag.json)
and [`.antigravity/workflows/plan-gen-dag.md`](../../../.antigravity/workflows/plan-gen-dag.md).

> **Non-Negotiable Invariant — Zero Repository Changes:**
> Never create, edit, stage, or delete files in the repository workspace, and never create branches, worktrees, or commits during `/plan-gen-dag` (unless the user explicitly passes `--out <path>` for Phase 5 export). All inner-loop agents **must** run strictly with read-only permissions.

## Reference Contracts (Read on Demand)

- **Planning DAG Blueprint**: [`.antigravity/workflows/plan-gen-dag.json`](../../../.antigravity/workflows/plan-gen-dag.json)
- **Downstream Execution DAG**: [`.antigravity/workflows/code-gen-dag.json`](../../../.antigravity/workflows/code-gen-dag.json)
- **Categorical & SOLID Synthesis Contracts**: [`../outer-loop-dag-orchestrator/SKILL.md`](../outer-loop-dag-orchestrator/SKILL.md) and [`../solid-code-synthesizer/SKILL.md`](../solid-code-synthesizer/SKILL.md)
- **Claude Code Contract & Personas (Branch 2A)**: [`CLAUDE.md`](../../../CLAUDE.md), [`.claude/agents/arch-designer.md`](../../../.claude/agents/arch-designer.md), [`.claude/agents/task-planner.md`](../../../.claude/agents/task-planner.md), and `.claude/skills/*/SKILL.md`
- **GitHub Copilot Contract & Persona (Branch 2B)**: [`.github/copilot-instructions.md`](../../../.github/copilot-instructions.md) and [`.claude/agents/test-agent.md`](../../../.claude/agents/test-agent.md)
- **Static Guardrails**: [`Directory.Build.props`](../../../Directory.Build.props) and [`BannedSymbols.txt`](../../../BannedSymbols.txt)

---

## Invocation & Parameters

Expected invocation format:

```text
/plan-gen-dag <taskId | description | Jira key> [--base origin/develop] [--retries 3] [--out <path>]
```

1. **`taskId`**:
   - If a ticket key or slug is given (e.g., `XD01-195-spatial-buffer-cache` or `XD01-195`), normalize to a valid slug (`^[a-zA-Z0-9._-]+$`) suitable for direct handoff to `/code-gen-dag`.
   - If only a free-form feature request is given, derive a concise `<kebab-slug>` `taskId`.
2. **`baseRef`** (default: `origin/develop`; `main`, `origin/main`, `master`, `origin/master` are forbidden as target integration baselines).
3. **`maxSelfHealRetries`** (default: `3`, range: `1..5`).
4. **`--out <path>`** (optional):
   - By default, deliver the final plan strictly as an **Antigravity Conversation Artifact** in `<artifactDir>/plan-<taskId>.md` so the repository working tree stays untouched.
   - Only write a Markdown file to `<path>` if `--out <path>` is explicitly passed.

---

## Execution Protocol

Execute the 5 read-only planning phases strictly in order.

### Phase 1 — Read-Only Baseline Snapshot & Scope Grounding

1. **Capture Working-Tree Baseline**:
   - Run a read-only inspection from the repository root:
     ```bash
     git status --porcelain && git rev-parse --short HEAD
     ```
   - Save `baselineGitStatus` so Phase 3 and Phase 5 can prove zero workspace files were mutated.
2. **Ground Requirements & Codebase Context**:
   - Read [`CLAUDE.md`](../../../CLAUDE.md), [`.github/copilot-instructions.md`](../../../.github/copilot-instructions.md), [`BannedSymbols.txt`](../../../BannedSymbols.txt), and relevant `.claude/skills/<domain>/SKILL.md` files.
   - If `taskId` references a Jira key and Jira MCP tools are available, read the full issue description (without transitioning status!) and check both formal `Blocks` links and prose `"Depends on <KEY>"` dependencies.
   - Locate real projects, interfaces, and existing test suites in `GeoAssets.sln` to produce grounded `architectureBrief` (`2A`) and `testAndScaffoldingBrief` (`2B`) inputs.

---

### Phase 2 — Parallel Read-Only Specialist Dispatch (`2A` + `2B`)

1. **Probe Inner-Loop Runtimes**:
   - Check CLI availability (`command -v claude`, `command -v copilot`).
2. **Dispatch Branch 2A (`claude-code`) and Branch 2B (`github-copilot`) in Parallel (Strictly Read-Only)**:
   - **Branch 2A (`claude-code` — Architecture & Domain Reasoning)**:
     - *External CLI mode* (if `claude` is installed): Run strictly read-only:
       `claude -p "<architectureBrief>" --allowedTools "Read,Glob,Grep"` (never grant `Edit`, `Write`, or mutating `Bash`).
     - *Native Subagent fallback* (if `claude` CLI is unavailable): Invoke the read-only `research` subagent (or `define_subagent` with **`EnableWriteTools: false`**) loaded with [`CLAUDE.md`](../../../CLAUDE.md), [`.claude/agents/arch-designer.md`](../../../.claude/agents/arch-designer.md), [`.claude/agents/task-planner.md`](../../../.claude/agents/task-planner.md), and relevant `.claude/skills/*`.
     - *Required Outputs*:
       1. Architectural & Onion-layer design (`core/` isolation, provider/workflow ports & adapters, RFC 7946 `[lon, lat]` SRID 4326 coordinate-array source-of-truth).
       2. `System.TimeProvider`, `CancellationToken`, and OpenTelemetry `ActivitySource` / UI token considerations.
       3. Sequenced implementation tasks, risk/trade-off matrix, and proposed `coreLogicSpec` file list (`[NEW]` / `[MODIFY]`).
   - **Branch 2B (`github-copilot` — Contracts, Manifest & xUnit QA Matrix)**:
     - *External CLI mode* (if read-only non-mutating prompt flag is verified) or *Native Subagent mode* (`research` or `define_subagent` with **`EnableWriteTools: false`**) loaded with [`.github/copilot-instructions.md`](../../../.github/copilot-instructions.md) and [`.claude/agents/test-agent.md`](../../../.claude/agents/test-agent.md).
     - *Required Outputs*:
       1. Concrete C# 14 / .NET 10 type & interface signatures (`readonly record struct`, `sealed record`, `JsonSerializerContext`, `ArgumentNullException.ThrowIfNull`, `TimeProvider`, `CancellationToken`) as Markdown code blocks.
       2. File-by-file touch manifest enforcing `.razor` + `.razor.cs` code-behind separation and proposed `testAndBoilerplateSpec` file list (`[NEW]` / `[MODIFY]`).
       3. Exhaustive xUnit `[Fact]`/`[Theory]` test matrix (happy paths, guard clauses, boundary cases, `FakeTimeProvider`, cancellation) + `DomainTestDataBuilders` & `NSubstitute` mock design targeting 100% branch coverage.
3. **Join Barrier**:
   - Wait until **both** Branch `2A` and Branch `2B` return their structured planning outputs before entering Phase 3.

---

### Phase 3 — Deterministic Plan Oracle Verification

Audit the combined `2A` + `2B` plan deterministically against four gates:

1. **Gate 0 — Zero-Mutation Check (`Code 2` on failure)**:
   - Run `git status --porcelain` and verify it matches `baselineGitStatus` identically. If any file was modified or created, abort immediately and report the violation.
2. **Gate 1 — Path Existence & Disjoint File Ownership (`Code 10` on failure)**:
   - Verify every `[MODIFY]` file path and every referenced `.csproj` actually exists on disk.
   - Verify any UI component change pairs `.razor` markup with a `.razor.cs` partial class.
   - Verify `coreLogicSpec` (Branch `2A`) and `testAndBoilerplateSpec` (Branch `2B`) file lists have **zero intersection** (`2A ∩ 2B = ∅`) so `/code-gen-dag` can execute them in parallel without write races.
3. **Gate 2 — Static Guardrail & Banned Symbol Audit (`Code 20` on failure)**:
   - Audit every proposed C# signature and code snippet against [`BannedSymbols.txt`](../../../BannedSymbols.txt) and [`Directory.Build.props`](../../../Directory.Build.props):
     - Zero `DateTime.Now`/`UtcNow`/`Today` or `DateTimeOffset.Now`/`UtcNow` (`RS0030` — must use `TimeProvider` / `FakeTimeProvider`).
     - Zero `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`, `Thread.Sleep`, or unguarded `Task.Run` (`AsyncFixer`).
     - Every proposed async signature accepts `CancellationToken`.
     - Zero `#pragma warning disable`, `[SuppressMessage]`, or `<NoWarn>`.
4. **Gate 3 — Onion Architecture, SOLID & Branch Coverage Audit (`Code 30` on failure)**:
   - Confirm zero `Npgsql`, `Microsoft.EntityFrameworkCore`, `GeoAssets.Provider.PostgreSQL`, or raw PostGIS references are planned inside `core/` projects.
   - Confirm proposed interfaces respect ISP role segregation (`<= 5` cohesive methods per new port; split `I*Reader` vs. `I*Writer`), preserve LSP across providers/REST boundaries (`ServiceOrderRepositoryContractTests` / bidirectional HTTP exception translation), and introduce zero `NotImplementedException` stubs.
   - Confirm geometry plans keep coordinate arrays as the serialized source of truth (RFC 7946 `[lon, lat]`, SRID 4326) rather than NTS objects.
   - Confirm every proposed public method, branch, and guard clause in `2A` has corresponding xUnit test cases in `2B`'s test matrix.

If all 4 gates pass (`Code 0`), proceed to **Phase 5**. On `Code 10`, `20`, or `30`, enter **Phase 4**.

---

### Phase 4 — Bounded Plan Self-Healing Loop

Track `planHealAttempt` from `1` up to `maxSelfHealRetries` (default `3`):

- **`Code 10` (`PathExistenceOrDisjointOwnershipViolation`) → Route to `github-copilot` planner (read-only)**:
  - Fix hallucinated `[MODIFY]` paths against real repo paths, add missing `.razor.cs` pairs, and re-partition any overlapping file paths cleanly between `2A` and `2B`, then re-run **Phase 3**.
- **`Code 20` (`BannedSymbolOrSignatureViolation`) → Route to `claude-code` planner (read-only)**:
  - Replace banned clock/async APIs with `System.TimeProvider` and `CancellationToken`-aware async signatures, sync updated contracts with `2B`, then re-run **Phase 3**.
- **`Code 30` (`OnionArchitectureOrCoverageGap`) → Route to `claude-code` & `github-copilot` planners (read-only)**:
  - Introduce clean domain ports in `core/` + infrastructure adapters in `providers/`/`workflow/`, add missing xUnit branch test cases to the QA matrix, then re-run **Phase 3**.

If issues remain after `maxSelfHealRetries` attempts, stop iterating and highlight the unresolved architectural trade-offs in a dedicated `> [!WARNING]` block inside the Phase 5 artifact for human decision.

---

### Phase 5 — Conversation Artifact Synthesis & `/code-gen-dag` Handoff

1. **Create Interactive Conversation Artifact**:
   - Write the verified plan to `<artifactDir>/plan-<taskId>.md` using `write_file` with `ArtifactMetadata: { UserFacing: true, RequestFeedback: true, Summary: "..." }`.
   - Structure the artifact with:
     1. **Executive Summary & Dependencies** (baseline commit SHA, `origin/develop` integration target, Jira `Blocks` / `"Depends on"` check)
     2. **Architecture & Data Flow Diagram** (`mermaid` flowchart/sequence diagram showing Onion layers)
     3. **Shared C# 14 Type & Interface Contract** (exact namespaces, records, interfaces, `TimeProvider` & `CancellationToken` signatures)
     4. **Disjoint Execution Manifest**:
        - **Branch 2A — `claude-code` (`coreLogicSpec`)**: Owned `[NEW]`/`[MODIFY]` production files + step-by-step domain logic rules
        - **Branch 2B — `github-copilot` (`testAndBoilerplateSpec`)**: Owned `[NEW]`/`[MODIFY]` DTO/builder/test files
     5. **Exhaustive xUnit Test & `DomainTestDataBuilders` Matrix** (table of test method names, inputs, `FakeTimeProvider` setup, expected assertions)
     6. **Risks, Observability (`ActivitySource`) & Rollback Notes**
     7. **One-Command Execution Handoff**:
        ```text
        /code-gen-dag <taskId> Implement verified plan from plan-<taskId>.md
        ```
2. **Optional Export (`--out <path>`) & Final Clean-Repo Proof**:
   - If `--out <path>` was explicitly provided, also write the Markdown file to `<path>`.
   - Otherwise, run `git status --porcelain` one final time to confirm zero files were modified in the repository, and point the user to the generated plan artifact for review.
