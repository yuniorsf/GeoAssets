namespace GeoAssets.Core.Isp;

/// <summary>
/// FSAN/MAC address validation patterns for <see cref="IspAssetTypes.Ont"/> — pulled out as named
/// constants (XD01-275) so <see cref="IspAssetTypes.Ont"/>'s JSON Schema and
/// <c>GeoAssets.Shared.Services.Scanning.FieldWorkBarcodeScanValidator</c> (the mobile
/// barcode/QR scan-to-field validator) share one source of truth instead of two regexes that
/// could silently drift apart.
/// </summary>
public static class OpticalNetworkTerminalPatterns
{
    /// <summary>
    /// FSAN serial pattern — alphanumeric, 8 to 20 characters. A representative, structurally
    /// reasonable pattern (ONT vendor FSAN serials are commonly a short alpha vendor prefix plus
    /// digits, e.g. "HWTC12345678"), not independently verified against any one vendor's actual
    /// FSAN specification — same "representative starting point" caveat as
    /// <c>TechnicalNormSchemas</c>' own disclaimer.
    /// </summary>
    public const string FsanPattern = "^[A-Za-z0-9]{8,20}$";

    /// <summary>Colon-separated MAC address pattern, e.g. <c>AA:BB:CC:DD:EE:FF</c>.</summary>
    public const string MacAddressPattern = "^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$";
}
