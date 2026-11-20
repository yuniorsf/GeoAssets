using System.Text.Json;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>An <see cref="IWorkflowProblemMapper"/> double answering from caller-supplied functions.</summary>
internal sealed class FakeProblemMapper(
    Func<WorkflowProblem, Exception?>? toException = null,
    Func<WorkflowProblem, Exception?>? toWriteConflict = null) : IWorkflowProblemMapper
{
    public List<string> Asked { get; } = [];

    public Exception? ToException(WorkflowProblem problem, string? orderId, OrderAction action)
    {
        Asked.Add(problem.Code);
        return toException?.Invoke(problem);
    }

    public Exception? ToWriteConflictException(WorkflowProblem problem, string orderId) => toWriteConflict?.Invoke(problem);

    public static WorkflowProblem Problem(string code) =>
        new(code, "detail of " + code, JsonDocument.Parse($$"""{ "code": "{{code}}" }""").RootElement.Clone());
}
