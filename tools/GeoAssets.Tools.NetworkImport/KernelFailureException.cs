namespace GeoAssets.Tools.NetworkImport;

/// <summary>The kernel failed a request after retries (exit code <see cref="NetworkImportExitCodes.KernelFailure"/>).</summary>
internal sealed class KernelFailureException : Exception
{
    public KernelFailureException()
    {
    }

    public KernelFailureException(string message)
        : base(message)
    {
    }

    public KernelFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
