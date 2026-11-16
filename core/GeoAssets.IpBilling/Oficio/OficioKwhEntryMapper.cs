using GeoAssets.IpBilling.Calculation;

namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// Normalizes <see cref="OficioLine"/>s into the <see cref="InspectionPointEntry"/>s
/// <see cref="InspectionKwhConverter"/> converts to kWh (XD01-301), applying exactly the signs of
/// <see cref="OficioLineCalculator"/>'s formulas:
/// <list type="bullet">
/// <item>group 1 <see cref="OficioOperationGroup.Inclusao"/>: <c>+</c>, normal burn hours, retroactive;</item>
/// <item>group 2 <see cref="OficioOperationGroup.Retirada"/>: <c>−</c>, normal burn hours, retroactive;</item>
/// <item>group 3 <see cref="OficioOperationGroup.Novos24hIrregulares"/>: <c>+</c>, 24h-irregular burn hours;</item>
/// <item>group 4 <see cref="OficioOperationGroup.Regularizacao24h"/>: <c>−</c>, 24h-irregular burn hours.</item>
/// </list>
/// Whether groups 3/4 are retroactive follows the <see cref="OficioRetroativoPolicy"/> in effect —
/// the open XD01-185 symmetry question. This mapper does not resolve it: it defaults to
/// <see cref="OficioLineCalculator.DefaultRetroativoPolicy"/>, the same constant that produced the
/// line's persisted <see cref="OficioLine.Retroativo"/>, so the kWh and the point count can never
/// disagree and flipping that one constant flips both.
/// </summary>
public static class OficioKwhEntryMapper
{
    /// <summary>
    /// One entry per lamp detail (exact) plus, per line and operation group, one lamp-less entry
    /// for the points no lamp detail covers (provisional). Groups with nothing to report yield no entry.
    /// </summary>
    /// <exception cref="InvalidOperationException">See <see cref="CountWithoutLampDetail"/>.</exception>
    public static List<InspectionPointEntry> ToEntries(
        IReadOnlyList<OficioLine> lines, OficioRetroativoPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var groups3And4AreRetroactive =
            (policy ?? OficioLineCalculator.DefaultRetroativoPolicy) == OficioRetroativoPolicy.SymmetricWithAgregado;
        GroupRule[] rules =
        [
            new(OficioOperationGroup.Inclusao, 1, IsIrregular24h: false, CountsTowardRetroativo: true),
            new(OficioOperationGroup.Retirada, -1, IsIrregular24h: false, CountsTowardRetroativo: true),
            new(OficioOperationGroup.Novos24hIrregulares, 1, IsIrregular24h: true, groups3And4AreRetroactive),
            new(OficioOperationGroup.Regularizacao24h, -1, IsIrregular24h: true, groups3And4AreRetroactive),
        ];

        var entries = new List<InspectionPointEntry>();
        foreach (var line in lines)
        {
            foreach (var rule in rules)
            {
                entries.AddRange(line.LampDetails
                    .Where(lamp => lamp.OperationGroup == rule.Group)
                    .Select(lamp => new InspectionPointEntry(
                        lamp.Quantity, rule.Sign, rule.IsIrregular24h, rule.CountsTowardRetroativo, lamp.Code, lamp.WattageW)));

                var remainder = CountWithoutLampDetail(line, rule.Group);
                if (remainder > 0)
                {
                    entries.Add(new InspectionPointEntry(
                        remainder, rule.Sign, rule.IsIrregular24h, rule.CountsTowardRetroativo));
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// How many of <paramref name="line"/>'s points under <paramref name="group"/> are NOT covered
    /// by a lamp detail: the group's summed count minus its lamp details' summed quantity.
    /// </summary>
    /// <exception cref="InvalidOperationException">The lamp details claim more points than the
    /// group's own count — the line is internally inconsistent and must be re-uploaded.</exception>
    public static int CountWithoutLampDetail(OficioLine line, OficioOperationGroup group)
    {
        ArgumentNullException.ThrowIfNull(line);

        var count = line.OperationGroups.Where(d => d.OperationGroup == group).Sum(d => d.Count);
        var withLampDetail = line.LampDetails.Where(l => l.OperationGroup == group).Sum(l => l.Quantity);
        if (withLampDetail > count)
        {
            throw new InvalidOperationException(
                $"Line '{line.CodigoInstalacao}' has lamp detail for {withLampDetail} point(s) in operation group '{group}' but that group only counts {count}.");
        }

        return count - withLampDetail;
    }

    private sealed record GroupRule(OficioOperationGroup Group, int Sign, bool IsIrregular24h, bool CountsTowardRetroativo);
}
