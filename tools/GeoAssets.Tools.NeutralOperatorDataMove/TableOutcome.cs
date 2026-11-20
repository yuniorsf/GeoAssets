namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>What one run found and did on one table.</summary>
internal sealed class TableOutcome(string table)
{
    /// <summary>The most differing keys a report lists per table.</summary>
    public const int MaxSamples = 10;

    private readonly List<string> _differentKeys = [];

    public string Table { get; } = table;

    /// <summary>Moved rows in the kernel's database.</summary>
    public long SourceRows { get; set; }

    /// <summary>Moved rows the target already holds with identical content.</summary>
    public long Identical { get; set; }

    /// <summary>Moved rows the target lacks.</summary>
    public long Missing { get; set; }

    /// <summary>Moved rows whose id the target holds with other content (or that collide with another of its rows).</summary>
    public long Different { get; set; }

    /// <summary>Rows <c>copy</c> inserted.</summary>
    public long Copied { get; set; }

    /// <summary>Rows <c>remove</c> deleted from the kernel (for <c>ServiceOrderTargets</c>: re-keyed to the vertical).</summary>
    public long Removed { get; set; }

    /// <summary>Up to <see cref="MaxSamples"/> keys of the differing rows.</summary>
    public IReadOnlyList<string> DifferentKeys => _differentKeys;

    public void AddDifferent(long rows, string key)
    {
        Different += rows;
        if (_differentKeys.Count < MaxSamples) _differentKeys.Add(key);
    }
}
