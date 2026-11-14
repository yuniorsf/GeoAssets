# Tri-Planar Read-Only Planning Blueprint (`/plan-gen-dag`)

Declarative read-only planning specification ([`plan-gen-dag.json`](plan-gen-dag.json)) for **Google Antigravity** coordinating **Claude Code** (Architecture & Clean/Hexagonal Domain Plan) and **GitHub Copilot** (Type Contracts, Disjoint File Manifest & 100%-Branch QA Test Matrix) with **zero working-tree mutations**, producing a ready-to-execute handoff artifact for `/code-gen-dag`.

This is Google Antigravity's own concrete implementation of the agent-neutral ControlPlane-OuterLoop (Read-Only Planning) protocol defined in [`.agents/skills/plan-gen-dag/SKILL.md`](../../.agents/skills/plan-gen-dag/SKILL.md); Claude Code and GitHub Copilot each run their own native adaptation of that same protocol from `.claude/skills/plan-gen-dag/SKILL.md` and `.github/skills/plan-gen-dag/SKILL.md` respectively.
