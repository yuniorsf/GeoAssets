using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Services.Evidence;

public static class EvidenceCaptureServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IEvidenceCaptureService"/>. The host must separately register an
    /// <see cref="IEvidenceWatermarkComposer"/> (MAUI-only, since it needs a raster library —
    /// see that interface's doc comment).
    /// </summary>
    public static IServiceCollection AddFieldWorkEvidenceCapture(this IServiceCollection services) =>
        services.AddSingleton<IEvidenceCaptureService, EvidenceCaptureService>();
}
