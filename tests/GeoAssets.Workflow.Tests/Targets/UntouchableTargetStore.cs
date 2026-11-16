using GeoAssets.Core.Models;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Tests.Targets;

/// <summary>
/// A target store that fails the test the moment it is asked anything: stands in wherever a
/// guard must fire before the store is reached.
/// </summary>
internal sealed class UntouchableTargetStore : IServiceOrderTargetReader, IServiceOrderTargetWriter
{
    public int Calls { get; private set; }

    private T Touched<T>()
    {
        Calls++;
        throw new InvalidOperationException("The target store must not be touched here.");
    }

    public Task<PagedResult<ServiceOrderTarget>> GetPageAsync(OrderTargetQuery query, CancellationToken ct) =>
        Touched<Task<PagedResult<ServiceOrderTarget>>>();

    public Task<ServiceOrderTarget?> GetByIdAsync(string orderId, string targetId, CancellationToken ct) =>
        Touched<Task<ServiceOrderTarget?>>();

    public Task<int> CountAsync(string orderId, CancellationToken ct) => Touched<Task<int>>();

    public Task<IReadOnlyList<OrderTargetUsage>> FindOpenUsagesAsync(
        IReadOnlyCollection<string> featureIds, string excludingOrderId, CancellationToken ct) =>
        Touched<Task<IReadOnlyList<OrderTargetUsage>>>();

    public Task<OrderTargetExtent?> GetExtentAsync(string orderId, CancellationToken ct) => Touched<Task<OrderTargetExtent?>>();

    public Task<int> AddRangeAsync(string orderId, IReadOnlyCollection<ServiceOrderTarget> targets, CancellationToken ct) =>
        Touched<Task<int>>();

    public Task UpdateAsync(ServiceOrderTarget target, CancellationToken ct) => Touched<Task>();

    public Task<int> RemoveAsync(string orderId, IReadOnlyCollection<string> targetIds, CancellationToken ct) => Touched<Task<int>>();
}
