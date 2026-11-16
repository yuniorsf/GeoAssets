using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Calculation;

/// <summary>
/// Thrown when a Fiscalização/Ofício line's lamp detail names a <c>(lamp code, wattage)</c> pair
/// found neither in the município's Distribuidora chart nor in the global standard chart — the
/// order cannot be validated until the chart or the spreadsheet is corrected (XD01-301).
/// </summary>
public sealed class InspectionLampNotInChartException : InvalidOperationException
{
    public LampTypeCode Code { get; }

    public int WattageW { get; }

    public InspectionLampNotInChartException(LampTypeCode code, int wattageW)
        : base($"Lamp '{code}' at {wattageW} W is not in the Distribuidora lamp wattage chart nor in the standard chart; cannot convert points to kWh.")
    {
        Code = code;
        WattageW = wattageW;
    }

    public InspectionLampNotInChartException()
    {
    }

    public InspectionLampNotInChartException(string message) : base(message)
    {
    }

    public InspectionLampNotInChartException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
