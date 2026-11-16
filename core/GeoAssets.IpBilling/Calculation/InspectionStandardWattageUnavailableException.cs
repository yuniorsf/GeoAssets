namespace GeoAssets.IpBilling.Calculation;

/// <summary>
/// Thrown when a Fiscalização/Ofício has points without lamp detail but neither the município's
/// Distribuidora chart nor the global standard chart has any row to average a watts-per-point
/// value from (XD01-301).
/// </summary>
public sealed class InspectionStandardWattageUnavailableException : InvalidOperationException
{
    public InspectionStandardWattageUnavailableException()
        : base("No lamp wattage chart row exists to derive a standard watts-per-point value; cannot convert points without lamp detail to kWh.")
    {
    }

    public InspectionStandardWattageUnavailableException(string message) : base(message)
    {
    }

    public InspectionStandardWattageUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
