using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace GeoAssets.Gateway.Tests;

/// <summary>A real Kestrel backend on an OS-assigned loopback port, identifying itself in every response — shared by every test that needs YARP to forward to a genuine HTTP destination rather than a stub.</summary>
internal static class TestBackend
{
    public static async Task<WebApplication> StartAsync(string identity)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapGet("/{**catchAll}", () => Results.Ok(new { backend = identity }));
        await app.StartAsync();
        return app;
    }

    public static string Address(WebApplication app) => app.Urls.Single();
}
