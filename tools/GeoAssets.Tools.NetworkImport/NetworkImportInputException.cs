namespace GeoAssets.Tools.NetworkImport;

/// <summary>An input the tool cannot use (exit code <see cref="NetworkImportExitCodes.InputError"/>).</summary>
internal sealed class NetworkImportInputException : Exception
{
    public NetworkImportInputException()
    {
    }

    public NetworkImportInputException(string message)
        : base(message)
    {
    }

    public NetworkImportInputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
