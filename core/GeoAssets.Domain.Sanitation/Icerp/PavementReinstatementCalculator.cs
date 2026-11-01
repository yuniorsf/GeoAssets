namespace GeoAssets.Domain.Sanitation.Icerp;

/// <summary>
/// Computes the pavement reinstatement (ICERP) area a trench repair actually needs to cut back
/// and repave — not just the raw excavated footprint, but that footprint widened by a margin on
/// every edge, the standard civil-works practice for a sound bond between old and new pavement.
///
/// <paramref name="marginMeters"/> (the saw-cut/overcut allowance per edge) is deliberately
/// caller-supplied, not a hardcoded constant — the required margin varies by municipality/
/// utility technical standard and pavement type, and isn't something this engine should
/// present as a fixed, authoritative number.
/// </summary>
public static class PavementReinstatementCalculator
{
    public static double CalculateReinstatementAreaSquareMeters(TrenchDimensions trench, double marginMeters)
    {
        ArgumentNullException.ThrowIfNull(trench);
        if (marginMeters < 0) throw new ArgumentOutOfRangeException(nameof(marginMeters), "Must not be negative.");

        var reinstatementLength = trench.LengthMeters + (2 * marginMeters);
        var reinstatementWidth = trench.WidthMeters + (2 * marginMeters);
        return reinstatementLength * reinstatementWidth;
    }
}
