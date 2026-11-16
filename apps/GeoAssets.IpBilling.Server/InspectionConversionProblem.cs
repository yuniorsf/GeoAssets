namespace GeoAssets.IpBilling.Server;

/// <summary>
/// 422 body returned by the Fiscalização/Ofício <c>/validate</c> routes when the inspected
/// points cannot be converted to kWh (XD01-301): <paramref name="Code"/> is
/// <c>lamp-not-in-chart</c> (with the offending <paramref name="LampCode"/>/<paramref name="WattageW"/>)
/// or <c>standard-wattage-unavailable</c>. Nothing was posted to the Faturamento ledger and the
/// order is still <c>Calculado</c>.
/// </summary>
internal sealed record InspectionConversionProblem(string Code, string Message, string? LampCode, int? WattageW);
