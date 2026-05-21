namespace GeoAssets.Examples.MultiAgent.Agents;

internal sealed class AnalysisAgent : SubAgent
{
    public override string Name => "call_analysis_agent";
    public override string Description => "Delegate a research or analysis task to the Analysis Agent (research, pattern detection, summarization).";
    protected override string Model => "claude-haiku-4-5";

    protected override string SystemPrompt =>
        """
        You are a research and analysis specialist. Your role is to:
        - Examine the given topic or dataset thoroughly
        - Identify patterns, risks, and key insights
        - Summarize findings in a structured, concise format
        - Highlight anything that requires attention from downstream agents
        Keep responses focused and under 300 words.
        """;
}
