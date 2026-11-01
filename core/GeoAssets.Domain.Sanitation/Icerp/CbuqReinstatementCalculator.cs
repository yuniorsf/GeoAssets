namespace GeoAssets.Domain.Sanitation.Icerp;

/// <summary>
/// Computes the CBUQ (Concreto Betuminoso Usinado a Quente — hot-mix asphalt concrete) volume
/// needed to reinstate a given pavement area at a given layer thickness: a plain area × depth
/// calculation, nothing regulation-specific. <paramref name="thicknessMeters"/> is
/// caller-supplied — asphalt layer thickness standards vary by road classification and
/// utility/municipal technical spec.
/// </summary>
public static class CbuqReinstatementCalculator
{
    public static double CalculateVolumeCubicMeters(double areaSquareMeters, double thicknessMeters)
    {
        if (areaSquareMeters < 0) throw new ArgumentOutOfRangeException(nameof(areaSquareMeters), "Must not be negative.");
        if (thicknessMeters < 0) throw new ArgumentOutOfRangeException(nameof(thicknessMeters), "Must not be negative.");

        return areaSquareMeters * thicknessMeters;
    }
}
