---
name: jira-conventions
description: >-
  Conventions for creating and structuring Jira tickets in the GeoAssets
  project (XD01) — when and how to split a multi-part feature into an Epic
  with sequential child tickets sized for review, plus a createJiraIssue
  formatting quirk. Use when creating or planning Jira tickets/issues for
  GeoAssets work.
---

# Jira Conventions

## Split large features into an Epic + sequential child tickets

Don't file one large ticket for a multi-part feature. Split it so each
ticket is independently reviewable — one focused, PR-sized diff per ticket.

**How to split:** follow the natural build-order/layer boundaries of the
implementation (e.g. data model → API → client → UI), not arbitrary line
splits. Each child should be a coherent, testable slice on its own, not a
fragment that can't be verified in isolation.

**Structure:**

- The umbrella ticket becomes an **Epic** (issue type `Epic`): a short
  summary, a table of children with their dependency order, out-of-scope
  items, and the overall acceptance criteria. Implementation detail —
  endpoint tables, file lists, per-slice acceptance criteria — belongs in
  the children, not duplicated at the epic level.
- Each child is a **Historia** (delivers a user-facing capability) or
  **Tarea** (infrastructure/plumbing with no direct user-facing surface) —
  hierarchyLevel 0, created with `parent: <epic-key>`. This project's Jira
  is team-managed ("simplified"), so the plain `parent` field links an
  issue to an Epic directly — there's no separate Epic Link field to set.
- Link children sequentially with `Blocks` (child N blocks child N+1) when
  the build order has a real dependency (e.g. the API must exist before the
  client that calls it). Also state the dependency in each child's
  description — don't make a reviewer reconstruct build order from the link
  graph alone.

**Precedent in this project:** XD01-28 (OTel decoupling), XD01-34
(TimeProvider migration), and XD01-40 (business-logic instrumentation) all
follow this Epic + Historia/Tarea-children shape. XD01-54 (Identity &
Access Admin, Phase 1) was filed as a single oversized ticket, then split
into this shape after the fact (children XD01-55..58) once a reviewer
flagged its size — do the split at creation time instead of after.

## createJiraIssue mangles multi-paragraph Markdown — create, then edit

`createJiraIssue`'s `description` parameter mangles multi-paragraph
Markdown (headings, tables, multiple paragraphs) into literal `\n`
characters instead of rendering them. `editJiraIssue` does not have this
problem.

**Workaround:** create the issue with a short placeholder description, then
immediately call `editJiraIssue` with the real, fully-formatted
description. Use `getJiraIssue` to verify if there's any doubt the
formatting landed correctly.

## PR titles and commit subjects reference the ticket key

See CLAUDE.md § Pull Requests & Commits — a PR implementing a ticket is
titled `<TICKET-KEY>: <summary>`; a PR spanning multiple tickets is
prefixed with the primary/parent key. This applies whether the PR closes an
Epic child or the Epic itself. Commit subjects end with `(TICKET-KEY)`
instead of a prefix. Both patterns are what the **GitHub for Atlassian** app
(connected to this repo) scans for to auto-populate a ticket's Development
panel.
