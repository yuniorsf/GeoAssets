# AI-Augmented Development in GeoAssets

GeoAssets uses multiple AI layers at different scopes: GitHub Actions automation, Claude Code CLI tooling, and in-process SDK orchestration. This document describes each layer, where it lives, and how to use it.

---

## 1. GitHub Actions — Automated AI Workflows

Two workflows run on every repository event without any manual trigger.

### Automated PR Review — `claude-code-review.yml`

Every pull request (opened, synchronized, re-opened, or moved out of draft) triggers an automated code review by Claude Code.

```
Trigger  : pull_request → opened | synchronize | ready_for_review | reopened
Action   : anthropics/claude-code-action@v1
Plugin   : code-review@claude-code-plugins
Secret   : CLAUDE_CODE_OAUTH_TOKEN
```

Claude reads the full diff, runs the `code-review` plugin, and posts structured feedback as a PR review. No human action required.

### On-Demand Agent — `claude.yml`

Mention `@claude` anywhere in a PR comment, review body, issue comment, or issue title to invoke Claude Code as an interactive agent.

```
Trigger  : issue_comment | pull_request_review_comment | pull_request_review | issues
Condition: body or title contains "@claude"
Action   : anthropics/claude-code-action@v1
Secret   : CLAUDE_CODE_OAUTH_TOKEN
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

Ten domain-specific agents, each with a short prefix keyword. Claude Code selects the right agent automatically from the `description` frontmatter; they can also be invoked explicitly with `@<name>`.

| Keyword | Agent file | Role |
|---------|-----------|------|
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

Each agent has an isolated system prompt scoped to its domain. The naming convention `{prefix}-{descriptor}.md` ties the file name, the `name:` frontmatter field, and the CLI keyword together.

### 3.2 Slash Commands — `.claude/commands/`

Reusable prompt templates invoked with `/command-name <args>` in the Claude Code CLI.

| Command | File | What it does |
|---------|------|--------------|
| `/gen-tests <ClassName>` | `gen-tests.md` | Generates a full xUnit test class with 100% branch coverage for the named class, including test project scaffolding if none exists |
| `/gen-method-tests <Class/Method>` | `gen-method-tests.md` | Generates tests for a single method — exhaustively covering every branch, guard clause, null path, async path, and overload |

Both commands discover the existing test framework (xUnit/NUnit/MSTest), assertion library (FluentAssertions/Shouldly), and mocking library (Moq/NSubstitute) from the test project before generating anything.

### 3.3 Feature Release Flow Controller — `.claude/feature_release_controller.py`

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
| Automated PR review | `.github/workflows/claude-code-review.yml` | Every PR event | Claude Code (cloud) |
| On-demand agent | `.github/workflows/claude.yml` | `@claude` mention | Claude Code (cloud) |
| Copilot context | `.github/copilot-instructions.md` | Editor IDE | Copilot |
| LLM contract | `CLAUDE.md` | All agent layers | — |
| Sub-agents (10) | `.claude/agents/` | Automatic / `@name` | Claude Code (local) |
| Slash commands (2) | `.claude/commands/` | `/command` | Claude Code (local) |
| Release flow CLI | `.claude/feature_release_controller.py` | `$keyword` | claude-haiku-4-5 |

> The in-process SDK orchestration layer (multi-agent plugin generation and the classic orchestrator example) is a GeoAssets application feature, documented separately in [`examples/MultiAgent/README.md`](examples/MultiAgent/README.md).
