namespace GeoAssets.Workflow.Isp.Bss;

/// <summary>Thrown by <see cref="IBssOssOrderAdapter.ParseOrderSpawn"/> when a provider payload is malformed or missing a required field.</summary>
public sealed class BssOrderSpawnParseException : Exception
{
    public BssOrderSpawnParseException() { }

    public BssOrderSpawnParseException(string message) : base(message) { }

    public BssOrderSpawnParseException(string message, Exception innerException) : base(message, innerException) { }
}
