namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>A distinct exception type a fake mapper names, so a test sees which mapper won.</summary>
internal sealed class MappedByException : Exception
{
    public MappedByException()
    {
        By = string.Empty;
    }

    public MappedByException(string by) : base(by)
    {
        By = by;
    }

    public MappedByException(string by, Exception innerException) : base(by, innerException)
    {
        By = by;
    }

    public string By { get; }
}
