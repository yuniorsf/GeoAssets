namespace GeoAssets.Core.Services;

/// <summary>
/// Thrown by an <see cref="Interfaces.IAssetProvider"/> that serves a large catalog from its store instead of from memory
/// (XD01-449) when a read would return more features than the provider's enumeration limit: nothing is returned, because
/// materializing such a read is what the bounded catalog exists to avoid. The caller narrows the read (a bounding box, a
/// page — <see cref="Interfaces.IAssetProvider.GetPageAsync"/>, a closer topology seed). A host answering a caller reports
/// it as <c>422</c> with the code <c>catalog.enumeration-limit</c>.
/// </summary>
public sealed class CatalogEnumerationLimitException : InvalidOperationException
{
    /// <summary>The refused read, e.g. <c>GetAll</c> or <c>GetDescendants</c>; empty when built with a standard constructor.</summary>
    public string Operation { get; } = string.Empty;

    /// <summary>The most features the read may return; <c>0</c> when built with a standard constructor.</summary>
    public int Limit { get; }

    public CatalogEnumerationLimitException(string operation, int limit)
        : base($"{operation} would return more than {limit} features; narrow the read (a bounding box, a page or a closer seed).")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        Operation = operation;
        Limit = limit;
    }

    public CatalogEnumerationLimitException()
    {
    }

    public CatalogEnumerationLimitException(string message) : base(message)
    {
    }

    public CatalogEnumerationLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
