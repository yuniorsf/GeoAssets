namespace GeoAssets.Workflow.Erp.Sap;

/// <summary>Thrown by <see cref="ISapPmOrderAdapter.ParseWorkOrderNotification"/> when a SAP PM payload is malformed or missing a required field.</summary>
public sealed class SapPmOrderParseException : Exception
{
    public SapPmOrderParseException() { }

    public SapPmOrderParseException(string message) : base(message) { }

    public SapPmOrderParseException(string message, Exception innerException) : base(message, innerException) { }
}
