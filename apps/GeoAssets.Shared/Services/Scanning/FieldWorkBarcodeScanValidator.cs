using System.Text.RegularExpressions;
using GeoAssets.Core.Isp;

namespace GeoAssets.Shared.Services.Scanning;

/// <summary>
/// Validates and normalizes a decoded barcode/QR payload into the work-order field it's meant
/// for (XD01-275). Pure, offline, no I/O — the on-device barcode engine (native MAUI layer) only
/// needs to decode an image into raw text; every format/re-scan decision lives here where it's
/// unit-testable without a camera.
///
/// FSAN/MAC patterns are shared with <see cref="GeoAssets.Core.Isp.IspAssetTypes.Ont"/>'s own
/// attribute schema via <see cref="OpticalNetworkTerminalPatterns"/>, so a scanned value that
/// passes here is guaranteed to also pass <c>GeoFeatureAttributeValidator</c> when written to the
/// ONT feature's <c>CustomAttributes</c>.
/// </summary>
public static class FieldWorkBarcodeScanValidator
{
    /// <summary>
    /// Yellow identification tag ID format — alphanumeric with optional hyphens, 4 to 40
    /// characters. No real tag-printing specification was available to verify this against;
    /// a representative, structurally reasonable starting point (same caveat as
    /// <c>OpticalNetworkTerminalPatterns.FsanPattern</c>'s own) pending the real format.
    /// </summary>
    public const string YellowTagIdPattern = "^[A-Za-z0-9-]{4,40}$";

    private const string BareMacHexPattern = "^[0-9A-Fa-f]{12}$";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex FsanRegex = new(OpticalNetworkTerminalPatterns.FsanPattern, RegexOptions.Compiled, MatchTimeout);
    private static readonly Regex MacAddressRegex = new(OpticalNetworkTerminalPatterns.MacAddressPattern, RegexOptions.Compiled | RegexOptions.ExplicitCapture, MatchTimeout);
    private static readonly Regex BareMacHexRegex = new(BareMacHexPattern, RegexOptions.Compiled, MatchTimeout);
    private static readonly Regex YellowTagIdRegex = new(YellowTagIdPattern, RegexOptions.Compiled, MatchTimeout);

    /// <summary>
    /// Validates <paramref name="rawPayload"/> against <paramref name="expectedField"/>'s format.
    /// A MAC address scanned without separators (12 bare hex digits, as many barcode encodings
    /// produce) is normalized to colon-separated form on success — every other recognized format
    /// is returned verbatim (trimmed).
    /// </summary>
    public static BarcodeScanResult Validate(ExpectedScanField expectedField, string? rawPayload)
    {
        var raw = rawPayload ?? string.Empty;
        var trimmed = raw.Trim();

        if (trimmed.Length == 0)
            return new BarcodeScanResult(false, null, raw, "Empty scan — please try again.");

        return expectedField switch
        {
            ExpectedScanField.YellowTagId => ValidateYellowTagId(trimmed, raw),
            ExpectedScanField.OntFsan => ValidateFsan(trimmed, raw),
            ExpectedScanField.OntMacAddress => ValidateMacAddress(trimmed, raw),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedField), expectedField, "Unknown expected scan field."),
        };
    }

    private static BarcodeScanResult ValidateYellowTagId(string trimmed, string raw) =>
        YellowTagIdRegex.IsMatch(trimmed)
            ? new BarcodeScanResult(true, trimmed, raw, null)
            : new BarcodeScanResult(false, null, raw, $"'{trimmed}' doesn't look like a tag ID — please re-scan the yellow tag.");

    private static BarcodeScanResult ValidateFsan(string trimmed, string raw) =>
        FsanRegex.IsMatch(trimmed)
            ? new BarcodeScanResult(true, trimmed, raw, null)
            : new BarcodeScanResult(false, null, raw, $"'{trimmed}' doesn't match the expected FSAN format — please re-scan the ONT's serial label.");

    private static BarcodeScanResult ValidateMacAddress(string trimmed, string raw)
    {
        if (MacAddressRegex.IsMatch(trimmed))
            return new BarcodeScanResult(true, trimmed, raw, null);

        var bareHex = trimmed
            .Replace(":", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);

        if (BareMacHexRegex.IsMatch(bareHex))
            return new BarcodeScanResult(true, NormalizeMacAddress(bareHex), raw, null);

        return new BarcodeScanResult(false, null, raw, $"'{trimmed}' doesn't match the expected MAC address format — please re-scan the ONT's MAC label.");
    }

    private static string NormalizeMacAddress(string bareHex)
    {
        var upper = bareHex.ToUpperInvariant();
        return string.Join(':', Enumerable.Range(0, 6).Select(i => upper.Substring(i * 2, 2)));
    }
}
