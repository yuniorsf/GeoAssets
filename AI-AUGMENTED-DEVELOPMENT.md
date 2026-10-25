# AI-Augmented Development in GeoAssets

GeoAssets uses multiple AI layers at different scopes: GitHub Actions automation, Claude Code CLI tooling, and in-process SDK orchestration. This document describes each layer, where it lives, and how to use it.

---

## 1. GitHub Actions — Automated AI Workflows

Two workflows run on every repository event without any manual trigger.

### Automated PR Review — `claude-auto-pr-review.yml`

Every pull request (opened, synchronized, re-opened, or moved out of draft) triggers an automated code review by Claude Code.

```
Trigger  : pull_request → opened | synchronize | ready_for_review | reopened
Action   : anthropics/claude-code-action@v1
Plugin   : code-review@claude-code-plugins
Auth     : Anthropic OIDC Federation (ANTHROPIC_FEDERATION_RULE_ID + ANTHROPIC_ORGANIZATION_ID)
```

Claude reads the full diff, runs the `code-review` plugin, and posts structured feedback as a PR review. No human action required.

### On-Demand Agent — `claude-on-demand-agent.yml`

Mention `@claude` anywhere in a PR comment, review body, issue comment, or issue title to invoke Claude Code as an interactive agent.

```
Trigger  : issue_comment | pull_request_review_comment | pull_request_review | issues
Condition: body or title contains "@claude"
Action   : anthropics/claude-code-action@v1
Auth     : Anthropic OIDC Federation (ANTHROPIC_FEDERATION_RULE_ID + ANTHROPIC_ORGANIZATION_ID)
```

Claude reads CI results (via `actions: read` permission) and executes whatever the comment instructs — explain code, fix a test failure, update a description, generate a migration, etc.

---

## 2. Editor Assistance — GitHub Copilot

`.github/copilot-instructions.md` provides Copilot with project-specific context:

- Build and test commands (SDK version, solution path, single-test filter syntax)
- High-level architecture walkthrough (core layers, UI entry points, provider discovery, map interop)
- Key conventions (RFC 7946 coordinates, event propagation, repository patterns, CSS tokens)

This file is read automatically by GitHub Copilot in supported editors.

---

## 3. Claude Code CLI — Local Development Tooling

All Claude Code artifacts live under `.claude/`. The `CLAUDE.md` at the repository root is the shared contract read by every agent layer (local, GitHub Actions, sub-agents) and defines the stack, folder structure, key file paths, architecture rules, and coding conventions.

### 3.1 Sub-Agents — `.claude/agents/`

Eleven specialized agents: ten SDLC domain personas (each with a short prefix keyword) plus the `ticket-worker` agent used for isolated git-worktree Jira ticket execution. Claude Code selects the right agent automatically from the `description` frontmatter; they can also be invoked explicitly with `@<name>`.

| Keyword / Name | Agent file | Role |
|----------------|-----------|------|
| `ticket-worker` | `ticket-worker.md` | Autonomous worker that runs `/ship-ticket <KEY>` inside an isolated git worktree and handles rebase conflict resolution when resumed by `/orchestrate-tickets` |
| `feat` | `feat-analyzer.md` | Feature feasibility, risk, and complexity assessment |
| `req` | `req-gatherer.md` | Requirements, user stories, and acceptance criteria |
| `arch` | `arch-designer.md` | Architectural patterns, component breakdown, tech stack |
| `task` | `task-planner.md` | Sprint task decomposition with estimates and sequencing |
| `code` | `code-reviewer.md` | Code quality, security (OWASP), and coverage review |
| `test` | `test-agent.md` | Unit / integration / E2E testing strategy and test cases |
| `doc` | `doc-writer.md` | API docs, ADRs, READMEs, changelogs (Keep a Changelog) |
| `rel` | `rel-manager.md` | Release readiness, rollout strategy, go/no-go framework |
| `dep` | `dep-automator.md` | CI/CD pipeline design, IaC, health checks, rollback |
| `ver` | `ver-manager.md` | Git workflows, branching strategy, SemVer, PR conventions |

Each agent has an isolated system prompt scoped to its domain. For the ten SDLC agents, the naming convention `{prefix}-{descriptor}.md` ties the file name, the `name:` frontmatter field, and the CLI keyword together.

### 3.2 Slash Commands — `.claude/commands/`

Reusable workflow and prompt templates invoked with `/command-name <args>` in the Claude Code CLI.

| Command | File | What it does |
|---------|------|--------------|
| `/implement-ticket <KEY\|FIRST_TO_DO>` | `implement-ticket.md` | Implements a Jira ticket end-to-end (design, code, tests, Jira status transitions, commit-granularity plan) while leaving all changes unstaged and uncommitted for human review |
| `/ship-ticket <KEY\|FIRST_TO_DO>` | `ship-ticket.md` | Autonomous counterpart to `/implement-ticket`: branches from `origin/develop`, implements, runs full-solution build/test hard-stop gates, commits with `(TICKET-KEY)`, pushes, and opens a PR targeting `develop` |
| `/orchestrate-tickets <KEYS\|JQL: ...>` | `orchestrate-tickets.md` | Claims non-dependent Jira tickets serially, spawns up to 5 concurrent `ticket-worker` sub-agents in isolated git worktrees, and serially rebases/tests/merges their PRs into `develop` (resuming the authoring worker via `SendMessage` on rebase conflicts) |
| `/gen-tests <ClassName>` | `gen-tests.md` | Generates a full xUnit test class with 100% branch coverage for the named class, including test project scaffolding if none exists |
| `/gen-method-tests <Class/Method>` | `gen-method-tests.md` | Generates tests for a single method — exhaustively covering every branch, guard clause, null path, async path, and overload |

### 3.3 Domain Skills — `.claude/skills/`

Curated, lazy-loaded reference skills that provide project-specific engineering conventions and design intelligence without bloating every conversation's base context.

| Skill | Directory | Purpose |
|-------|-----------|---------|
| `csharp-conventions` | `.claude/skills/csharp-conventions/` | 17 source-attributed C# / .NET 10 reference guides (DDD, EF Core, SOLID, async/await, `TimeProvider`, NRTs, REST contract testing, plus `future` guardrails for C# 15 / .NET 11) |
| `observability-conventions` | `.claude/skills/observability-conventions/` | 7 reference guides on W3C trace propagation, native `ActivitySource` / OpenTelemetry correlation, high-cardinality telemetry, SLO burn-rate alerting, and tail-based sampling |
| `jira-conventions` | `.claude/skills/jira-conventions/` | Rules for splitting multi-part features into an `Epic` + sequential `Historia`/`Tarea` child tickets, `createJiraIssue` Markdown workarounds, and GitHub-for-Atlassian PR/commit linking |
| `ui-ux-pro-max` | `.claude/skills/ui-ux-pro-max/` | Searchable UI/UX design intelligence (`scripts/search.py` + local CSV/JSON catalogs covering accessibility, color palettes, typography, motion, and component guidelines) |

### 3.4 Feature Release Flow Controller — `.claude/feature_release_controller.py`

A standalone Python CLI that routes natural-language input to the same 10 domain agents via `$`-prefixed keywords, calling the Anthropic API directly with prompt caching on each agent's system prompt.

```
Requirements: Python 3.11+, anthropic package, ANTHROPIC_API_KEY env var
Run         : python .claude/feature_release_controller.py
Model       : claude-haiku-4-5-20251001 (all agents)
```

Available commands mirror the sub-agent keywords:

```
$feat  <description>   feat-analyzer  — feature feasibility & risk
$req   <description>   req-gatherer   — requirements & user stories
$arch  <description>   arch-designer  — architecture & patterns
$task  <description>   task-planner   — sprint task decomposition
$code  <code or desc>  code-reviewer  — quality & security review
$test  <feature/code>  test-agent     — testing strategy & cases
$doc   <topic>         doc-writer     — docs, ADRs, changelogs
$rel   <version/feat>  rel-manager    — release planning & go/no-go
$dep   <description>   dep-automator  — CI/CD pipeline design
$ver   <description>   ver-manager    — git workflow & SemVer
<no $prefix>           Generic Claude software assistant
```

System prompts are cached with `cache_control: {"type": "ephemeral"}`, reducing cost on repeated calls to the same agent within a session.

---

## Summary

| Layer | Location | Trigger | Model |
|-------|----------|---------|-------|
| Automated PR review | `.github/workflows/claude-auto-pr-review.yml` | Every PR event | Claude Code (cloud, OIDC) |
| On-demand agent | `.github/workflows/claude-on-demand-agent.yml` | `@claude` mention | Claude Code (cloud, OIDC) |
| Copilot context | `.github/copilot-instructions.md` | Editor IDE | Copilot |
| LLM contract | `CLAUDE.md` | All agent layers | — |
| Sub-agents (11) | `.claude/agents/` | Automatic / `@name` / `/orchestrate-tickets` | Claude Code (local) |
| Slash commands (5) | `.claude/commands/` | `/command` | Claude Code (local) |
| Domain skills (4) | `.claude/skills/` | Contextual / on-demand | Claude Code (local) |
| Release flow CLI | `.claude/feature_release_controller.py` | `$keyword` | claude-haiku-4-5 |

> The in-process SDK orchestration Layers (both the Microsoft Agent Framework service-order executors in `workflow/GeoAssets.Workflow.Agents` and the Anthropic SDK multi-agent plugin generator in [`examples/MultiAgent/README.md`](examples/MultiAgent/README.md)) are GeoAssets application features documented in [`ServiceOrder.md`](ServiceOrder.md) and [`examples/MultiAgent/README.md`](examples/MultiAgent/README.md).
