Implement a Jira ticket end-to-end: design, code, test, commit — moving the ticket's Jira status forward at each real step, not just at the end. Stops after the commit(s); does not push. Argument: the ticket key (e.g. `XD01-4`).

Parse `$ARGUMENTS` as the ticket key. If empty, ask for one instead of guessing.

Jira site for this project: `xdicor.atlassian.net`, project key `XD01` (see the `reference_jira_project` memory if available — the key has changed before, verify with `getVisibleJiraProjects` if a call 404s).

## Steps

1. **Fetch the ticket** — `getJiraIssue` for `$ARGUMENTS`. Read its description, findings, and any prior "Update"/"Resolution" notes in full before doing anything else. If it links to other tickets (blocked-by, "depends on" text references, epic parent), fetch those too if they materially affect scope.

2. **Check readiness** — if the ticket is genuinely still "gap diagnosed only, no design proposed" with an open design question that only the user can settle (a product/business call, not a technical one), stop and ask rather than guessing. Otherwise proceed — most tickets in this project already carry enough analysis to implement directly.

3. **Move to In Progress** — `getTransitionsForJiraIssue` for the ticket, find the transition named "In Progress" (don't hardcode transition IDs — they're workflow-specific, look them up), then `transitionJiraIssue`.

4. **Design the fix** — read the actual source it touches (don't trust the ticket's file:line citations blindly, they may be stale). Follow the codebase's existing conventions and patterns for the module being changed (e.g. how sibling rules/repositories/services are already shaped) rather than inventing a new style. Respect `CLAUDE.md`: no unrelated refactors, no speculative abstractions, minimal footprint.

5. **Implement** — make the code change. If the ticket's own "suggested direction" conflicts with what the code actually shows, follow the code and note the deviation later in the Jira resolution note.

6. **Write/update tests** — match this repo's existing coverage convention (100% line/branch on touched classes is the norm in `GeoAssets.Workflow.Tests` and similar). Prefer extending existing test files/patterns over new scaffolding when a matching file already exists. Include at least one test that would fail without the fix (proves the test isn't vacuous) and, for any authorization/permission change, an explicit test that the grant does *not* leak beyond its intended scope.

7. **Build and test** — `dotnet build` the touched project(s), then `dotnet test` the relevant test project(s) filtered to the touched area first, then the full project's test suite to catch ripple effects (enum/interface changes especially). Fix failures before proceeding — do not move the ticket forward on red tests.

8. **Update design docs if the ticket references one** (e.g. `ServiceOrder.md`) — a small, targeted edit to the relevant table/section, not a rewrite.

9. **Move to In Review** — same transition-lookup pattern as step 3.

10. **Review the diff** — `git status` / `git diff --stat` before staging. Confirm every changed file is actually relevant to this ticket; nothing unrelated should be swept in.

11. **Decide commit granularity** — don't assume one commit by default. Look at the actual diff and split it into as many commits as there are genuinely independent, separately-coherent units of work. Signals that mean *separate* commits:
    - A data-model/schema change that a later behavioral change depends on (model should land before the logic using it)
    - Production code vs. a docs-only update (e.g. `ServiceOrder.md`) that isn't tightly coupled to one specific code commit
    - Two distinct fixes/features that happen to share a ticket but don't depend on each other
    - A mechanical/generated change (e.g. a migration) vs. the hand-written logic around it

    Signals that mean it should stay *one* commit:
    - Code and its own tests (never split a change from the tests that exercise it)
    - Small, tightly coupled edits across a few files that only make sense together (e.g. an interface change + its one implementer + the DI wiring)

    Each commit must build and pass tests on its own — verify this, don't assume it (re-run the build/test steps against the state after each commit if there's any doubt, e.g. via `git stash` on the not-yet-committed remainder). Don't split just to have more commits — over-fragmenting a single coherent change is as wrong as bundling unrelated ones.

12. **Commit** — for each planned commit, stage only its relevant files by name (never `git add -A`/`.`). Conventional-commit style matching `git log` history in this repo (e.g. `feat(workflow): ... (XD01-N)`), body explaining *why*, ending with:
    ```
    Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
    ```

13. **Move to Done** — same transition-lookup pattern as step 3. Before transitioning, `editJiraIssue` to append a "## Resolution" section to the ticket's description (don't overwrite the original problem statement) with: the commit hash(es), a summary of what changed, test counts/pass status, and any follow-up gaps deliberately left out of scope (file those as new tickets only if they're substantial — a one-line note is enough for small ones).

14. **Report back** — a short summary: what was implemented, the commit hash(es) (and why split that way, if more than one), ticket's final state, and anything that needed a judgment call worth flagging. Note explicitly that nothing was pushed, so the user can review before pushing themselves.

If any step fails (build error, test failure, Jira transition unavailable), stop and surface the failure rather than skipping ahead — do not silently mark the ticket Done on a red step.
