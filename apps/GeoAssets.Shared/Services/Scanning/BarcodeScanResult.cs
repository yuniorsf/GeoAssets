namespace GeoAssets.Shared.Services.Scanning;

/// <summary>
/// Result of <see cref="FieldWorkBarcodeScanValidator.Validate"/> (XD01-275). A rejected scan
/// always carries a <see cref="RejectionReason"/> the UI can show as a re-scan prompt — AC:
/// "Malformed/unrecognized barcode formats surface a clear re-scan prompt, not a silent failure."
/// </summary>
/// <param name="IsValid">Whether the scan matched the expected field's format.</param>
/// <param name="NormalizedValue">
/// The value to write into the work-order field — e.g. a bare 12-hex-digit MAC scan normalized
/// to colon-separated form, matching <see cref="GeoAssets.Core.Isp.OpticalNetworkTerminalPatterns.MacAddressPattern"/>
/// exactly. Null when <see cref="IsValid"/> is false.
/// </param>
/// <param name="RawPayload">The raw, unmodified decoded barcode payload, for diagnostics.</param>
/// <param name="RejectionReason">Human-readable re-scan prompt. Null when <see cref="IsValid"/> is true.</param>
public sealed record BarcodeScanResult(bool IsValid, string? NormalizedValue, string RawPayload, string? RejectionReason);
