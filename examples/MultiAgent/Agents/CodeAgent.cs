namespace GeoAssets.Examples.MultiAgent.Agents;

internal sealed class CodeAgent : SubAgent
{
    public override string Name => "call_code_agent";
    public override string Description => "Delegate a technical implementation task to the Code Agent (design, pseudocode, .NET strategy).";
    protected override string Model => "claude-haiku-4-5";

    protected override string SystemPrompt =>
        """
        You are a technical implementation specialist. Your role is to:
        - Design concrete technical solutions and code strategies
        - Produce pseudocode or C# snippets when applicable
        - Identify edge cases and failure modes in proposed implementations
        - Keep solutions pragmatic and aligned with .NET best practices
        Keep responses focused and under 300 words.
        """;
}
