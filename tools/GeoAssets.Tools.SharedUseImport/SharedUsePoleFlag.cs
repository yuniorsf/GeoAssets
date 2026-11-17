namespace GeoAssets.Tools.SharedUseImport;

/// <summary>Something about an imported pole as a whole a person should look at.</summary>
internal enum SharedUsePoleFlag
{
    /// <summary>More than six occupied points; the ones beyond the sixth are over-capacity entries.</summary>
    OverCapacity,

    /// <summary>At least one occupied point has no occupant organization.</summary>
    UnidentifiedOccupant,

    /// <summary>More than one legacy pole is mapped to this pole feature; their rows were combined.</summary>
    MergedLegacyPoles,
}
