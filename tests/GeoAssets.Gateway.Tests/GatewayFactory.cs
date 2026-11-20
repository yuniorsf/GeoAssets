using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Gateway.Tests;

/// <summary>
/// The gateway's own <c>Program</c> (its real route table, CORS, rate limiter and observability pipeline), in process, with
/// its destinations pointed at <paramref name="settings"/>'s addresses.
/// </summary>
internal sealed class GatewayFactory(IReadOnlyDictionary<string, string?> settings, Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    public static GatewayFactory For(
        RecordingBackend kernel, RecordingBackend neutralOperator, IReadOnlyDictionary<string, string?>? more = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Destinations:GeoAssets:Shared"] = kernel.Address,
            ["Destinations:NeutralOperator:Shared"] = neutralOperator.Address,
            ["Destinations:IpBilling:Shared"] = kernel.Address,
            ["Destinations:FieldService:Shared"] = kernel.Address,
            ["Cors:AllowedOrigins:0"] = WebOrigin,
            ["Cors:AllowedOrigins:1"] = string.Empty,
            ["Observability:Otlp:Endpoint"] = string.Empty,
        };
        foreach (var (key, value) in more ?? new Dictionary<string, string?>(StringComparer.Ordinal)) settings[key] = value;

        return new GatewayFactory(settings);
    }

    /// <summary>The gateway pointed at <paramref name="kernelAddress"/> and <paramref name="verticalAddress"/> (the NeutralOperator destination) with <paramref name="configureServices"/> applied to its services.</summary>
    public static GatewayFactory ForAddresses(string kernelAddress, string verticalAddress, Action<IServiceCollection> configureServices) =>
        new(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Destinations:GeoAssets:Shared"] = kernelAddress,
                ["Destinations:IpBilling:Shared"] = kernelAddress,
                ["Destinations:FieldService:Shared"] = kernelAddress,
                ["Destinations:NeutralOperator:Shared"] = verticalAddress,
                ["Cors:AllowedOrigins:0"] = WebOrigin,
                ["Cors:AllowedOrigins:1"] = string.Empty,
                ["Observability:Otlp:Endpoint"] = string.Empty,
            },
            configureServices);

    public const string WebOrigin = "https://web.test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");
        foreach (var (key, value) in settings) builder.UseSetting(key, value);
        if (configureServices is not null) builder.ConfigureTestServices(configureServices);
    }
}
