namespace GeoAssets.Core.Isp.Compliance;

/// <summary>
/// Regulatory minimum safety distances for pole-sharing (XD01-284's AC2) — fixed constants, not
/// computed per pole, so every exported record carries the same two values.
/// </summary>
public static class PoleSafetyDistances
{
    public const double SecondaryMeters = 0.60;
    public const double MediumVoltageMeters = 1.50;
}
