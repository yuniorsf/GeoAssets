using GeoAssets.Core.Agents;

namespace GeoAssets.Examples.MultiAgent;

internal sealed class AnthropicMultiAgentOrchestrator(IEnumerable<IAgentWorker> agents) : AnthropicToolOrchestrator(agents)
{
    protected override string SystemPrompt =>
        """
        You are an orchestrator that breaks complex problems into specialized sub-tasks and
        delegates them to specialist agents via tool calls.

        Workflow:
        1. Analyze the user's request.
        2. Call the analysis agent first with the core analytical question.
        3. Call the code agent with the implementation task, passing the analysis as context.
        4. Call the review agent to validate both outputs, passing them as context.
        5. Synthesize all results into a final, coherent answer for the user.

        Always use the 'context' field to chain relevant output from earlier agents into
        later agent calls. Do not skip agents — use all registered agents in a sensible order.
        """;
}
