using Microsoft.Agents.AI.Workflows;

namespace GeoAssets.Workflow.Agents.Tests.TestDoubles;

/// <summary>
/// An <see cref="IWorkflowContext"/> stub for calling an executor's <c>HandleAsync</c> directly,
/// bypassing the MAF runtime. Only valid for executors whose handler body never touches
/// <paramref name="context"/> — throws <see cref="NotSupportedException"/> on every member so a
/// test fails loudly (not silently no-ops) if that assumption ever stops holding.
/// </summary>
public sealed class NoOpWorkflowContext : IWorkflowContext
{
    public static readonly NoOpWorkflowContext Instance = new();

    public IReadOnlyDictionary<string, string> TraceContext => throw new NotSupportedException();
    public bool ConcurrentRunsEnabled => throw new NotSupportedException();

    public ValueTask AddEventAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public ValueTask SendMessageAsync(object message, string? targetId = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public ValueTask YieldOutputAsync(object output, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public ValueTask RequestHaltAsync() => throw new NotSupportedException();

    public ValueTask<T?> ReadStateAsync<T>(string key, string? scopeName = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public ValueTask<T> ReadOrInitStateAsync<T>(string key, Func<T> initialStateFactory, string? scopeName = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public ValueTask<T?> ReadStateAsync<T>(string key, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public ValueTask<T> ReadOrInitStateAsync<T>(string key, Func<T> initialStateFactory, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public ValueTask<HashSet<string>> ReadStateKeysAsync(string? scopeName = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public ValueTask QueueStateUpdateAsync<T>(string key, T? value, string? scopeName = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public ValueTask QueueStateUpdateAsync<T>(string key, T? value, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public ValueTask QueueClearScopeAsync(string? scopeName = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public ValueTask QueueClearScopeAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();
}
