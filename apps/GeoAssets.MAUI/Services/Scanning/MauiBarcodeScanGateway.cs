using GeoAssets.Shared.Services.Scanning;
using Microsoft.Maui.Media;
using SkiaSharp;
using ZXing;

namespace GeoAssets.MAUI.Services.Scanning;

/// <summary>
/// The MAUI-side half of XD01-275's scan flow: captures a still photo of the tag/ONT label via
/// <see cref="MediaPicker"/> (the same capture call <c>MauiEvidenceCaptureGateway</c> — XD01-244
/// — already uses), decodes it fully on-device with the pure, platform-agnostic
/// <c>ZXing.Net</c> engine (<see cref="BarcodeReaderGeneric"/> over an <see cref="RGBLuminanceSource"/>
/// built from the decoded bitmap's own pixels — no cloud OCR/ML Kit call, satisfying the AC's
/// "works fully offline"), then hands the raw decoded text to the portable, testable
/// <see cref="FieldWorkBarcodeScanValidator"/> (<c>GeoAssets.Shared</c>) for format
/// validation/normalization.
///
/// A still-photo-then-decode flow rather than a live camera-preview viewfinder (the
/// <c>ZXing.Net.Maui.Controls</c> package's <c>CameraBarcodeReaderView</c>) — simpler, reuses an
/// already-proven capture path, and avoids a second per-platform camera-handler dependency on
/// top of the SkiaSharp reference already in this project. A re-scan is just calling
/// <see cref="ScanAsync"/> again.
/// </summary>
internal static class MauiBarcodeScanGateway
{
    public static async Task<BarcodeScanResult> ScanAsync(ExpectedScanField expectedField, CancellationToken ct = default)
    {
        var photo = await MediaPicker.Default.CapturePhotoAsync().ConfigureAwait(false);
        if (photo is null)
            return new BarcodeScanResult(false, null, string.Empty, "Scan cancelled — please try again.");

        using var stream = await photo.OpenReadAsync().ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);

        var decodedText = DecodeBarcode(buffer.ToArray());
        if (decodedText is null)
            return new BarcodeScanResult(false, null, string.Empty, "No barcode detected in that photo — please re-scan with the code centered and in focus.");

        return FieldWorkBarcodeScanValidator.Validate(expectedField, decodedText);
    }

    private static string? DecodeBarcode(byte[] photoBytes)
    {
        using var bitmap = SKBitmap.Decode(photoBytes);
        if (bitmap is null) return null;

        var format = bitmap.ColorType == SKColorType.Bgra8888
            ? RGBLuminanceSource.BitmapFormat.BGRA32
            : RGBLuminanceSource.BitmapFormat.RGBA32;

        var luminanceSource = new RGBLuminanceSource(bitmap.Bytes, bitmap.Width, bitmap.Height, format);
        var reader = new BarcodeReaderGeneric();
        return reader.Decode(luminanceSource)?.Text;
    }
}
