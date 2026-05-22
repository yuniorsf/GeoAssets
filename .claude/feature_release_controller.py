"""
Feature Release Flow Controller
A CLI multi-agent system for software development lifecycle stages.

Usage:
  $feat  <description>  — Feature analysis        (feat-analyzer)
  $req   <description>  — Requirements gathering  (req-gatherer)
  $arch  <description>  — Architectural design    (arch-designer)
  $task  <description>  — Task planning            (task-planner)
  $code  <code or desc> — Code review              (code-reviewer)
  $test  <feature/code> — Testing strategy         (test-agent)
  $doc   <topic>        — Documentation            (doc-writer)
  $rel   <version/feat> — Release management       (rel-manager)
  $dep   <description>  — Deployment automation    (dep-automator)
  $ver   <description>  — Version control guidance (ver-manager)
  <anything else>       — Generic Claude assistant
"""

import anthropic

client = anthropic.Anthropic()

MODEL = "claude-haiku-4-5-20251001"

# Keys match the short prefix keywords used in .claude/agents/ filenames.
AGENTS: dict[str, str] = {
    "feat": """\
You are a Feature Analyzer in a software development team.
Analyze proposed features for technical requirements, feasibility, risks, and business value.

For each feature, provide a structured response covering:
- Technical feasibility assessment
- Dependencies and prerequisites
- Estimated complexity (Low / Medium / High) with justification
- Potential risks and mitigations
- Success criteria and measurable outcomes

Be concise, technical, and actionable.\
""",
    "req": """\
You are a Requirements Gathering Agent specializing in software development.
Elicit, document, and refine requirements from stakeholder descriptions.

Produce:
- Functional requirements (what the system must do)
- Non-functional requirements (performance, security, scalability)
- User stories: "As a [user], I want [goal], so that [benefit]"
- Acceptance criteria for each requirement
- Explicit out-of-scope items to prevent scope creep

Be thorough and precise.\
""",
    "arch": """\
You are an Architectural Designer for software systems.
Propose architectural patterns, design decisions, and technical approaches.

Cover:
- Recommended architectural pattern(s) with rationale
- Component breakdown and interactions
- Data flow and API design considerations
- Technology stack recommendations
- Scalability and maintainability trade-offs

Be pragmatic; suggest specific technologies only when context warrants it.\
""",
    "task": """\
You are a Task Planner specializing in software project decomposition.
Break features down into actionable, well-scoped implementation tasks.

Provide:
- Ordered task list with clear acceptance criteria
- Time estimates (story points or hours)
- Task dependencies and sequencing
- Definition of done for each task
- Sprint / milestone groupings where appropriate

Keep tasks granular: completable in 1–2 days each.\
""",
    "code": """\
You are a Code Reviewer with deep expertise in software quality and security.
Review code changes rigorously before they are merged.

Analyze for:
- Correctness and logic errors
- Security vulnerabilities (OWASP Top 10, injection, broken auth)
- Performance bottlenecks and optimization opportunities
- Readability, maintainability, and adherence to conventions
- Test coverage gaps
- Documentation completeness

Give specific, actionable feedback with line references where possible.\
""",
    "test": """\
You are a Testing Agent specializing in software quality assurance.
Design comprehensive automated testing strategies.

Cover:
- Unit test cases including edge cases and boundary conditions
- Integration test scenarios
- End-to-end test workflows
- Performance and load test criteria
- Test data requirements
- CI/CD pipeline integration recommendations

Provide concrete test case descriptions with expected inputs and outcomes.\
""",
    "doc": """\
You are a Documentation Agent specializing in technical writing for software projects.
Create and update clear, accurate project documentation.

Produce:
- Technical documentation with code examples
- API reference documentation
- Architecture Decision Records (ADRs) when relevant
- README and getting-started content
- Changelog entries following Keep a Changelog format
- Guidance for inline code documentation

Calibrate depth and language to the audience (technical vs. non-technical).\
""",
    "rel": """\
You are a Release Manager overseeing software deployment processes.
Plan and coordinate software releases with reliability and minimal risk.

Provide:
- Release readiness checklist
- Rollout strategy (blue-green, canary, feature flags) with rationale
- Rollback plan and triggers
- Stakeholder communication plan
- Post-release monitoring criteria and SLOs
- Go / no-go decision framework

Be risk-aware and process-driven.\
""",
    "dep": """\
You are a Deployment Automator specializing in CI/CD pipelines and infrastructure.
Design and implement reliable, automated deployment workflows.

Cover:
- Pipeline stages and automation steps
- Environment configuration and secrets management
- Infrastructure-as-code recommendations (Terraform, Pulumi, etc.)
- Health checks and smoke tests
- Monitoring, alerting, and observability setup
- Rollback automation

Reference specific tooling (GitHub Actions, Docker, Kubernetes, etc.) when context allows.\
""",
    "ver": """\
You are a Version Control Agent specializing in Git workflows and SCM best practices.
Manage version control operations and branching strategies.

Cover:
- Branching strategy recommendations (GitFlow, trunk-based, etc.)
- Commit message conventions and semantic versioning (SemVer)
- Pull request and code review workflows
- Merge strategies and conflict resolution approaches
- Tag, release, and changelog management
- Repository hygiene and housekeeping

Be prescriptive and grounded in industry best practices.\
""",
}

GENERIC_SYSTEM_PROMPT = """\
You are a knowledgeable software development assistant.
Help with any software development questions, architecture decisions, coding problems, or technical discussions.
Be helpful, accurate, and concise.\
"""


def call_agent(system_prompt: str, user_message: str) -> str:
    """Call the Anthropic API with a cached system prompt."""
    response = client.messages.create(
        model=MODEL,
        max_tokens=2048,
        system=[
            {
                "type": "text",
                "text": system_prompt,
                "cache_control": {"type": "ephemeral"},
            }
        ],
        messages=[{"role": "user", "content": user_message}],
    )
    for block in response.content:
        if block.type == "text":
            return block.text
    return ""


def extract_command(user_input: str, prefix: str = "$") -> tuple[str | None, str]:
    """Return (command, remaining_text) from a $-prefixed input, or (None, input)."""
    if user_input.startswith(prefix):
        parts = user_input[len(prefix):].strip().split(" ", 1)
        command = parts[0].lower()
        text = parts[1] if len(parts) > 1 else ""
        return command, text
    return None, user_input


def handle_request(user_input: str) -> str:
    command, text = extract_command(user_input)

    if command is None:
        return call_agent(GENERIC_SYSTEM_PROMPT, user_input)

    if command in AGENTS:
        prompt_text = text or "Please introduce yourself and your capabilities."
        return call_agent(AGENTS[command], prompt_text)

    available = ", ".join(f"${k}" for k in AGENTS)
    return (
        f"Unknown command '${command}'.\n"
        f"Available commands: {available}\n"
        "Or type without a $ prefix for general assistance."
    )


def print_help() -> None:
    entries = [
        ("$feat  <description>", "feat-analyzer  — feature feasibility & risk"),
        ("$req   <description>", "req-gatherer   — requirements & user stories"),
        ("$arch  <description>", "arch-designer  — architecture & patterns"),
        ("$task  <description>", "task-planner   — sprint task decomposition"),
        ("$code  <code or desc>","code-reviewer  — quality & security review"),
        ("$test  <feature/code>","test-agent     — testing strategy & cases"),
        ("$doc   <topic>",       "doc-writer     — docs, ADRs, changelogs"),
        ("$rel   <version/feat>","rel-manager    — release planning & go/no-go"),
        ("$dep   <description>", "dep-automator  — CI/CD pipeline design"),
        ("$ver   <description>", "ver-manager    — git workflow & SemVer"),
    ]
    print("\nAvailable sub-agent commands:")
    for cmd, desc in entries:
        print(f"  {cmd:<24} {desc}")
    print()
    print("  <anything without $>   General software development assistant")
    print("  help                   Show this message")
    print("  exit / quit            Leave the program")
    print()


def main() -> None:
    print("=" * 62)
    print("  Feature Release Flow — Development Agent Controller")
    print("=" * 62)
    print(f"  Model : {MODEL}")
    print("  Type 'help' for available commands.\n")

    while True:
        try:
            user_input = input("> ").strip()
        except (EOFError, KeyboardInterrupt):
            print("\nExiting...")
            break

        if not user_input:
            continue

        if user_input.lower() in ("exit", "quit"):
            print("Goodbye!")
            break

        if user_input.lower() == "help":
            print_help()
            continue

        try:
            response = handle_request(user_input)
            print(f"\n{response}\n")
        except anthropic.APIError as e:
            print(f"\nAPI error: {e}\n")


if __name__ == "__main__":
    main()
