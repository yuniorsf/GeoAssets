Implement a Jira ticket end-to-end **and land it**: design, code, test, commit, push, open a PR — the autonomous counterpart to `/implement-ticket`, which deliberately stops short of committing. Argument: the ticket key (e.g. `XD01-4`), or the literal `FIRST_TO_DO`. Intended to be run either standalone or as the worker half of `/orchestrate-tickets` (which claims the ticket and creates the worktree before invoking this).

This is the only command in this repo allowed to run `git add`, `git commit`, `git push`, or open a PR on its own — kept separate from `/implement-ticket` precisely so that command's "committing is always the user's own action" guarantee is never weakened for its existing interactive callers.

## Steps

1. **Steps 1-9: identical to `/implement-ticket`.** Follow `.claude/commands/implement-ticket.md` steps 1 ("Fetch the ticket") through 9 ("Move to In Review") exactly as written there — ticket-key resolution (including `FIRST_TO_DO`), readiness check, →In Progress transition, design, implementation, tests, the `dotnet build`/`dotnet test` gate, the design-doc update, and the →In Review transition. Do not restate or duplicate that text; just execute it. If this command was invoked by `/orchestrate-tickets`, the ticket is already →In Progress — step 3 there becomes a no-op check (status already In Progress is fine; any other unexpected status should stop and report rather than forcing a transition).

2. **Final test gate — hard stop, no exceptions.** Run `dotnet build` on the full solution and `dotnet test` on the full solution (not just the touched project(s) — this is the real gate before anything gets committed). **Any test failure blocks this command, full stop — including one you believe is pre-existing, flaky, or unrelated to this change.** Do not commit, push, or open a PR. There is no judgment call here: "probably unrelated" is not a reason to proceed. Report the exact failing test(s), whether `git diff` shows the failing test's own file as touched by this branch, and why you suspect it's unrelated if you do — then stop and wait. Only a human confirming it's safe (by re-running it, or by explicitly telling you to proceed) unblocks this step; you never make that call yourself.

3. **Review the diff** (same as implement-ticket step 10) — `git status` / `git diff --stat`. Confirm every changed file is actually relevant to this ticket.

4. **Decide commit granularity** using implement-ticket's step 11 reasoning (same signals for split-vs-single commit: model-before-logic, docs-vs-code, independent fixes, generated-vs-hand-written on one side; code-with-its-own-tests and tightly-coupled small edits on the other) — but **execute it** instead of just drafting a plan: `git add` the relevant files per logical commit, then `git commit`. Commit subjects follow `git log` history conventions in this repo (e.g. `feat(workflow): ... (XD01-N)`) and **must** end with `(TICKET-KEY)` per `CLAUDE.md` § Pull Requests & Commits, so the GitHub-for-Atlassian app links them to the ticket.

5. **Push the branch** — `git push -u origin <branch>`. If the current branch name doesn't already follow this repo's convention (lowercase `xd01-<n>-<slug>`, e.g. `xd01-171-real-search-path`), rename it to match before pushing.

6. **Open the PR** — `pull_request_create` with `title: "<TICKET-KEY>: <summary>"` (per `CLAUDE.md`; if the diff incidentally spans a secondary ticket, prefix with the primary/parent key instead), source branch = this branch, target branch `develop`, body summarizing the change and linking the Jira ticket.

7. **Update the Jira resolution note** — same "## Resolution" append as implement-ticket step 13 (summary of the change, test counts/pass status, any deliberate scope cuts), plus the PR URL. Do **not** transition the ticket past In Review — only a human reviewer moves it to Done.

8. **Report back** — branch name, worktree path (if run inside one), PR URL, ticket's final state, and the commit(s) made.

If any step fails (build error, push rejected, Jira transition unavailable), stop and surface the failure rather than pushing/opening a PR on a broken or partial state. A test failure is covered by step 2 above and is never something to reason past — "it looks pre-existing" is a reason to stop and report, not a reason to continue. Never force-push over history you didn't just create in this run.
