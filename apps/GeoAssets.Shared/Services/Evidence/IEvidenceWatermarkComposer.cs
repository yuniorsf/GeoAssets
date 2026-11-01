namespace GeoAssets.Shared.Services.Evidence;

/// <summary>
/// Burns <paramref name="lines"/> into the lower 120px band of <paramref name="jpegBytes"/>'
/// pixel data (XD01-244 — "burned into pixel data, not just metadata"). The real implementation
/// needs a raster/graphics library (SkiaSharp, mirroring its existing use in
/// <c>GeoAssets.Server</c>'s WMS rendering), which this interface deliberately keeps out of
/// <c>GeoAssets.Shared</c> so the assembly stays safe to reference from Blazor WASM — the
/// implementation lives MAUI-side instead (<c>apps/GeoAssets.MAUI/Services/Evidence</c>), the
/// same Shared-interface/MAUI-implementation split already used for the offline SQLite store
/// (XD01-243).
/// </summary>
public interface IEvidenceWatermarkComposer
{
    byte[] BurnWatermark(byte[] jpegBytes, IReadOnlyList<string> lines);
}
