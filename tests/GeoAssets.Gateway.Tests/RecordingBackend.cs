using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Gateway.Tests;

/// <summary>A real Kestrel backend on a loopback port that keeps every request it received and answers through <paramref name="respond"/>.</summary>
internal sealed class RecordingBackend : IAsyncDisposable
{
    private readonly ConcurrentQueue<SeenRequest> _seen = new();
    private readonly WebApplication _app;

    private RecordingBackend(WebApplication app) => _app = app;

    public IReadOnlyList<SeenRequest> Requests => [.. _seen];

    public string Address => _app.Urls.Single();

    public static async Task<RecordingBackend> StartAsync(string identity, Func<HttpContext, Task>? respond = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var backend = new RecordingBackend(app);
        app.Map("/{**catchAll}", async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            backend._seen.Enqueue(new SeenRequest(
                context.Request.Method,
                context.Request.Path.Value!,
                context.Request.QueryString.Value ?? string.Empty,
                context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                body));
            if (respond is not null) await respond(context);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($$"""{"backend":"{{identity}}"}""", context.RequestAborted);
        });
        await app.StartAsync();
        return backend;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
