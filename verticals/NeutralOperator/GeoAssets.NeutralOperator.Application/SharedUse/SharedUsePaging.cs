using GeoAssets.Core.Models;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.NeutralOperator.Application.SharedUse;

/// <summary>Paging of the plan's lists, with the limits every paged workflow read uses.</summary>
internal static class SharedUsePaging
{
    public static void Check(int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, OrderTargetQuery.MaxPageSize);
    }

    public static PagedResult<TItem> Slice<TSource, TItem>(
        IReadOnlyList<TSource> all, int page, int pageSize, Func<TSource, TItem> select)
    {
        var skip = (long)(page - 1) * pageSize;
        return new PagedResult<TItem>
        {
            Items = skip >= all.Count ? [] : [.. all.Skip((int)skip).Take(pageSize).Select(select)],
            TotalCount = all.Count,
        };
    }
}
