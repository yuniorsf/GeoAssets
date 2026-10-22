namespace GeoAssets.IpBilling.Models;

/// <summary>
/// The fixed catalog of public-lighting lamp technologies carried over from the legacy
/// GEOS_FAT_IP module. This set is closed — new codes are never added by an end user,
/// only by a future migration, mirroring <see cref="LampType"/>'s own "seeded, not
/// user-editable" nature.
/// </summary>
public enum LampTypeCode
{
    /// <summary>Fluorescente.</summary>
    FL,

    /// <summary>Halógena.</summary>
    HL,

    /// <summary>Incandescente.</summary>
    IN,

    /// <summary>LED.</summary>
    LD,

    /// <summary>Mista (mixed, self-ballasted mercury lamp).</summary>
    MT,

    /// <summary>Multivapor (metal halide).</summary>
    MV,

    /// <summary>Vapor de Mercúrio.</summary>
    VM,

    /// <summary>Vapor de Sódio.</summary>
    VS,
}
