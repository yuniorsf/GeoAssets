using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Tools.SharedUseImport;

/// <summary>
/// The checked, lookup-ready form of a <see cref="SharedUseImportMappingDocument"/>. Everything
/// that would make an entry invalid for <see cref="PoleOccupancy.Create"/> is refused here, before
/// any row is looked at; measurements are already rounded to
/// <see cref="PoleOccupancyEntry.MeasurementDecimals"/> decimals.
/// </summary>
internal sealed class SharedUseImportMapping
{
    private SharedUseImportMapping(
        Dictionary<int, string> poleFeatureIds,
        Dictionary<int, string?> occupantCnpjs,
        Dictionary<string, Guid> organizationsByCnpj,
        Dictionary<int, WireTypeMapping> wireTypes)
    {
        PoleFeatureIds = poleFeatureIds;
        OccupantCnpjs = occupantCnpjs;
        OrganizationsByCnpj = organizationsByCnpj;
        WireTypes = wireTypes;
    }

    /// <summary>Legacy pole id to pole feature id. Several legacy poles may name one feature.</summary>
    public IReadOnlyDictionary<int, string> PoleFeatureIds { get; }

    /// <summary>Legacy occupant id to its CNPJ as given (null when it has none).</summary>
    public IReadOnlyDictionary<int, string?> OccupantCnpjs { get; }

    /// <summary>CNPJ digits (14) to organization id.</summary>
    public IReadOnlyDictionary<string, Guid> OrganizationsByCnpj { get; }

    /// <summary>Legacy wire type id to its rounded measurements.</summary>
    public IReadOnlyDictionary<int, WireTypeMapping> WireTypes { get; }

    /// <summary>Checks <paramref name="document"/> and indexes it.</summary>
    /// <exception cref="InvalidDataException">An entry is incomplete, out of range, or repeated.</exception>
    public static SharedUseImportMapping From(SharedUseImportMappingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new SharedUseImportMapping(
            IndexPoles(document.Poles),
            IndexOccupants(document.Occupants),
            IndexOrganizations(document.Organizations),
            IndexWireTypes(document.WireTypes));
    }

    /// <summary>The digits of a CNPJ, dropping its punctuation.</summary>
    public static string CnpjDigits(string cnpj) => string.Concat(cnpj.Where(char.IsAsciiDigit));

    /// <summary>Rounds a measurement to <see cref="PoleOccupancyEntry.MeasurementDecimals"/> decimals, halves away from zero.</summary>
    public static decimal RoundMeasurement(decimal value) =>
        Math.Round(value, PoleOccupancyEntry.MeasurementDecimals, MidpointRounding.AwayFromZero);

    private static Dictionary<int, string> IndexPoles(IReadOnlyList<PoleMapping> poles)
    {
        var index = new Dictionary<int, string>();
        foreach (var pole in poles)
        {
            if (string.IsNullOrWhiteSpace(pole.PoleFeatureId) || pole.PoleFeatureId.Length > PoleOccupancyEntry.MaxFeatureIdLength)
                throw new InvalidDataException(
                    $"Legacy pole {pole.LegacyPoleId}: the pole feature id must be 1 to {PoleOccupancyEntry.MaxFeatureIdLength} non-blank characters.");
            if (!index.TryAdd(pole.LegacyPoleId, pole.PoleFeatureId))
                throw new InvalidDataException($"Legacy pole {pole.LegacyPoleId} is mapped more than once.");
        }

        return index;
    }

    private static Dictionary<int, string?> IndexOccupants(IReadOnlyList<OccupantMapping> occupants)
    {
        var index = new Dictionary<int, string?>();
        foreach (var occupant in occupants)
        {
            if (occupant.LegacyOccupantId == LegacySharedUseRow.UnidentifiedOccupantId)
                throw new InvalidDataException(
                    $"Legacy occupant {LegacySharedUseRow.UnidentifiedOccupantId} is Geos' unidentified occupant and cannot be mapped.");
            if (occupant.Cnpj is { } cnpj && (string.IsNullOrWhiteSpace(cnpj) || cnpj.Length > PoleOccupancyEntry.MaxOperatorCnpjLength))
                throw new InvalidDataException(
                    $"Legacy occupant {occupant.LegacyOccupantId}: a CNPJ is null or 1 to {PoleOccupancyEntry.MaxOperatorCnpjLength} non-blank characters.");
            if (!index.TryAdd(occupant.LegacyOccupantId, occupant.Cnpj))
                throw new InvalidDataException($"Legacy occupant {occupant.LegacyOccupantId} is mapped more than once.");
        }

        return index;
    }

    private static Dictionary<string, Guid> IndexOrganizations(IReadOnlyList<OrganizationMapping> organizations)
    {
        var index = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var organization in organizations)
        {
            var digits = CnpjDigits(organization.Cnpj);
            if (digits.Length != 14)
                throw new InvalidDataException($"Organization CNPJ '{organization.Cnpj}' does not have 14 digits.");
            if (organization.OrganizationId == Guid.Empty)
                throw new InvalidDataException($"Organization CNPJ '{organization.Cnpj}' has an empty organization id.");
            if (!index.TryAdd(digits, organization.OrganizationId))
                throw new InvalidDataException($"Organization CNPJ '{organization.Cnpj}' is mapped more than once.");
        }

        return index;
    }

    private static Dictionary<int, WireTypeMapping> IndexWireTypes(IReadOnlyList<WireTypeMapping> wireTypes)
    {
        var index = new Dictionary<int, WireTypeMapping>();
        foreach (var wireType in wireTypes)
        {
            var rounded = wireType with
            {
                CableDiameterMm = RoundMeasurement(wireType.CableDiameterMm),
                CableLoadDaN = RoundMeasurement(wireType.CableLoadDaN),
            };
            if (rounded.CableDiameterMm is < 0 or > PoleOccupancyEntry.MaxCableDiameterMm)
                throw new InvalidDataException(
                    $"Legacy wire type {wireType.LegacyWireTypeId}: the cable diameter must be 0 to {PoleOccupancyEntry.MaxCableDiameterMm} mm.");
            if (rounded.CableLoadDaN is < 0 or > PoleOccupancyEntry.MaxCableLoadDaN)
                throw new InvalidDataException(
                    $"Legacy wire type {wireType.LegacyWireTypeId}: the cable load must be 0 to {PoleOccupancyEntry.MaxCableLoadDaN} daN.");
            if (!index.TryAdd(wireType.LegacyWireTypeId, rounded))
                throw new InvalidDataException($"Legacy wire type {wireType.LegacyWireTypeId} is mapped more than once.");
        }

        return index;
    }
}
