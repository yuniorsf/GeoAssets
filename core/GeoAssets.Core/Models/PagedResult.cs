using System.Text.Json.Serialization;

namespace GeoAssets.Core.Models;

/// <summary>One page of <typeparamref name="T"/> plus the total count across all pages.</summary>
public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Total number of items matching the query, not just the count of <see cref="Items"/>.</summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// <c>true</c> when the reader stopped counting at a count limit (XD01-479, the kernel's <c>POST features/query</c>):
    /// more items match than that limit, and <see cref="TotalCount"/> is the limit + 1 — a lower bound, not the exact
    /// total. <c>false</c> (the default, left out of JSON so every other page's body is unchanged): <see cref="TotalCount"/>
    /// is exact.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TotalCountCapped { get; init; }
}
