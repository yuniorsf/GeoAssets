using System.Globalization;

namespace GeoAssets.Shared.Services.Evidence;

/// <summary>
/// Formats the text lines XD01-244 requires burned into an evidence photo's lower 120px band
/// (order ID, UTC-3 timestamp, technician, GPS, asset tag) — pure string formatting, kept separate
/// from <see cref="IEvidenceWatermarkComposer"/>'s actual pixel rendering so it is testable without
/// a graphics library. UTC-3 is a fixed offset (Brasília time, no DST since 2019) rather than a
/// looked-up time zone, matching this ticket's literal "UTC-3 timestamp" wording.
/// </summary>
public static class EvidenceWatermarkTextFormatter
{
    private static readonly TimeSpan Utc3Offset = TimeSpan.FromHours(-3);

    public static IReadOnlyList<string> BuildLines(EvidenceCaptureContext context, GpsFix fix)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(fix);

        var utc3 = context.CapturedAtUtc.ToOffset(Utc3Offset);

        return
        [
            $"Order {context.OrderId} — Asset {context.AssetTag}",
            $"{utc3.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC-3 — Tech {context.TechnicianId}",
            $"GPS {fix.Latitude.ToString("F6", CultureInfo.InvariantCulture)}, {fix.Longitude.ToString("F6", CultureInfo.InvariantCulture)} (±{fix.HorizontalAccuracyMeters.ToString("F1", CultureInfo.InvariantCulture)}m)",
        ];
    }
}
