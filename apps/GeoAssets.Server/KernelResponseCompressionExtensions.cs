using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace GeoAssets.Server;

/// <summary>
/// Response compression for the kernel (XD01-430): Brotli, else gzip, both at <see cref="CompressionLevel.Fastest"/>,
/// for ASP.NET Core's default text MIME types (<see cref="ResponseCompressionDefaults.MimeTypes"/> — JSON and XML
/// included, PNG tiles not). The bounds read's GeoJSON is the reason: its bodies run to megabytes and compress several
/// times over. The gateway (YARP) forwards <c>Accept-Encoding</c> and passes the encoded body through untouched.
///
/// <para><b>HTTPS and BREACH.</b> Compression stays on over HTTPS. BREACH needs a response that mixes a secret with
/// attacker-chosen text, fetched with the victim's credentials by a page the attacker controls. Every kernel caller
/// authenticates with a bearer token it attaches itself — no cookie a cross-site request would carry — so such a page
/// cannot make an authenticated request to measure. Revisit this if the kernel ever accepts cookie authentication.</para>
/// </summary>
public static class KernelResponseCompressionExtensions
{
    public static IServiceCollection AddKernelResponseCompression(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        return services;
    }
}
