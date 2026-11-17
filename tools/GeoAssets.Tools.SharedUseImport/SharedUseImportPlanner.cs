using System.Security.Cryptography;
using System.Text;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Tools.SharedUseImport;

/// <summary>
/// Turns legacy <c>SharedUses</c> rows into pole occupancy (pure: no I/O, no clock).
///
/// <list type="bullet">
///   <item><description>A row stands for <see cref="LegacySharedUseRow.Count"/> attachment
///   points, so it becomes that many entries.</description></item>
///   <item><description>On each pole, entries take the regulated slots 1..6 in ascending legacy
///   row id; every entry beyond the sixth is an over-capacity entry (no slot). Nothing is
///   dropped.</description></item>
///   <item><description>An identified occupant (CNPJ matched to an organization) is
///   <see cref="PoleAttachmentOccupantStatus.AuthorizedContracted"/>; any other is unidentified
///   and <see cref="PoleAttachmentOccupantStatus.ClandestineUnauthorized"/>.</description></item>
///   <item><description>Entry ids are derived from the owner, pole, legacy row and point, so the
///   same input always plans the same entries.</description></item>
/// </list>
/// </summary>
internal static class SharedUseImportPlanner
{
    /// <summary>Plans the import of <paramref name="rows"/> for the pole owner <paramref name="ownerOrganizationId"/>.</summary>
    /// <param name="rows">The legacy rows, in input order.</param>
    /// <param name="mapping">The checked mapping.</param>
    /// <param name="ownerOrganizationId">The pole owner the import runs as.</param>
    /// <param name="asOf">The stamp put on the planned occupancy.</param>
    /// <exception cref="ArgumentException"><paramref name="ownerOrganizationId"/> is empty.</exception>
    public static SharedUseImportPlan Plan(
        IReadOnlyList<LegacySharedUseRow> rows,
        SharedUseImportMapping mapping,
        Guid ownerOrganizationId,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(mapping);
        if (ownerOrganizationId == Guid.Empty)
            throw new ArgumentException("The pole owner organization id must not be empty.", nameof(ownerOrganizationId));

        var skipped = new List<SkippedSharedUseRow>();
        var seenRowIds = new HashSet<int>();
        var rowsByPole = new SortedDictionary<string, List<LegacySharedUseRow>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (Skip(row, mapping, seenRowIds) is { } reason)
            {
                skipped.Add(new SkippedSharedUseRow(row.Id, row.PoleId, row.LineNumber, reason));
                continue;
            }

            var poleFeatureId = mapping.PoleFeatureIds[row.PoleId];
            if (!rowsByPole.TryGetValue(poleFeatureId, out var poleRows))
                rowsByPole.Add(poleFeatureId, poleRows = []);
            poleRows.Add(row);
        }

        var poles = rowsByPole
            .Select(pole => PlanPole(pole.Key, pole.Value, mapping, ownerOrganizationId, asOf))
            .ToList();
        return new SharedUseImportPlan(ownerOrganizationId, rows.Count, poles, skipped);
    }

    /// <summary>The id of point <paramref name="point"/> (0-based) of legacy row <paramref name="legacyRowId"/> on a pole.</summary>
    public static Guid EntryId(Guid ownerOrganizationId, string poleFeatureId, int legacyRowId, int point)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"geos-shared-use|{ownerOrganizationId:D}|{poleFeatureId}|{legacyRowId}|{point}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static SharedUseRowSkipReason? Skip(LegacySharedUseRow row, SharedUseImportMapping mapping, HashSet<int> seenRowIds)
    {
        if (!seenRowIds.Add(row.Id)) return SharedUseRowSkipReason.DuplicateRow;
        if (row.Count <= 0) return SharedUseRowSkipReason.NonPositiveCount;
        if (!mapping.PoleFeatureIds.ContainsKey(row.PoleId)) return SharedUseRowSkipReason.UnmappedPole;
        return null;
    }

    private static SharedUsePolePlan PlanPole(
        string poleFeatureId,
        List<LegacySharedUseRow> rows,
        SharedUseImportMapping mapping,
        Guid ownerOrganizationId,
        DateTimeOffset asOf)
    {
        var ordered = rows.OrderBy(row => row.Id).ToList();
        var entries = new List<PoleOccupancyEntry>();
        var flagged = new List<FlaggedSharedUseRow>();
        foreach (var row in ordered)
        {
            var (occupantOrganizationId, cnpj, occupantFlag) = ResolveOccupant(row.OccupantId, mapping);
            if (occupantFlag is { } flag)
                flagged.Add(new FlaggedSharedUseRow(row.Id, row.LineNumber, flag));

            var hasWireType = mapping.WireTypes.TryGetValue(row.WireTypeId, out var wireType);
            if (!hasWireType)
                flagged.Add(new FlaggedSharedUseRow(row.Id, row.LineNumber, SharedUseRowFlag.UnmappedWireType));

            for (var point = 0; point < row.Count; point++)
            {
                var position = entries.Count + 1;
                entries.Add(new PoleOccupancyEntry
                {
                    Id = EntryId(ownerOrganizationId, poleFeatureId, row.Id, point),
                    PoleFeatureId = poleFeatureId,
                    PoleOwnerOrganizationId = ownerOrganizationId,
                    SlotIndex = position <= PoleAttachmentSlotMatrix.MaxSlots ? position : null,
                    Status = occupantOrganizationId is null
                        ? PoleAttachmentOccupantStatus.ClandestineUnauthorized
                        : PoleAttachmentOccupantStatus.AuthorizedContracted,
                    OccupantOrganizationId = occupantOrganizationId,
                    OperatorCnpj = cnpj,
                    CableDiameterMm = wireType?.CableDiameterMm ?? 0m,
                    CableLoadDaN = wireType?.CableLoadDaN ?? 0m,
                });
            }
        }

        var occupancy = PoleOccupancy.Create(poleFeatureId, ownerOrganizationId, entries, PoleOccupancy.FirstVersion, asOf);
        return new SharedUsePolePlan(
            occupancy,
            [.. ordered.Select(row => row.PoleId).Distinct().Order()],
            [.. ordered.Select(row => row.Id)],
            flagged);
    }

    private static (Guid? OrganizationId, string? Cnpj, SharedUseRowFlag? Flag) ResolveOccupant(int legacyOccupantId, SharedUseImportMapping mapping)
    {
        if (legacyOccupantId == LegacySharedUseRow.UnidentifiedOccupantId)
            return (null, null, SharedUseRowFlag.UnidentifiedOccupant);
        if (!mapping.OccupantCnpjs.TryGetValue(legacyOccupantId, out var cnpj))
            return (null, null, SharedUseRowFlag.UnknownOccupant);
        if (cnpj is null)
            return (null, null, SharedUseRowFlag.OccupantWithoutCnpj);
        return mapping.OrganizationsByCnpj.TryGetValue(SharedUseImportMapping.CnpjDigits(cnpj), out var organizationId)
            ? (organizationId, cnpj, null)
            : (null, cnpj, SharedUseRowFlag.UnmatchedCnpj);
    }
}
