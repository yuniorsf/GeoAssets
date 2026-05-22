# Multi-Agent Orchestration — GeoAssets Examples

This folder contains C# examples demonstrating the multi-agent orchestration patterns built into the GeoAssets platform. Each example uses the Anthropic .NET SDK directly inside the application process.

## Running

```bash
cd examples/GeoAssets.Examples
dotnet run
```

Examples 07 and 08 require the `ANTHROPIC_API_KEY` environment variable to be set. The runner skips them with a yellow warning if it is absent.

---

## Example 07 — Classic Multi-Agent Orchestrator (`MultiAgentExample.cs`)

```
Orchestrator : AnthropicMultiAgentOrchestrator  (claude-opus-4-7)
Subagents    : AnalysisAgent | CodeAgent | ReviewAgent  (claude-haiku-4-5)
Pattern      : tool-call dispatch loop — CreateMessage → dispatch → ToolResultBlockParam → repeat
```

The orchestrator receives a task, breaks it into subtasks, and dispatches each to a specialist agent via tool calls. Outputs are chained through the `context` field so each agent can build on previous results. The orchestrator then synthesizes a final answer.

SDK patterns demonstrated:

- Tool definitions via `InputSchema.FromRawUnchecked`
- Manual tool loop driven by the `StopReason.ToolUse` sentinel
- `ContentBlock.TryPickText` / `TryPickToolUse` union discriminators

Agent hierarchy:

```
User
 └─ AnthropicMultiAgentOrchestrator  (claude-opus-4-7)
      ├─ call_analysis_agent  →  AnalysisAgent  (claude-haiku-4-5)
      ├─ call_code_agent      →  CodeAgent      (claude-haiku-4-5)
      └─ call_review_agent    →  ReviewAgent    (claude-haiku-4-5)
               └─ results → Orchestrator → synthesized final answer
```

---

## Example 08 — Agent-Orchestrated Plugin Generation (`CommandPluginGenerationExample.cs`)

```
Orchestrator : AnthropicCommandPluginOrchestrator
Pattern      : analysis → plugin-spec authoring → review → deterministic scaffolder
Output       : compilable GeoAssets.Plugin.* command project under generated-plugins/
```

A natural-language request (e.g., "create a plugin that summarizes assets by type") passes through a multi-agent pipeline:

1. **Analysis agent** — extracts requirements and constraints from the request
2. **Plugin-spec agent** — authors a structured plugin specification
3. **Review agent** — validates the spec against GeoAssets plugin contracts
4. **`GeoAssets.Commands.Generation` scaffolder** — deterministically emits a compilable project

This is the production multi-agent pattern used by the GeoAssets platform to generate plugin command projects from natural language. The generated output is a real `.csproj` + source files ready to build.

---

## Key Types

| Type | File | Role |
|------|------|------|
| `AnthropicToolOrchestrator` | `AnthropicToolOrchestrator.cs` | Base orchestrator — owns the tool-call loop |
| `AnthropicMultiAgentOrchestrator` | `AnthropicMultiAgentOrchestrator.cs` | Specializes the loop for analysis → code → review |
| `AnthropicCommandPluginOrchestrator` | `AnthropicCommandPluginOrchestrator.cs` | Specializes the loop for plugin generation |
| `AnalysisAgent` / `CodeAgent` / `ReviewAgent` | `Agents/` | Specialist worker agents |
| `IAgentWorker` | `GeoAssets.Core.Agents` | Vendor-neutral agent contract |
