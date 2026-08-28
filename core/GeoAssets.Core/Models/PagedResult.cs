namespace GeoAssets.Core.Models;

/// <summary>One page of <typeparamref name="T"/> plus the total count across all pages.</summary>
public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Total number of items matching the query, not just the count of <see cref="Items"/>.</summary>
    public required int TotalCount { get; init; }
}
