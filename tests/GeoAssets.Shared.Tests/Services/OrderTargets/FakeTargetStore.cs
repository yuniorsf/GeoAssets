using GeoAssets.Core.Models;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>
/// The target ports over an in-memory list per order. Reads page by the query (ordered by feature
/// id) unless a page is scripted; every call is recorded, and any call can be made to wait or fail.
/// </summary>
internal sealed class FakeTargetStore : IServiceOrderTargetReader, IServiceOrderTargetWriter
{
    public Dictionary<string, List<ServiceOrderTarget>> Orders { get; } = new(StringComparer.Ordinal);
    public List<OrderTargetQuery> PageQueries { get; } = [];
    public List<CancellationToken> PageTokens { get; } = [];
    public List<ServiceOrderTarget> Updates { get; } = [];
    public List<(string OrderId, string TargetId)> ByIdReads { get; } = [];

    /// <summary>Overrides the total a page reports, to simulate targets removed while paging.</summary>
    public int? ReportedTotal { get; set; }

    /// <summary>Called before a page is answered; may wait or throw.</summary>
    public Func<OrderTargetQuery, CancellationToken, Task>? BeforePage { get; set; }

    /// <summary>Called before an update is applied; may wait or throw.</summary>
    public Func<ServiceOrderTarget, CancellationToken, Task>? BeforeUpdate { get; set; }

    /// <summary>Answers <see cref="GetByIdAsync"/>; defaults to the stored target.</summary>
    public Func<string, string, CancellationToken, Task<ServiceOrderTarget?>>? ById { get; set; }

    public void Add(params ServiceOrderTarget[] targets)
    {
        foreach (var target in targets)
        {
            if (!Orders.TryGetValue(target.OrderId, out var list)) Orders[target.OrderId] = list = [];
            list.Add(target);
        }
    }

    public async Task<PagedResult<ServiceOrderTarget>> GetPageAsync(OrderTargetQuery query, CancellationToken ct)
    {
        PageQueries.Add(query);
        PageTokens.Add(ct);
        if (BeforePage is not null) await BeforePage(query, ct);
        ct.ThrowIfCancellationRequested();

        var all = Orders.TryGetValue(query.OrderId, out var list) ? list.OrderBy(t => t.FeatureId, StringComparer.Ordinal).ToList() : [];
        return new PagedResult<ServiceOrderTarget>
        {
            Items = [.. all.Skip(query.Skip).Take(query.PageSize)],
            TotalCount = ReportedTotal ?? all.Count,
        };
    }

    public Task<ServiceOrderTarget?> GetByIdAsync(string orderId, string targetId, CancellationToken ct)
    {
        ByIdReads.Add((orderId, targetId));
        if (ById is not null) return ById(orderId, targetId, ct);

        var found = Orders.TryGetValue(orderId, out var list) ? list.FirstOrDefault(t => string.Equals(t.Id, targetId, StringComparison.Ordinal)) : null;
        return Task.FromResult(found);
    }

    public async Task UpdateAsync(ServiceOrderTarget target, CancellationToken ct)
    {
        Updates.Add(target);
        if (BeforeUpdate is not null) await BeforeUpdate(target, ct);
        ct.ThrowIfCancellationRequested();
    }

    public Task<int> CountAsync(string orderId, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyList<OrderTargetUsage>> FindOpenUsagesAsync(IReadOnlyCollection<string> featureIds, string excludingOrderId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<OrderTargetExtent?> GetExtentAsync(string orderId, CancellationToken ct) => throw new NotSupportedException();

    public Task<int> AddRangeAsync(string orderId, IReadOnlyCollection<ServiceOrderTarget> targets, CancellationToken ct) => throw new NotSupportedException();

    public Task<int> RemoveAsync(string orderId, IReadOnlyCollection<string> targetIds, CancellationToken ct) => throw new NotSupportedException();
}
