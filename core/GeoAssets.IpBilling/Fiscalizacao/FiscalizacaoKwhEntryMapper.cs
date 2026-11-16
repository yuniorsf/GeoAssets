using GeoAssets.IpBilling.Calculation;

namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// Normalizes <see cref="FiscalizacaoLine"/>s into the <see cref="InspectionPointEntry"/>s
/// <see cref="InspectionKwhConverter"/> converts to kWh (XD01-301), applying exactly the signs of
/// <see cref="FiscalizacaoLineCalculator"/>'s formulas:
/// <list type="bullet">
/// <item>group 1 <see cref="FiscalizacaoOperationGroup.Fiscalizacao"/>: <c>+</c>, normal burn hours, retroactive;</item>
/// <item>group 2 <see cref="FiscalizacaoOperationGroup.UltimoLevantamento"/>: <c>−</c>, normal burn hours, retroactive;</item>
/// <item>group 3 <see cref="FiscalizacaoOperationGroup.Novos24hIrregulares"/>: <c>+</c>, 24h-irregular burn hours, NOT retroactive;</item>
/// <item>group 4 <see cref="FiscalizacaoOperationGroup.Regularizacao24h"/>: <c>−</c>, 24h-irregular burn hours, NOT retroactive.</item>
/// </list>
/// Groups 3/4 are excluded from the retroactive figure because Fiscalização's
/// <c>Retroativo = Retroativo₁ − Retroativo₂</c> excludes them — the confirmed rule, not re-decided here.
/// </summary>
public static class FiscalizacaoKwhEntryMapper
{
    private static readonly GroupRule[] Rules =
    [
        new(FiscalizacaoOperationGroup.Fiscalizacao, 1, IsIrregular24h: false, CountsTowardRetroativo: true),
        new(FiscalizacaoOperationGroup.UltimoLevantamento, -1, IsIrregular24h: false, CountsTowardRetroativo: true),
        new(FiscalizacaoOperationGroup.Novos24hIrregulares, 1, IsIrregular24h: true, CountsTowardRetroativo: false),
        new(FiscalizacaoOperationGroup.Regularizacao24h, -1, IsIrregular24h: true, CountsTowardRetroativo: false),
    ];

    /// <summary>
    /// One entry per lamp detail (exact) plus, per line and operation group, one lamp-less entry
    /// for the points no lamp detail covers (provisional). Groups with nothing to report yield no entry.
    /// </summary>
    /// <exception cref="InvalidOperationException">See <see cref="CountWithoutLampDetail"/>.</exception>
    public static List<InspectionPointEntry> ToEntries(IReadOnlyList<FiscalizacaoLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var rules = Rules;

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
    public static int CountWithoutLampDetail(FiscalizacaoLine line, FiscalizacaoOperationGroup group)
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

    private sealed record GroupRule(FiscalizacaoOperationGroup Group, int Sign, bool IsIrregular24h, bool CountsTowardRetroativo);
}
