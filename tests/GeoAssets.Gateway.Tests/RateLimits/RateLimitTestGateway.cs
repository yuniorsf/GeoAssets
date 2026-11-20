using System.Diagnostics.Metrics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Gateway.Tests.RateLimits;

/// <summary>The gateway's own <c>Program</c> in process with tiny, test-only buckets and a test-chosen client address (XD01-393).</summary>
internal static class RateLimitTestGateway
{
    public const string RemoteAddressHeader = "X-Test-Remote-Ip";
    public const string ApiPath = "/api/geoassets/widgets";
    public const string TilesPath = "/wms?SERVICE=WMS";

    /// <summary>
    /// A gateway whose every destination is <paramref name="backend"/>; <paramref name="more"/> are extra settings. The
    /// connection's address of a request is the one its <see cref="RemoteAddressHeader"/> names (a TestServer has none).
    /// </summary>
    public static GatewayFactory Create(RecordingBackend backend, params (string Key, string Value)[] more) =>
        Create(backend, null, more);

    /// <summary><see cref="Create(RecordingBackend, (string Key, string Value)[])"/> with <paramref name="configureServices"/> applied to the gateway's services too.</summary>
    public static GatewayFactory Create(RecordingBackend backend, Action<IServiceCollection>? configureServices, params (string Key, string Value)[] more)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Destinations:GeoAssets:Shared"] = backend.Address,
            ["Destinations:NeutralOperator:Shared"] = backend.Address,
            ["Destinations:IpBilling:Shared"] = backend.Address,
            ["Destinations:FieldService:Shared"] = backend.Address,
            ["Cors:AllowedOrigins:0"] = GatewayFactory.WebOrigin,
            ["Cors:AllowedOrigins:1"] = string.Empty,
            ["Observability:Otlp:Endpoint"] = string.Empty,
        };
        foreach (var (key, value) in more) settings[key] = value;

        return new GatewayFactory(settings, services =>
        {
            services.AddSingleton<IStartupFilter, TestRemoteAddressStartupFilter>();
            configureServices?.Invoke(services);
        });
    }

    /// <summary>The settings of one policy's bucket that never refills during a test.</summary>
    public static (string Key, string Value)[] Bucket(string policy, int tokenLimit, int period = 3600) =>
    [
        ($"RateLimiting:{policy}:TokenLimit", tokenLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ($"RateLimiting:{policy}:TokensPerPeriod", "1"),
        ($"RateLimiting:{policy}:ReplenishmentPeriodSeconds", period.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    ];

    public static string Token(params (string Type, string Value)[] claims) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims: claims.Select(c => new Claim(c.Type, c.Value))));

    public static async Task<HttpResponseMessage> GetAsync(
        HttpClient client, string path, string? token = null, string? remoteIp = null, string? origin = null, string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (remoteIp is not null) request.Headers.Add(RemoteAddressHeader, remoteIp);
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request);
    }

    /// <summary>Sends <paramref name="count"/> requests and returns their status codes.</summary>
    public static async Task<IReadOnlyList<HttpStatusCode>> BurstAsync(
        HttpClient client, string path, int count, string? token = null, string? remoteIp = null, string? forwardedFor = null)
    {
        var statuses = new List<HttpStatusCode>(count);
        for (var i = 0; i < count; i++)
        {
            using var response = await GetAsync(client, path, token, remoteIp, forwardedFor: forwardedFor);
            statuses.Add(response.StatusCode);
        }

        return statuses;
    }

    /// <summary>The 429s of <c>geoassets.gateway.rate_limited</c> of one gateway's <see cref="IMeterFactory"/>.</summary>
    internal sealed class RateLimitedMeasurements : IDisposable
    {
        private readonly MeterListener _listener = new();

        public RateLimitedMeasurements(IMeterFactory scope)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, scope)
                    && string.Equals(instrument.Meter.Name, GatewayTelemetry.MeterName, StringComparison.Ordinal)
                    && string.Equals(instrument.Name, GatewayTelemetry.RateLimitedCounterName, StringComparison.Ordinal))
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) => Measurements.Add((value, tags.ToArray())));
            _listener.Start();
        }

        public System.Collections.Concurrent.ConcurrentBag<(long Value, KeyValuePair<string, object?>[] Tags)> Measurements { get; } = [];

        public void Dispose() => _listener.Dispose();
    }

    private sealed class TestRemoteAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    if (context.Request.Headers.TryGetValue(RemoteAddressHeader, out var value) && IPAddress.TryParse(value.ToString(), out var address))
                        context.Connection.RemoteIpAddress = address;
                    return nextMiddleware(context);
                });
                next(app);
            };
        }
    }
}
