using GeoAssets.Shared.Services.Evidence;
using SkiaSharp;

namespace GeoAssets.MAUI.Services.Evidence;

/// <summary>
/// Burns <see cref="IEvidenceWatermarkComposer"/>'s lines into the lower 120px band of the
/// image's own pixel data (XD01-244) using SkiaSharp — the same rendering library already used
/// server-side for WMS tile rendering (<c>GeoAssets.Server/WmsEndpointExtensions.cs</c>), kept
/// out of <c>GeoAssets.Shared</c> so that assembly stays safe to reference from Blazor WASM (see
/// <see cref="IEvidenceWatermarkComposer"/>'s doc comment).
/// </summary>
internal sealed class SkiaEvidenceWatermarkComposer : IEvidenceWatermarkComposer
{
    private const float BandHeight = 120f;

    public byte[] BurnWatermark(byte[] jpegBytes, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(jpegBytes);
        ArgumentNullException.ThrowIfNull(lines);

        using var original = SKBitmap.Decode(jpegBytes);
        using var surface = SKSurface.Create(new SKImageInfo(original.Width, original.Height));
        var canvas = surface.Canvas;

        canvas.DrawBitmap(original, 0, 0);

        var bandTop = Math.Max(0, original.Height - BandHeight);
        using var bandPaint = new SKPaint { Color = new SKColor(0, 0, 0, 160), Style = SKPaintStyle.Fill };
        canvas.DrawRect(new SKRect(0, bandTop, original.Width, original.Height), bandPaint);

        using var font = new SKFont { Size = 20 };
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var lineHeight = BandHeight / (lines.Count + 1);
        for (var i = 0; i < lines.Count; i++)
            canvas.DrawText(lines[i], 12, bandTop + (lineHeight * (i + 1)), font, textPaint);

        canvas.Flush();
        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, 90);
        return encoded.ToArray();
    }
}
